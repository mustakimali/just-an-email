"use strict"

var EndToEndEncryption = {

    //
    // Diffie-Hellman key exchange specific
    //
    p: null,    // public prime-base (server generated)
    g: null,    // public prime-modulus (server generated)

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

    private_key: null,
    public_key_alias: null,

    keys: [],

    /**
     * Initialise Key Exchance
     * Listen for command from the server 
     * @param {hub} hub 
     */
    initKeyExchange: function (hub) {
        //
        // Register event handler
        this.initCallbacks(hub);

        //
        // SERVER CALLS:
        //   Start the key exchange
        //
        hub.client.startKeyExchange = function (peerId, p, g, pka, initiate) {

            Log("Peer Id: " + peerId);

            EndToEndEncryption.init(peerId, hub, p, g, pka, initiate);

        }

    },

    /**
     * Start the Key Exchange process
     * 
     * @param {string} peerId the ID of the other deive
     * @param {any} hub 
     * @param {string} p prime key (public key), 1024 digits
     * @param {string} g prime base, 2 digits
     * @param {string} pka the (much shorter) alias of the public key, to be used to identify message encrypted with associated private key
     * @param {boolean} initiate true to start the computation, only one client will receive this 'true'
     */
    init: function (peerId, hub, p, g, pka, initiate) {
        
        var that = EndToEndEncryption;

        that.peerId = peerId;
        that.hub = hub;
        that.public_key_alias = pka;

        that.setPrime(p, g);

        if (initiate) {
            that.computeA();
        }
    },

    initCallbacks: function (hub) {
        var that = EndToEndEncryption;

        hub.client.callback = function (method, data) {

            Log("Request received [" + method + "] ->" + data);

            switch (method) {
                case "ComputeB":
                    that.computeB(JSON.parse(data).A);
                    break;

                case "ComputeK":
                    that.computeK(JSON.parse(data).B);
                    break;
            }

        };

        Log("Listening");
    },

    setPrime: function (p, g) {
        var that = EndToEndEncryption;

        Log("p=" + p);
        Log("g=" + g);

        that.p = new BigNumber(p);
        that.g = new BigNumber(g);
    },

    //
    // Generate a,
    // return A
    //
    computeA: function () {
        var that = EndToEndEncryption;

        Log("Request Arrived");

        that.a = PrimeHelper.randomNumberOfLength(that.a_digits);
        var A = that.g.pow(that.a).mod(that.p);

        Log("Send A=" + A.toString(10));

        that.callPeer("ComputeB", { A: A.toString(10) });
    },

    computeB: function (A) {
        var that = EndToEndEncryption;

        // compute B
        this.b = new BigNumber(PrimeHelper.randomNumberOfLength(that.b_digits));
        var B = that.g.pow(that.b).mod(that.p);

        Log("Computed, sending, B=" + B.toString(10));
        // send B
        // ask peer to compute secret
        that.callPeer("ComputeK", { B: B.toString(10) });

        // generate my secret
        that.computeK_withA(A);
    },

    computeK: function (B) {
        var that = EndToEndEncryption;
        that.B = new BigNumber(B);

        Log("Computing secret with B=" + that.B.toString(10));

        that.k = that.B.pow(that.a).mod(that.p);

        this.onHandshakeDone();
    },

    computeK_withA: function (A) {
        var that = EndToEndEncryption;

        var A = new BigNumber(A);
        that.k = A.pow(that.b).mod(that.p);

        this.onHandshakeDone();
    },

    callPeer: function (method, data) {
        var that = EndToEndEncryption;
        Log("Calling peer '" + method + "' [" + that.peerId + "]");

        that
            .hub
            .server
            .callPeer(that.peerId, method, JSON.stringify(data))
            .catch(function (e) {
                Log("ERROR: " + e);
            })
            .then(function (b, d, e) {
                // Request sent
            });
    },

    onHandshakeDone: function () {
        
        this.p = null;
        this.g = null;
        this.a = null;
        this.b = null;
        this.private_key = this.k.toString(10)
        this.k = null;

        $("#EncryptionPublicKeyAlias").val(this.public_key_alias);

        this.keys[this.public_key_alias] = this.private_key;

        Log("Handshake is done. K=" + this.private_key);
        console.clear();
    }
}

var PrimeHelper = {

    randomNumberSingle: function () {
        return parseInt(Math.random() * 9);

    },

    randomNumberOfLength: function (digit) {
        var s = [];
        for (var i = 1; i <= digit; i++) {
            s.push(this.randomNumberSingle());
        }
        return s.join("");
    },

    startFrom: new BigNumber(2),

    isPrime: function (num) {
        if (typeof (num) != "object")
            num = new BigNumber(num);

        for (var i = this.startFrom; i.lessThan(num.squareRoot()); i = i.add(1)) {
            if (num.div(i).isInteger())
                return false;
        }
        return num.greaterThan(1);
    },

    primeOfDigit: function (digit) {
        return this.nextRandomPrimeAfter(this.randomNumberOfLength(digit));
    },

    nextRandomPrimeAfter: function (startFrom) {
        if (typeof (startFrom) != "object")
            startFrom = new BigNumber(startFrom);

        var start = startFrom.add(1);

        while (!this.isPrime(start)) {
            start = start.add(1);
        }

        return start.toString(10);
    }
}
