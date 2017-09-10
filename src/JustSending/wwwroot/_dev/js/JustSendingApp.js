var JustSendingApp = {
    init: function () {
        this.loadMessages();
        this.initWebSocket();
        this.switchView(false);
        this.initFileShare();
        this.initAutoSizeComposer();

        $("*[title]").tooltip();

        var options = {
            success: function () {
                JustSendingApp.onSendComplete();
                $("#please-wait").slideUp(250);
            },
            beforeSubmit: function () {
                $("#please-wait").slideDown(250);
                $("#percent").text("");
                return true;
            },
            uploadProgress: function (e, pos, t, per) {
                $("#percent").text(per + "%");
            },

            resetForm: true
        };

        $("#form").ajaxForm(options);

    },
    initAutoSizeComposer: function () {
        autosize($("#ComposerText"));
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
            ajax_service.sendPostRequest("/a/message", data, function (data) {
                $cnt.val(data.content);
            });
        });
    },

    initFileShare: function () {
        var $file = $("#file");
        var $text = $("#ComposerText");
        var $clearBtn = $(".clearSelectedFileBtn");
        var $fileData = $("#fileData");
        var $form = $("#form");

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
            $text.val("");
            autosize.update($text);
            $fileData.val("");
            JustSendingApp.beforeSubmit = null;
            JustSendingApp.initAutoSizeComposer();
            return false;
        })
    },

    beforeSubmit: null,

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

        JustSendingApp.initKeyExchange(hub);

        $.connection
            .hub
            .start()
            .done(function () {
                hub
                    .server
                    .connect($("#SessionId").val())
                    .then(function (socketConnectionId) {
                        $("#SocketConnectionId").val(socketConnectionId);
                        l("Handshake: Id=" + socketConnectionId);

                    });
            });
    },

    initKeyExchange: function (hub) {

        EndToEndEncryption.initCallbacks(hub);

        hub.client.startKeyExchange = function (peerId) {

            l("Handshake: with peer: " + peerId);
            EndToEndEncryption.init(peerId, hub);

        }

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

    loadMessages: function () {
        var id = $("#SessionId").val();
        var id2 = $("#SessionVarification").val();

        ajax_service.sendRequest("POST",
            "/a/messages",
            { id: id, id2: id2 },
            function (response) {
                $("#conversation-list").html(response);
                JustSendingApp.convertLinks();
                JustSendingApp.processTime();
                JustSendingApp.initViewSource();
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
        $.each($(".msg .text p"),
            function (idx, itm) {

                if ($(itm).children().length)
                    return;
                //$(itm).html(JustSendingApp.replaceURLWithHTMLLinks($(itm).text()));
                $(itm)
                    .removeClass("text")
                    .addClass("text-d");

                var x = new EmbedJS({
                    input: $(itm)[0],
                    tweetsEmbed: true,
                    googleAuthKey: 'AIzaSyCqFouT8h5DKAbxlrTZmjXEmNBjC69f0ts',
                    inlineEmbed: 'all'
                });

                x.render();
            });

        $('pre code').each(function (i, block) {

            var $el = $(block);
            $el.html(JustSendingApp.htmlDecode($el.html()));
            hljs.highlightBlock(block);

        });
    },

    replaceURLWithHTMLLinks: function (text) {
        var re = /(\(.*?)?\b((?:https?|ftp|file):\/\/[-a-z0-9+&@#\/%?=~_()|!:,.;]*[-a-z0-9+&@#\/%=~_()|])/ig;
        return text.replace(re, function (match, lParens, url) {
            var rParens = '';
            lParens = lParens || '';

            // Try to strip the same number of right parens from url
            // as there are left parens.  Here, lParenCounter must be
            // a RegExp object.  You cannot use a literal
            //     while (/\(/g.exec(lParens)) { ... }
            // because an object is needed to store the lastIndex state.
            var lParenCounter = /\(/g;
            while (lParenCounter.exec(lParens)) {
                var m;
                // We want m[1] to be greedy, unless a period precedes the
                // right parenthesis.  These tests cannot be simplified as
                //     /(.*)(\.?\).*)/.exec(url)
                // because if (.*) is greedy then \.? never gets a chance.
                if (m = /(.*)(\.\).*)/.exec(url) ||
                    /(.*)(\).*)/.exec(url)) {
                    url = m[1];
                    rParens = m[2] + rParens;
                }
            }
            return lParens + "<a href='" + url + "'>" + url + "</a>" + rParens;
        });
    }
};

var EndToEndEncryption = {

    //
    // Diffie-Hellman key exchange specific
    //
    p: null,    // public prime-base
    g: null,    // public prime-modulus


    p_digits: 10,
    g_digits: 2,


    A: null, // public

    b: null,    // private 1
    b_digits: 2,

    a: null,    // private 2
    a_digits: 2,

    k: null,

    //
    // Application specific
    ///
    hub: null,
    peerId: null,

    init: function (peerId, hub) {
        var that = EndToEndEncryption;

        l("Handshake: Generating Primes");
        this.generatePrimes(function () {
            l("Handshake: p=" + that.p.toString(10));
            l("Handshake: g=" + that.g.toString(10));

            hub.server.callPeer(peerId, "setPrimes", [that.p.toString(10), that.g.toString(10)]);


            return;
            // send
            hub
                .server
                .setPrimes(peerId, that.p.toString(10), that.g.toString(10))
                .then(function (A) {

                    l("Handshake: Receive A=" + A);
                    
                    // receive A
                    
                    // compute B
                    this.b = new BigNumber(JustEncrypt.randomNumberOfLength(that.b_digits));
                    var B = that.g.pow(that.b).mod(that.p);
                    
                    l("Handshake: Computed, sending, B=" + B.toString(10));
                    // send B
                    // ask peer to compute secret
                    //
                    hub
                        .server
                        .computeSecret(peerId, B.toString(10))
                        .then(function () {
                            
                            // let me compute the same secret
                            // using A

                            that.A = new BigNumber(A);
                            that.k = that.A.pow(that.b).mod(that.p);

                            console.info("Handshake is done. K=" + that.k.toString(10));
                        });

                });
        })
    },

    initCallbacks: function (hub) {
        var that = EndToEndEncryption;
        //
        // Set Prime
        // Generate a,
        // return A
        //
        hub.client.setPrimes = function (p, g) {
            l("Handshake: Request Arrived");

            that.p = new BigNumber(p);
            that.g = new BigNumber(g);

            l("Handshake: p=" + that.p.toString(10));
            l("Handshake: g=" + that.g.toString(10));

            that.a = JustEncrypt.randomNumberOfLength(that.a_digits);
            var A = that.g.pow(that.a).mod(that.p);

            l("Handshake: Send A=" + A.toString(10));

            hub.server.callPeer()

            //return A.toString(10);
        };

        hub.client.computeSecret = function (B) {
            l("Handshake: Computing secret with B=" + B.toString(10));

            that.B = new BigNumber(B);
            that.k = that.B.pow(that.a).mod(that.p);

            console.info("Handshake is done. K=" + that.k.toString(10));
        };

        l("Handshake: Listening");
    },

    // Step 1
    //
    generatePrimes: function (then) {
        this.p = new BigNumber(JustEncrypt.primeOfDigit(this.p_digits));
        this.g = new BigNumber(JustEncrypt.primeOfDigit(this.g_digits));
        
        then();
    },
}