const { chromium } = require('playwright');
const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const NUM_SESSIONS = 10;
const BASE_URL = 'http://127.0.0.1:5239';
const AUTH_FILE = path.join(__dirname, 'auth.json');

async function ensureAuth() {
    if (fs.existsSync(AUTH_FILE)) {
        console.log('[Auth] Using existing auth.json credentials...');
        return;
    }
    console.log('[Auth] Performing initial login to generate auth.json...');
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    const ctx = await browser.newContext();
    const page = await ctx.newPage();

    await page.goto(`${BASE_URL}/Account/Login`, { waitUntil: 'domcontentloaded' });
    await page.selectOption('#FiscalYearId', '1');
    await page.fill('#EmployeeCode', 'admin');
    await page.dispatchEvent('#EmployeeCode', 'input');
    await page.waitForFunction(() => document.querySelector('#CompanyId')?.options?.length > 1, { timeout: 25000 });
    await page.selectOption('#CompanyId', '1');
    await page.waitForFunction(() => document.querySelector('#BranchId')?.options?.length > 1, { timeout: 25000 });
    await page.selectOption('#BranchId', '3');
    await page.fill('#Password', '123456');
    await page.click('#loginForm button[type="submit"]');
    await page.waitForURL(url => url.pathname === '/' || url.pathname.includes('/Home'), { timeout: 25000 });

    await ctx.storageState({ path: AUTH_FILE });
    await browser.close();
    console.log('[Auth] Authentication established and saved to auth.json!');
}

async function prepareSession(sessionId) {
    console.log(`\n--- [Session ${sessionId}] Preparing Browser Session ---`);
    const browser = await chromium.launch({
        channel: 'msedge',
        headless: true
    });

    const context = await browser.newContext({
        storageState: AUTH_FILE,
        viewport: { width: 1280, height: 720 },
        locale: 'ar-EG'
    });
    const page = await context.newPage();

    const sessionData = {
        sessionId,
        browser,
        context,
        page,
        capturedResponse: null,
        sessionResult: {
            sessionId,
            status: 'PREPARING',
            httpStatus: null,
            entryNumber: null,
            entryId: null,
            durationMs: null,
            finalUrl: null,
            errors: []
        }
    };

    page.on('pageerror', err => {
        sessionData.sessionResult.errors.push(`PageError: ${err.message}`);
    });

    page.on('dialog', async dialog => {
        const msg = dialog.message();
        sessionData.sessionResult.errors.push(`DialogAlert: ${msg}`);
        await dialog.dismiss().catch(() => {});
    });

    // Intercept POST /JournalEntries/Create to capture response body cleanly
    await page.route('**/JournalEntries/Create', async route => {
        if (route.request().method() === 'POST') {
            try {
                const startFetch = Date.now();
                const response = await route.fetch({ timeout: 90000 });
                const fetchLatency = Date.now() - startFetch;
                const bodyText = await response.text();
                let json = null;
                try { json = JSON.parse(bodyText); } catch (e) {}
                sessionData.capturedResponse = {
                    status: response.status(),
                    json,
                    raw: bodyText,
                    fetchLatency
                };
                await route.fulfill({ response, body: bodyText });
            } catch (err) {
                sessionData.sessionResult.errors.push(`RouteFetch: ${err.message}`);
                await route.continue().catch(() => {});
            }
        } else {
            await route.continue();
        }
    });

    // Navigate directly to /JournalEntries/Create
    console.log(`[Session ${sessionId}] Navigating to /JournalEntries/Create...`);
    await page.goto(`${BASE_URL}/JournalEntries/Create`, { waitUntil: 'domcontentloaded', timeout: 35000 });

    await page.waitForFunction(() => {
        const rows = document.querySelectorAll('#journalLinesTable tbody tr');
        return rows && rows.length >= 2;
    }, { timeout: 25000 });

    // Populate Mock Data
    const testAmount = (sessionId * 1000);
    const entryDesc = `[CONCURRENT_TEST_BATCH] Session #${sessionId} - Parallel Stress Run Amount ${testAmount}`;
    console.log(`[Session ${sessionId}] Populating form (Amount: ${testAmount} EGP)...`);

    const descInput = page.locator('input[placeholder*="قيد تسوية رواتب"]');
    await descInput.fill(entryDesc);

    const rows = page.locator('#journalLinesTable tbody tr');
    const row1 = rows.nth(0);
    const row2 = rows.nth(1);

    // Line 1: Finished Goods (110301)
    const row1AccInput = row1.locator('input[placeholder*="ابحث برقم"]');
    await row1AccInput.fill('110301');
    await page.waitForSelector('.dropdown-menu.show .dropdown-item', { timeout: 15000 });
    await page.click('.dropdown-menu.show .dropdown-item');
    await page.waitForTimeout(400);

    const row1DebitInput = row1.locator('td:nth-child(5) input[type="number"]');
    await row1DebitInput.fill(testAmount.toString());
    await row1DebitInput.dispatchEvent('input');
    await page.waitForTimeout(400);

    // Line 2: Paid-in Capital (310101)
    const row2AccInput = row2.locator('input[placeholder*="ابحث برقم"]');
    await row2AccInput.fill('310101');
    await page.waitForSelector('.dropdown-menu.show .dropdown-item', { timeout: 15000 });
    await page.click('.dropdown-menu.show .dropdown-item');
    await page.waitForTimeout(400);

    const row2CreditInput = row2.locator('td:nth-child(6) input[type="number"]');
    await row2CreditInput.fill(testAmount.toString());
    await row2CreditInput.dispatchEvent('input');
    await page.waitForTimeout(400);

    // Wait for Save button to become enabled
    await page.waitForFunction(() => {
        const btn = document.querySelector('button.btn-primary:not([disabled])');
        return btn && btn.innerText.includes('حفظ قيد اليومية');
    }, { timeout: 15000 });

    sessionData.sessionResult.status = 'READY';
    console.log(`[Session ${sessionId}] Form is balanced and Save button is READY.`);

    return sessionData;
}

