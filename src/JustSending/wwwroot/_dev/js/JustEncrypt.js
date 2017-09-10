"use strict"

var JustEncrypt = {

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

    isPrime: function (num) {
        if (typeof (num) != "object")
            num = new BigNumber(num);

        var startFrom = new BigNumber(2);

        for (var i = startFrom; i.lessThan(num); i = i.add(1)) {
            if (num.div(i).isInteger())
                return false;
        }
        return num.greaterThan(1);
    },

    primeOfDigit: function (digit) {
        return this.nextRandomPrimeAfter(this.randomNumberOfLength(digit));
    },

    nextRandomPrimeAfter: function (startFrom) {
        if (typeof (num) != "object")
            startFrom = new BigNumber(startFrom);

        var start = startFrom.add(1);

        while (!this.isPrime(start)) {

            start = start.add(1);
        }

        return start.toString(10);
    },
    
    twoPrime: function () {
        return [JustEncrypt.primeOfDigit(5), JustEncrypt.primeOfDigit(6)];
    },

    /**
     * Start the key exchange process
     * @param {me} me
     * @param {me} peer
     */
    keyExchange: function (me, peer) {
        me.generateTwoPrimes();
        
        peer.setTwoPrimes(me.g, me.p, function (A) {

            me.A = A;

            me.computeB(function (B) {

                // generate peer.k using B
                peer.computeK(B, function () {

                    // generate me.k using A
                    me.computeK(A, function () {

                        console.clear();
                        console.log("     p*: " + me.p.toString(10));
                        console.log("     g*: " + me.g.toString(10));

                        console.log("  me/b : " + me.b.toString(10));
                        console.log("  me/B*: " + me.A.toString(10));

                        console.log("peer/a : " + peer.a.toString(10));
                        console.log("peer/A*: " + peer.B.toString(10));

                        console.log("  me/K : " + me.k.toString(10));
                        console.log("peer/K : " + peer.k.toString(10));

                    });

                })

            });



        });

    }
}

var me = {
    name: "me",
    g: null,
    p: null,

    p_digits: 1024,
    g_digits: 2,


    A: null, // comes from peer

    b: null,
    b_digits: 2,

    k: null,

    generateTwoPrimes: function () {
        this.g = new BigNumber(JustEncrypt.primeOfDigit(this.g_digits));
        this.p = new BigNumber(JustEncrypt.primeOfDigit(this.p_digits));
    },

    computeB: function (then) {
        this.b = new BigNumber(JustEncrypt.randomNumberOfLength(this.b_digits));
        var B = this.g.pow(this.b).mod(this.p);

        then(B);
    },

    computeK: function (A, then) {
        this.A = new BigNumber(A);
        this.k = this.A.pow(this.b).mod(this.p);

        then();
    }
}

var peer = {
    g: null,
    p: null,

    a: null,
    a_digits: 2,

    B: null,

    k: null,

    setTwoPrimes: function (g, p, then) {
        this.g = new BigNumber(g);
        this.p = new BigNumber(p);

        // secret
        //
        this.a = JustEncrypt.randomNumberOfLength(this.a_digits);
        var A = this.g.pow(this.a).mod(this.p);

        then(A);
    },

    computeK: function (B, then) {
        this.B = new BigNumber(B);
        this.k = this.B.pow(this.a).mod(this.p);

        then();
    }
}

function l(s) {
    console.log(s);
}