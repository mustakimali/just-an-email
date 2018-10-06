var SecureLine = {
    init: function (id) {
        if (id.length < 32) {
            throw "The id must be at least 32 character long unique shared between two client.";
        }
        this.ensureSignalR(function () {

        });
    },

    ensureSignalR: function (then) {
        if (signalR) {
            then();
            return;
        }

        var script = document.createElement('script');

        script.onload = function () {
            then();
        };

        script.src = "https://ajax.aspnetcdn.com/ajax/signalr/jquery.signalr-2.2.1.min.js";
        script.integrity = "sha384-Ga+6DRkFqkoCM1rr0T7ZE1yKNe8hDhPxdJ4wJaG8jfKbEkYjfsT4uUVav4ZEp84M";
        script.crossorigin = "anonymous";

        document.head.appendChild(script);
        return;
    }

}