async function triggerSave(session) {
    const { sessionId, page, sessionResult } = session;
    console.log(`[Session ${sessionId}] >>> FIRING SAVE BUTTON CLICK <<<`);
    const clickStart = Date.now();

    try {
        const saveBtn = page.locator('button.btn-primary:has-text("حفظ قيد اليومية")');
        await saveBtn.click();

        // Wait for redirect to Details page
        await page.waitForURL(url => url.pathname.includes('/JournalEntries/Details'), { timeout: 90000 });
        sessionResult.finalUrl = page.url();
        sessionResult.status = 'SUCCESS';
    } catch (err) {
        sessionResult.finalUrl = page.url();
        sessionResult.status = 'FAILED_OR_TIMEOUT';
        sessionResult.errors.push(`WaitDetails: ${err.message}`);
    }

    sessionResult.durationMs = Date.now() - clickStart;

    if (session.capturedResponse) {
        sessionResult.httpStatus = session.capturedResponse.status;
        if (session.capturedResponse.json) {
            sessionResult.entryNumber = session.capturedResponse.json.entryNumber || (session.capturedResponse.json.data && session.capturedResponse.json.data.entryNumber);
            sessionResult.entryId = session.capturedResponse.json.id;
            sessionResult.message = session.capturedResponse.json.message;
            if (session.capturedResponse.json.success === false) {
                sessionResult.status = 'BACKEND_REJECTED';
            }
        } else {
            sessionResult.message = session.capturedResponse.raw;
        }
    }

    console.log(`[Session ${sessionId}] Finished: Status=${sessionResult.status}, EntryNumber=${sessionResult.entryNumber || 'N/A'}, Latency=${sessionResult.durationMs}ms`);
    return sessionResult;
}

