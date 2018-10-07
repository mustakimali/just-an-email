var SecureLine = {
    init: function (id) {
        if (id.length < 32) {
            throw "The id must be at least 32 character long unique shared between two client.";
        }
        this.ensureDependencies(function () {
            Log("Dependencies loaded");

            var ws = new signalR.HubConnectionBuilder()
                .withUrl("/signalr/secure-line")
                .build();
            
            ws.on("Broadcast", function (data) {
                Log("Received: " + data);
            })

            var onConnected = function () {
                Log("WebSocket connection established.");

                ws.send("Init", id);

            };
            ws.connection.onclose = function(msg) {
                Log("WebSocket connection closing...");
            };

            ws
                .start()
                .then(onConnected)
                .catch(function(err) {
                    Log("Error: " + err.toString());
                });

            
        });
    },

    ensureDependencies: function (then) {
        var app = this;

        app.includeJs(window.jQuery, "https://cdnjs.cloudflare.com/ajax/libs/jquery/1.9.1/jquery.min.js", "sha256-wS9gmOZBqsqWxgIVgA8Y9WcQOa7PgSIX+rPA0VL2rbQ=", function () {
            app.includeJs(window.signalR, "https://cdn.jsdelivr.net/npm/@aspnet/signalr@1.0.0/dist/browser/signalr.min.js", "sha256-DeOex/tR7FzkV208FN2wnFJvIUIKXWsVjbW0171naJo=", function () {
                app.includeJs(window.BigNumber, "https://cdnjs.cloudflare.com/ajax/libs/bignumber.js/4.0.4/bignumber.min.js", "sha256-MomdVZFkolQP//Awk1YjBtrVF1Dehp9OlWj5au4owVo=", function () {
                    app.includeJs(window.sjcl, "https://cdnjs.cloudflare.com/ajax/libs/sjcl/1.0.7/sjcl.min.js", "sha256-dFf9Iqsg4FM3OHx2k9aK6ba1l28881fMWFjhMV9MB4c=", function () {
                        then();
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
        script.integrity = integrity;
        script.setAttribute("crossorigin", "anonymous");

        document.head.appendChild(script);
        return;
    }

}

function Log(message) {
    console.log(message);
}
