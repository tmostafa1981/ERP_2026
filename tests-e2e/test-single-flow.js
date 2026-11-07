const { chromium } = require('playwright');

const isHeaded = process.argv.includes('--headed');
const baseUrl = process.env.BASE_URL || 'https://localhost:7119';

(async () => {
    let browser;
    try {
        console.log('--- Testing single session complete flow ---');
        browser = await chromium.launch({
            channel: 'msedge',
            headless: !isHeaded,
            slowMo: isHeaded ? 300 : 0
        });
        const context = await browser.newContext({ ignoreHTTPSErrors: true });
        const page = await context.newPage();

        // 1. Login
        console.log('Navigating to Login page...');
        await page.goto(`${baseUrl}/Account/Login`, { waitUntil: 'domcontentloaded' });

        console.log('Selecting Fiscal Year...');
        await page.waitForSelector('#FiscalYearId');
        await page.selectOption('#FiscalYearId', '1');

        console.log('Entering Employee Code...');
        await page.fill('#EmployeeCode', 'admin');
        await page.dispatchEvent('#EmployeeCode', 'input');

        console.log('Waiting for Company dropdown to populate...');
        await page.waitForFunction(() => {
            const el = document.querySelector('#CompanyId');
            return el && !el.disabled && el.options.length > 1;
        }, { timeout: 15000 });

        const companyVal = await page.evaluate(() => {
            const el = document.querySelector('#CompanyId');
            return el.options[1].value;
        });
        console.log(`Selecting Company: ${companyVal}`);
        await page.selectOption('#CompanyId', companyVal);
        await page.dispatchEvent('#CompanyId', 'change');

        console.log('Waiting for Branch dropdown to populate...');
        await page.waitForFunction(() => {
            const el = document.querySelector('#BranchId');
            return el && !el.disabled && el.options.length > 1;
        }, { timeout: 15000 });

        const branchVal = await page.evaluate(() => {
            const el = document.querySelector('#BranchId');
            return el.options[1].value;
        });
        console.log(`Selecting Branch: ${branchVal}`);
        await page.selectOption('#BranchId', branchVal);

        console.log('Entering Password...');
        await page.fill('#Password', '123456');

        console.log('Submitting login form...');
        await Promise.all([
            page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 20000 }),
            page.click('#loginForm button[type="submit"]')
        ]);
        console.log('Login successful! Current URL:', page.url());

        // 2. Navigate to Add New Journal Entry
        console.log('Navigating to JournalEntries/Create...');
        await page.goto(`${baseUrl}/JournalEntries/Create`, { waitUntil: 'domcontentloaded' });
        await page.waitForSelector('#journalEntryApp', { timeout: 15000 });

        // Wait for Vue lines table to have at least 2 rows
        console.log('Waiting for Journal Entry table rows to be ready...');
        await page.waitForFunction(() => {
            const rows = document.querySelectorAll('#journalLinesTable tbody tr');
            return rows && rows.length >= 2;
        }, { timeout: 10000 });

        // Fill Description
        console.log('Filling Header description...');
        const descInput = page.locator('input[placeholder*="قيد تسوية رواتب"]');
        await descInput.fill('Single Session Verification Test');

        const rows = page.locator('#journalLinesTable tbody tr');
        const row1 = rows.nth(0);
        const row2 = rows.nth(1);

        // Fill Line 1 Account
        console.log('Filling Line 1 Account search query...');
        const row1AccountInput = row1.locator('input[placeholder*="ابحث برقم"]');
        await row1AccountInput.fill('110301');
        
        console.log('Waiting for Line 1 account search dropdown...');
        await page.waitForSelector('.dropdown-menu.show .dropdown-item', { timeout: 10000 });
        console.log('Clicking account dropdown item...');
        await page.click('.dropdown-menu.show .dropdown-item');
        await page.waitForTimeout(500);

        // Fill Line 1 Debit
        console.log('Filling Line 1 Debit amount...');
        const row1DebitInput = row1.locator('td:nth-child(5) input[type="number"]');
        await row1DebitInput.fill('5000');
        await row1DebitInput.dispatchEvent('input');
        await page.waitForTimeout(500);

        // Fill Line 2 Account
        console.log('Filling Line 2 Account search query...');
        const row2AccountInput = row2.locator('input[placeholder*="ابحث برقم"]');
        await row2AccountInput.fill('310101');

        console.log('Waiting for Line 2 account search dropdown...');
        await page.waitForSelector('.dropdown-menu.show .dropdown-item', { timeout: 10000 });
        console.log('Clicking account dropdown item...');
        await page.click('.dropdown-menu.show .dropdown-item');
        await page.waitForTimeout(500);

        // Fill Line 2 Credit
        console.log('Filling Line 2 Credit amount...');
        const row2CreditInput = row2.locator('td:nth-child(6) input[type="number"]');
        await row2CreditInput.fill('5000');
        await row2CreditInput.dispatchEvent('input');
        await page.waitForTimeout(500);

        let capturedResponse = null;
        await page.route('**/JournalEntries/Create', async route => {
            if (route.request().method() === 'POST') {
                const response = await route.fetch();
                const bodyText = await response.text();
                let json = null;
                try { json = JSON.parse(bodyText); } catch (e) {}
                capturedResponse = { status: response.status(), json, raw: bodyText };
                await route.fulfill({ response, body: bodyText });
            } else {
                await route.continue();
            }
        });

        // Wait for Save button to become enabled
        console.log('Waiting for Save button to become enabled...');
        const saveBtn = page.locator('button:has-text("حفظ قيد اليومية")');
        await page.waitForFunction(() => {
            const btn = document.querySelector('button.btn-primary:not([disabled])');
            return btn && btn.innerText.includes('حفظ قيد اليومية');
        }, { timeout: 10000 });

        console.log('Save button is enabled! Clicking Save...');
        await saveBtn.click();

        console.log('Waiting for navigation to Details page or response...');
        await page.waitForURL(url => url.pathname.includes('/JournalEntries/Details'), { timeout: 30000 });
        console.log('Successfully navigated to Details page! URL:', page.url());
        console.log('Captured save response:', capturedResponse);

        console.log('--- Single session test passed successfully! ---');
    } catch (err) {
        console.error('--- Test failed ---', err);
        process.exit(1);
    } finally {
        if (browser) await browser.close();
    }
})();
