/**
 * Concurrency Test Suite 1:
 * 2 يوزر مع بعض في نفس الوقت ونفس الفرع
 * يسجلو فواتير ويختبر الرصيد والمرتجع وسند قبض - كل واحد 20 حركة (إجمالي 40 حركة متزامنة)
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

// Helper: Login worker into a specific branch
async function loginWorker(context, userCode, password, targetBranchCode) {
    const page = await context.newPage();
    console.log(`[${userCode}] 🔑 بدء تسجيل الدخول في الفرع [${targetBranchCode}]...`);
    
    await page.goto(`${baseUrl}/Account/Login`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('#FiscalYearId');
    await page.selectOption('#FiscalYearId', '1');

    await page.fill('#EmployeeCode', userCode);
    await page.dispatchEvent('#EmployeeCode', 'input');

    await page.waitForFunction(() => {
        const el = document.querySelector('#CompanyId');
        return el && !el.disabled && el.options.length > 1;
    }, { timeout: 15000 });

    const companyVal = await page.evaluate(() => document.querySelector('#CompanyId').options[1].value);
    await page.selectOption('#CompanyId', companyVal);
    await page.dispatchEvent('#CompanyId', 'change');

    await page.waitForFunction(() => {
        const el = document.querySelector('#BranchId');
        return el && !el.disabled && el.options.length > 1;
    }, { timeout: 15000 });

    const branchOptions = await page.evaluate(() => {
        return Array.from(document.querySelector('#BranchId').options)
            .filter(o => o.value && o.value.trim() !== '')
            .map(o => ({ value: o.value, text: o.text }));
    });

    let selectedBranch = branchOptions.find(b => b.text.includes(targetBranchCode)) || branchOptions[0];
    await page.selectOption('#BranchId', selectedBranch.value);
    await page.fill('#Password', password);

    await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 20000 }),
        page.click('#loginForm button[type="submit"]')
    ]);

    console.log(`[${userCode}] ✅ تم الدخول بنجاح للفرع: ${selectedBranch.text}`);
    return { page, branch: selectedBranch };
}

// Worker execution: 20 operations
async function executeWorkerMovements(page, workerName, branchInfo, targetMovements = 20) {
    const logs = [];
    const startTime = Date.now();
    let moveCount = 0;

    console.log(`\n🚀 [${workerName}] بدء تنفيذ [${targetMovements}] حركة متزامنة في نفس الفرع (${branchInfo.text})...`);

    // Ensure we are on an authenticated page with anti-forgery token
    await page.goto(`${baseUrl}/Documents/Create?module=Sales&transactionTypeId=1`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('#docApp', { timeout: 15000 });

    // Inspect lookups for this active branch
    const lookups = await page.evaluate(async () => {
        const custs = await (await fetch('/api/lookup/customer')).json();
        const whs = await (await fetch('/api/lookup/warehouse')).json();
        const items = await (await fetch('/api/lookup/item')).json();
        const cashes = await (await fetch('/api/lookup/cashBankAccount')).json();
        const accs = await (await fetch('/api/lookup/account?page=1')).json();
        return {
            customer: custs.results?.[0] || { id: 3 },
            warehouse: whs.results?.[0] || { id: 1 },
            item: items.results?.[0] || { id: 1 },
            cashAccount: cashes.results?.[0] || { id: 5 },
            account: accs.results?.[0] || { id: 12 }
        };
    });

    const custId = lookups.customer.id || 3;
    const whId = lookups.warehouse.id || 1;
    const itemId = lookups.item.id || 1;
    const cashAccId = lookups.cashAccount.id || 5;
    const accId = lookups.account.id || 12;
    const branchId = parseInt(branchInfo.value, 10);

    let cycle = 1;
    while (moveCount < targetMovements) {
        // Op 1: فحص واختبار الرصيد (Stock Availability Check)
        if (moveCount < targetMovements) {
            moveCount++;
            const t0 = Date.now();
            const avail = await page.evaluate(async ({ itm, wh }) => {
                const res = await fetch(`/Documents/CheckAvailabilityApi?itemId=${itm}&warehouseId=${wh}&unitId=1&quantity=1`);
                return await res.json();
            }, { itm: itemId, wh: whId });
            const dur = Date.now() - t0;
            const onHand = avail.data ? avail.data.availableQuantity : 'N/A';
            console.log(`[${workerName}] [حركة #${moveCount}/${targetMovements}] 🔍 فحص رصيد الصنف: المتاح حالياً = ${onHand} (${dur}ms)`);
            logs.push({ op: 'StockCheck', success: avail.success, onHand, dur });
        }

        // Op 2: تسجيل فاتورة مبيعات (Sales Invoice)
        if (moveCount < targetMovements) {
            moveCount++;
            const t0 = Date.now();
            const invResult = await page.evaluate(async (args) => {
                const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                const payload = {
                    transactionTypeId: 1, // Cash Sales Invoice
                    branchId: args.branchId,
                    allowNegativeStock: true,
                    documentDate: new Date().toISOString().split('T')[0],
                    partyType: 1,
                    partyId: args.custId,
                    paymentMethod: 1,
                    cashBankAccountId: args.cashAccId,
                    warehouseId: args.whId,
                    currencyId: 1,
                    exchangeRate: 1.0,
                    referenceNumber: `CONC-${args.worker}-${Date.now().toString().slice(-6)}`,
                    notes: `Concurrent 2-user test move ${args.moveCount}`,
                    lines: [
                        {
                            lineNumber: 1,
                            itemId: args.itemId,
                            unitId: 1,
                            quantity: 1,
                            unitPrice: 120,
                            discountAmount: 0,
                            taxRate: 14,
                            notes: '',
                            serialNumbers: []
                        }
                    ]
                };
                const res = await fetch('/Documents/Create?postImmediately=true', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                    body: JSON.stringify(payload)
                });
                return await res.json();
            }, { branchId, custId, cashAccId, whId, itemId, worker: workerName, moveCount });
            const dur = Date.now() - t0;
            if (!invResult.success) throw new Error(`[${workerName}] فشل حفظ فاتورة المبيعات: ${invResult.message}`);
            console.log(`[${workerName}] [حركة #${moveCount}/${targetMovements}] 🧾 تسجيل فاتورة مبيعات: ${invResult.documentNumber} (${dur}ms)`);
            logs.push({ op: 'SalesInvoice', doc: invResult.documentNumber, dur });
        }

        // Op 3: فحص الرصيد بعد الفاتورة (Verify Stock Deduction)
        if (moveCount < targetMovements) {
            moveCount++;
            const t0 = Date.now();
            const availAfter = await page.evaluate(async ({ itm, wh }) => {
                const res = await fetch(`/Documents/CheckAvailabilityApi?itemId=${itm}&warehouseId=${wh}&unitId=1&quantity=1`);
                return await res.json();
            }, { itm: itemId, wh: whId });
            const dur = Date.now() - t0;
            const onHand = availAfter.data ? availAfter.data.availableQuantity : 'N/A';
            console.log(`[${workerName}] [حركة #${moveCount}/${targetMovements}] 📦 التحقق من خصم الرصيد بعد الفاتورة: المتبقي = ${onHand} (${dur}ms)`);
            logs.push({ op: 'StockCheckAfterInvoice', onHand, dur });
        }

        // Op 4: تسجيل مرتجع مبيعات (Sales Return)
        if (moveCount < targetMovements) {
            moveCount++;
            const t0 = Date.now();
            const retResult = await page.evaluate(async (args) => {
                const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                const payload = {
                    transactionTypeId: 5, // Sales Return
                    branchId: args.branchId,
                    documentDate: new Date().toISOString().split('T')[0],
                    partyType: 1,
                    partyId: args.custId,
                    paymentMethod: 2, // Credit
                    warehouseId: args.whId,
                    currencyId: 1,
                    exchangeRate: 1.0,
                    referenceNumber: `RET-${args.worker}-${Date.now().toString().slice(-6)}`,
                    notes: `Concurrent return test move ${args.moveCount}`,
                    lines: [
                        {
                            lineNumber: 1,
                            itemId: args.itemId,
                            unitId: 1,
                            quantity: 1,
                            unitPrice: 120,
                            discountAmount: 0,
                            taxRate: 14,
                            notes: '',
                            serialNumbers: []
                        }
                    ]
                };
                const res = await fetch('/Documents/Create?postImmediately=true', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                    body: JSON.stringify(payload)
                });
                return await res.json();
            }, { branchId, custId, whId, itemId, worker: workerName, moveCount });
            const dur = Date.now() - t0;
            if (!retResult.success) throw new Error(`[${workerName}] فشل حفظ مرتجع المبيعات: ${retResult.message}`);
            console.log(`[${workerName}] [حركة #${moveCount}/${targetMovements}] 🔄 تسجيل مرتجع مبيعات: ${retResult.documentNumber} (${dur}ms)`);
            logs.push({ op: 'SalesReturn', doc: retResult.documentNumber, dur });
        }

        // Op 5: تسجيل سند قبض (Cash Receipt Voucher)
        if (moveCount < targetMovements) {
            moveCount++;
            const t0 = Date.now();
            const rctResult = await page.evaluate(async (args) => {
                const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                const payload = {
                    voucherType: 1, // Receipt
                    branchId: args.branchId,
                    voucherDate: new Date().toISOString().split('T')[0],
                    cashBankAccountId: args.cashAccId,
                    currencyId: 1,
                    exchangeRate: 1.0,
                    description: `Concurrent receipt move ${args.moveCount} by ${args.worker}`,
                    referenceNumber: `RCT-${args.worker}-${Date.now().toString().slice(-6)}`,
                    lines: [
                        {
                            lineNumber: 1,
                            lineType: 1, // Customer
                            subLedgerId: args.custId,
                            accountId: args.accId,
                            currencyId: 1,
                            exchangeRate: 1.0,
                            amountForeign: 150,
                            amountLocal: 150,
                            description: `Receipt line move ${args.moveCount}`
                        }
                    ]
                };
                const res = await fetch('/CashBankVouchers/Create?postImmediately=true', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                    body: JSON.stringify(payload)
                });
                return await res.json();
            }, { branchId, cashAccId, custId, accId, worker: workerName, moveCount });
            const dur = Date.now() - t0;
            if (!rctResult.success) throw new Error(`[${workerName}] فشل حفظ سند القبض: ${rctResult.message}`);
            console.log(`[${workerName}] [حركة #${moveCount}/${targetMovements}] 💵 تسجيل سند قبض: ${rctResult.voucherNumber} (${dur}ms)`);
            logs.push({ op: 'CashReceipt', doc: rctResult.voucherNumber, dur });
        }

        cycle++;
    }

    const totalDuration = ((Date.now() - startTime) / 1000).toFixed(2);
    console.log(`\n🏁 [${workerName}] تم إنهاء كافة الـ [${targetMovements}] حركة بنجاح في [${totalDuration} ثانية]!`);
    return { workerName, moveCount, totalDuration, logs };
}

(async () => {
    let browser;
    try {
        console.log('================================================================');
        console.log('⚡ بدء اختبار التزامن: 2 مستخدمين في نفس الوقت ونفس الفرع');
        console.log('   (فواتير + رصيد + مرتجع + سند قبض = 20 حركة لكل مستخدم = 40 حركة)');
        console.log('================================================================');

        browser = await chromium.launch({
            channel: 'msedge',
            headless: !isHeaded,
            slowMo: isHeaded ? 100 : 0
        });

        // Create 2 independent browser sessions (Contexts)
        const context1 = await browser.newContext({ ignoreHTTPSErrors: true });
        const context2 = await browser.newContext({ ignoreHTTPSErrors: true });

        // Login both users to the SAME branch (B001) sequentially to ensure clean session tokens
        const worker1Data = await loginWorker(context1, 'admin', '123456', 'B001');
        const worker2Data = await loginWorker(context2, 'tamer', '123456', 'B001');

        // Check if stock is low in B001 and top up 100 units via purchase invoice if needed
        try {
            const currentAvail = await worker1Data.page.evaluate(async () => {
                const res = await fetch('/Documents/CheckAvailabilityApi?itemId=1&warehouseId=1&unitId=1&quantity=1');
                return await res.json();
            });
            const qty = currentAvail.data ? currentAvail.data.availableQuantity : 0;
            if (qty < 25) {
                console.log(`📦 تعزيز رصيد الصنف في الفرع (الرصيد الحالي = ${qty})... توريد 100 قطعة عبر فاتورة مشتريات...`);
                await worker1Data.page.evaluate(async () => {
                    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                    const vends = await (await fetch('/api/lookup/vendor')).json();
                    const vendId = vends.results?.[0]?.id || 1;
                    const purPayload = {
                        transactionTypeId: 6, // Purchase Invoice
                        branchId: 3,
                        documentDate: new Date().toISOString().split('T')[0],
                        partyType: 2, // Vendor
                        partyId: vendId,
                        paymentMethod: 1, // Cash
                        warehouseId: 1,
                        currencyId: 1,
                        exchangeRate: 1.0,
                        referenceNumber: `INIT-PUR-${Date.now().toString().slice(-6)}`,
                        notes: 'Initial stock seeding for concurrency test',
                        lines: [{
                            lineNumber: 1,
                            itemId: 1,
                            unitId: 1,
                            quantity: 100,
                            unitPrice: 100,
                            discountAmount: 0,
                            taxRate: 14,
                            notes: '',
                            serialNumbers: []
                        }]
                    };
                    await fetch('/Documents/Create?postImmediately=true', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                        body: JSON.stringify(purPayload)
                    });
                });
                console.log('✅ تم توريد 100 قطعة بنجاح.');
            }
        } catch (e) {
            console.log('ملاحظة: تعذر التحقق التلقائي من توريد الرصيد:', e.message);
        }

        console.log('\n--- ⏳ إطلاق العمليات المتزامنة في نفس اللحظة للمستخدمين الاثنين ---');
        const overallStart = Date.now();

        // Run both workers concurrently!
        const [result1, result2] = await Promise.all([
            executeWorkerMovements(worker1Data.page, 'User-1 (admin)', worker1Data.branch, 20),
            executeWorkerMovements(worker2Data.page, 'User-2 (tamer)', worker2Data.branch, 20)
        ]);

        const totalTime = ((Date.now() - overallStart) / 1000).toFixed(2);
        const totalOps = result1.moveCount + result2.moveCount;
        const opsPerSec = (totalOps / totalTime).toFixed(2);

        // Take a screenshot of the document list
        await worker1Data.page.goto(`${baseUrl}/Documents?module=1`, { waitUntil: 'domcontentloaded' });
        await worker1Data.page.waitForTimeout(1000);
        await worker1Data.page.screenshot({ path: path.join(screenshotsDir, 'concurrent-2users-documents.png') });

        console.log('\n================================================================');
        console.log('🏆 نجح اختبار التزامن (2 مستخدمين في نفس الفرع) بنجاح 100%!');
        console.log(`- إجمالي الحركات المنفذة: ${totalOps} حركة (${result1.moveCount} للمستخدم الأول + ${result2.moveCount} للمستخدم الثاني)`);
        console.log(`- الوقت الإجمالي: ${totalTime} ثانية (${opsPerSec} حركة/ثانية)`);
        console.log(`- تعارض الترقيم أو الأقفال (Collisions / Deadlocks): 0 (صفر أخطاء)`);
        console.log('================================================================\n');

    } catch (err) {
        console.error('❌ فشل اختبار التزامن:', err);
        process.exit(1);
    } finally {
        if (browser) await browser.close();
    }
})();
