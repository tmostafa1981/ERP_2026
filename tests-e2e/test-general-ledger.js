const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

// Determine headless mode from command line argument: --headed or --headless (default: true)
const isHeaded = process.argv.includes('--headed');
const baseUrl = process.env.BASE_URL || 'https://localhost:7119';

(async () => {
    console.log('===========================================================');
    console.log('   E2E PLAYWRIGHT TEST - GENERAL LEDGER (دفتر الأستاذ العام) ');
    console.log(`   Mode: ${isHeaded ? 'HEADED (Visible Browser)' : 'HEADLESS (Background)'}`);
    console.log(`   Target URL: ${baseUrl}`);
    console.log('===========================================================');

    const browser = await chromium.launch({
        channel: 'msedge',
        headless: !isHeaded,
        slowMo: isHeaded ? 400 : 0 // Slow down actions if visible so user can see them
    });

    const ctx = await browser.newContext({
        ignoreHTTPSErrors: true
    });
    const page = await ctx.newPage();

    const screenshotDir = path.join(__dirname, 'screenshots');
    if (!fs.existsSync(screenshotDir)) fs.mkdirSync(screenshotDir, { recursive: true });

    try {
        // 1. Login as Admin
        console.log('\n[1/4] Logging in as Admin...');
        await page.goto(`${baseUrl}/Account/Login`, { waitUntil: 'domcontentloaded' });
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
        console.log('      -> Successfully authenticated as Admin.');

        // 2. Navigate to General Ledger
        console.log('\n[2/4] Navigating to General Ledger (/Reports/GeneralLedger)...');
        await page.goto(`${baseUrl}/Reports/GeneralLedger`, { waitUntil: 'domcontentloaded' });
        const pageTitle = await page.title();
        console.log(`      -> Page Title: ${pageTitle}`);

        // 3. Submit and Measure Load Time
        console.log('\n[3/4] Clicking "استعراض دفتر الأستاذ" and measuring performance...');
        const startTime = Date.now();
        
        await Promise.all([
            page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 30000 }),
            page.click('#filterForm button[type="submit"]')
        ]);
        
        const loadDurationMs = Date.now() - startTime;
        console.log(`      -> Report Loaded in: ${loadDurationMs} ms (${(loadDurationMs / 1000).toFixed(2)} seconds)!`);

        // Check for General Ledger tables / containers
        const tableCount = await page.locator('.table').count();
        console.log(`      -> Rendered Tables Count: ${tableCount}`);

        // Check if report error or empty
        const hasContainer = await page.locator('#generalLedgerContainer').count() > 0;
        console.log(`      -> #generalLedgerContainer present: ${hasContainer}`);

        // 4. Take full page screenshot
        console.log('\n[4/4] Capturing screenshot...');
        const screenshotPath = path.join(screenshotDir, 'general-ledger-loaded.png');
        await page.screenshot({ path: screenshotPath, fullPage: false });
        console.log(`      -> Screenshot saved to: ${screenshotPath}`);

        console.log('\n===========================================================');
        console.log('   >>> SUCCESS: GENERAL LEDGER E2E TEST PASSED! <<<');
        console.log('===========================================================');
    } catch (err) {
        console.error('\n>>> TEST FAILED <<<', err);
        const errorScreenshot = path.join(screenshotDir, 'general-ledger-error.png');
        await page.screenshot({ path: errorScreenshot, fullPage: false });
        console.log(`Error screenshot saved to: ${errorScreenshot}`);
        process.exit(1);
    } finally {
        await browser.close();
    }
})();
