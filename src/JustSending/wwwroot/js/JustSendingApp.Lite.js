/* Vanilla JS only */
"use strict";
(function () {
    JustSendingApp.processTime();
    JustSendingApp.initAutoSizeComposer();

    setInterval(function () {
        pollNewMessage();
    }, 5000);

    get("file").onchange = function (evt) {
        var message = get("ComposerText");

        if (message.value != null) {
            message.value = evt.target.files[0].name;
        }
    };
})();

function pollNewMessage() {
    var messageElements = document.getElementsByClassName("msg-c");

    var refresh = get("refresh");

    refresh.classList.remove("updating");
    refresh.classList.remove("new");

    var id = get("SessionId").value;
    var id2 = get("SessionVerification").value;
    var seq = messageElements.length == 0
        ? 0
        : messageElements[0].attributes["data-seq"].value;

    var data = new FormData();

    data.append("id", id);
    data.append("id2", id2);
    data.append("from", seq);

    execute_xhr("POST", "/app/lite/poll", function (response) {
        onPollSuccess(refresh, response);
    }, data);
}

function onPollSuccess(refresh, responseString) {
    refresh.classList.add("updating");

    var response = JSON.parse(responseString);

    if (!response.hasSession) {
        location.href = "/";
        return;
    }

    if (response.messageHtml) {
        var dest = get("conversation-list");

        dest.innerHTML = response.messageHtml + dest.innerHTML;

        JustSendingApp.processTime();
        refresh.classList.add("new");
    }

    var tokenPanel = get("panel-token");
    var connectPanel = get("panel-connect");
    var token = get("token");

    if (response.hasToken) {
        tokenPanel.style.display = "block";
        connectPanel.style.display = "none";
        token.innerText = response.token;
    }
    else {
        tokenPanel.style.display = "none";
        connectPanel.style.display = "block";
    }
}