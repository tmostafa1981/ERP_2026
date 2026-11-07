const https = require('https');
const { chromium } = require('playwright');

(async () => {
    console.log('Launching browser to test /Reports/GeneralLedger...');
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();

    // Log page console errors or network failures
    page.on('console', msg => console.log('PAGE LOG:', msg.text()));
    page.on('pageerror', err => console.log('PAGE ERROR:', err.message));
    page.on('requestfailed', req => console.log('REQ FAILED:', req.url(), req.failure()?.errorText));

    console.log('1. Logging in...');
    await page.goto('https://localhost:7119/Account/Login', { waitUntil: 'domcontentloaded' });
    await page.selectOption('#FiscalYearId', '1');
    await page.fill('#EmployeeCode', 'admin');
    await page.dispatchEvent('#EmployeeCode', 'input');
    await page.waitForFunction(() => document.querySelector('#CompanyId')?.options?.length > 1, { timeout: 20000 });
    await page.selectOption('#CompanyId', '1');
    await page.waitForFunction(() => document.querySelector('#BranchId')?.options?.length > 1, { timeout: 20000 });
    await page.selectOption('#BranchId', '3');
    await page.fill('#Password', '123456');
    await page.click('#loginForm button[type="submit"]');
    await page.waitForURL(url => url.pathname === '/' || url.pathname.includes('/Home'), { timeout: 20000 });
    console.log('Logged in successfully!');

    console.log('2. Navigating to /Reports/GeneralLedger...');
    await page.goto('https://localhost:7119/Reports/GeneralLedger', { waitUntil: 'domcontentloaded' });
    console.log('General Ledger page loaded.');

    console.log('3. Clicking submit button...');
    const submitBtn = page.locator('#filterForm button[type="submit"]');
    
    const startTime = Date.now();
    await submitBtn.click();
    console.log('Clicked submit! Waiting for response or DOM update...');

    // Wait for either URL change or timeout, or check if page is frozen
    try {
        await page.waitForNavigation({ waitUntil: 'networkidle', timeout: 45000 });
        console.log(`Navigation finished in ${Date.now() - startTime} ms! URL: ${page.url()}`);
    } catch (e) {
        console.log(`Navigation wait error: ${e.message}`);
        console.log(`Current URL after wait: ${page.url()}`);
    }

    const htmlLen = (await page.content()).length;
    console.log(`Page content length: ${htmlLen} characters.`);
    const tablesCount = await page.locator('.table').count();
    console.log(`Tables count on page: ${tablesCount}`);

    await page.screenshot({ path: 'screenshots/bench-gl.png', fullPage: false });
    console.log('Saved screenshot to screenshots/bench-gl.png');

    await browser.close();
})();