(async () => {
    console.log(`=============================================================`);
    console.log(`  STARTING 10-SESSION CONCURRENT E2E UI STRESS TEST`);
    console.log(`=============================================================`);
    const overallStart = Date.now();

    // Step 0: Ensure valid authentication state
    await ensureAuth();

    // Step 1: Initialize and arm all 10 parallel browser sessions
    const preparedSessions = [];
    for (let i = 1; i <= NUM_SESSIONS; i++) {
        try {
            const session = await prepareSession(i);
            preparedSessions.push(session);
        } catch (prepErr) {
            console.error(`[Session ${i}] Preparation failed:`, prepErr.message);
            preparedSessions.push({
                sessionId: i,
                browser: null,
                sessionResult: {
                    sessionId: i,
                    status: 'PREP_FAILED',
                    httpStatus: null,
                    entryNumber: null,
                    entryId: null,
                    durationMs: null,
                    finalUrl: null,
                    errors: [prepErr.message]
                }
            });
        }
    }

    const readySessions = preparedSessions.filter(s => s.sessionResult && s.sessionResult.status === 'READY');
    console.log(`\n=============================================================`);
    console.log(`  PREPARATION COMPLETE: ${readySessions.length}/${NUM_SESSIONS} SESSIONS ARE ARMED AND READY!`);
    console.log(`=============================================================`);

    if (readySessions.length === 0) {
        console.error('No sessions were prepared successfully. Aborting test.');
        process.exit(1);
    }

    // Small stabilization pause before releasing barrier
    await new Promise(r => setTimeout(r, 1000));

    // Step 2: Simultaneous Concurrent Save Execution across all armed sessions
    console.log(`\n*************************************************************`);
    console.log(`  >>> RELEASING THE BARRIER: FIRING ALL ${readySessions.length} SESSIONS SIMULTANEOUSLY! <<<`);
    console.log(`*************************************************************\n`);

    const concurrentStart = Date.now();
    const savePromises = readySessions.map(s => triggerSave(s));

    const finalResults = await Promise.all(savePromises);
    const concurrentDuration = ((Date.now() - concurrentStart) / 1000).toFixed(2);

    // Merge in any prep-failed results for full 10-session accounting
    preparedSessions
        .filter(s => s.sessionResult.status === 'PREP_FAILED')
        .forEach(s => finalResults.push(s.sessionResult));

    // Step 3: Cleanup all browser sessions
    console.log('\nClosing all browser sessions...');
    for (const session of preparedSessions) {
        if (session.browser) {
            await session.browser.close().catch(() => {});
        }
    }

    const totalRunTime = ((Date.now() - overallStart) / 1000).toFixed(2);
    console.log(`\n=============================================================`);
    console.log(`  TEST COMPLETED: ${finalResults.length} SESSIONS`);
    console.log(`  Concurrent Execution Window: ${concurrentDuration}s`);
    console.log(`  Total Run Time: ${totalRunTime}s`);
    console.log(`=============================================================`);

    // Results Summary Table
    console.log('\n--- CONCURRENT TEST RESULTS TABLE ---');
    console.table(finalResults.sort((a, b) => a.sessionId - b.sessionId).map(r => ({
        Session: r.sessionId,
        Status: r.status,
        HTTP: r.httpStatus,
        EntryNumber: r.entryNumber || 'N/A',
        EntryId: r.entryId || 'N/A',
        DurationMs: r.durationMs != null ? `${r.durationMs}ms` : 'N/A',
        Errors: r.errors.length > 0 ? r.errors.join('; ').substring(0, 60) : 'None'
    })));

    // Save results to JSON file
    const resultsPath = path.join(__dirname, 'concurrent-results.json');
    fs.writeFileSync(resultsPath, JSON.stringify({
        totalRunTime,
        concurrentDuration,
        results: finalResults
    }, null, 2));
    console.log(`Detailed results saved to: ${resultsPath}`);

    // Concurrency & Sequence Validation
    const entryNumbers = finalResults.map(r => r.entryNumber).filter(Boolean);
    const uniqueEntryNumbers = new Set(entryNumbers);
    console.log(`\n--- CONCURRENCY ANALYSIS ---`);
    console.log(`Total Entry Numbers Generated: ${entryNumbers.length}`);
    console.log(`Unique Entry Numbers: ${uniqueEntryNumbers.size}`);
    console.log(`Generated Sequence: ${entryNumbers.join(', ')}`);

    if (uniqueEntryNumbers.size === NUM_SESSIONS && entryNumbers.length === NUM_SESSIONS) {
        console.log(`\n>>> VERIFICATION PASSED: ALL 10 CONCURRENT ENTRIES WERE CREATED WITH 100% UNIQUE NUMBERS! <<<`);
    } else if (uniqueEntryNumbers.size === entryNumbers.length && entryNumbers.length > 0) {
        console.log(`\n>>> ALL CREATED ENTRIES ARE UNIQUE (${uniqueEntryNumbers.size} entries), but some sessions failed.`);
    } else {
        console.error(`\n>>> WARNING: DUPLICATE ENTRY NUMBERS DETECTED! Potential sequence race condition!`);
    }

    // Direct SQL Server Verification via SQLCMD
    console.log('\n--- SQL SERVER VERIFICATION (LATEST COMMITTED ENTRIES) ---');
    try {
        const sqlCmd = `sqlcmd -S tcp:SQL1002.site4now.net,1433 -d db_ab95d6_sewtec2026 -U db_ab95d6_sewtec2026_admin -P "Mostafa@1981$" -Q "SELECT TOP 15 Id, EntryNumber, BranchId, FiscalYearId, TotalDebitLocal, TotalCreditLocal, CreatedAt FROM JournalEntryHeaders ORDER BY Id DESC"`;
        const sqlOutput = execSync(sqlCmd, { encoding: 'utf-8' });
        console.log(sqlOutput);
    } catch (sqlErr) {
        console.error('Failed to run sqlcmd query:', sqlErr.message);
    }
})();
