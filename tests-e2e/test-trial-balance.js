const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const isHeaded = process.argv.includes('--headed');
const baseUrl = process.env.BASE_URL || 'https://localhost:7119';

(async () => {
    console.log('===========================================================');
    console.log('       E2E PLAYWRIGHT TEST - TRIAL BALANCE (ميزان المراجعة)  ');
    console.log('===========================================================');

    const browser = await chromium.launch({ channel: 'msedge', headless: !isHeaded });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();

    try {
        // 1. Login
        console.log('[1/6] Logging in as Admin...');
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
        await page.waitForURL(url => url.pathname === '/' || url.pathname.includes('/Home'));
        console.log('      -> Successfully authenticated.');

        // 2. Navigate to Trial Balance initial page
        console.log('\n[2/6] Navigating to Trial Balance (/Reports/TrialBalance)...');
        await page.goto(`${baseUrl}/Reports/TrialBalance`, { waitUntil: 'domcontentloaded' });
        
        const initialPrompt = await page.locator('text=يرجى تحديد معايير البحث ثم الضغط على "استعراض الميزان"').isVisible();
        console.log(`      -> Initial empty-state guidance box displayed: ${initialPrompt}`);
        if (!initialPrompt) throw new Error('Initial prompt box was not displayed as expected.');

        // 3. Submit Filter for Full Year 2026 Trial Balance
        console.log('\n[3/6] Generating Full Year 2026 Trial Balance (All Accounts)...');
        await page.click('#filterForm button[type="submit"]');
        await page.waitForSelector('#trialBalanceTable', { timeout: 30000 });

        // Check Balanced Badge
        const balancedBadge = await page.locator('.badge.bg-success').textContent();
        console.log(`      -> Balance Badge Text: "${balancedBadge.trim()}"`);
        if (!balancedBadge.includes('متزن')) {
            throw new Error(`Trial Balance is not showing balanced badge: ${balancedBadge}`);
        }

        // Count rows
        const rowCount = await page.locator('#trialBalanceTable tbody tr').count();
        console.log(`      -> Total Account Rows in Table: ${rowCount}`);

        // Read Grand Totals from tfoot
        const footerCells = await page.locator('#trialBalanceTable tfoot tr td').allTextContents();
        console.log(`      -> Grand Totals Footer:`, footerCells.map(c => c.trim()).filter(c => c.length > 0));
        
        const periodDebitText = footerCells[3]?.trim();
        const periodCreditText = footerCells[4]?.trim();
        console.log(`      -> Period Debit Total : ${periodDebitText}`);
        console.log(`      -> Period Credit Total: ${periodCreditText}`);

        if (periodDebitText !== periodCreditText) {
            throw new Error(`Totals do not match! Debit: ${periodDebitText} vs Credit: ${periodCreditText}`);
        }

        // Take Full Screenshot
        const screenshotDir = path.join(__dirname, 'screenshots');
        if (!fs.existsSync(screenshotDir)) fs.mkdirSync(screenshotDir, { recursive: true });
        
        await page.screenshot({ path: path.join(screenshotDir, 'trial-balance-full.png'), fullPage: true });
        console.log('      -> Saved screenshot to screenshots/trial-balance-full.png');

        // 4. Test Filter: Account Level 1 (Summary Groups)
        console.log('\n[4/6] Testing Filter: Account Level = 1 (Root Summary Groups)...');
        await page.selectOption('select[name="AccountLevel"]', '1');
        await page.click('#filterForm button[type="submit"]');
        await page.waitForSelector('#trialBalanceTable', { timeout: 15000 });

        const l1RowCount = await page.locator('#trialBalanceTable tbody tr').count();
        console.log(`      -> Level 1 Rows Count: ${l1RowCount}`);
        if (l1RowCount !== 5) {
            throw new Error(`Expected exactly 5 root group accounts (1-5), but found ${l1RowCount}`);
        }

        const l1AccountCodes = await page.locator('#trialBalanceTable tbody tr td:first-child').allTextContents();
        console.log(`      -> Root Account Codes:`, l1AccountCodes.map(c => c.trim()));

        await page.screenshot({ path: path.join(screenshotDir, 'trial-balance-level-1.png') });
        console.log('      -> Saved screenshot to screenshots/trial-balance-level-1.png');

        // 5. Test Filter: Only With Transactions (Zero-Balance Suppression)
        console.log('\n[5/6] Testing Filter: Zero-Balance Suppression (OnlyWithTransactions = true)...');
        await page.selectOption('select[name="AccountLevel"]', ''); // All levels
        await page.check('#chkOnlyTrans');
        await page.click('#filterForm button[type="submit"]');
        await page.waitForSelector('#trialBalanceTable', { timeout: 30000 });

        const activeRowCount = await page.locator('#trialBalanceTable tbody tr').count();
        console.log(`      -> Active Accounts Count: ${activeRowCount} (vs full count ${rowCount})`);
        if (activeRowCount >= rowCount || activeRowCount === 0) {
            throw new Error(`Zero-balance filter did not reduce row count properly: ${activeRowCount} vs ${rowCount}`);
        }

        // 6. Test Drill-Down to Account Statement
        console.log('\n[6/6] Testing Drill-Down to Account Statement from Trial Balance row...');
        // Find first link in table
        const firstAccountLink = page.locator('#trialBalanceTable tbody tr td a[target="_blank"]').first();
        const accountCodeText = (await firstAccountLink.textContent()).trim();
        console.log(`      -> Clicking drill-down link for Account Code: ${accountCodeText}`);

        // Wait for popup / new tab
        const [statementPage] = await Promise.all([
            ctx.waitForEvent('page'),
            firstAccountLink.click()
        ]);

        await statementPage.waitForLoadState('domcontentloaded');
        const statementTitle = await statementPage.title();
        console.log(`      -> Opened Tab Title: "${statementTitle}"`);
        
        await statementPage.waitForSelector('#accountStatementTable, .report-print-header', { timeout: 15000 });
        const statementHeader = await statementPage.locator('.report-print-header, h4, h5').first().textContent();
        console.log(`      -> Statement Page Header: "${statementHeader.trim()}"`);

        await statementPage.screenshot({ path: path.join(screenshotDir, 'account-statement-drilldown.png') });
        console.log('      -> Saved drill-down screenshot to screenshots/account-statement-drilldown.png');

        console.log('\n===========================================================');
        console.log('  ALL TRIAL BALANCE PLAYWRIGHT E2E TESTS PASSED (100%)     ');
        console.log('===========================================================');
    } catch (err) {
        console.error('TEST ERROR:', err.message);
        process.exit(1);
    } finally {
        await browser.close();
    }
})();
