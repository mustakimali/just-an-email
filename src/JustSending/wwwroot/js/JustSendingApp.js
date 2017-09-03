$(function() {
    JustSendingApp.init();
    $("*[title]").tooltip();

    var options = {
        success: function() {
            JustSendingApp.onSendComplete();
            $("#please-wait").slideUp(250);
        },
        beforeSubmit: function() {
            $("#please-wait").slideDown(250);
            $("#percent").text("");
            return true;
        },
        uploadProgress: function(e, pos, t, per) {
            $("#percent").text(per + "%");
        },

        resetForm: true
    };

    $("#form").ajaxForm(options);
});

var JustSendingApp = {
    init: function () {
        this.loadMessages();
        this.initWebSocket();
        this.switchView(false);
        this.initFileShare();
        this.initAutoSizeComposer();
        
    },
    initAutoSizeComposer: function () {
        autosize($('textarea'));
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
                $cnt.html(data.content);
            });
        });
    },

    initFileShare: function () {
        var $file = $("#file");
        var $text = $("#ComposerText");
        var $clearBtn = $("#clearSelectedFileBtn");
        var $fileData = $("#fileData");
        var $form = $("#form");

        $text.keypress(function (e) {
            if ((e.which == 13 || e.which == 10) && e.ctrlKey) {
                $('#sendBtn').trigger("click");
                return false;
            }
            return true;
        });

        $file.on("change", function () {
            if ($file[0].files.length > 0) {
                var maxFileSize = parseInt($form.data("max-b"));
                var file = $file[0].files[0];

                if (file.size > maxFileSize) {
                    var maxFileSizeMb = parseInt($form.data("max-mb"));
                    swal({
                        title: "Maximum " + maxFileSizeMb + "MB",
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

        $("#selectFileBtn").on("click", function () {
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

                window.onbeforeunload = null;
                window.onunload = null;
            } else {
                $el.css("display", "none");

                if (!$(".share-panel").is(":visible")) {
                    swal({
                        title: "Nobody to share to.",
                        text: "Click 'Connect Another Device' to allow other device to connect securely using a PIN.",
                        type: "warning"
                    });
                }

                window.onbeforeunload = function () {
                    return true;
                };
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

        $.connection
            .hub
            .start()
            .done(function () {
                hub.server.connect($("#SessionId").val());
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

    loadMessages: function () {
        var id = $("#SessionId").val();
        var id2 = $("#SessionVarification").val();

        ajax_service.sendRequest("POST",
            "/a/messages?id={0}&id2={1}".format(id, id2),
            null,
            function (response) {
                $("#conversation-list").html(response);
                JustSendingApp.convertLinks();
                JustSendingApp.initViewSource();
            },
            true,
            "text/html");
    },

    onSendComplete: function () {
        $("#ComposerText").select();
        $("#clearSelectedFileBtn").trigger("click");
        JustSendingApp.convertLinks();
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
        
            $('pre code').each(function(i, block) {
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