const puppeteer = require('puppeteer');
const random = require('random');

(async () => {

    const browser = await puppeteer.launch({
        headless: true,
    });

    let page = await newPage();
    await page.click("#new-session");

    let tokenElement = await page.waitForSelector("#token", {
        visible: true,
    });

    let token = "&nbsp;";
    while (token == "&nbsp;") {
        token = await tokenElement.evaluate(el => el.innerHTML);
    }
    console.log(`Token is ${token}`);

    let page2 = await newPage();
    await page2.click("#connect");

    await page2.waitFor("#Token");
    await page2.type("#Token", token);
    await page2.click("#connect");

    await page2.waitFor(".msg-c[data-seq]", {
        visible: true
    })

    let numMsg = random.int(5, 20);
    console.log(`Sending ${numMsg} test messages...`);
    for (var i = 0; i < numMsg; i++)
        await transmitMessage(page, page2);
    
    console.info("Closing the session...");
    await page.click("#deleteBtn");
    await page.waitFor("button.confirm", { visible: true });

    await page.click("button.confirm", {delay: 500});
    await page.waitForNavigation();
    await page.waitFor(".modal-title", { visible: true });
    await page2.waitFor(".modal-title", { visible: true });

    await browser.close();

    async function newPage() {
        const page = await browser.newPage();
        await page.goto('http://localhost:5000');
        return page;
    }
})();

/**
 * Something 
 * @param {import('puppeteer').Page} pageOne
 * @param {import('puppeteer').Page} pageTwo
 */
async function transmitMessage(pageOne, pageTwo) {
    let pageOneSends = random.boolean();
    let pageToSendFrom = pageOneSends ? pageOne : pageTwo;
    let pageToReceive = pageOneSends ? pageTwo : pageOne;
    let messageToSend = uuid();

    if (pageOneSends) {
        console.log(`Old Client -> New Client: ${messageToSend}`);
    } else {
        console.log(`Old Client <- New Client: ${messageToSend}`);
    }

    await pageToSendFrom.type("#ComposerText", messageToSend);
    await pageToSendFrom.click("button.sendBtn");

    let started = new Date();
    while (true) {
        let newMsgEl = await pageToReceive.waitFor("span.data.embedded");
        let t = await newMsgEl.evaluate(e => e.textContent);

        if (t !== messageToSend) {
            if (new Date() - started > 5000) {
                console.error(`*** The message ${messageToSend} did not arrive ***`);
                break;
            }
            continue;
        }
        break;
    }

    function uuid() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }
}