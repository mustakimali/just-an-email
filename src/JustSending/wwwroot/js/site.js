var ajax_service = {
    sendRequest: function (method, serviceName, data, success, error, onLocalUrl, dataType, beforeRequest, afterResponse) {
        if (beforeRequest && typeof (beforeRequest) === "function") beforeRequest();

        Log("Ajax {0} Request to: {1}".format(method, serviceName));

        $.ajax({
            type: method,
            async: true,
            url: serviceName.toString(),
            dataType: (!onLocalUrl ? "JSON" : dataType),
            data: data,
            xhrFields: {
                withCredentials: true
            },
            success: function (response) {
                if (afterResponse && typeof (afterResponse) === "function") afterResponse();
                //l(response);
                if (success && typeof (success) === "function") {
                    success(response);
                }
            },
            error: function (jqXhr, textStatus, errorThrown) {
                showAjaxError(jqXhr, errorThrown);

                if (afterResponse && typeof (afterResponse) === "function") afterResponse();

                if (error && typeof (error) === "function") {
                    error(jqXhr, textStatus, errorThrown);
                }
            }
        });
    },

    sendPostRequest: function (serviceName, data, success, error, beforeRequest, afterResponse) {
        ajax_service.sendRequest("POST", serviceName, data, success, error, false, null, beforeRequest, afterResponse);
    },

    sendGetRequest: function (serviceName, data, success, error) {
        ajax_service.sendRequest("GET", serviceName, data, success, error, false);
    },

    loadLocalFile: function (serviceName, success, error, dataType) {
        if (dataType == null) dataType = "text";
        ajax_service.sendRequest("GET", serviceName, null, success, error, true, dataType);
    }
};

$(function () {
    $.ajaxSetup({
        error: function (jqXhr, textStatus, errorThrown) {
            showAjaxError(jqXhr, errorThrown);
        }
    });
});

function showAjaxError(jqXhr, errorThrown) {
    var message = errorThrown;

    if (jqXhr != null) {
        if (jqXhr.responseText !== "") {
            var status = jqXhr.status;
            var errorMessage = "Unknown Error.";

            // Parse Web API Error
            try {
                var responseTextObject = $.parseJSON(jqXhr.responseText);

                if (responseTextObject.ExceptionMessage != null)
                    errorMessage = responseTextObject.ExceptionMessage;
                else if (responseTextObject.Message != null)
                    errorMessage = responseTextObject.Message;

                message = "{0}: {1}\n{2}".format(status, message, errorMessage);
            } catch (ex) {
                console.log("Error: " + ex + ": " + jqXhr.responseText);
            }

            // Parse ASP.NET Error
            errorMessage = $($(jqXhr.responseText)[1]).text();
            message = "{0}: {1}\n{2}".format(status, message, errorMessage);

        } else if (message === "" && jqXhr.responseText != null) {
            message = jqXhr.responseText;
        } else if (message === "") {
            message = "Unknown Error!";
        }

        // Status code specific
        if (jqXhr.status === 401) {
            message = "Can not login, invalid user name and password.\n" + message;
        }
    }

    console.error(jqXhr);
    swal({
        title: "Unexpected Error",
        text: message + "\r\nPlease try refreshing this page.",
        type: "warning"
    });
}

function Log(text) {
    if (is_dev())
        console.log(text);
}

function is_dev() {
    return window.location.href.indexOf("localhost") > 0 || window.location.href.indexOf("show_log") > 0;
}

function app_busy(show) {
    if (show)
        $("body").addClass("busy");
    else
        $("body").removeClass("busy");
}

function onLoad(code) {
    window.onload = code()
}

function showNoscripts($) {
    var el = document.getElementById("new-session");
    el.href = "/app/lite";
    el.innerText += "*";
}

function hasjQuery() {
    try {
        var dno = window.$;
        return true;
    } catch (ex) {
        return false;
    }
}

function execute_xhr(method, url, onSuccess, data) {
    var xhr = new XMLHttpRequest();

    xhr.open(method, url, true);
    xhr.onload = function () {
        onSuccess(xhr.responseText);
    }

    if (data != undefined)
        xhr.send(data);
    else
        xhr.send();
}

function get(id) {
    return document.getElementById(id);
}

String.prototype.format = function () {
    var args = arguments;
    return this.replace(/{(\d+)}/g, function (match, number) {
        return typeof args[number] != "undefined"
            ? args[number]
            : match;
    });
};