"use strict";

var EndToEndEncryption = {

    //
    // Diffie-Hellman key exchange specific (used only for secure private key sharing between peers)
    //
    p: null,    // public prime-base (server generated)
    g: null,    // public prime-modulus (server generated)

    b: null,    // private 1
    b_digits: 2,

    a: null,    // private 2
    a_digits: 2,

    k: null,    // DH shared secret (used to encrypt private keys during peer sharing)

    //
    // Application specific
    //
    hub: null,
    peerId: null,

    // true - mean this device was in charge of
    // initiating connection with a new device
    //
    initiator: false,

    // RSA key pair for this session
    publicKey: null,      // CryptoKey object
    privateKey: null,     // CryptoKey object
    publicKeyJwk: null,   // Exported public key as JWK (can be shared)
    privateKeyJwk: null,  // Exported private key as JWK
    public_key_alias: null,

    // Key store: maps alias -> { publicKey, privateKey, publicKeyJwk, privateKeyJwk }
    keys: [],
    keys_hash: {},

    isEstablished: function () {
        return this.keys.length > 0;
    },

    /**
     * Generate RSA key pair using Web Crypto API
     */
    generateRsaKeyPair: async function () {
        var keyPair = await crypto.subtle.generateKey(
            {
                name: "RSA-OAEP",
                modulusLength: 2048,
                publicExponent: new Uint8Array([1, 0, 1]),
                hash: "SHA-256"
            },
            true,  // extractable
            ["encrypt", "decrypt"]
        );

        return keyPair;
    },

    /**
     * Export key to JWK format
     */
    exportKeyToJwk: async function (key) {
        return await crypto.subtle.exportKey("jwk", key);
    },

    /**
     * Import public key from JWK
     */
    importPublicKeyFromJwk: async function (jwk) {
        return await crypto.subtle.importKey(
            "jwk",
            jwk,
            { name: "RSA-OAEP", hash: "SHA-256" },
            true,
            ["encrypt"]
        );
    },

    /**
     * Import private key from JWK
     */
    importPrivateKeyFromJwk: async function (jwk) {
        return await crypto.subtle.importKey(
            "jwk",
            jwk,
            { name: "RSA-OAEP", hash: "SHA-256" },
            true,
            ["decrypt"]
        );
    },

    /**
     * Hybrid encrypt: Generate AES key, encrypt data with AES, encrypt AES key with RSA
     * Returns: { encryptedKey: base64, iv: base64, encryptedData: base64 }
     */
    hybridEncrypt: async function (data, publicKey) {
        // Generate random AES key
        var aesKey = await crypto.subtle.generateKey(
            { name: "AES-GCM", length: 256 },
            true,
            ["encrypt", "decrypt"]
        );

        // Generate IV
        var iv = crypto.getRandomValues(new Uint8Array(12));

        // Encrypt data with AES
        var dataBytes = typeof data === 'string'
            ? new TextEncoder().encode(data)
            : data;

        var encryptedData = await crypto.subtle.encrypt(
            { name: "AES-GCM", iv: iv },
            aesKey,
            dataBytes
        );

        // Export and encrypt AES key with RSA
        var aesKeyRaw = await crypto.subtle.exportKey("raw", aesKey);
        var encryptedKey = await crypto.subtle.encrypt(
            { name: "RSA-OAEP" },
            publicKey,
            aesKeyRaw
        );

        return {
            encryptedKey: this.arrayBufferToBase64(encryptedKey),
            iv: this.arrayBufferToBase64(iv),
            encryptedData: this.arrayBufferToBase64(encryptedData)
        };
    },

    /**
     * Hybrid decrypt: Decrypt AES key with RSA, decrypt data with AES
     */
    hybridDecrypt: async function (encryptedObj, privateKey) {
        var encryptedKey = this.base64ToArrayBuffer(encryptedObj.encryptedKey);
        var iv = this.base64ToArrayBuffer(encryptedObj.iv);
        var encryptedData = this.base64ToArrayBuffer(encryptedObj.encryptedData);

        // Decrypt AES key with RSA
        var aesKeyRaw = await crypto.subtle.decrypt(
            { name: "RSA-OAEP" },
            privateKey,
            encryptedKey
        );

        // Import AES key
        var aesKey = await crypto.subtle.importKey(
            "raw",
            aesKeyRaw,
            { name: "AES-GCM" },
            false,
            ["decrypt"]
        );

        // Decrypt data with AES
        var decryptedData = await crypto.subtle.decrypt(
            { name: "AES-GCM", iv: iv },
            aesKey,
            encryptedData
        );

        return decryptedData;
    },

    /**
     * Helper: ArrayBuffer to Base64
     */
    arrayBufferToBase64: function (buffer) {
        var bytes = new Uint8Array(buffer);
        var binary = '';
        for (var i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    },

    /**
     * Helper: Base64 to ArrayBuffer
     */
    base64ToArrayBuffer: function (base64) {
        var binary = atob(base64);
        var bytes = new Uint8Array(binary.length);
        for (var i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes.buffer;
    },

    /**
     * Initialise Key Exchange
     * Listen for command from the server
     * @param {hub} hub
     */
    initKeyExchange: function (hub) {
        this.initCallbacks(hub);

        hub.on("startKeyExchange", function(peerId, p, g, pka, initiate) {
            app_busy(true);
            Log("Peer Id: " + peerId);
            EndToEndEncryption.init(peerId, hub, p, g, pka, initiate);
        });
    },

    /**
     * Start the Key Exchange process
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
        this.b = new BigNumber(PrimeHelper.randomNumberOfLength(that.b_digits));
        var B = that.g.pow(that.b).mod(that.p);
        Log("computeB: Computed, sending");
        that.callPeer("ComputeK", { B: B.toString(10) });
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

        that.hub
            .send("callPeer", peerId, method, JSON.stringify(data))
            .catch(function(e) { Log("ERROR: " + e); })
            .then(function(b, d, e) { });
    },

    callAllPeers: function (method, data) {
        this.callPeerInternal("ALL", method, data);
    },

    onHandshakeDone: async function () {
        var that = this;
        var dhSecret = this.k.toString(10);

        this.p = null;
        this.g = null;
        this.a = null;
        this.b = null;
        this.k = null;

        $("#EncryptionPublicKeyAlias").val(this.public_key_alias);

        // Generate RSA key pair for this session
        var keyPair = await this.generateRsaKeyPair();
        this.publicKey = keyPair.publicKey;
        this.privateKey = keyPair.privateKey;
        this.publicKeyJwk = await this.exportKeyToJwk(keyPair.publicKey);
        this.privateKeyJwk = await this.exportKeyToJwk(keyPair.privateKey);

        // Store in key store
        var keyEntry = {
            Key: this.public_key_alias,
            PublicKeyJwk: this.publicKeyJwk,
            PrivateKeyJwk: this.privateKeyJwk,
            DhSecret: dhSecret  // Used for encrypting private keys during broadcast
        };

        this.keys.push(keyEntry);
        this.keys_hash[this.public_key_alias] = keyEntry;

        console.clear();
        Log("Handshake is done - RSA key pair generated");

        // Upload public key to server
        this.uploadPublicKey(this.public_key_alias, this.publicKeyJwk);

        // Broadcast keys to all peers (encrypted with DH secret)
        if (!this.initiator) {
            var encKeys = [];
            var keysJson = JSON.stringify(this.keys.map(function(k) {
                return {
                    Key: k.Key,
                    PublicKeyJwk: k.PublicKeyJwk,
                    PrivateKeyJwk: k.PrivateKeyJwk
                };
            }));

            for (var i in this.keys) {
                var k = this.keys[i];
                if (k.DhSecret) {
                    encKeys.push({
                        Key: k.Key,
                        EncryptedSecrets: this.encryptSymmetric(keysJson, k.DhSecret)
                    });
                }
            }

            this.callAllPeers("broadcastKeys", encKeys);
            this.printKeyStoreStats();
        }

        this.initiator = false;
        app_busy(false);
    },

    publicKeyId: null,

    /**
     * Upload public key to server for CLI access
     */
    uploadPublicKey: function (alias, publicKeyJwk) {
        var sessionId = $("#SessionId").val();
        if (!sessionId) return;

        var self = this;
        fetch('/app/key', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                sessionId: sessionId,
                alias: alias,
                publicKey: JSON.stringify(publicKeyJwk)
            })
        })
        .then(function(response) { return response.json(); })
        .then(function(data) {
            if (data.id) {
                self.publicKeyId = data.id;
                $(".PublicKeyUrl").text(location.protocol + "//" + location.host + "/k/" + data.id);
            }
        })
        .catch(function(e) {
            console.error('Failed to upload public key:', e);
        });
    },

    /**
     * Generate own key pair (when no peer is connected yet)
     */
    generateOwnPrivateKey: async function (then) {
        this.public_key_alias = PrimeHelper.randomNumberOfLength(32);

        // Generate RSA key pair
        var keyPair = await this.generateRsaKeyPair();
        this.publicKey = keyPair.publicKey;
        this.privateKey = keyPair.privateKey;
        this.publicKeyJwk = await this.exportKeyToJwk(keyPair.publicKey);
        this.privateKeyJwk = await this.exportKeyToJwk(keyPair.privateKey);

        $("#EncryptionPublicKeyAlias").val(this.public_key_alias);

        var keyEntry = {
            Key: this.public_key_alias,
            PublicKeyJwk: this.publicKeyJwk,
            PrivateKeyJwk: this.privateKeyJwk
        };

        this.keys.push(keyEntry);
        this.keys_hash[this.public_key_alias] = keyEntry;

        // Upload public key to server
        this.uploadPublicKey(this.public_key_alias, this.publicKeyJwk);

        this.printKeyStoreStats();
        then(this.public_key_alias);
    },

    receiveKeys: async function (dataObj) {
        for (var i in dataObj) {
            var nk = dataObj[i];

            for (var j in this.keys) {
                var ik = this.keys[j];

                if (ik.Key == nk.Key && ik.DhSecret) {
                    console.clear();
                    Log("Decrypting incoming private keys using: \r\n\t\t" + ik.Key);
                    await this.updateKeyStore(ik.DhSecret, nk.EncryptedSecrets);
                    return;
                }
            }
        }
    },

    updateKeyStore: async function (dhSecret, encryptedData) {
        var decryptedJson = this.decryptSymmetric(encryptedData, dhSecret);
        var incomingKeyStore = JSON.parse(decryptedJson);

        // Import all keys
        for (var i = 0; i < incomingKeyStore.length; i++) {
            var k = incomingKeyStore[i];

            // Check if we already have this key
            if (this.keys_hash[k.Key]) continue;

            var keyEntry = {
                Key: k.Key,
                PublicKeyJwk: k.PublicKeyJwk,
                PrivateKeyJwk: k.PrivateKeyJwk
            };

            // Import the CryptoKey objects
            if (k.PublicKeyJwk) {
                keyEntry.PublicKey = await this.importPublicKeyFromJwk(k.PublicKeyJwk);
            }
            if (k.PrivateKeyJwk) {
                keyEntry.PrivateKey = await this.importPrivateKeyFromJwk(k.PrivateKeyJwk);
            }

            this.keys.push(keyEntry);
            this.keys_hash[k.Key] = keyEntry;
        }

        this.printKeyStoreStats();
    },

    printKeyStoreStats: function () {
        Log("Total Keys: " + this.keys.length);
        Log("Known Keys: \r\n\t\t" + this.keys.map(function (t) {
            return t.Key + (t.Key == EndToEndEncryption.public_key_alias ? " (In Use)" : "");
        }).join("\r\n\t\t"));
        this.showStatus();
    },

    /**
     * Symmetric encryption using CryptoJS AES (for DH-encrypted key sharing)
     */
    encryptSymmetric: function (data, secret) {
        return CryptoJS.AES.encrypt(data, secret).toString();
    },

    /**
     * Symmetric decryption using CryptoJS AES
     */
    decryptSymmetric: function (data, secret) {
        return CryptoJS.AES.decrypt(data, secret).toString(CryptoJS.enc.Utf8);
    },

    /**
     * Encrypt with public key (hybrid encryption)
     * Returns base64 encoded JSON string
     */
    encryptWithPublicKey: async function (data) {
        if (!this.publicKey) {
            throw new Error("No public key available");
        }
        var encrypted = await this.hybridEncrypt(data, this.publicKey);
        return btoa(JSON.stringify(encrypted));
    },

    /**
     * Encrypt with a specific key alias's public key
     */
    encryptWithKeyAlias: async function (data, alias) {
        var keyEntry = this.keys_hash[alias];
        if (!keyEntry) {
            throw new Error("Key not found: " + alias);
        }

        var publicKey = keyEntry.PublicKey;
        if (!publicKey && keyEntry.PublicKeyJwk) {
            publicKey = await this.importPublicKeyFromJwk(keyEntry.PublicKeyJwk);
            keyEntry.PublicKey = publicKey;
        }

        if (!publicKey) {
            throw new Error("No public key for alias: " + alias);
        }

        var encrypted = await this.hybridEncrypt(data, publicKey);
        return btoa(JSON.stringify(encrypted));
    },

    /**
     * Decrypt with private key (hybrid decryption)
     */
    decrypt: async function (encryptedBase64, alias) {
        var keyEntry = this.keys_hash[alias];
        if (!keyEntry) {
            console.warn('No key found for alias:', alias);
            return null;
        }

        var privateKey = keyEntry.PrivateKey;
        if (!privateKey && keyEntry.PrivateKeyJwk) {
            privateKey = await this.importPrivateKeyFromJwk(keyEntry.PrivateKeyJwk);
            keyEntry.PrivateKey = privateKey;
        }

        if (!privateKey) {
            console.warn('No private key for alias:', alias);
            return null;
        }

        try {
            var encryptedObj = JSON.parse(atob(encryptedBase64));
            var decrypted = await this.hybridDecrypt(encryptedObj, privateKey);
            return new TextDecoder().decode(decrypted);
        } catch (error) {
            console.error('Decryption failed:', error);
            return null;
        }
    },

    /**
     * Decrypt file content (returns ArrayBuffer)
     */
    decryptFile: async function (encryptedBase64, alias) {
        var keyEntry = this.keys_hash[alias];
        if (!keyEntry) {
            console.warn('No key found for alias:', alias);
            return null;
        }

        var privateKey = keyEntry.PrivateKey;
        if (!privateKey && keyEntry.PrivateKeyJwk) {
            privateKey = await this.importPrivateKeyFromJwk(keyEntry.PrivateKeyJwk);
            keyEntry.PrivateKey = privateKey;
        }

        if (!privateKey) {
            console.warn('No private key for alias:', alias);
            return null;
        }

        try {
            var encryptedObj = JSON.parse(atob(encryptedBase64));
            return await this.hybridDecrypt(encryptedObj, privateKey);
        } catch (error) {
            console.error('File decryption failed:', error);
            return null;
        }
    },

    // Legacy compatibility wrapper
    encrypt: function (data, secret) {
        return CryptoJS.AES.encrypt(data, secret).toString();
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
