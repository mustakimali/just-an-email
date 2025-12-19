var JustSendingApp = {
    hub: null,
    initialSecretToSend: "",

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
        window.onbeforeunload = function (event) {
            JustSendingApp.goHome();
        };
    },

    initQrCode: function () {
        var qrEl = $("#qr-code");
        if (qrEl.hasClass("done")) {
            return;
        }
        new QRCode("qr-code", {
            text: window.location.href,
            width: 256,
            height: 256,
            colorDark: "#000000",
            colorLight: "#d9edf7",
            correctLevel: QRCode.CorrectLevel.H
        });
        qrEl.addClass("done");
    },

    getOrGenId: function () {
        var $id = $("#SessionId");
        if ($id.val() === "")
            return this.generateGuid();
        else
            return $id.val();
    },

    getOrGenId2: function () {
        var $id = $("#SessionVerification");
        if ($id.val() === "")
            return this.generateGuid();
        else
            return $id.val();
    },

    generateGuid: function () {
        function S4() { return Math.floor((1 + Math.random()) * 0x10000).toString(16).substring(1); }
        var guid = S4() + S4() + S4() + S4() + S4() + S4() + S4() + S4();
        return guid.toLowerCase();
    },

    initPermalink: function (then) {
        var $id = $("#SessionId");
        var $id2 = $("#SessionVerification");

        var id = this.getOrGenId();
        var id2 = this.getOrGenId2();

        if (window.location.hash && window.location.hash.length >= 65) {
            var hash = window.location.hash.substr(1);

            id = hash.substr(0, 32);
            id2 = hash.substr(32, 32);
            const next = hash.substr(64, 1);

            if (next === "/") {
                this.initialSecretToSend = atob(hash.substr(65));
                history.replaceState(null, '', '/app#' + id + id2);
                window.location.hash = id + id2;
            }
        } else {
            history.replaceState(null, '', '/app');
            window.location.hash = id + id2;
        }

        setTimeout(function () { JustSendingApp.initQrCode(); }, 1);

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
            ajax_service.sendPostRequest("/app/message-raw", data, async function (data) {
                var pka = $this.parents(".msg").data("key");
                var decrypted = await JustSendingApp.decryptMessageInternal(pka, data.content);
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
            function () {
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

        var replaceFormValue = function (name, value) {
            for (var i = 0; i < formData.length; i++) {
                if (formData[i].name == name) {
                    formData[i].value = value;
                    return true;
                }
            }
            return false;
        }

        if (!EndToEndEncryption.isEstablished()) {
            EndToEndEncryption.generateOwnPrivateKey(function (pka) {
                replaceFormValue("EncryptionPublicKeyAlias", pka);
                $("#form").submit();
            });
            return false;
        }

        if ($("#file")[0].files.length > 0) {
            if (JustSendingApp.fileProcessed) {
                JustSendingApp.fileProcessed = false;
                formOptions.url += "/files-stream";
                hasFile = true;
            } else {
                var file = $("#file")[0].files[0];
                JustSendingApp.processFileForUpload(file, function (encryptedFile, encryptedFileName) {
                    var fileInput = $("#file")[0];
                    var dt = new DataTransfer();
                    dt.items.add(encryptedFile);
                    fileInput.files = dt.files;
                    $("#ComposerText").val(encryptedFileName);
                    JustSendingApp.fileProcessed = true;
                    $("#form").submit();
                });
                return false;
            }
        } else if (JustSendingApp.finishedStreamingFile) {
            JustSendingApp.finishedStreamingFile = false;
            hasFile = true;
        }

        if (!hasFile) {
            var text = $("#ComposerText").val();
            EndToEndEncryption.encryptWithPublicKey(text).then(function(encrypted) {
                $("#ComposerText").val(encrypted);
                JustSendingApp.showStatus("Sending, please wait...");
                JustSendingApp.submitFormDirectly();
            }).catch(function(err) {
                console.error('Encryption failed:', err);
                JustSendingApp.showStatus("Encryption failed: " + err.message);
            });
            return false;
        }

        JustSendingApp.showStatus("Sending, please wait...");
        return true;
    },

    submitFormDirectly: function() {
        var $form = $("#form");
        var formData = new FormData($form[0]);

        $.ajax({
            url: $form.attr('action'),
            type: 'POST',
            data: formData,
            processData: false,
            contentType: false,
            success: function() {
                JustSendingApp.onSendComplete();
                JustSendingApp.showStatus();
                app_busy(false);
            },
            error: function(xhr, status, error) {
                console.error('Submit failed:', error);
                JustSendingApp.showStatus("Send failed: " + error);
            }
        });
    },

    processFileForUpload: async function (file, done) {
        var self = this;

        self.showStatus("Encrypting file...");

        try {
            var arrayBuffer = await file.arrayBuffer();
            var uint8Array = new Uint8Array(arrayBuffer);

            var encryptedContent = await EndToEndEncryption.hybridEncrypt(uint8Array, EndToEndEncryption.publicKey);
            var encryptedContentStr = btoa(JSON.stringify(encryptedContent));

            var encryptedFileName = await EndToEndEncryption.encryptWithPublicKey(file.name);

            var blob = new Blob([encryptedContentStr], { type: 'application/octet-stream' });
            var encryptedFile = new File([blob], file.name + '.enc', { type: 'application/octet-stream' });

            self.showStatus("File encrypted successfully");
            done(encryptedFile, encryptedFileName);

        } catch (error) {
            console.error('Error encrypting file:', error);
            self.showStatus("Error encrypting file: " + error.message);
        }
    },

    downloadAndDecryptFile: async function (downloadUrl, messageId, publicKeyAlias) {
        var self = this;

        self.showStatus("Downloading and decrypting file...");

        try {
            var response = await fetch(downloadUrl);
            if (!response.ok) {
                throw new Error('Network response was not ok');
            }

            var encryptedFileContent = await response.text();

            var encryptedObj = JSON.parse(atob(encryptedFileContent));
            var decryptedArrayBuffer = await EndToEndEncryption.decryptFile(btoa(JSON.stringify(encryptedObj)), publicKeyAlias);

            if (!decryptedArrayBuffer) {
                throw new Error('Failed to decrypt file content');
            }

            var bytes = new Uint8Array(decryptedArrayBuffer);

            var $msgEl = $(".msg[data-id='" + messageId + "']");
            var $fileNameEl = $msgEl.find('.file-name');
            var decryptedFileName = $fileNameEl.text().trim() || 'decrypted_file';

            var blob = new Blob([bytes]);
            var url = URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url;
            a.download = decryptedFileName;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);

            self.showStatus("File decrypted and downloaded successfully!");
            setTimeout(function() { self.showStatus(); }, 2000);

        } catch (error) {
            console.error('Error downloading/decrypting file:', error);
            self.showStatus("Error: " + error.message);
        }
    },

    decryptMessages: async function () {
        var $msgEls = $(".msg");
        for (var idx = 0; idx < $msgEls.length; idx++) {
            var $itm = $($msgEls[idx]);
            if ($itm.hasClass("decrypted")) continue;

            var $data = $itm.find(".data");
            if (!$data.length) continue;

            var encryptedData = $data.attr("data-value");
            var pka = $itm.data("key");
            var decryptedData = "";

            if (pka == null) {
                decryptedData = encryptedData;
            } else {
                decryptedData = await JustSendingApp.decryptMessageInternal(pka, encryptedData);

                if (decryptedData == null) {
                    $data.html("<span class='text-danger'><i class='fa fa-lock'></i> Encrypted</span>");
                    continue;
                }
            }

            $data.text(decryptedData);
            $data.removeAttr("data-value");
            $itm.addClass("decrypted");
        }
        JustSendingApp.convertLinks();
    },

    decryptMessageInternal: async function (pka, encryptedData) {
        var pk = EndToEndEncryption.keys_hash[pka];
        if (pk == null) {
            console.warn('No private key found for alias:', pka);
            return null;
        }
        try {
            return await EndToEndEncryption.decrypt(encryptedData, pka);
        } catch (error) {
            console.error('Decryption failed:', error);
            return null;
        }
    },

    initWebSocket: function () {
        var ws = new signalR.HubConnectionBuilder()
            .withUrl("/signalr/hubs")
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: function () {
                    return 2000
                }
            })
            .build();
        var conn = ws;
        this.hub = ws;

        conn.on("requestReloadMessage", function () {
            JustSendingApp.loadMessages();
        });

        conn.on("showSharePanel", function (token) {
            $("#token").text(token);
            JustSendingApp.switchView(true);
        });

        conn.on("hideSharePanel", function () {
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
            }, function () {
                window.onbeforeunload = null;
                conn.send("eraseSession");

                swal("Erasing...", "You will be taken to the homepage when it's done.", "success");
            });

            return false;
        });

        EndToEndEncryption.initKeyExchange(conn);

        var onConnected = function () {
            conn
                .invoke("connect", $("#SessionId").val())
                .then(function (socketConnectionId) {

                    $("#SocketConnectionId").val(socketConnectionId);

                    Log("Device Id: " + socketConnectionId);
                    $(".FilePostUrl").text(JustSendingApp.getPostFromCliPath());

                    app_busy(false);

                    if (JustSendingApp.initialSecretToSend !== "") {
                        $("#ComposerText").val(JustSendingApp.initialSecretToSend);
                        $("#form").submit();
                        JustSendingApp.initialSecretToSend = "";

                        swal({
                            title: "Password sent! now connect another device...",
                            text: "To retrieve this from another device, use the QR code or the code displayed below to securely connect to this session.",
                            type: "success"
                        });
                    }
                });
        };

        conn
            .start()
            .then(onConnected)
            .catch(function (err) {
                Log("Error: " + err.toString());

            });

        conn.connection.onclose = function (msg) {
            Log("Closing");
        };

        window.onbeforeunload = function (e) {
            JustSendingApp.hub.stop();
        };
    },

    goHome: function () {
        var dest = "/?ref=app";
        try {
            history.replaceState({}, "", dest);
        } catch (e) { }
        window.location.replace(dest);
    },

    getPostFromCliPath: function () {
        var sessionId = $("#SessionId").val();
        return location.protocol + "//" + location.host + "/f/" + sessionId;
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

