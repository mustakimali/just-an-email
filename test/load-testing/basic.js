const puppeteer = require('puppeteer');

(async() => {

    const browser = await puppeteer.launch({
        headless: false,
    });
    
    let page = await newPage();
    await page.click("#new-session");

    let tokenElement = await page.waitForSelector("#token", {
        visible: true,
    });

    await page.waitFor("#shareBtn", {
        visible: true
    })

    let token = await tokenElement.evaluate(el => el.innerHTML);
    console.log(`Token is ${token}`);

    let page2 = await newPage();
    await page2.click("#connect");

    await page2.waitFor("#Token");
    await page2.type("#Token", token);
    await page2.click("#connect");

    await page2.waitFor(".msg-c[data-seq]", {
        visible: true
    })

    let sentText = new Date().toISOString();
    await page2.type("#ComposerText", sentText);
    await page2.click("button.sendBtn");

    await page.waitFor("span.data.embedded");
    
    await page2.click(".navbar-brand");

    await page2.waitFor("#new-session");

    await browser.close();

    async function newPage() {
        const page = await browser.newPage();
        await page.goto('http://localhost:5000');
        return page;
    }
})();