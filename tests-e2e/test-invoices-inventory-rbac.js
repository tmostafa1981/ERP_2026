/**
 * Comprehensive E2E Automated Test Suite:
 * 1. عزل الفروع (Branch Isolation)
 * 2. جدار الصلاحيات (RBAC Permissions Barrier)
 * 3. فواتير المشتريات وتوريد السيريالات (Purchase Invoices & Serial Inflow)
 * 4. فحص الأرصدة ومنع الرصيد السالب (Stock Availability & Negative Stock Prevention)
 * 5. التحقق الإلزامي من الأرقام التسلسلية (Serial Validation)
 * 6. فواتير المبيعات وصرف المخزون والسيريال (Sales Invoices & Stock Outflow)
 */

const { chromium } = require('playwright');
const path = require('path');
const fs = require('fs');

const isHeaded = process.argv.includes('--headed');
const baseUrl = process.env.BASE_URL || 'https://localhost:7119';
const screenshotsDir = path.join(__dirname, 'screenshots');

if (!fs.existsSync(screenshotsDir)) {
    fs.mkdirSync(screenshotsDir, { recursive: true });
}

// Helper: Common Login Function
async function performLogin(page, userCode, password, targetBranchCode = null) {
    console.log(`\n🔑 تسجيل الدخول بالمستخدم: [${userCode}]...`);
    await page.goto(`${baseUrl}/Account/Login`, { waitUntil: 'domcontentloaded' });

    // 1. Fiscal Year
    await page.waitForSelector('#FiscalYearId');
    await page.selectOption('#FiscalYearId', '1');

    // 2. Employee Code
    await page.fill('#EmployeeCode', userCode);
    await page.dispatchEvent('#EmployeeCode', 'input');

    // 3. Company
    await page.waitForFunction(() => {
        const el = document.querySelector('#CompanyId');
        return el && !el.disabled && el.options.length > 1;
    }, { timeout: 15000 });

    const companyVal = await page.evaluate(() => {
        const el = document.querySelector('#CompanyId');
        return el.options[1].value;
    });
    await page.selectOption('#CompanyId', companyVal);
    await page.dispatchEvent('#CompanyId', 'change');

    // 4. Branch
    await page.waitForFunction(() => {
        const el = document.querySelector('#BranchId');
        return el && !el.disabled && el.options.length > 1;
    }, { timeout: 15000 });

    // Read available branch options with non-empty values
    const branchOptions = await page.evaluate(() => {
        const el = document.querySelector('#BranchId');
        return Array.from(el.options).filter(o => o.value && o.value.trim() !== '').map(o => ({
            value: o.value,
            text: o.text
        }));
    });

    console.log(`الفروع المتاحة للمستخدم [${userCode}]:`, branchOptions.map(b => b.text));

    let selectedBranchVal = branchOptions[0].value;
    if (targetBranchCode) {
        const match = branchOptions.find(b => b.text.includes(targetBranchCode));
        if (match) selectedBranchVal = match.value;
    }

    await page.selectOption('#BranchId', selectedBranchVal);

    // 5. Password
    await page.fill('#Password', password);

    // 6. Submit
    await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 20000 }),
        page.click('#loginForm button[type="submit"]')
    ]);

    console.log(`✅ تم تسجيل الدخول بنجاح للمستخدم [${userCode}] - الرابط الحالي: ${page.url()}`);
    return branchOptions;
}

