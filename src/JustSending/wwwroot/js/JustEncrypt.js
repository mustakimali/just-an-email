"use strict";

var EndToEndEncryption = {

    // ECDH key pair (temporary, cleared after handshake)
    ecdhKeyPair: null,

    hub: null,
    peerId: null,
    initiator: false,

    // RSA key pair for this session
    publicKey: null,
    privateKey: null,
    publicKeyJwk: null,
    privateKeyJwk: null,
    public_key_alias: null,

    // Key store: maps alias -> { PublicKey, PrivateKey, PublicKeyJwk, PrivateKeyJwk, DhKey }
    keys: [],
    keys_hash: {},

    isEstablished: function () {
        return this.keys.length > 0;
    },

    //
    // RSA operations
    //

    generateRsaKeyPair: async function () {
        return await crypto.subtle.generateKey(
            {
                name: "RSA-OAEP",
                modulusLength: 2048,
                publicExponent: new Uint8Array([1, 0, 1]),
                hash: "SHA-256"
            },
            true,
            ["encrypt", "decrypt"]
        );
    },

    exportKeyToJwk: async function (key) {
        return await crypto.subtle.exportKey("jwk", key);
    },

    importPublicKeyFromJwk: async function (jwk) {
        return await crypto.subtle.importKey(
            "jwk",
            jwk,
            { name: "RSA-OAEP", hash: "SHA-256" },
            true,
            ["encrypt"]
        );
    },

    importPrivateKeyFromJwk: async function (jwk) {
        return await crypto.subtle.importKey(
            "jwk",
            jwk,
            { name: "RSA-OAEP", hash: "SHA-256" },
            true,
            ["decrypt"]
        );
    },

    //
    // Hybrid RSA+AES encrypt/decrypt (for messages and files)
    //

    hybridEncrypt: async function (data, publicKey) {
        var aesKey = await crypto.subtle.generateKey(
            { name: "AES-GCM", length: 256 },
            true,
            ["encrypt", "decrypt"]
        );

        var iv = crypto.getRandomValues(new Uint8Array(12));

        var dataBytes = typeof data === 'string'
            ? new TextEncoder().encode(data)
            : data;

        var encryptedData = await crypto.subtle.encrypt(
            { name: "AES-GCM", iv: iv },
            aesKey,
            dataBytes
        );

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

    hybridDecrypt: async function (encryptedObj, privateKey) {
        var encryptedKey = this.base64ToArrayBuffer(encryptedObj.encryptedKey);
        var iv = this.base64ToArrayBuffer(encryptedObj.iv);
        var encryptedData = this.base64ToArrayBuffer(encryptedObj.encryptedData);

        var aesKeyRaw = await crypto.subtle.decrypt(
            { name: "RSA-OAEP" },
            privateKey,
            encryptedKey
        );

        var aesKey = await crypto.subtle.importKey(
            "raw",
            aesKeyRaw,
            { name: "AES-GCM" },
            false,
            ["decrypt"]
        );

        return await crypto.subtle.decrypt(
            { name: "AES-GCM", iv: iv },
            aesKey,
            encryptedData
        );
    },

    //
    // ECDH key exchange (replaces custom Diffie-Hellman)
    //

    generateEcdhKeyPair: async function () {
        return await crypto.subtle.generateKey(
            { name: "ECDH", namedCurve: "P-256" },
            false,
            ["deriveKey"]
        );
    },

    exportEcdhPublicKey: async function (key) {
        return await crypto.subtle.exportKey("jwk", key);
    },

    importEcdhPublicKey: async function (jwk) {
        return await crypto.subtle.importKey(
            "jwk",
            jwk,
            { name: "ECDH", namedCurve: "P-256" },
            true,
            []
        );
    },

    deriveAesKeyFromEcdh: async function (ecdhPrivateKey, ecdhPublicKey) {
        return await crypto.subtle.deriveKey(
            { name: "ECDH", public: ecdhPublicKey },
            ecdhPrivateKey,
            { name: "AES-GCM", length: 256 },
            false,
            ["encrypt", "decrypt"]
        );
    },

    //
    // Symmetric encrypt/decrypt using ECDH-derived AES key (for key broadcast between peers)
    //

    encryptWithDhKey: async function (data, aesKey) {
        var iv = crypto.getRandomValues(new Uint8Array(12));
        var dataBytes = new TextEncoder().encode(data);
        var encrypted = await crypto.subtle.encrypt(
            { name: "AES-GCM", iv: iv },
            aesKey,
            dataBytes
        );
        return JSON.stringify({
            iv: this.arrayBufferToBase64(iv),
            data: this.arrayBufferToBase64(encrypted)
        });
    },

    decryptWithDhKey: async function (encryptedStr, aesKey) {
        var obj = JSON.parse(encryptedStr);
        var iv = this.base64ToArrayBuffer(obj.iv);
        var data = this.base64ToArrayBuffer(obj.data);
        var decrypted = await crypto.subtle.decrypt(
            { name: "AES-GCM", iv: iv },
            aesKey,
            data
        );
        return new TextDecoder().decode(decrypted);
    },

    //
    // Base64 helpers
    //

    arrayBufferToBase64: function (buffer) {
        var bytes = new Uint8Array(buffer);
        var binary = '';
        for (var i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    },

    base64ToArrayBuffer: function (base64) {
        var binary = atob(base64);
        var bytes = new Uint8Array(binary.length);
        for (var i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes.buffer;
    },

    //
    // Key exchange protocol
    //

    initKeyExchange: function (hub) {
        this.initCallbacks(hub);

        hub.on("startKeyExchange", function(peerId, pka, initiate) {
            app_busy(true);
            Log("Peer Id: " + peerId);
            EndToEndEncryption.init(peerId, hub, pka, initiate);
        });
    },

    init: function (peerId, hub, pka, initiate) {
        var that = EndToEndEncryption;

        that.showStatus("");

        that.peerId = peerId;
        that.hub = hub;
        that.public_key_alias = pka;
        that.initiator = initiate;

        if (initiate) {
            setTimeout(function () {
                that.startEcdhExchange();
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

    _callbackQueue: Promise.resolve(),

    initCallbacks: function (hub) {
        var that = EndToEndEncryption;

        hub.on("callback", function(method, data) {
            that._callbackQueue = that._callbackQueue.then(function () {
                Log("Request received [" + method + "] -> Payload Size: " + data.length);

                var dataObj = JSON.parse(data);
                switch (method) {
                case "ExchangeKey":
                    return that.handleExchangeKey(dataObj.publicKey);

                case "DeriveSecret":
                    return that.handleDeriveSecret(dataObj.publicKey);

                case "broadcastKeys":
                    that.showStatus("Decrypting keys");
                    return that.receiveKeys(dataObj);
                }
            });
        });

        Log("Ready to shake hands.");
    },

    startEcdhExchange: async function () {
        var that = EndToEndEncryption;
        Log("Starting ECDH key exchange...");

        that.ecdhKeyPair = await that.generateEcdhKeyPair();
        var pubJwk = await that.exportEcdhPublicKey(that.ecdhKeyPair.publicKey);
        that.callPeer("ExchangeKey", { publicKey: pubJwk });
    },

    handleExchangeKey: async function (peerPublicKeyJwk) {
        var that = EndToEndEncryption;
        Log("Received peer ECDH public key, deriving shared secret...");

        that.ecdhKeyPair = await that.generateEcdhKeyPair();

        var peerPublicKey = await that.importEcdhPublicKey(peerPublicKeyJwk);
        var derivedKey = await that.deriveAesKeyFromEcdh(that.ecdhKeyPair.privateKey, peerPublicKey);

        var pubJwk = await that.exportEcdhPublicKey(that.ecdhKeyPair.publicKey);
        that.callPeer("DeriveSecret", { publicKey: pubJwk });

        that.ecdhKeyPair = null;
        await that.onHandshakeDone(derivedKey);
    },

    handleDeriveSecret: async function (peerPublicKeyJwk) {
        var that = EndToEndEncryption;
        Log("Received peer ECDH public key, deriving shared secret...");

        var peerPublicKey = await that.importEcdhPublicKey(peerPublicKeyJwk);
        var derivedKey = await that.deriveAesKeyFromEcdh(that.ecdhKeyPair.privateKey, peerPublicKey);

        that.ecdhKeyPair = null;
        await that.onHandshakeDone(derivedKey);
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
            .then(function() { });
    },

    callAllPeers: function (method, data) {
        this.callPeerInternal("ALL", method, data);
    },

    onHandshakeDone: async function (dhKey) {
        $("#EncryptionPublicKeyAlias").val(this.public_key_alias);

        var keyPair = await this.generateRsaKeyPair();
        this.publicKey = keyPair.publicKey;
        this.privateKey = keyPair.privateKey;
        this.publicKeyJwk = await this.exportKeyToJwk(keyPair.publicKey);
        this.privateKeyJwk = await this.exportKeyToJwk(keyPair.privateKey);

        var keyEntry = {
            Key: this.public_key_alias,
            PublicKeyJwk: this.publicKeyJwk,
            PrivateKeyJwk: this.privateKeyJwk,
            DhKey: dhKey
        };

        this.keys.push(keyEntry);
        this.keys_hash[this.public_key_alias] = keyEntry;

        console.clear();
        Log("Handshake is done - RSA key pair generated");

        this.uploadPublicKey(this.public_key_alias, this.publicKeyJwk);

        if (!this.initiator) {
            var encKeys = [];
            var keysJson = JSON.stringify(this.keys.map(function(k) {
                return {
                    Key: k.Key,
                    PublicKeyJwk: k.PublicKeyJwk,
                    PrivateKeyJwk: k.PrivateKeyJwk
                };
            }));

            for (var i = 0; i < this.keys.length; i++) {
                var k = this.keys[i];
                if (k.DhKey) {
                    encKeys.push({
                        Key: k.Key,
                        EncryptedSecrets: await this.encryptWithDhKey(keysJson, k.DhKey)
                    });
                }
            }

            this.callAllPeers("broadcastKeys", encKeys);
            this.printKeyStoreStats();
        }

        this.initiator = false;
        app_busy(false);
    },

    //
    // Public key management
    //

    publicKeyId: null,

    uploadPublicKey: function (alias, publicKeyJwk) {
        var sessionId = $("#SessionId").val();
        var sessionVerification = $("#SessionVerification").val();
        if (!sessionId) return;

        var self = this;
        fetch('/app/key', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                sessionId: sessionId,
                sessionVerification: sessionVerification,
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

    generateOwnPrivateKey: async function (then) {
        this.public_key_alias = PrimeHelper.randomNumberOfLength(32);

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

        this.uploadPublicKey(this.public_key_alias, this.publicKeyJwk);

        this.printKeyStoreStats();
        then(this.public_key_alias);
    },

    //
    // Key store management
    //

    receiveKeys: async function (dataObj) {
        for (var i = 0; i < dataObj.length; i++) {
            var nk = dataObj[i];

            for (var j = 0; j < this.keys.length; j++) {
                var ik = this.keys[j];

                if (ik.Key == nk.Key && ik.DhKey) {
                    console.clear();
                    Log("Decrypting incoming private keys using: \r\n\t\t" + ik.Key);
                    await this.updateKeyStore(ik.DhKey, nk.EncryptedSecrets);
                    return;
                }
            }
        }
    },

    updateKeyStore: async function (dhKey, encryptedData) {
        var decryptedJson = await this.decryptWithDhKey(encryptedData, dhKey);
        var incomingKeyStore = JSON.parse(decryptedJson);

        for (var i = 0; i < incomingKeyStore.length; i++) {
            var k = incomingKeyStore[i];

            var existing = this.keys_hash[k.Key];
            if (existing) {
                // Replace with the incoming key pair so all peers share the same keys
                existing.PublicKeyJwk = k.PublicKeyJwk;
                existing.PrivateKeyJwk = k.PrivateKeyJwk;
                existing.PublicKey = null;
                existing.PrivateKey = null;

                if (k.Key == this.public_key_alias) {
                    this.publicKeyJwk = k.PublicKeyJwk;
                    this.privateKeyJwk = k.PrivateKeyJwk;
                    this.publicKey = await this.importPublicKeyFromJwk(k.PublicKeyJwk);
                    this.privateKey = await this.importPrivateKeyFromJwk(k.PrivateKeyJwk);
                    this.uploadPublicKey(this.public_key_alias, k.PublicKeyJwk);
                }
                continue;
            }

            var keyEntry = {
                Key: k.Key,
                PublicKeyJwk: k.PublicKeyJwk,
                PrivateKeyJwk: k.PrivateKeyJwk
            };

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

    //
    // Public encryption API
    //

    encryptWithPublicKey: async function (data) {
        if (!this.publicKey) {
            throw new Error("No public key available");
        }
        var encrypted = await this.hybridEncrypt(data, this.publicKey);
        return btoa(JSON.stringify(encrypted));
    },

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
    }
}

var PrimeHelper = {

    randomNumberSingle: function () {
        var arr = new Uint8Array(1);
        crypto.getRandomValues(arr);
        return arr[0] % 10;
    },

    randomNumberOfLength: function (digit) {
        var s = [];
        for (var i = 1; i <= digit; i++) {
            s.push(this.randomNumberSingle());
        }
        return s.join("");
    }
}
