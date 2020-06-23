var JustSendingApp = {
    hub: null,

    init: function () {
        app_busy(true);

        var that = this;
        this.initPermalink(function () {

            that.loadMessages(function () {
                that.initWebSocket();
            });
        });

        that.switchView(false);
        that.initComposer();
        that.initAutoSizeComposer();

        $("*[title]").tooltip();
        $("a.navbar-brand").on("click", function () {
            JustSendingApp.goHome();
            return false;
        });

        window.onbeforeunload = function(event) {
            JustSendingApp.goHome();
        };
    },

    initQrCode: function () {
        var qrEl = $("#qr-code");
        if(qrEl.hasClass("done")) {
            return;
        }
        new QRCode("qr-code", {
            text: window.location.href,
            width: 256,
            height: 256,
            colorDark : "#000000",
            colorLight : "#d9edf7",
            correctLevel : QRCode.CorrectLevel.H
        });
        qrEl.addClass("done");
    },

    initPermalink: function (then) {
        var $id = $("#SessionId");
        var $id2 = $("#SessionVerification");

        var id = $id.val();
        var id2 = $id2.val();

        if (window.location.hash && window.location.hash.length === 65) {
            var hash = window.location.hash.substr(1);

            id = hash.substr(0, 32);
            id2 = hash.substr(32);
        } else {
            window.location.hash = id + id2;
        }
        this.initQrCode();

        // Request to create session
        //
        ajax_service.sendRequest("POST", "/app/new", { id: id, id2: id2 },
            function (data) {
                // Success
                $id.val(id);
                $id2.val(id2);

                then();
            }, function () {
                // Error
            }, true, "text");


    },

    initAutoSizeComposer: function () {
        try {
            autosize($("#ComposerText"));
        } catch (ex) { }
    },

    autosizesUpdate: function (text) {
        try {
            autosizes.update(text);
        } catch (ex) { }
    },

    lastStatusUpdatedEpoch: 0,
    statusUpdateInterval: 500,

    showStatus: function (msg, progress) {
        if (msg !== undefined && progress !== undefined && Date.now() - this.lastStatusUpdatedEpoch < this.statusUpdateInterval) {
            return;
        }

        if (msg === undefined) msg = null;
        if (progress === undefined) progress = null;

        var $el = $("#please-wait");
        var $text = $("#status");
        var $percent = $("#percent");

        if (msg != null)
            $text.html(msg);

        $percent.text(progress == null ? "" : progress + "%");

        if (msg == null && progress == null) {
            $el.slideUp(250);
        } else if ($el.is(":hidden")) {
            $el.slideDown(250);
        }

        this.lastStatusUpdatedEpoch = Date.now();
    },

    copySource: function () {
        var $el = $("#source-pre");
        $el.focus();
        $el.select();

        try {

            var successful = document.execCommand('copy');

            if (successful) {
                swal({
                    title: "Copied!",
                    text: "Copied to your clipboard.",
                    timer: 2000,
                    type: "success"
                });
                $("#sourceModal").modal("toggle");
            } else {
                throw "up";
            }

        } catch (err) {

            swal({
                title: "Not supported in this browser",
                text: "Please select the text above and manually copy.",
                type: "error"
            });
        }

        return false;
    },

    initViewSource: function () {
        $(".msg .source").on("click", function () {
            var $this = $(this);
            var $modal = $("#sourceModal");
            var $cnt = $("#source-pre");

            $cnt.html("");
            $modal.modal("show");

            var id = $(this).data("id");
            var sid = $("#SessionId").val();
            var data = {
                messageId: id,
                sessionId: sid
            };
            ajax_service.sendPostRequest("/app/message-raw", data, function (data) {
                // Decrypt
                var pka = $this.parents(".msg").data("key");
                var decrypted = JustSendingApp.decryptMessageInternal(pka, data.content);
                if (decrypted != null)
                    data.content = decrypted;

                $cnt.val(data.content);
            });
        });
    },

    initComposer: function () {
        var $file = $("#file");
        var $text = $("#ComposerText");
        var $clearBtn = $(".clearSelectedFileBtn");
        var $fileData = $("#fileData");
        var $form = $("#form");

        var options = {
            success: function () {
                JustSendingApp.onSendComplete();
                JustSendingApp.showStatus();
                app_busy(false);
            },
            beforeSubmit: JustSendingApp.beforeSubmit,
            uploadProgress: function (e, pos, t, per) {
                JustSendingApp.showStatus("Sending...", per);

            },

            resetForm: true
        };

        $("#form").ajaxForm(options);

        $text.keypress(function (e) {
            if (e.which == 13 && e.ctrlKey) {
                $("#form").submit();
                return false;
            }
            return true;
        });

        $file.on("change", function () {
            if ($file[0].files.length > 0) {
                var maxFileSize = parseInt($form.data("max-b"));
                var file = $file[0].files[0];

                if (file.size > maxFileSize) {
                    var maxFileSizeDisplay = $form.data("max-display");
                    swal({
                        title: "Maximum " + maxFileSizeDisplay,
                        text: "This file is too big to share.",
                        type: "error"
                    });
                    $clearBtn.trigger("click");
                    return;
                }
                $text.data("old", $text.val());
                $text.val(file.name);
                $text.attr("readonly", "readonly");
                $clearBtn.show();
            } else {
                $clearBtn.trigger("click");
            }
        });

        $(".selectFileBtn").on("click", function () {
            $file.trigger("click");
            return false;
        });

        $clearBtn.on("click",
            function() {
                $file.val("");
                $clearBtn.hide();
                $text.removeAttr("readonly");
                $text.val($text.data("old"));
                $text.data("old", "");
                JustSendingApp.autosizesUpdate($text);
                $fileData.val("");
                JustSendingApp.initAutoSizeComposer();
                return false;
            });
    },

    finishedStreamingFile: false,

    beforeSubmit: function (formData, formObject, formOptions) {
        if (!$("#form").valid()) {
            return false;
        }
        
        var hasFile = false;

        var replaceFormValue = function (name, factory) {
            for (var i = 0; i < formData.length; i++) {
                if (formData[i].name == name) {
                    var newValue = factory(formData[i].value);
                    if (newValue != null) {
                        formData[i].value = newValue;
                        return true;
                    }
                }
            }

            return false;
        }

        if (!EndToEndEncryption.isEstablished()) {
            // no client has been connected yet,
            // generate a secret key
            // Which then be transmitted to peer
            //
            EndToEndEncryption.generateOwnPrivateKey(function (pka) {
                // the form was serialized before generating the key,
                // so i need to make sure this public key is included in this post
                //
                replaceFormValue("EncryptionPublicKeyAlias", function (v) { return pka; });

                $("#form").submit();
            });

            return false;
        }

        if ($("#file")[0].files.length > 0) {

            JustSendingApp.finishedStreamingFile = false;
            formOptions.url += "/files-stream";
            hasFile = true;
            
            /*var file = $("#file")[0].files[0];

            JustSendingApp.processFile(file, function () {
                // Unselect file
                //
                $("#file").val("");
                // Do normal post
                //
                JustSendingApp.finishedStreamingFile = true;
                $("#form").submit();
            });

            return false;*/

        } else if (JustSendingApp.finishedStreamingFile) {
            
            JustSendingApp.finishedStreamingFile = false;
            formOptions.url += "/files";

        }

        JustSendingApp.showStatus("Sending, please wait...");

        if(!hasFile)
            replaceFormValue("ComposerText", function (v) { return EndToEndEncryption.encryptWithPrivateKey(v); });

        return true;
    },

    processFile: function (file, done) {
        var fileSize = file.size;
        var bufferSize = 64 * 1024;
        var offset = 0;
        var sessionId = $("#SessionId").val();
        var self = this;
        var pageOneSent = false;

        var load = function (evt) {

            if (evt.target.error == null) {
                offset += evt.target.result.length;

                var encData = EndToEndEncryption.encryptWithPrivateKey(evt.target.result);
                if (pageOneSent) {
                    var j = JSON.parse(encData);
                    encData = JSON.stringify({
                        iv: j.iv,
                        ct: j.ct
                    });
                    j = null;
                }

                self
                    .hub
                    .server
                    .streamFile(sessionId, encData)
                    .then(function () {
                        pageOneSent = true;

                        // read the next block
                        //
                        readBuffer(offset, bufferSize, file);
                    });
                
                self.showStatus("Encrypting...", parseInt(offset * 100 / fileSize));

            } else {
                Log("Read error: " + evt.target.error);
                app_busy(false);
                return;
            }
            if (offset >= fileSize) {
                Log("Done streaming file...");
                
                r.onload = null;
                app_busy(false);
                done(file.name)
                return;
            }

        }
    
        var r = new FileReader();
        r.onload = load;

        var readBuffer = function (_offset, length, _file) {
            var blob = _file.slice(_offset, length + _offset);
            r.readAsText(blob);
        }

        app_busy(true);
        readBuffer(offset, bufferSize, file);
    },

    decryptMessages: function () {
        var $msgEls = $(".msg");
        $.each($msgEls, function (idx, itm) {
            var $itm = $(itm);
            if ($itm.hasClass("decrypted")) return;

            var $data = $itm.find(".data");

            if (!$data.length) return;

            var encryptedData = $data.attr("data-value");
            var pka = $itm.data("key");
            var decryptedData = "";

            if (pka == null) {
                decryptedData = encryptedData;
            } else {
                decryptedData = JustSendingApp.decryptMessageInternal(pka, encryptedData);

                if (decryptedData == null) {
                    $data.html("<span class='text-danger'><i class='fa fa-lock'></i> Encrypted</span>");
                    return;
                }
            }

            $data.text(decryptedData);

            $data.removeAttr("data-value");
            $itm.addClass("decrypted");

            JustSendingApp.convertLinks();
        });
    },

    decryptMessageInternal: function (pka, encryptedData) {
        var pk = EndToEndEncryption.keys_hash[pka];
        if (pk == null) {
            return null;
        }
        return EndToEndEncryption.decrypt(encryptedData, pk);
    },

    initWebSocket: function () {
        var ws = new signalR.HubConnectionBuilder()
            .withUrl("/signalr/hubs")
            .build();
        var conn = ws;
        this.hub = ws;

        conn.on("requestReloadMessage", function() {
            JustSendingApp.loadMessages();
        });

        conn.on("showSharePanel", function (token) {
            $("#token").text(token);
            JustSendingApp.switchView(true);
        });

        conn.on("hideSharePanel", function() {
            JustSendingApp.switchView(false);
        });

        conn.on("sessionDeleted", function () {
            JustSendingApp.goHome();
        });

        conn.on("setNumberOfDevices", function (num) {
            var $el = $("#connectedDevices");

            if (num > 1) {
                $("#connectedDevices span").text(num - 1);
                $el.css("display", "inline-block");
            } else {
                $el.css("display", "none");

                if (!$(".connect-instruction-panel").is(":visible")) {
                    swal({
                        title: "Nobody to share to.",
                        text: "Click 'Connect Another Device' to allow other device to connect securely using a PIN.",
                        type: "warning"
                    });
                }
            }
        });

        conn.on("redirect", function (url) {
            document.location.href = url;
        });

        $("#shareBtn").on("click", function () {
            conn.send("share");
            return false;
        });

        $(".cancelShareBtn").on("click", function () {
            conn.send("cancelShare");
            return false;
        });

        $("#deleteBtn").on("click", function () {

            swal({
                title: "Are you sure?",
                text: "This will destroy this sharing session and erase everything you've shared here!",
                type: "warning",
                showCancelButton: true,
                confirmButtonColor: "#d9534f",
                confirmButtonText: "Erase everything!",
                closeOnConfirm: false
            }, function() {
                window.onbeforeunload = null;
                conn.send("eraseSession");

                swal("Erasing...", "You will be taken to the homepage when it's done.", "success");
            });

            return false;
        });

        EndToEndEncryption.initKeyExchange(conn);
        
        var onConnected = function() {
            conn
                .send("connect", $("#SessionId").val())
                .then(function(socketConnectionId) {

                    $("#SocketConnectionId").val(socketConnectionId);

                    Log("Device Id: " + socketConnectionId);

                    app_busy(false);
                });
        };

        conn
            .start()
            .then(onConnected)
            .catch(function(err) {
                Log("Error: " + err.toString());

            });

        conn.connection.onclose = function(msg) {
            Log("Closing");
        };

        window.onbeforeunload = function (e) {
            JustSendingApp.hub.stop();
        };
    },

    goHome: function() {
        window.location.replace("/?ref=app");
    },

    switchView: function (showSharePanel) {
        var $sharePanel = $(".connect-instruction-panel");
        var $shareActions = $(".share-actions");

        if (showSharePanel) {
            $sharePanel.slideDown(500);
            $shareActions.slideUp(500);
        } else {
            $sharePanel.slideUp(500);
            $shareActions.slideDown(500);
        }
    },

    loadMessageInProgress: false,
    loadMessageTimer: null,

    loadMessages: function (then) {
        if (this.loadMessageInProgress) {
            this.loadMessageTimer = setTimeout(function () { JustSendingApp.loadMessages(); }, 500);
            return;
        }

        this.loadingMessage = true;
        clearTimeout(this.loadMessageTimer);
        
        var id = $("#SessionId").val();
        var id2 = $("#SessionVerification").val();
        var from = parseInt($(".msg-c:first").data("seq"));

        ajax_service.sendRequest("POST",
            "/app/messages",
            {
                id: id,
                id2: id2,
                from: isNaN(from) ? 0 : from
            },
            function (response) {

                $($.parseHTML(response))
                    .prependTo($("#conversation-list"));
                    
                JustSendingApp.decryptMessages();
                
                JustSendingApp.processTime();
                JustSendingApp.initViewSource();

                if (then != undefined) then();
                JustSendingApp.loadMessageInProgress = false;

            },
            true,
            "text/html");
    },

    onSendComplete: function () {
        $("#ComposerText").select();
        $(".clearSelectedFileBtn").trigger("click");
        JustSendingApp.convertLinks();
    },

    processTime: function () {
        $.each($(".msg .time"), function (idx, itm) {
            var $this = $(itm);
            if (isNaN(Date.parse($this.data("val")))) {
                $this.find(".val").text($this.attr("title"));
                return;
            }
            var gmt = new Date($this.data("val"));
            $this.find(".val").text(gmt.toLocaleTimeString());
        });
    },

    htmlDecode: function (encodedString) {
        var textArea = document.createElement('textarea');
        textArea.innerHTML = encodedString;
        return textArea.value;
    },

    convertLinks: function () {
        $.each($(".msg .text .data"),
            function (idx, itm) {
                if ($(itm).hasClass("embedded"))
                    return;

                if ($(itm).children().length)
                    return;

                $(itm).linkify({
                    target: "_blank",
                    defaultProtocol: "https",
                    className: "linkified"
                });
                
                $(itm).addClass("embedded");
            });
    }
};