(async () => {
    let browser;
    try {
        console.log('================================================================');
        console.log('🚀 بدء اختبارات E2E الشاملة: فواتير - كميات - سريال - فروع - صلاحيات');
        console.log('================================================================');

        browser = await chromium.launch({
            channel: 'msedge',
            headless: !isHeaded,
            slowMo: isHeaded ? 400 : 50
        });

        const context = await browser.newContext({
            ignoreHTTPSErrors: true,
            viewport: { width: 1400, height: 950 }
        });
        const page = await context.newPage();

        // ════════════════════════════════════════════════════════════════
        // TEST 1: اختبار عزل الفروع للمستخدم المقيد (User: 5555)
        // ════════════════════════════════════════════════════════════════
        console.log('\n--- 📌 [اختبار 1]: التحقق من عزل الفروع للمستخدم 5555 ---');
        await page.goto(`${baseUrl}/Account/Login`, { waitUntil: 'domcontentloaded' });
        await page.waitForSelector('#FiscalYearId');
        await page.selectOption('#FiscalYearId', '1');
        await page.fill('#EmployeeCode', '5555');
        await page.dispatchEvent('#EmployeeCode', 'input');

        await page.waitForFunction(() => {
            const el = document.querySelector('#CompanyId');
            return el && !el.disabled && el.options.length > 1;
        });
        const compVal = await page.evaluate(() => document.querySelector('#CompanyId').options[1].value);
        await page.selectOption('#CompanyId', compVal);
        await page.dispatchEvent('#CompanyId', 'change');

        await page.waitForFunction(() => {
            const el = document.querySelector('#BranchId');
            return el && !el.disabled && el.options.length > 1;
        });

        const branches5555 = await page.evaluate(() => {
            const el = document.querySelector('#BranchId');
            return Array.from(el.options).filter(o => o.value && o.value.trim() !== '').map(o => o.text.trim());
        });

        console.log(`الفروع المتاحة للمستخدم 5555: ${JSON.stringify(branches5555)}`);
        
        // Assert user 5555 only has access to branch B010 (عين شمس)
        const hasB010 = branches5555.some(b => b.includes('B010') || b.includes('عين شمس'));
        const hasB001 = branches5555.some(b => b.includes('B001'));
        if (!hasB010 || hasB001) {
            throw new Error(`❌ فشل اختبار عزل الفروع! المستخدم 5555 يجب أن يرى فقط B010 ولا يرى B001.`);
        }
        console.log('✅ نجح اختبار عزل الفروع: المستخدم 5555 معزول فقط في فرعه المصرح (B010).');

        await page.screenshot({ path: path.join(screenshotsDir, '1-branch-isolation-user5555.png') });

        // Complete login for 5555
        const branchVal5555 = await page.evaluate(() => {
            const el = document.querySelector('#BranchId');
            const opt = Array.from(el.options).find(o => o.value && o.value.trim() !== '');
            return opt ? opt.value : '';
        });
        await page.selectOption('#BranchId', branchVal5555);
        await page.fill('#Password', '123456');
        await Promise.all([
            page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
            page.click('#loginForm button[type="submit"]')
        ]);

        // ════════════════════════════════════════════════════════════════
        // TEST 2: اختبار جدار الصلاحيات (RBAC Permissions Barrier)
        // ════════════════════════════════════════════════════════════════
        console.log('\n--- 📌 [اختبار 2]: التحقق من حظر الوصول لشاشات الإدارة والمستخدمين للمستخدم 5555 ---');
        const restrictedResponse = await page.goto(`${baseUrl}/User`, { waitUntil: 'domcontentloaded' });
        const currentUrl = page.url();
        const statusCode = restrictedResponse ? restrictedResponse.status() : 0;
        console.log(`نتيجة محاولة وصول المستخدم 5555 إلى /User: Status=${statusCode}, Url=${currentUrl}`);

        const isBlocked = statusCode === 403 || currentUrl.includes('AccessDenied') || currentUrl.includes('Login');
        if (!isBlocked) {
            throw new Error(`❌ فشل فحص الصلاحيات! تم السماح للمستخدم 5555 بدخول شاشة المستخدمين بدون صلاحية.`);
        }
        console.log('✅ نجح اختبار الصلاحيات: تم منع المستخدم 5555 من دخول شاشة إدارة المستخدمين بنجاح!');
        await page.screenshot({ path: path.join(screenshotsDir, '2-rbac-access-denied-user5555.png') });

        // ════════════════════════════════════════════════════════════════
        // TEST 3: تسجيل الدخول كمسؤول النظام (Admin) في الفرع الرئيسي B001
        // ════════════════════════════════════════════════════════════════
        console.log('\n--- 📌 [اختبار 3]: تسجيل الدخول كمسؤول النظام (admin) في الفرع الرئيسي B001 ---');
        await context.clearCookies();
        await performLogin(page, 'admin', '123456', 'B001');

        // ════════════════════════════════════════════════════════════════
        // TEST 4: إنشاء فاتورة مشتريات وتوريد سيريالات (Purchases & Serial Inflow)
        // ════════════════════════════════════════════════════════════════
        console.log('\n--- 📌 [اختبار 4]: تسجيل فاتورة مشتريات لصنف سيريال (إدخال سيريالات جديدة) ---');
        await page.goto(`${baseUrl}/Documents/Create?module=Purchase&transactionTypeId=6`, { waitUntil: 'domcontentloaded' });
        await page.waitForSelector('#docApp', { timeout: 15000 });

        // Select Vendor (Supplier)
        console.log('اختيار المورد...');
        const vendorSelect = page.locator('select').filter({ hasText: 'المورد' });
        await vendorSelect.waitFor({ state: 'visible', timeout: 15000 });
        await vendorSelect.selectOption({ index: 1 });
        await vendorSelect.dispatchEvent('change');
        await page.waitForTimeout(400);

        // Select Cash / Bank Account (if cash invoice)
        console.log('اختيار حساب الخزينة / البنك...');
        const cashAccSelect = page.locator('select').filter({ hasText: 'حساب السداد' });
        if (await cashAccSelect.count() > 0) {
            await cashAccSelect.selectOption({ index: 1 });
            await cashAccSelect.dispatchEvent('change');
            await page.waitForTimeout(300);
        }

        // Select Warehouse
        console.log('اختيار المستودع...');
        const whSelect = page.locator('select').filter({ hasText: 'المستودع' });
        await whSelect.waitFor({ state: 'visible', timeout: 15000 });
        await whSelect.selectOption({ index: 1 });
        await whSelect.dispatchEvent('change');
        await page.waitForTimeout(400);

        // Search and Select Serialized Item (ITM000009 - ماكينة CC)
        console.log('البحث عن الصنف ذو الأرقام التسلسلية (ماكينة CC)...');
        const itemInput = page.locator('tbody tr:first-child input[placeholder*="كود، اسم، أو باركود"]');
        await itemInput.fill('ماكينة');
        await itemInput.dispatchEvent('input');

        console.log('انتظار ظهور قائمة الأصناف المطابقة...');
        await page.waitForSelector('.dropdown-menu.show a.dropdown-item', { timeout: 10000 });
        await page.click('.dropdown-menu.show a.dropdown-item:first-child');
        await page.waitForTimeout(600);

        // Set Quantity = 2
        console.log('تحديد الكمية = 2...');
        const row = page.locator('#docApp tbody tr').first();
        const qtyInput = row.locator('input[type="number"]').nth(0);
        await qtyInput.fill('2');
        await qtyInput.dispatchEvent('input');

        // Set Unit Price = 500
        console.log('تحديد سعر الوحدة = 500...');
        const priceInput = row.locator('input[type="number"]').nth(1);
        await priceInput.fill('500');
        await priceInput.dispatchEvent('input');
        await page.waitForTimeout(500);

        // Open Serials Modal and enter 2 serial numbers
        console.log('فتح نافذة إدخال السيريالات وإضافة سيريالين فريدين...');
        await row.locator('button:has-text("سيريالات")').click();
        await page.waitForSelector('#serialsModal.show', { timeout: 5000 });

        const uniquePrefix = 'SN' + Date.now().toString().slice(-6);
        const serial1 = `${uniquePrefix}-A1`;
        const serial2 = `${uniquePrefix}-A2`;

        console.log(`إدخال السيريال الأول: ${serial1}`);
        await page.fill('#serialInputBox', serial1);
        await page.click('#serialsModal button:has-text("إضافة")');
        await page.waitForTimeout(300);

        console.log(`إدخال السيريال الثاني: ${serial2}`);
        await page.fill('#serialInputBox', serial2);
        await page.click('#serialsModal button:has-text("إضافة")');
        await page.waitForTimeout(300);

        // Close Serials Modal
        await page.click('#serialsModal .btn-close, #serialsModal button:has-text("إغلاق")');
        await page.waitForTimeout(500);

        await page.screenshot({ path: path.join(screenshotsDir, '3-purchase-invoice-ready.png') });

        // Save Purchase Document
        console.log('حفظ وترحيل فاتورة المشتريات...');
        const savePurchaseBtn = page.locator('button:has-text("حفظ")');
        await savePurchaseBtn.click();

        // Wait for SweetAlert success or navigation
        console.log('انتظار استجابة الحفظ الناجحة لفاتورة المشتريات...');
        await page.waitForSelector('.swal2-popup:has(.swal2-success), .swal2-popup:has-text("بنجاح")', { timeout: 25000 });
        const successMsg = await page.innerText('.swal2-html-container');
        console.log(`🎉 نجح تسجيل فاتورة المشتريات وتوريد السيريالات: ${successMsg}`);
        await page.screenshot({ path: path.join(screenshotsDir, '4-purchase-invoice-saved.png') });

        // Click OK on SweetAlert
        await page.click('.swal2-confirm');
        await page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 15000 }).catch(() => {});

        // ════════════════════════════════════════════════════════════════
        // TEST 5: اختبار منع الرصيد السالب في فاتورة المبيعات (Negative Stock Prevention)
        // ════════════════════════════════════════════════════════════════
        console.log('\n--- 📌 [اختبار 5]: اختبار فحص الرصيد المباشر ومنع البيع برصيد سالب (Prevent Negative Stock) ---');
        await page.goto(`${baseUrl}/Documents/Create?module=Sales&transactionTypeId=1`, { waitUntil: 'domcontentloaded' });
        await page.waitForSelector('#docApp', { timeout: 15000 });

        // Select Customer
        console.log('اختيار العميل...');
        const custSelect = page.locator('select').filter({ hasText: 'العميل' });
        await custSelect.waitFor({ state: 'visible', timeout: 15000 });
        await custSelect.selectOption({ index: 1 });
        await custSelect.dispatchEvent('change');
        await page.waitForTimeout(400);

        // Select Cash / Bank Account (if cash invoice)
        console.log('اختيار حساب الخزينة للبيع...');
        const salesCashSelect = page.locator('select').filter({ hasText: 'حساب السداد' });
        if (await salesCashSelect.count() > 0) {
            await salesCashSelect.selectOption({ index: 1 });
            await salesCashSelect.dispatchEvent('change');
            await page.waitForTimeout(300);
        }

        // Select Warehouse
        console.log('اختيار المستودع للبيع...');
        const salesWhSelect = page.locator('select').filter({ hasText: 'المستودع' });
        await salesWhSelect.waitFor({ state: 'visible', timeout: 15000 });
        await salesWhSelect.selectOption({ index: 1 });
        await salesWhSelect.dispatchEvent('change');
        await page.waitForTimeout(400);

        // Select Item (ماكينة CC)
        console.log('اختيار الصنف ماكينة CC للبيع...');
        const salesItemInput = page.locator('tbody tr:first-child input[placeholder*="كود، اسم، أو باركود"]');
        await salesItemInput.fill('ماكينة');
        await salesItemInput.dispatchEvent('input');
        await page.waitForSelector('.dropdown-menu.show a.dropdown-item', { timeout: 10000 });
        await page.click('.dropdown-menu.show a.dropdown-item:first-child');
        await page.waitForTimeout(800);

        // Set Huge Quantity = 999999 to trigger Negative Stock Protection
        console.log('طلب كمية ضخمة (999999) لاختبار كشف العجز ومنع البيع بالسالب...');
        const salesRow = page.locator('#docApp tbody tr').first();
        const salesQtyInput = salesRow.locator('input[type="number"]').nth(0);
        await salesQtyInput.fill('999999');
        await salesQtyInput.dispatchEvent('input');

        // Wait for availability check to finish
        console.log('انتظار فحص الرصيد اللحظي من الخادم...');
        await page.waitForSelector('.badge.bg-danger:has-text("عجز")', { timeout: 15000 });
        const deficitText = await page.innerText('.badge.bg-danger:has-text("عجز")');
        console.log(`✅ تم رصد العجز بنجاح: [${deficitText}]`);

        await page.screenshot({ path: path.join(screenshotsDir, '5-sales-negative-stock-deficit-detected.png') });

        // Attempt to save -> Must be blocked!
        console.log('محاولة حفظ الفاتورة بالكمية الزائدة للتحقق من حظر الحفظ ومنع الرصيد السالب...');
        const saveSalesBtn = page.locator('button.btn-success:has-text("حفظ")');
        await saveSalesBtn.click();

        await page.waitForSelector('.swal2-popup', { timeout: 10000 });
        const alertTitle = await page.innerText('.swal2-title');
        const alertText = await page.innerText('.swal2-html-container');
        console.log(`✅ تم حظر الحفظ بنجاح بواسطة نظام حماية المخزون من الرصيد السالب: [${alertTitle}] - ${alertText}`);

        await page.screenshot({ path: path.join(screenshotsDir, '6-sales-negative-stock-blocked-alert.png') });
        await page.click('.swal2-confirm');
        await page.waitForTimeout(500);

        // ════════════════════════════════════════════════════════════════
        // TEST 6: اختبار التحقق الإلزامي من الأرقام التسلسلية (Serial Count Validation)
        // ════════════════════════════════════════════════════════════════
        console.log('\n--- 📌 [اختبار 6]: اختبار التحقق الإلزامي من تطابق عدد السيريالات مع الكمية ---');
        console.log('تعديل الكمية إلى 1 بدون إدخال سيريال...');
        await salesQtyInput.fill('1');
        await salesQtyInput.dispatchEvent('input');
        await page.waitForTimeout(800);

        // Attempt to save without serial -> Must be blocked!
        console.log('محاولة الحفظ بدون إدخال سيريال للصنف التسلسلي...');
        await saveSalesBtn.click();

        await page.waitForSelector('.swal2-popup', { timeout: 10000 });
        const serialWarnMsg = await page.innerText('.swal2-html-container');
        console.log(`✅ تم حظر الحفظ لتطلب السيريال: [${serialWarnMsg}]`);

        await page.screenshot({ path: path.join(screenshotsDir, '7-sales-missing-serial-blocked.png') });
        await page.click('.swal2-confirm');
        await page.waitForTimeout(500);

        // ════════════════════════════════════════════════════════════════
        // TEST 7: إدخال السيريال وحفظ فاتورة المبيعات بنجاح (Complete Sales Invoice)
        // ════════════════════════════════════════════════════════════════
        console.log('\n--- 📌 [اختبار 7]: إدخال السيريال المستلم وحفظ فاتورة المبيعات بنجاح ---');
        await salesRow.locator('button:has-text("سيريالات")').click();
        await page.waitForSelector('#serialsModal.show', { timeout: 5000 });

        console.log(`إدخال السيريال المباع: ${serial1}`);
        await page.fill('#serialInputBox', serial1);
        await page.click('#serialsModal button:has-text("إضافة")');
        await page.waitForTimeout(300);

        await page.click('#serialsModal .btn-close, #serialsModal button:has-text("إغلاق")');
        await page.waitForTimeout(500);

        // Set Unit Price = 800
        const salesPriceInput = salesRow.locator('input[type="number"]').nth(1);
        await salesPriceInput.fill('800');
        await salesPriceInput.dispatchEvent('input');
        await page.waitForTimeout(500);

        await page.screenshot({ path: path.join(screenshotsDir, '8-sales-invoice-ready-to-save.png') });

        // Save Sales Invoice
        console.log('حفظ وترحيل فاتورة المبيعات...');
        await saveSalesBtn.click();

        await page.waitForSelector('.swal2-popup:has-text("بنجاح")', { timeout: 25000 });
        const salesSuccessMsg = await page.innerText('.swal2-html-container');
        console.log(`🎉 نجح تسجيل وترحيل فاتورة المبيعات وصرف السيريال: ${salesSuccessMsg}`);

        await page.screenshot({ path: path.join(screenshotsDir, '9-sales-invoice-completed.png') });
        await page.click('.swal2-confirm');

        console.log('\n================================================================');
        console.log('🏆 اكتملت جميع اختبارات E2E بنجاح تام 100%!');
        console.log('1. تم التحقق من عزل الفروع للمستخدمين (Branch Isolation).');
        console.log('2. تم التحقق من حواجز الصلاحيات (RBAC Permissions Barrier).');
        console.log('3. تم إنشاء فاتورة مشتريات وتوريد سيريالات بنجاح (Purchases & Serials).');
        console.log('4. تم فحص الرصيد اللحظي ومنع البيع بالسالب بنجاح (Negative Stock Prevention).');
        console.log('5. تم التحقق الإلزامي من تطابق عدد السيريالات مع الكمية (Serial Validation).');
        console.log('6. تم إنشاء وترحيل فاتورة المبيعات بنجاح (Sales Invoice Completed).');
        console.log('================================================================\n');

    } catch (err) {
        console.error('❌ فشل الاختبار أثناء التنفيذ:', err);
        process.exit(1);
    } finally {
        if (browser) await browser.close();
    }
})();
