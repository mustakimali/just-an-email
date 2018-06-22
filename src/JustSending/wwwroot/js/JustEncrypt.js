"use strict";

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

    // true - mean this device was in charge of
    // initiating connection with a new device
    //
    initiator: false,

    private_key: null,
    public_key_alias: null,

    keys: [],
    keys_hash: {},

    isEstablished: function () {
        return this.keys.length > 0;
    },

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
        hub.on("startKeyExchange", function(peerId, p, g, pka, initiate) {
            app_busy(true);

            Log("Peer Id: " + peerId);

            EndToEndEncryption.init(peerId, hub, p, g, pka, initiate);

        });

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

        that.showStatus("");

        that.peerId = peerId;
        that.hub = hub;
        that.public_key_alias = pka;
        that.initiator = initiate;

        that.setPrime(p, g);

        if (initiate) {
            setTimeout(function () {
                that.computeA();
            }, 500);

        }
    },
    showStatus: function (msg) {
        if (msg == undefined) {
            JustSendingApp.showStatus();
            return;
        }

        JustSendingApp.showStatus("<i class=\"fa fa-lock\"></i> Establishing secure connection..." + msg);
    },

    initCallbacks: function (hub) {
        var that = EndToEndEncryption;

        hub.on("callback", function(method, data) {

            Log("Request received [" + method + "] -> Payload Size: " + data.length);

            var dataObj = JSON.parse(data);
            switch (method) {
            case "ComputeB":
                that.computeB(dataObj.A);
                break;

            case "ComputeK":
                that.computeK(dataObj.B);
                break;

            case "broadcastKeys":
                that.showStatus("Decrypting keys");
                that.receiveKeys(dataObj);
                break;
            }

        });

        Log("Ready to shake hands.");
    },

    setPrime: function (p, g) {
        var that = EndToEndEncryption;

        Log("p LEN " + p.length);
        Log("g LEN " + g.length);

        that.p = new BigNumber(p);
        that.g = new BigNumber(g);
    },

    //
    // Generate a,
    // return A
    //
    computeA: function () {
        var that = EndToEndEncryption;

        Log("computeA: Working...");

        that.a = PrimeHelper.randomNumberOfLength(that.a_digits);
        var A = that.g.pow(that.a).mod(that.p);

        Log("Send A");

        that.callPeer("ComputeB", { A: A.toString(10) });
    },

    computeB: function (A) {
        var that = EndToEndEncryption;

        // compute B
        this.b = new BigNumber(PrimeHelper.randomNumberOfLength(that.b_digits));
        var B = that.g.pow(that.b).mod(that.p);

        Log("computeB: Computed, sending");
        // send B
        // ask peer to compute secret
        that.callPeer("ComputeK", { B: B.toString(10) });

        // generate my secret
        that.computeK_withA(A);
    },

    computeK: function (B) {
        var that = EndToEndEncryption;
        that.B = new BigNumber(B);

        Log("Computing secret with B");

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
        this.callPeerInternal(this.peerId, method, data);
    },

    callPeerInternal: function (peerId, method, data) {
        var that = EndToEndEncryption;
        Log("Calling peer '" + method + "' [" + peerId + "]");

        that
            .hub
            .send("callPeer", peerId, method, JSON.stringify(data))
            .catch(function(e) {
                Log("ERROR: " + e);
            })
            .then(function(b, d, e) {
                // Request sent
            });
    },

    callAllPeers: function (method, data) {
        this.callPeerInternal("ALL", method, data);
    },

    onHandshakeDone: function () {

        this.p = null;
        this.g = null;
        this.a = null;
        this.b = null;
        this.private_key = this.k.toString(10)
        this.k = null;

        $("#EncryptionPublicKeyAlias").val(this.public_key_alias);

        this.keys.push({ Key: this.public_key_alias, Secret: this.private_key });
        this.keys_hash[this.public_key_alias] = this.private_key;

        console.clear();
        Log("Handshake is done");

        //
        // Securely bradcast all keys with all peers
        // Private Key Exchange! HOW!!
        // For each private key, Encrypt the entire key store with the private key itself
        // Each peer receiving thse must have at least one private key already known
        // (identified by the public key alias which is also incuded in plaintext)
        // to be able to decrypt them in order to receive all secrets.
        // How cool is that? (:
        //
        if (!this.initiator) {
            var encKeys = [];
            var keysJson = JSON.stringify(this.keys);
            for (var i in this.keys) {
                var k = this.keys[i];
                encKeys.push({
                    // Pubilc Key Alias (unencrypted)
                    Key: k.Key,
                    // Private Key (Encrypted)
                    EncryptedSecrets: EndToEndEncryption.encrypt(keysJson, k.Secret)
                });
            }

            this.callAllPeers("broadcastKeys", encKeys);
            this.printKeyStoreStats();
        }

        this.initiator = false;
        app_busy(false);
    },

    generateOwnPrivateKey: function (then) {
        this.public_key_alias = PrimeHelper.randomNumberOfLength(32);
        this.private_key = PrimeHelper.randomNumberOfLength(1026);

        $("#EncryptionPublicKeyAlias").val(this.public_key_alias);

        this.keys.push({ Key: this.public_key_alias, Secret: this.private_key });
        this.keys_hash[this.public_key_alias] = this.private_key;

        this.printKeyStoreStats();

        then(this.public_key_alias);
    },

    receiveKeys: function (dataObj) {
        for (var i in dataObj) {
            var nk = dataObj[i];

            for (var j in this.keys) {
                var ik = this.keys[j];

                if (ik.Key == nk.Key) {
                    // I have the private key
                    console.clear();
                    Log("Decrypting incoming private keys using: \r\n\t\t" + ik.Key);

                    this.updateKeyStore(ik.Secret, nk.EncryptedSecrets);
                    return;
                }
            }
        }
    },

    updateKeyStore: function (secret, data) {
        var incomingKeyStore = JSON.parse(this.decrypt(data, secret));

        this.keys = incomingKeyStore;
        this.keys_hash = {};
        for (var i = 0; i < incomingKeyStore.length; i++) {
            var c = incomingKeyStore[i];
            this.keys_hash[c.Key] = c.Secret;
        }
        this.printKeyStoreStats();
    },

    printKeyStoreStats: function () {
        Log("Total Keys: " + this.keys.length);
        Log("Known Keys: \r\n\t\t" + this.keys.map(function (t) { return t.Key + (t.Key == EndToEndEncryption.public_key_alias ? " (In Use)" : ""); }).join("\r\n\t\t"));

        this.showStatus();
    },

    encrypt: function (data, secret) {
        return sjcl.encrypt(secret, data);
    },

    encryptWithPrivateKey: function (data) {
        return this.encrypt(data, this.private_key);
    },

    decrypt: function (data, secret) {
        return sjcl.decrypt(secret, data);
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
