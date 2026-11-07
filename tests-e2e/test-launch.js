const { chromium } = require('playwright');

(async () => {
    try {
        console.log('Launching browser with msedge channel...');
        const browser = await chromium.launch({
            channel: 'msedge',
            headless: true
        });
        console.log('Browser launched successfully!');
        const context = await browser.newContext({ ignoreHTTPSErrors: true });
        const page = await context.newPage();
        await page.goto(process.env.BASE_URL || 'https://localhost:7119/Account/Login', { waitUntil: 'domcontentloaded' });
        console.log('Page loaded! Title:', await page.title());
        await page.waitForSelector('#loginForm', { timeout: 5000 });
        console.log('LoginForm selector found!');
        await browser.close();
        console.log('Test completed successfully!');
    } catch (err) {
        console.error('Launch test failed:', err);
        process.exit(1);
    }
})();
