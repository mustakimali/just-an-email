var SecureLine = {
    sockConnection: null,
    hostname: "https://tnxfr.com",
    private_key: null,
    on_event: null,
    on_line_secured: null,
    on_line_disconnected: null,

    initiator: true,

    init: function (id, onEvent, onLineSecured) {
        if (id.length < 32) {
            throw "The id must be at least 32 character long unique shared between two client.";
        }

        if (typeof (onEvent) !== "function") {
            throw "onEvent must be a function with two arguments. => (event, data)";
        }

        if (typeof (onLineSecured) !== "function") {
            throw "onLineSecured callback function must be defined.";
        }

        var secure_line = this;

        secure_line.on_event = onEvent;
        secure_line.on_line_secured = onLineSecured;

        secure_line.ensureDependencies(function () {
            Log("Dependencies loaded");

            var ws = new signalR.HubConnectionBuilder()
                .withUrl(secure_line.hostname + "/signalr/secure-line")
                .build();

            secure_line.sockConnection = ws;

            ws.on("Boradcast", function (event, data) {
                Log(event);
                switch (event) {
                    case "Start":
                        EndToEndEncryption.initKeyExchange(ws);
                        break;

                    case "GONE":
                        if (typeof (SecureLine.on_line_disconnected) == "function") {
                            SecureLine.on_line_disconnected();
                        }
                        break;

                    case "GET":

                        if (!EndToEndEncryption.isEstablished()) {
                            throw "Message received before secure line was established.";
                        }

                        $.get(secure_line.hostname + "/secure-line/message?id=" + data, function (result) {

                            var messageObject = result;

                            try {
                                var event_decrypted = EndToEndEncryption.decrypt(messageObject.event, secure_line.private_key);
                                var data_decrypted = data == null ? "" : EndToEndEncryption.decrypt(messageObject.data, secure_line.private_key);

                                secure_line.on_event(event_decrypted, data_decrypted);
                            } catch (e) {
                                throw "Unrecognised message received: [" + event + "," + data + "]:> Error: " + e;
                            }

                        });

                        break;
                }
            });

            var onConnected = function () {
                Log("WebSocket connection established.");

                ws.send("Init", id);

            };
            ws.connection.onclose = function (msg) {
                Log("WebSocket connection closing...");
            };

            ws
                .start()
                .then(onConnected)
                .catch(function (err) {
                    Log("Error: " + err.toString());
                });


        });
    },

    onDone: function () {
        console.clear();
        this.private_key = this.sha256(EndToEndEncryption.private_key);

        Log("Secure Line has been established.");

        this.on_line_secured(this.initiator);
    },

    send: function (event, data) {
        if (!EndToEndEncryption.isEstablished()) {
            throw "Secure Line has not been established.";
        }

        if (event == "" || event == null) {
            throw "event must not be null or empty";
        }

        var event_encrypted = EndToEndEncryption.encrypt(event, this.private_key);
        var data_encrypted = data == null ? "" : EndToEndEncryption.encrypt(data, this.private_key);

        $.ajax({
            type: "POST",
            async: true,
            url: this.hostname + "/secure-line/message",
            headers: {
                "Content-Type": "application/json"
            },
            processData: false,
            data: JSON.stringify({
                Id: SecureLine.newGuid(),
                Data: JSON.stringify({
                    event: event_encrypted,
                    data: data_encrypted
                })
            }),
            success: function (response) {
                console.log(response);

                SecureLine.sockConnection.send("Broadcast", "GET", response, false);
            }
        });
    },

    sha256: function (data) {
        return sjcl.codec.hex.fromBits(sjcl.hash.sha256.hash(data));
    },

    newGuid: function () {
        function s4() {
            return Math.floor((1 + Math.random()) * 0x10000)
                .toString(16)
                .substring(1);
        }
        return s4() + s4() + s4() + s4() + s4() + s4() + s4() + s4();
    },

    ensureDependencies: function (then) {
        var app = this;

        app.includeJs(window.jQuery, "https://cdnjs.cloudflare.com/ajax/libs/jquery/1.9.1/jquery.min.js", "sha256-wS9gmOZBqsqWxgIVgA8Y9WcQOa7PgSIX+rPA0VL2rbQ=", function () {
            app.includeJs(window.signalR, "https://cdn.jsdelivr.net/npm/@aspnet/signalr@1.0.4/dist/browser/signalr.min.js", "sha256-51sQPE7Pj6aicVoT5i+HOaNgW7s+3i9xGiEUCwhVvVM=", function () {
                app.includeJs(window.BigNumber, "https://cdnjs.cloudflare.com/ajax/libs/bignumber.js/4.0.4/bignumber.min.js", "sha256-MomdVZFkolQP//Awk1YjBtrVF1Dehp9OlWj5au4owVo=", function () {
                    app.includeJs(window.sjcl, "https://cdnjs.cloudflare.com/ajax/libs/sjcl/1.0.7/sjcl.min.js", "sha256-dFf9Iqsg4FM3OHx2k9aK6ba1l28881fMWFjhMV9MB4c=", function () {
                        app.includeJs(window.EndToEndEncryption, app.hostname + "/js/JustEncrypt.js", null, function () {
                            then();
                        });
                    })
                })
            });
        });
    },

    includeJs: function (check, url, integrity, then) {
        if (check) {
            then();
            return;
        }

        var script = document.createElement('script');

        script.onload = function () {
            then();
        };

        script.src = url;
        if (integrity) {
            script.integrity = integrity;
            script.setAttribute("crossorigin", "anonymous");
        }

        document.head.appendChild(script);
        return;
    }

}

var JustSendingApp = {
    showStatus: function (status) {
        if (!status) {
            SecureLine.onDone();
            return;
        }
    }
}

function Log(message) {
    console.log(message);

    if (message.indexOf("Decrypting incoming") === 0) {
        SecureLine.initiator = false;
    }
}

function app_busy(busy) {
}
