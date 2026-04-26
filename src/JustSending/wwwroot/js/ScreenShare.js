var ScreenShare = {
    hub: null,
    isSharing: false,
    isViewing: false,
    usingRelay: false,
    _initialized: false,
    _peerConnection: null,
    _localStream: null,
    _pendingCandidates: [],
    _relayInterval: null,
    _relayVideo: null,
    _relayCanvas: null,
    _relayCtx: null,

    ICE_CONFIG: {
        iceServers: [
            { urls: "stun:stun.l.google.com:19302" },
            { urls: "stun:stun1.l.google.com:19302" }
        ]
    },
    RELAY_FPS_INTERVAL: 100,
    RELAY_JPEG_QUALITY: 0.6,

    init: function (hub) {
        if (this._initialized) return;
        this._initialized = true;
        this.hub = hub;
        this._setupRelayCanvas();
        this._registerHubEvents();
        $("#screenShareBtn").on("click", function () { ScreenShare.startSharing(); return false; });
        $("#screenShareStopBtn").on("click", function () { ScreenShare.stopSharing(); return false; });
    },

    _setupRelayCanvas: function () {
        this._relayCanvas = document.createElement("canvas");
        this._relayCtx = this._relayCanvas.getContext("2d");
    },

    _registerHubEvents: function () {
        this.hub.on("rtcOffer", function (offerSdp) {
            ScreenShare._handleIncomingOffer(offerSdp);
        });

        this.hub.on("rtcAnswer", async function (answerSdp) {
            if (!ScreenShare._peerConnection) return;
            try {
                await ScreenShare._peerConnection.setRemoteDescription(
                    new RTCSessionDescription({ type: "answer", sdp: answerSdp })
                );
                ScreenShare._flushPendingCandidates();
            } catch (e) {
                Log("[RTC] setRemoteDescription(answer) failed: " + e);
            }
        });

        this.hub.on("rtcIceCandidate", function (candidateJson) {
            var pc = ScreenShare._peerConnection;
            if (!pc || !pc.remoteDescription) {
                ScreenShare._pendingCandidates.push(candidateJson);
                return;
            }
            try {
                pc.addIceCandidate(new RTCIceCandidate(JSON.parse(candidateJson)));
            } catch (e) {
                Log("[RTC] addIceCandidate failed: " + e);
            }
        });

        this.hub.on("rtcStop", function () {
            ScreenShare._teardownViewer();
        });

        this.hub.on("rtcRelayFrame", function (frameBase64) {
            if (!ScreenShare.isViewing) return;
            $("#screen-share-relay-img").attr("src", frameBase64);
        });
    },

    startSharing: async function () {
        if (this.isSharing) return;
        var stream;
        try {
            stream = await navigator.mediaDevices.getDisplayMedia({ video: { frameRate: 15 }, audio: false });
        } catch (e) {
            return;
        }

        this._localStream = stream;
        this.isSharing = true;
        this._updateUI();

        stream.getVideoTracks()[0].addEventListener("ended", function () {
            ScreenShare.stopSharing();
        });

        await this._createPeerConnectionAsOfferer();
    },

    _createPeerConnectionAsOfferer: async function () {
        var pc = new RTCPeerConnection(this.ICE_CONFIG);
        this._peerConnection = pc;
        this._pendingCandidates = [];

        var self = this;
        this._localStream.getTracks().forEach(function (track) {
            pc.addTrack(track, self._localStream);
        });

        pc.onicecandidate = function (e) { self._handleIceCandidate(e); };
        pc.oniceconnectionstatechange = function () { self._onIceConnectionStateChange(); };

        var offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        await this.hub.invoke("RtcOffer", offer.sdp);
    },

    _handleIncomingOffer: async function (offerSdp) {
        this.isViewing = true;
        this._updateUI();
        this._pendingCandidates = [];

        var pc = new RTCPeerConnection(this.ICE_CONFIG);
        this._peerConnection = pc;

        var self = this;
        pc.ontrack = function (e) { self._showViewer(e.streams[0]); };
        pc.onicecandidate = function (e) { self._handleIceCandidate(e); };
        pc.oniceconnectionstatechange = function () { self._onIceConnectionStateChange(); };

        await pc.setRemoteDescription(new RTCSessionDescription({ type: "offer", sdp: offerSdp }));
        this._flushPendingCandidates();

        var answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        await this.hub.invoke("RtcAnswer", answer.sdp);
    },

    _handleIceCandidate: function (e) {
        if (!e.candidate) return;
        this.hub.invoke("RtcIceCandidate", JSON.stringify(e.candidate.toJSON()))
            .catch(function (err) { Log("[RTC] ICE send failed: " + err); });
    },

    _flushPendingCandidates: function () {
        var pc = this._peerConnection;
        if (!pc) return;
        var pending = this._pendingCandidates.splice(0);
        pending.forEach(function (json) {
            try { pc.addIceCandidate(new RTCIceCandidate(JSON.parse(json))); } catch (e) {}
        });
    },

    _onIceConnectionStateChange: function () {
        if (!this._peerConnection) return;
        var state = this._peerConnection.iceConnectionState;
        Log("[RTC] ICE state: " + state);

        if (state === "connected" || state === "completed") {
            this.usingRelay = false;
            this._updateUI();
        }

        if (state === "failed") {
            if (this.isSharing) {
                this._activateRelay();
            } else if (this.isViewing) {
                this._startViewingRelay();
            }
        }
    },

    _activateRelay: function () {
        if (this.usingRelay) return;
        this.usingRelay = true;
        Log("[RTC] Relay mode active (sharer)");
        this._updateUI();

        if (!this._relayVideo) {
            this._relayVideo = document.createElement("video");
            this._relayVideo.autoplay = true;
            this._relayVideo.muted = true;
            this._relayVideo.style.display = "none";
            document.body.appendChild(this._relayVideo);
        }
        this._relayVideo.srcObject = this._localStream;

        var self = this;
        this._relayInterval = setInterval(function () {
            if (!self.isSharing || !self.usingRelay || !self._relayVideo.videoWidth) return;
            var vw = self._relayVideo.videoWidth;
            var vh = self._relayVideo.videoHeight;
            self._relayCanvas.width = vw;
            self._relayCanvas.height = vh;
            self._relayCtx.drawImage(self._relayVideo, 0, 0, vw, vh);
            var frameBase64 = self._relayCanvas.toDataURL("image/jpeg", self.RELAY_JPEG_QUALITY);
            self.hub.invoke("RtcRelayFrame", frameBase64)
                .catch(function (e) { Log("[RTC] Frame relay failed: " + e); });
        }, this.RELAY_FPS_INTERVAL);
    },

    _startViewingRelay: function () {
        if (this.usingRelay) return;
        this.usingRelay = true;
        Log("[RTC] Relay mode active (viewer)");
        this._updateUI();
        $("#screen-share-video").hide();
        $("#screen-share-relay-img").show();
    },

    _stopRelay: function () {
        if (this._relayInterval) {
            clearInterval(this._relayInterval);
            this._relayInterval = null;
        }
        if (this._relayVideo) {
            this._relayVideo.srcObject = null;
        }
        this.usingRelay = false;
    },

    _stopPeerConnection: function () {
        var pc = this._peerConnection;
        if (!pc) return;
        pc.ontrack = null;
        pc.onicecandidate = null;
        pc.oniceconnectionstatechange = null;
        pc.close();
        this._peerConnection = null;
    },

    stopSharing: function () {
        if (!this.isSharing) return;
        if (this._localStream) {
            this._localStream.getTracks().forEach(function (t) { t.stop(); });
            this._localStream = null;
        }
        this._stopRelay();
        this._stopPeerConnection();
        this.isSharing = false;
        this._updateUI();
        this.hub.invoke("RtcStop").catch(function (e) { Log("[RTC] RtcStop failed: " + e); });
    },

    _teardownViewer: function () {
        this._stopRelay();
        this._stopPeerConnection();
        this._hideViewer();
        this.isViewing = false;
        this._updateUI();
    },

    _showViewer: function (stream) {
        var video = document.getElementById("screen-share-video");
        video.srcObject = stream;
        video.style.display = "block";
        $("#screen-share-relay-img").hide();
        $("#screen-share-panel").slideDown(200);
    },

    _hideViewer: function () {
        var video = document.getElementById("screen-share-video");
        if (video) video.srcObject = null;
        $("#screen-share-relay-img").attr("src", "").hide();
        $("#screen-share-video").show();
        $("#screen-share-panel").slideUp(200);
    },

    _updateUI: function () {
        if (this.isSharing) {
            $("#screenShareBtn").hide();
            $("#screenShareStopBtn").show();
            $("#screenShareIndicator").show();
        } else {
            $("#screenShareStopBtn").hide();
            $("#screenShareIndicator").hide();
        }

        if (this.usingRelay) {
            $("#screen-share-relay-badge").show();
        } else {
            $("#screen-share-relay-badge").hide();
        }
    }
};
