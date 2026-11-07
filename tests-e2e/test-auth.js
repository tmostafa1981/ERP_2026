const { chromium } = require('playwright');

const isHeaded = process.argv.includes('--headed');
const baseUrl = process.env.BASE_URL || 'https://localhost:7119';

(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: !isHeaded });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();
    console.log(`Logging in to ${baseUrl}...`);
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
    console.log('Logged in! Saving storageState to auth.json...');
    await ctx.storageState({ path: 'auth.json' });
    await ctx.close();

    console.log('Opening new context with saved auth.json...');
    const ctx2 = await browser.newContext({ storageState: 'auth.json', ignoreHTTPSErrors: true });
    const page2 = await ctx2.newPage();
    await page2.goto(`${baseUrl}/JournalEntries/Create`, { waitUntil: 'domcontentloaded' });
    const title = await page2.title();
    console.log('Create Page Title:', title);
    const hasTable = await page2.waitForFunction(() => document.querySelectorAll('#journalLinesTable tbody tr').length >= 2, { timeout: 15000 });
    console.log('Table found and ready:', !!hasTable);

    await browser.close();
    console.log('Auth test completed successfully!');
})();
