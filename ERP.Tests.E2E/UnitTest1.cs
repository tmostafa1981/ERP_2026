using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace ERP.Tests.E2E
{
    public class SerialNumbersE2ETests
    {
        private readonly ITestOutputHelper _output;
        private const string BaseUrl = "http://localhost:5239";

        public SerialNumbersE2ETests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task FullSerialNumberWorkflow_ExcelCopyPaste_AvailableSerialsIssue_Concurrency_And_Return()
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false,
                SlowMo = 100
            });

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
                IgnoreHTTPSErrors = true
            });

            var page = await context.NewPageAsync();
            string runPrefix = DateTime.UtcNow.ToString("HHmmss");

            // ─── 0. LOGIN AS ADMIN ─────────────────────────────────────────────
            _output.WriteLine("Navigating to Login page...");
            await page.GotoAsync($"{BaseUrl}/Account/Login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Step 1: Select Fiscal Year (2026)
            var fyOptions = await page.Locator("#FiscalYearId option").AllAsync();
            string? fyVal = null;
            foreach (var opt in fyOptions)
            {
                var txt = await opt.InnerTextAsync();
                var val = await opt.GetAttributeAsync("value");
                if (!string.IsNullOrWhiteSpace(val) && txt.Contains("2026"))
                {
                    fyVal = val;
                    break;
                }
            }
            if (string.IsNullOrEmpty(fyVal))
            {
                foreach (var opt in fyOptions)
                {
                    var val = await opt.GetAttributeAsync("value");
                    if (!string.IsNullOrWhiteSpace(val)) { fyVal = val; break; }
                }
            }
            Assert.False(string.IsNullOrEmpty(fyVal), "No FiscalYear option found on Login page.");
            await page.SelectOptionAsync("#FiscalYearId", fyVal!);

            // Step 2: Enter Employee Code "ADMIN"
            await page.FillAsync("#EmployeeCode", "ADMIN");
            await page.DispatchEventAsync("#EmployeeCode", "input");

            // Step 3: Wait for Company dropdown to populate and enable
            await page.WaitForFunctionAsync("() => !document.querySelector('#CompanyId').disabled && document.querySelector('#CompanyId').options.length > 1", null, new PageWaitForFunctionOptions { Timeout = 15000 });
            var compVal = await page.EvaluateAsync<string>("() => { const s = document.querySelector('#CompanyId'); if (!s.value) { s.value = s.options[1].value; s.dispatchEvent(new Event('change')); } return s.value; }");
            Assert.False(string.IsNullOrEmpty(compVal));

            // Step 4: Wait for Branch dropdown to populate and enable
            await page.WaitForFunctionAsync("() => !document.querySelector('#BranchId').disabled && document.querySelector('#BranchId').options.length > 1", null, new PageWaitForFunctionOptions { Timeout = 15000 });
            var branchVal = await page.EvaluateAsync<string>("() => { const s = document.querySelector('#BranchId'); if (!s.value) { s.value = s.options[1].value; } return s.value; }");
            Assert.False(string.IsNullOrEmpty(branchVal));

            // Step 5: Password & Submit
            await page.FillAsync("#Password", "Admin@123");
            await page.ClickAsync("#loginForm button[type='submit']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            _output.WriteLine($"Logged in successfully. Current URL: {page.Url}");

            // ─── 1. ENSURE SERIALIZED ITEM EXISTS ──────────────────────────────
            await page.GotoAsync($"{BaseUrl}/Item/Index", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Check if a serialized item already exists or create a dedicated one via form/AJAX
            var itemSetup = await page.EvaluateAsync<JsonElement>(@"async (prefix) => {
                const lookupRes = await fetch('/api/lookup/item?q=SN-TEST-ITEM').then(r => r.json());
                if (lookupRes && lookupRes.results) {
                    const found = lookupRes.results.find(x => x.trackingType === 1 || x.isSerial === true);
                    if (found) return { id: found.id, code: found.code, name: found.name };
                }

                // Get token, groupId, unitId from /Item/Index page DOM
                const token = document.querySelector('input[name=""__RequestVerificationToken""]')?.value || '';
                const groupOpt = document.querySelector('select[name=""ItemGroupId""] option:not([value=""""]):not([value=""0""])');
                const unitOpt = document.querySelector('select[name=""baseUnitId""] option:not([value=""""]):not([value=""0""])');
                const groupId = groupOpt ? groupOpt.value : '1';
                const unitId = unitOpt ? unitOpt.value : '1';
                const code = 'SN-TEST-ITEM-' + prefix;

                const formData = new URLSearchParams();
                formData.append('__RequestVerificationToken', token);
                formData.append('Code', code);
                formData.append('isManualCode', 'true');
                formData.append('NameAr', 'صنف سيريال اختباري ' + prefix);
                formData.append('NameEn', 'Serial Test Item ' + prefix);
                formData.append('ItemGroupId', groupId);
                formData.append('baseUnitId', unitId);
                formData.append('TrackingType', '1');
                formData.append('salesPrice', '1500');
                formData.append('purchasePrice', '1000');
                formData.append('IsActive', 'true');

                await fetch('/Item/Create', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'X-Requested-With': 'XMLHttpRequest'
                    },
                    body: formData.toString()
                });

                const verifyRes = await fetch('/api/lookup/item?q=' + encodeURIComponent(code)).then(r => r.json());
                const created = (verifyRes && verifyRes.results) ? verifyRes.results[0] : null;
                return created ? { id: created.id, code: created.code, name: created.name } : { id: 0, code: '', name: '' };
            }", runPrefix);

            int testItemId = itemSetup.GetProperty("id").GetInt32();
            string testItemCode = itemSetup.GetProperty("code").GetString() ?? "";
            string testItemName = itemSetup.GetProperty("name").GetString() ?? "";
            _output.WriteLine($"Using Serialized Item: Id={testItemId}, Code={testItemCode}, Name={testItemName}");
            Assert.True(testItemId > 0, "Failed to find or create a serialized test item.");

            // ─── 2. OPEN PURCHASE INVOICE (RECEIVE STOCK) & ENSURE VENDOR EXISTS ─
            await page.GotoAsync($"{BaseUrl}/Documents/Create?module=2", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Ensure a Vendor and Customer exist in the current branch
            await page.EvaluateAsync(@"async (prefix) => {
                const token = document.querySelector('input[name=""__RequestVerificationToken""]')?.value || '';
                // Check if vendor select has options
                const partySelects = document.querySelectorAll('select.form-select');
                let hasParty = false;
                partySelects.forEach(s => {
                    if (s.options.length > 1 && s.innerHTML.includes('اختر')) hasParty = true;
                });
            }", runPrefix);

            // Make sure we are on a Purchase Invoice (InventoryEffect == 3) and have a Vendor & Warehouse selected
            var purchaseSetup = await page.EvaluateAsync<JsonElement>(@"async () => {
                // Find the Vue app root or check dropdowns
                return { url: window.location.href };
            }");

            // Select Warehouse and Party if needed, or create a Vendor/Customer if empty
            bool hasVendorOption = await page.EvaluateAsync<bool>(@"() => {
                const selects = Array.from(document.querySelectorAll('select.form-select'));
                const partySelect = selects.find(s => s.options[0] && s.options[0].text.includes('المورد'));
                if (partySelect && partySelect.options.length > 1) {
                    partySelect.value = partySelect.options[1].value;
                    partySelect.dispatchEvent(new Event('change'));
                    return true;
                }
                return false;
            }");

            if (!hasVendorOption)
            {
                await page.GotoAsync($"{BaseUrl}/Vendor/Index", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                await page.EvaluateAsync(@"async (prefix) => {
                    const token = document.querySelector('input[name=""__RequestVerificationToken""]')?.value || '';
                    const catOpt = document.querySelector('select[name=""VendorCategoryId""] option:not([value=""""]):not([value=""0""])');
                    const currOpt = document.querySelector('select[name=""CurrencyId""] option:not([value=""""]):not([value=""0""])');
                    const fd = new URLSearchParams();
                    fd.append('__RequestVerificationToken', token);
                    fd.append('Code', 'V-TEST-' + prefix);
                    fd.append('isManualCode', 'true');
                    fd.append('NameAr', 'مورد اختبار السيريالات');
                    fd.append('NameEn', 'Serial Test Vendor');
                    fd.append('VendorCategoryId', catOpt ? catOpt.value : '1');
                    fd.append('CurrencyId', currOpt ? currOpt.value : '1');
                    fd.append('IsActive', 'true');
                    await fetch('/Vendor/Create', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'X-Requested-With': 'XMLHttpRequest' },
                        body: fd.toString()
                    });
                }", runPrefix);

                await page.GotoAsync($"{BaseUrl}/Customer/Index", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                await page.EvaluateAsync(@"async (prefix) => {
                    const token = document.querySelector('input[name=""__RequestVerificationToken""]')?.value || '';
                    const catOpt = document.querySelector('select[name=""CustomerCategoryId""] option:not([value=""""]):not([value=""0""])');
                    const currOpt = document.querySelector('select[name=""CurrencyId""] option:not([value=""""]):not([value=""0""])');
                    const fd = new URLSearchParams();
                    fd.append('__RequestVerificationToken', token);
                    fd.append('Code', 'C-TEST-' + prefix);
                    fd.append('isManualCode', 'true');
                    fd.append('NameAr', 'عميل اختبار السيريالات');
                    fd.append('NameEn', 'Serial Test Customer');
                    fd.append('CustomerCategoryId', catOpt ? catOpt.value : '1');
                    fd.append('CurrencyId', currOpt ? currOpt.value : '1');
                    fd.append('IsActive', 'true');
                    await fetch('/Customer/Create', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'X-Requested-With': 'XMLHttpRequest' },
                        body: fd.toString()
                    });
                }", runPrefix);

                await page.GotoAsync($"{BaseUrl}/Documents/Create?module=2", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            }

            // Select Vendor, Warehouse, and Cash/Credit on Purchase Invoice
            await page.EvaluateAsync(@"() => {
                const selects = Array.from(document.querySelectorAll('select.form-select'));
                selects.forEach(s => {
                    if (!s.value && s.options.length > 1 && !s.disabled) {
                        const firstText = s.options[0].text || '';
                        if (firstText.includes('اختر')) {
                            s.value = s.options[1].value;
                            s.dispatchEvent(new Event('change'));
                        }
                    }
                });
                // Set PaymentMethod to Credit (2) so CashBankAccount is not required if none is seeded
                const pmSelect = selects.find(s => Array.from(s.options).some(o => o.text.includes('آجل')));
                if (pmSelect) {
                    pmSelect.value = '2';
                    pmSelect.dispatchEvent(new Event('change'));
                }
            }");

            // Select the serialized item on Line 1 via Item Search input
            var lineItemInput = page.Locator("table tbody tr").First.Locator("input[placeholder*='ابحث بكود']");
            await lineItemInput.FillAsync(testItemCode);
            await page.WaitForSelectorAsync(".list-group-item-action", new PageWaitForSelectorOptions { Timeout = 10000 });
            await page.Locator(".list-group-item-action").First.ClickAsync();

            // Wait for the Serials button (.btn-open-serials) to appear on Line 1
            await page.WaitForSelectorAsync(".btn-open-serials", new PageWaitForSelectorOptions { Timeout = 10000 });
            await page.ClickAsync(".btn-open-serials");
            await page.WaitForSelectorAsync("#serialsModal.show", new PageWaitForSelectorOptions { Timeout = 10000 });

            // ─── CASE 1 & CASE 2: PASTE 100 SERIALS FROM EXCEL WITH EMPTY ROWS, SPACES, TABS, AND LEADING ZEROS ─
            _output.WriteLine("Testing Case 1 & Case 2: Pasting 100 serials from Excel with tabs, empty lines, spaces, and leading zeros...");
            var hundredSerials = Enumerable.Range(1, 100)
                .Select(i => $"00{runPrefix}{i:D4}")
                .ToList();

            // Format like an Excel copy/paste with Tabs (\t), Empty Rows (\r\n\r\n), Extra Spaces, Commas, and Semicolons
            var excelRows = new List<string> { "   ", "" };
            for (int i = 0; i < 100; i += 4)
            {
                excelRows.Add($"  {hundredSerials[i]} \t {hundredSerials[i + 1]}  \t  {hundredSerials[i + 2]} , {hundredSerials[i + 3]} ; ");
                if (i % 20 == 0) excelRows.Add("   \t   "); // Empty row with tabs/spaces
            }
            string excelClipboardText = string.Join("\r\n", excelRows);

            await page.FillAsync("#pasteSerialsTextarea", excelClipboardText);
            await page.ClickAsync("#btnValidatePastedSerials");

            await page.WaitForSelectorAsync("#pasteValidationResultsSection", new PageWaitForSelectorOptions { Timeout = 10000 });
            string totalCountText = await page.InnerTextAsync("#pasteTotalCount");
            string validCountText = await page.InnerTextAsync("#pasteValidCount");
            string firstValidatedSn = await page.Locator("#pasteValidationTable tbody tr").First.Locator("td").Nth(2).InnerTextAsync();
            string firstValidatedStatus = await page.Locator("#pasteValidationTable tbody tr").First.Locator("td").Nth(3).InnerTextAsync();

            _output.WriteLine($"Case 1 & 2 Result -> Total={totalCountText}, Valid={validCountText}, FirstSN={firstValidatedSn}, Status={firstValidatedStatus}");
            Assert.Equal("100", totalCountText.Trim());
            Assert.Equal("100", validCountText.Trim());
            Assert.Equal($"00{runPrefix}0001", firstValidatedSn.Trim()); // Leading zeros preserved!
            Assert.Contains("صالح للإضافة", firstValidatedStatus);

            // Save valid serials into the line
            await page.ClickAsync("#btnSaveValidPastedSerials");
            int addedInLineCount = await page.Locator("#addedLineSerialsList .added-serial-item").CountAsync();
            _output.WriteLine($"Saved valid serials into line: count={addedInLineCount}");
            Assert.Equal(100, addedInLineCount);

            // Close modal and save Purchase Invoice 1
            await page.ClickAsync("#btnCloseSerialsModal");
            await page.WaitForTimeoutAsync(400);

            // Verify line quantity automatically updated to 100
            string lineQtyVal = await page.Locator("table tbody tr").First.Locator("input[type='number']").First.InputValueAsync();
            _output.WriteLine($"Line Quantity after saving 100 pasted serials: {lineQtyVal}");
            Assert.Equal("100", lineQtyVal);

            // Save Purchase Invoice 1
            await page.ClickAsync("button:has-text('حفظ المستند')");
            await page.WaitForSelectorAsync(".swal2-popup", new PageWaitForSelectorOptions { Timeout = 15000 });
            string saveMsg1 = await page.InnerTextAsync("#swal2-html-container");
            _output.WriteLine($"Purchase Invoice 1 Save Result: {saveMsg1}");
            Assert.Contains("بنجاح", saveMsg1);
            await page.ClickAsync(".swal2-confirm");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // ─── CASE 3 & CASE 4: PASTE SERIALS WITH DUPLICATES & ALREADY EXISTING IN DB ─
            _output.WriteLine("Testing Case 3 & Case 4: Pasting serials with duplicates and already existing serials...");
            await page.GotoAsync($"{BaseUrl}/Documents/Create?module=2", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await page.EvaluateAsync(@"() => {
                const selects = Array.from(document.querySelectorAll('select.form-select'));
                selects.forEach(s => {
                    if (!s.value && s.options.length > 1 && !s.disabled) {
                        const firstText = s.options[0].text || '';
                        if (firstText.includes('اختر')) {
                            s.value = s.options[1].value;
                            s.dispatchEvent(new Event('change'));
                        }
                    }
                });
                const pmSelect = selects.find(s => Array.from(s.options).some(o => o.text.includes('آجل')));
                if (pmSelect) {
                    pmSelect.value = '2';
                    pmSelect.dispatchEvent(new Event('change'));
                }
            }");

            lineItemInput = page.Locator("table tbody tr").First.Locator("input[placeholder*='ابحث بكود']");
            await lineItemInput.FillAsync(testItemCode);
            await page.WaitForSelectorAsync(".list-group-item-action", new PageWaitForSelectorOptions { Timeout = 10000 });
            await page.Locator(".list-group-item-action").First.ClickAsync();

            await page.WaitForSelectorAsync(".btn-open-serials", new PageWaitForSelectorOptions { Timeout = 10000 });
            await page.ClickAsync(".btn-open-serials");
            await page.WaitForSelectorAsync("#serialsModal.show", new PageWaitForSelectorOptions { Timeout = 10000 });

            // Paste: 2 valid new serials + 1 duplicate in paste + 1 already existing in DB from Invoice 1
            string mixedPaste = $"00{runPrefix}NEW1\r\n00{runPrefix}NEW2\t00{runPrefix}NEW1\r\n00{runPrefix}0001";
            await page.FillAsync("#pasteSerialsTextarea", mixedPaste);
            await page.ClickAsync("#btnValidatePastedSerials");
            await page.WaitForSelectorAsync("#pasteValidationResultsSection", new PageWaitForSelectorOptions { Timeout = 10000 });

            Assert.Equal("4", (await page.InnerTextAsync("#pasteTotalCount")).Trim());
            Assert.Equal("2", (await page.InnerTextAsync("#pasteValidCount")).Trim());
            Assert.Equal("1", (await page.InnerTextAsync("#pasteDuplicateCount")).Trim());
            Assert.Equal("1", (await page.InnerTextAsync("#pasteExistingCount")).Trim());

            string tableHtml = await page.InnerTextAsync("#pasteValidationTable");
            _output.WriteLine($"Case 3 & 4 Validation Table:\n{tableHtml}");
            Assert.Contains("صالح للإضافة", tableHtml);
            Assert.Contains("مكرر في البيانات الملصقة", tableHtml);
            Assert.Contains("موجود مسبقًا", tableHtml);

            // Click Save Valid Serials -> Only the 2 valid serials should be added, while the 2 rejected remain in the table!
            await page.ClickAsync("#btnSaveValidPastedSerials");
            Assert.Equal(2, await page.Locator("#addedLineSerialsList .added-serial-item").CountAsync());
            Assert.Equal(2, await page.Locator("#pasteValidationTable tbody tr").CountAsync());

            // Test "حذف السيريالات المحددة" (Delete Selected) on the first remaining rejected row
            await page.Locator("#pasteValidationTable tbody tr").First.Locator(".paste-row-checkbox").CheckAsync();
            await page.ClickAsync("#btnDeleteSelectedPastedSerials");
            Assert.Equal(1, await page.Locator("#pasteValidationTable tbody tr").CountAsync());

            // Test "مسح البيانات" (Clear Data)
            await page.ClickAsync("#btnClearPastedSerials");
            Assert.Equal(0, await page.Locator("#pasteValidationTable tbody tr").CountAsync());

            await page.ClickAsync("#btnCloseSerialsModal");
            await page.WaitForTimeoutAsync(300);

            // Save Purchase Invoice 2 (with the 2 valid serials)
            await page.ClickAsync("button:has-text('حفظ المستند')");
            await page.WaitForSelectorAsync(".swal2-popup", new PageWaitForSelectorOptions { Timeout = 15000 });
            Assert.Contains("بنجاح", await page.InnerTextAsync("#swal2-html-container"));
            await page.ClickAsync(".swal2-confirm");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // ─── CASE 5, CASE 6, CASE 7: ISSUE SCREEN - AVAILABLE SERIALS, SERVER-SIDE SEARCH, MULTI-SELECT & AUTO-QUANTITY ─
            _output.WriteLine("Testing Case 5, Case 6, Case 7: Issue screen available serials display, search, multi-select, and auto-quantity...");
            await page.GotoAsync($"{BaseUrl}/Documents/Create?module=1", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await page.EvaluateAsync(@"() => {
                const selects = Array.from(document.querySelectorAll('select.form-select'));
                selects.forEach(s => {
                    if (!s.value && s.options.length > 1 && !s.disabled) {
                        const firstText = s.options[0].text || '';
                        if (firstText.includes('اختر')) {
                            s.value = s.options[1].value;
                            s.dispatchEvent(new Event('change'));
                        }
                    }
                });
                const pmSelect = selects.find(s => Array.from(s.options).some(o => o.text.includes('آجل')));
                if (pmSelect) {
                    pmSelect.value = '2';
                    pmSelect.dispatchEvent(new Event('change'));
                }
            }");

            lineItemInput = page.Locator("table tbody tr").First.Locator("input[placeholder*='ابحث بكود']");
            await lineItemInput.FillAsync(testItemCode);
            await page.WaitForSelectorAsync(".list-group-item-action", new PageWaitForSelectorOptions { Timeout = 10000 });
            await page.Locator(".list-group-item-action").First.ClickAsync();

            await page.WaitForSelectorAsync(".btn-open-serials", new PageWaitForSelectorOptions { Timeout = 10000 });
            await page.ClickAsync(".btn-open-serials");
            await page.WaitForSelectorAsync("#serialsModal.show", new PageWaitForSelectorOptions { Timeout = 10000 });

            // Wait for Available Serials table to load from DB
            await page.WaitForSelectorAsync("#availableSerialsTable tbody tr.available-serial-row", new PageWaitForSelectorOptions { Timeout = 10000 });
            int totalAvail = int.Parse((await page.InnerTextAsync("#availableSerialsTotalBadge")).Trim());
            _output.WriteLine($"Case 5 Result -> Total Available Serials in Warehouse: {totalAvail}");
            Assert.True(totalAvail >= 102, $"Expected at least 102 available serials, got {totalAvail}");

            // Case 6: Search for a specific serial ("0055") via server-side search
            string targetSn1 = $"00{runPrefix}0055";
            string targetSn2 = $"00{runPrefix}0056";
            string targetSn3 = $"00{runPrefix}0057";

            await page.FillAsync("#availableSerialSearchInput", targetSn1);
            await page.ClickAsync("#btnSearchAvailableSerials");
            await page.WaitForTimeoutAsync(500);

            int searchRowsCount = await page.Locator("#availableSerialsTable tbody tr.available-serial-row").CountAsync();
            string searchedRowText = await page.Locator("#availableSerialsTable tbody tr.available-serial-row").First.InnerTextAsync();
            _output.WriteLine($"Case 6 Search Result for '{targetSn1}' -> Rows={searchRowsCount}, Text={searchedRowText}");
            Assert.Equal(1, searchRowsCount);
            Assert.Contains(targetSn1, searchedRowText);

            // Case 7: Select multiple serials (targetSn1, targetSn2, targetSn3) and verify auto-quantity calculation
            await page.Locator("#availableSerialsTable tbody tr.available-serial-row").First.ClickAsync();

            await page.FillAsync("#availableSerialSearchInput", targetSn2);
            await page.ClickAsync("#btnSearchAvailableSerials");
            await page.WaitForTimeoutAsync(400);
            await page.Locator("#availableSerialsTable tbody tr.available-serial-row").First.ClickAsync();

            await page.FillAsync("#availableSerialSearchInput", targetSn3);
            await page.ClickAsync("#btnSearchAvailableSerials");
            await page.WaitForTimeoutAsync(400);
            await page.Locator("#availableSerialsTable tbody tr.available-serial-row").First.ClickAsync();

            string selectedCountBadge = (await page.InnerTextAsync("#selectedIssueSerialsCountBadge")).Trim();
            string selectedChipsText = await page.InnerTextAsync("#selectedIssueSerialsContainer");
            _output.WriteLine($"Case 7 Multi-Select -> SelectedCount={selectedCountBadge}, Chips={selectedChipsText}");
            Assert.Equal("3", selectedCountBadge);
            Assert.Contains(targetSn1, selectedChipsText);
            Assert.Contains(targetSn2, selectedChipsText);
            Assert.Contains(targetSn3, selectedChipsText);

            await page.ClickAsync("#btnCloseSerialsModal");
            await page.WaitForTimeoutAsync(300);

            // Verify Line Quantity automatically updated to 3
            string issueLineQty = await page.Locator("table tbody tr").First.Locator("input[type='number']").First.InputValueAsync();
            _output.WriteLine($"Issue Line Quantity auto-calculated: {issueLineQty}");
            Assert.Equal("3", issueLineQty);

            // Save Sales Invoice (Issue Document)
            await page.ClickAsync("button:has-text('حفظ المستند')");
            await page.WaitForSelectorAsync(".swal2-popup", new PageWaitForSelectorOptions { Timeout = 15000 });
            string issueSaveMsg = await page.InnerTextAsync("#swal2-html-container");
            _output.WriteLine($"Sales Invoice Issue Save Result: {issueSaveMsg}");
            Assert.Contains("بنجاح", issueSaveMsg);
            await page.ClickAsync(".swal2-confirm");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // ─── CASE 8, CASE 9, CASE 10: WRONG WAREHOUSE, ALREADY ISSUED, AND CONCURRENT RACE CONDITION ─
            _output.WriteLine("Testing Case 8, Case 9, Case 10 on server endpoints & document save...");
            await page.GotoAsync($"{BaseUrl}/Documents/Create?module=1", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Verify issued serial (targetSn1) no longer appears in Available Serials
            var checkIssuedNotAvailable = await page.EvaluateAsync<int>(@"async ({ itemId, sn }) => {
                const whSelect = Array.from(document.querySelectorAll('select.form-select')).find(s => s.options[0] && s.options[0].text.includes('المستودع'));
                const whId = whSelect && whSelect.options.length > 1 ? whSelect.options[1].value : 1;
                const res = await fetch(`/Documents/GetAvailableSerialsPagedApi?itemId=${itemId}&warehouseId=${whId}&search=${encodeURIComponent(sn)}`).then(r => r.json());
                return res.data.totalCount;
            }", new { itemId = testItemId, sn = targetSn1 });
            _output.WriteLine($"Issued serial '{targetSn1}' count in available serials list: {checkIssuedNotAvailable}");
            Assert.Equal(0, checkIssuedNotAvailable);

            // Case 8: Attempt to validate/issue serial from a different warehouse (warehouseId = 9999)
            var case8Res = await page.EvaluateAsync<JsonElement>(@"async ({ itemId, sn }) => {
                const res = await fetch('/Documents/ValidateSerialForIssueApi', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ itemId: itemId, warehouseId: 9999, branchId: 1, serialNumber: sn })
                }).then(r => r.json());
                return res;
            }", new { itemId = testItemId, sn = $"00{runPrefix}0001" });
            _output.WriteLine($"Case 8 (Wrong Warehouse) Result: {case8Res}");
            Assert.False(case8Res.GetProperty("success").GetBoolean());
            Assert.Contains("مستودع", case8Res.GetProperty("message").GetString());

            // Case 9: Attempt to validate/issue an already-issued serial (targetSn1)
            var case9Res = await page.EvaluateAsync<JsonElement>(@"async ({ itemId, sn }) => {
                const whSelect = Array.from(document.querySelectorAll('select.form-select')).find(s => s.options[0] && s.options[0].text.includes('المستودع'));
                const whId = whSelect && whSelect.options.length > 1 ? parseInt(whSelect.options[1].value) : 1;
                const res = await fetch('/Documents/ValidateSerialForIssueApi', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ itemId: itemId, warehouseId: whId, branchId: 1, serialNumber: sn })
                }).then(r => r.json());
                return res;
            }", new { itemId = testItemId, sn = targetSn1 });
            _output.WriteLine($"Case 9 (Already Issued Serial) Result: {case9Res}");
            Assert.False(case9Res.GetProperty("success").GetBoolean());
            Assert.Contains("تم بيعه مسبقاً", case9Res.GetProperty("message").GetString());

            // Case 10: Two simultaneous requests attempting to issue the same serial ("00{runPrefix}0080") at the exact same time
            string raceSn = $"00{runPrefix}0080";
            var case10Res = await page.EvaluateAsync<JsonElement>(@"async ({ itemId, sn }) => {
                const token = document.querySelector('#afForm input[name=""__RequestVerificationToken""]')?.value || document.querySelector('input[name=""__RequestVerificationToken""]')?.value || '';
                const selects = Array.from(document.querySelectorAll('select.form-select'));
                const typeSelect = selects[0];
                const ttId = parseInt(typeSelect.value);
                const partySelect = selects.find(s => s.options[0] && s.options[0].text.includes('العميل'));
                const partyId = partySelect && partySelect.options.length > 1 ? parseInt(partySelect.options[1].value) : 1;
                const whSelect = selects.find(s => s.options[0] && s.options[0].text.includes('المستودع'));
                const whId = whSelect && whSelect.options.length > 1 ? parseInt(whSelect.options[1].value) : 1;

                const payload = {
                    transactionTypeId: ttId,
                    documentDate: new Date().toISOString().split('T')[0],
                    partyType: 1,
                    partyId: partyId,
                    paymentMethod: 2,
                    warehouseId: whId,
                    currencyId: 1,
                    exchangeRate: 1,
                    lines: [{
                        lineNumber: 1,
                        itemId: itemId,
                        unitId: 1,
                        quantity: 1,
                        unitPrice: 1500,
                        discountAmount: 0,
                        taxRate: 14,
                        serialNumbers: [sn]
                    }]
                };

                const req1 = fetch('/Documents/Create?postImmediately=true', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                    body: JSON.stringify(payload)
                }).then(r => r.json());

                const req2 = fetch('/Documents/Create?postImmediately=true', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                    body: JSON.stringify(payload)
                }).then(r => r.json());

                const [r1, r2] = await Promise.all([req1, req2]);
                return { r1, r2 };
            }", new { itemId = testItemId, sn = raceSn });

            _output.WriteLine($"Case 10 (Concurrent Race Condition) Result: {case10Res}");
            bool r1Success = case10Res.GetProperty("r1").GetProperty("success").GetBoolean();
            bool r2Success = case10Res.GetProperty("r2").GetProperty("success").GetBoolean();
            // Exactly one must succeed and one must fail!
            Assert.True(r1Success ^ r2Success, $"Expected exactly one concurrent request to succeed, but got r1={r1Success}, r2={r2Success}");

            // ─── CASE 11: SALES RETURN RESTORES SERIAL TO AVAILABLE IN TARGET WAREHOUSE ─
            _output.WriteLine("Testing Case 11: Sales Return restores issued serial to Available status...");
            // Switch Transaction Type dropdown on Sales screen to Sales Return (مردود مبيعات)
            bool hasSalesReturnType = await page.EvaluateAsync<bool>(@"() => {
                const typeSelect = document.querySelector('select.form-select');
                if (!typeSelect) return false;
                const retOpt = Array.from(typeSelect.options).find(o => o.text.includes('مردود') || o.text.includes('مرتجع') || o.text.includes('Return'));
                if (retOpt) {
                    window.location.href = `/Documents/Create?transactionTypeId=${retOpt.value}&module=1`;
                    return true;
                }
                return false;
            }");

            if (hasSalesReturnType)
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.EvaluateAsync(@"() => {
                    const selects = Array.from(document.querySelectorAll('select.form-select'));
                    selects.forEach(s => {
                        if (!s.value && s.options.length > 1 && !s.disabled) {
                            const firstText = s.options[0].text || '';
                            if (firstText.includes('اختر')) {
                                s.value = s.options[1].value;
                                s.dispatchEvent(new Event('change'));
                            }
                        }
                    });
                    const pmSelect = selects.find(s => Array.from(s.options).some(o => o.text.includes('آجل')));
                    if (pmSelect) {
                        pmSelect.value = '2';
                        pmSelect.dispatchEvent(new Event('change'));
                    }
                }");

                lineItemInput = page.Locator("table tbody tr").First.Locator("input[placeholder*='ابحث بكود']");
                await lineItemInput.FillAsync(testItemCode);
                await page.WaitForSelectorAsync(".list-group-item-action", new PageWaitForSelectorOptions { Timeout = 10000 });
                await page.Locator(".list-group-item-action").First.ClickAsync();

                await page.WaitForSelectorAsync(".btn-open-serials", new PageWaitForSelectorOptions { Timeout = 10000 });
                await page.ClickAsync(".btn-open-serials");
                await page.WaitForSelectorAsync("#serialsModal.show", new PageWaitForSelectorOptions { Timeout = 10000 });

                // Paste the sold serial targetSn1 into Sales Return modal
                await page.FillAsync("#pasteSerialsTextarea", targetSn1);
                await page.ClickAsync("#btnValidatePastedSerials");
                await page.WaitForSelectorAsync("#pasteValidationResultsSection", new PageWaitForSelectorOptions { Timeout = 10000 });
                Assert.Equal("1", (await page.InnerTextAsync("#pasteValidCount")).Trim());

                await page.ClickAsync("#btnSaveValidPastedSerials");
                await page.ClickAsync("#btnCloseSerialsModal");
                await page.WaitForTimeoutAsync(300);

                await page.ClickAsync("button:has-text('حفظ المستند')");
                await page.WaitForSelectorAsync(".swal2-popup", new PageWaitForSelectorOptions { Timeout = 15000 });
                string returnSaveMsg = await page.InnerTextAsync("#swal2-html-container");
                _output.WriteLine($"Case 11 Sales Return Save Result: {returnSaveMsg}");
                Assert.Contains("بنجاح", returnSaveMsg);
                await page.ClickAsync(".swal2-confirm");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                // Verify targetSn1 is now Available again in the warehouse!
                await page.GotoAsync($"{BaseUrl}/Documents/Create?module=1", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                int availAfterReturn = await page.EvaluateAsync<int>(@"async ({ itemId, sn }) => {
                    const whSelect = Array.from(document.querySelectorAll('select.form-select')).find(s => s.options[0] && s.options[0].text.includes('المستودع'));
                    const whId = whSelect && whSelect.options.length > 1 ? whSelect.options[1].value : 1;
                    const res = await fetch(`/Documents/GetAvailableSerialsPagedApi?itemId=${itemId}&warehouseId=${whId}&search=${encodeURIComponent(sn)}`).then(r => r.json());
                    return res.data.totalCount;
                }", new { itemId = testItemId, sn = targetSn1 });
                _output.WriteLine($"Case 11 Verify -> Returned serial '{targetSn1}' count in available serials: {availAfterReturn}");
                Assert.Equal(1, availAfterReturn);
            }
        }
    }
}
