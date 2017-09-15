var JustSendingApp = {
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

    },

    initPermalink: function (then) {
        var $id = $("#SessionId");
        var $id2 = $("#SessionVerification");

        var id = $id.val();
        var id2 = $id2.val();

        if (window.location.hash && window.location.hash.length == 65) {
            var hash = window.location.hash.substr(1);

            id = hash.substr(0, 32);
            id2 = hash.substr(32);
        } else {
            window.location.hash = id + id2;
        }

        // Request to create session
        //
        ajax_service.sendRequest("POST", "/app/new", { id, id2 },
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
        autosize($("#ComposerText"));
    },

    showStatus: function (msg, progress) {
        if (msg == undefined) msg = null;
        if (progress == undefined) progress = null;

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
                $('.sendBtn').trigger("click");
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

        $clearBtn.on("click", function () {
            $file.val("");
            $clearBtn.hide();
            $text.removeAttr("readonly");
            $text.val($text.data("old"));
            $text.data("old", "");
            autosize.update($text);
            $fileData.val("");
            JustSendingApp.initAutoSizeComposer();
            return false;
        })
    },


    beforeSubmit: function (formData, formObject, formOptions) {
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

        if ($("#file")[0].files.length > 0) {
            //
            // Files are posted in a different endpoint
            //
            formOptions.url += "/files";
            hasFile = true;
            app_busy(true);
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
            });
        }

        JustSendingApp.showStatus("Sending, please wait...");

        if (!hasFile) {
            // not encrypting file name yet!
            //
            replaceFormValue("ComposerText", function (v) { return EndToEndEncryption.encryptWithPrivateKey(v); });
        }

        return true;
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
        var hub = $.connection.conversationHub;

        hub.client.requestReloadMessage = function () {
            JustSendingApp.loadMessages();
        };

        hub.client.showSharePanel = function (token) {
            $("#token").text(token);
            JustSendingApp.switchView(true);
        };

        hub.client.hideSharePanel = function () {
            JustSendingApp.switchView(false);
        };

        hub.client.sessionDeleted = function () {
            window.location.href = "/";
        };

        hub.client.setNumberOfDevices = function (num) {
            var $el = $("#connectedDevices");

            if (num > 1) {
                $("#connectedDevices span").text(num - 1);
                $el.css("display", "inline-block");
            } else {
                $el.css("display", "none");

                if (!$(".share-panel").is(":visible")) {
                    swal({
                        title: "Nobody to share to.",
                        text: "Click 'Connect Another Device' to allow other device to connect securely using a PIN.",
                        type: "warning"
                    });
                }
            }
        };

        $("#shareBtn").on("click", function () {
            hub.server.share();
            return false;
        });

        $(".cancelShareBtn").on("click", function () {
            hub.server.cancelShare();
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
                hub.server.eraseSession();

                swal("Erasing...", "You will be taken to the homepage when it's done.", "success");
            });

            return false;
        });

        EndToEndEncryption.initKeyExchange(hub);

        $.connection
            .hub
            .start()
            .done(function () {
                hub
                    .server
                    .connect($("#SessionId").val())
                    .then(function (socketConnectionId) {

                        $("#SocketConnectionId").val(socketConnectionId);

                        Log("Device Id: " + socketConnectionId);

                        app_busy(false);
                    });
            });
    },

    switchView: function (showSharePanel) {
        var $sharePanel = $(".share-panel");
        var $shareActions = $(".share-actions");

        if (showSharePanel) {
            $sharePanel.slideDown(500);
            $shareActions.slideUp(500);
        } else {
            $sharePanel.slideUp(500);
            $shareActions.slideDown(500);
        }
    },

    loadMessages: function (then) {
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

                $(response)
                    .hide()
                    .prependTo($("#conversation-list"))
                    .slideDown(250, function () {
                        JustSendingApp.decryptMessages();
                    });
                
                JustSendingApp.processTime();
                JustSendingApp.initViewSource();

                if (then != undefined) then();

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
                //$(itm).html(JustSendingApp.replaceURLWithHTMLLinks($(itm).text()));
                //$(itm)
                //    .removeClass("text")
                //    .addClass("text-d");

                var x = new EmbedJS({
                    input: $(itm)[0],
                    tweetsEmbed: true,
                    linkOptions: {
                        target: "_blank"
                    },
                    googleAuthKey: 'AIzaSyCqFouT8h5DKAbxlrTZmjXEmNBjC69f0ts',
                    inlineEmbed: 'all'
                });

                x.render();
                $(itm).addClass("embedded");
            });

        $('pre code').each(function (i, block) {

            var $el = $(block);
            $el.html(JustSendingApp.htmlDecode($el.html()));
            hljs.highlightBlock(block);

        });
    }
};

