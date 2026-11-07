const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const isHeaded = process.argv.includes('--headed');
const baseUrl = process.env.BASE_URL || 'https://localhost:7119';

(async () => {
    console.log('====================================================================');
    console.log('  E2E PLAYWRIGHT TEST - FINANCIAL STATEMENTS (P&L & BALANCE SHEET)  ');
    console.log('====================================================================');

    const browser = await chromium.launch({ channel: 'msedge', headless: !isHeaded });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();

    const screenshotDir = path.join(__dirname, 'screenshots');
    if (!fs.existsSync(screenshotDir)) fs.mkdirSync(screenshotDir, { recursive: true });

    try {
        // 1. Login
        console.log('[1/5] Logging in as Admin...');
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

        // 2. Reports Index Hub
        console.log('\n[2/5] Navigating to Reports Hub (/Reports)...');
        await page.goto(`${baseUrl}/Reports`, { waitUntil: 'domcontentloaded' });
        const cardsCount = await page.locator('.report-hub-card').count();
        console.log(`      -> Total Report Cards in Hub: ${cardsCount}`);
        if (cardsCount < 7) {
            throw new Error(`Expected at least 7 report cards, but found ${cardsCount}`);
        }
        await page.screenshot({ path: path.join(screenshotDir, 'reports-hub-index.png') });
        console.log('      -> Saved screenshot to screenshots/reports-hub-index.png');

        // 3. Income Statement (P&L)
        console.log('\n[3/5] Testing Income Statement (/Reports/IncomeStatement)...');
        await page.goto(`${baseUrl}/Reports/IncomeStatement`, { waitUntil: 'domcontentloaded' });
        
        // Initial state prompt
        const promptVisible = await page.locator('text=يرجى تحديد معايير البحث ثم الضغط على "استعراض قائمة الدخل"').isVisible();
        console.log(`      -> Initial prompt visible: ${promptVisible}`);

        // Submit form
        await page.click('#filterForm button[type="submit"]');
        await page.waitForSelector('#incomeStatementTable', { timeout: 30000 });

        // Verify KPI values
        const totalRevenuesText = await page.locator('.card:has-text("إجمالي الإيرادات") .fs-4').textContent();
        const grossProfitText = await page.locator('.card:has-text("مجمل الربح") .fs-4').textContent();
        const netIncomeText = await page.locator('.card:has-text("صافي نتيجة الفترة") .fs-4').textContent();

        console.log(`      -> KPI Total Revenues : ${totalRevenuesText.trim()}`);
        console.log(`      -> KPI Gross Profit    : ${grossProfitText.trim()}`);
        console.log(`      -> KPI Net Income      : ${netIncomeText.trim()}`);

        if (!totalRevenuesText.includes('8,396,000.00')) {
            throw new Error(`Expected revenues 8,396,000.00, got ${totalRevenuesText}`);
        }

        await page.screenshot({ path: path.join(screenshotDir, 'income-statement.png'), fullPage: true });
        console.log('      -> Saved screenshot to screenshots/income-statement.png');

        // 4. Balance Sheet (Financial Position)
        console.log('\n[4/5] Testing Balance Sheet (/Reports/BalanceSheet)...');
        await page.goto(`${baseUrl}/Reports/BalanceSheet`, { waitUntil: 'domcontentloaded' });

        // Submit form
        await page.click('#filterForm button[type="submit"]');
        await page.waitForSelector('#balanceSheetTable', { timeout: 30000 });

        // Check Balance Badge
        const balancedBadge = await page.locator('.badge.bg-success').textContent();
        console.log(`      -> Balance Sheet Badge: "${balancedBadge.trim()}"`);
        if (!balancedBadge.includes('متزنة تماماً')) {
            throw new Error(`Balance Sheet is not showing balanced badge: ${balancedBadge}`);
        }

        // Verify Balance Equation KPI cards
        const totalAssetsText = await page.locator('.card:has-text("إجمالي الأصول") .fs-4').textContent();
        const totalLiabText = await page.locator('.card:has-text("إجمالي الالتزامات") .fs-4').textContent();
        const totalEqText = await page.locator('.card:has-text("حقوق الملكية") .fs-4').textContent();
        const balanceDiffText = await page.locator('.card:has-text("فارق التوازن") .fs-4').textContent();

        console.log(`      -> Total Assets        : ${totalAssetsText.trim()}`);
        console.log(`      -> Total Liabilities   : ${totalLiabText.trim()}`);
        console.log(`      -> Total Equity        : ${totalEqText.trim()}`);
        console.log(`      -> Balance Difference  : ${balanceDiffText.trim()}`);

        if (balanceDiffText.trim() !== '0.0000') {
            throw new Error(`Expected balance difference 0.0000, got ${balanceDiffText}`);
        }

        await page.screenshot({ path: path.join(screenshotDir, 'balance-sheet.png'), fullPage: true });
        console.log('      -> Saved screenshot to screenshots/balance-sheet.png');

        // 5. Test Drill-Down from Balance Sheet to Account Statement
        console.log('\n[5/5] Testing Drill-Down from Balance Sheet row to Account Statement...');
        const firstLink = page.locator('#balanceSheetTable tbody tr td a[target="_blank"]').first();
        const clickedCode = (await firstLink.textContent()).trim();
        console.log(`      -> Clicking Account Code: ${clickedCode}`);

        const [statementPage] = await Promise.all([
            ctx.waitForEvent('page'),
            firstLink.click()
        ]);

        await statementPage.waitForLoadState('domcontentloaded');
        await statementPage.waitForSelector('.report-print-header, #accountStatementTable', { timeout: 15000 });
        const stmtTitle = await statementPage.title();
        console.log(`      -> Opened Statement Title: "${stmtTitle}"`);

        await statementPage.screenshot({ path: path.join(screenshotDir, 'bs-drilldown-statement.png') });
        console.log('      -> Saved screenshot to screenshots/bs-drilldown-statement.png');

        console.log('\n====================================================================');
        console.log('  ALL FINANCIAL STATEMENTS PLAYWRIGHT E2E TESTS PASSED (100%)       ');
        console.log('====================================================================');
    } catch (err) {
        console.error('TEST ERROR:', err.message);
        process.exit(1);
    } finally {
        await browser.close();
    }
})();
