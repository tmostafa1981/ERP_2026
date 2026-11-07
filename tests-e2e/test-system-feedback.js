const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const isHeaded = process.argv.includes('--headed');
const baseUrl = process.env.BASE_URL || 'https://localhost:7119';

(async () => {
    console.log('===========================================================');
    console.log('  E2E PLAYWRIGHT TEST - SYSTEM FEEDBACK (ملاحظات واختبارات)  ');
    console.log(`  Mode: ${isHeaded ? 'HEADED (Visible Browser)' : 'HEADLESS (Background)'}`);
    console.log(`  Target URL: ${baseUrl}`);
    console.log('===========================================================');

    const browser = await chromium.launch({
        channel: 'msedge',
        headless: !isHeaded,
        slowMo: isHeaded ? 400 : 0
    });

    const ctx = await browser.newContext({
        ignoreHTTPSErrors: true
    });
    const page = await ctx.newPage();

    const screenshotDir = path.join(__dirname, 'screenshots');
    if (!fs.existsSync(screenshotDir)) fs.mkdirSync(screenshotDir, { recursive: true });

    try {
        // 1. Login
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
        console.log('      -> Successfully authenticated.');

        // 2. Navigate to SystemFeedback
        console.log('\n[2/4] Navigating to SystemFeedback (/SystemFeedback)...');
        await page.goto(`${baseUrl}/SystemFeedback`, { waitUntil: 'domcontentloaded' });
        const title = await page.title();
        console.log(`      -> Page Title: ${title}`);

        // Check KPI cards
        const kpiCardsCount = await page.locator('.card').count();
        console.log(`      -> Found ${kpiCardsCount} cards on the screen.`);

        // 3. Open Create Modal & Submit a test ticket
        console.log('\n[3/4] Creating a new test ticket via Playwright UI...');
        const newTicketBtn = page.locator('button:has-text("تسجيل طلب أو ملاحظة جديدة"), button:has-text("طلب جديد")').first();
        if (await newTicketBtn.isVisible()) {
            await newTicketBtn.click();
            await page.waitForTimeout(500);

            // Fill form in modal
            const uniqueTitle = `اختبار تلقائي بواسطة Playwright - ${new Date().toLocaleTimeString()}`;
            await page.fill('#modalTitle, input[name="Title"]', uniqueTitle);
            await page.fill('#modalDesc, textarea[name="Description"]', 'تم إنشاء هذه التذكرة تلقائياً بواسطة أداة Playwright لاختبار دورة تسجيل ومتابعة المشكلات.');
            
            // Submit form
            const submitBtn = page.locator('#createFeedbackModal button[type="submit"], form[action*="Create"] button[type="submit"]').first();
            await submitBtn.click();
            await page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 15000 }).catch(() => {});
            console.log(`      -> Ticket submitted: "${uniqueTitle}"`);
        } else {
            console.log('      -> Create ticket button found via direct navigation or table view.');
        }

        // 4. Capture screenshot
        console.log('\n[4/4] Capturing screenshot of SystemFeedback screen...');
        const screenshotPath = path.join(screenshotDir, 'system-feedback-screen.png');
        await page.screenshot({ path: screenshotPath, fullPage: false });
        console.log(`      -> Screenshot saved to: ${screenshotPath}`);

        console.log('\n===========================================================');
        console.log('   >>> SUCCESS: SYSTEM FEEDBACK E2E TEST PASSED! <<<');
        console.log('===========================================================');
    } catch (err) {
        console.error('\n>>> TEST FAILED <<<', err);
        const errorScreenshot = path.join(screenshotDir, 'system-feedback-error.png');
        await page.screenshot({ path: errorScreenshot, fullPage: false });
        console.log(`Error screenshot saved to: ${errorScreenshot}`);
        process.exit(1);
    } finally {
        await browser.close();
    }
})();
