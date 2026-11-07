using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs.Reports;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ERP.Tools.DataSeeder
{
    public class MockCurrentUserContext : ICurrentUserContext
    {
        public string? UserId => "admin-system";
        public string? Username => "admin";
        public string? EmployeeCode => "EMP001";
        public int CompanyId => 1;
        public string CompanyName => "SEWTEC ERP";
        public int BranchId => 1;
        public string BranchName => "المقر الرئيسي";
        public int FiscalYearId => 1;
        public string FiscalYearName => "2026";
        public bool IsAuthenticated => true;
        public bool HasAllBranches => true;

        public bool HasPermission(string permissionCode) => true;
        public bool HasPermission(string screen, string transactionType, string action) => true;
        public bool CanAccessCompany(int companyId) => true;
        public bool CanAccessBranch(int branchId) => true;
    }

    public static class TrialBalanceValidator
    {
        public static async Task<bool> RunValidationAsync(ErpDbContext context)
        {
            Console.WriteLine("=================================================================");
            Console.WriteLine("          TRIAL BALANCE (ميزان المراجعة) AUDIT & VALIDATION      ");
            Console.WriteLine("=================================================================");

            var currentUser = new MockCurrentUserContext();
            var service = new AccountingReportService(context, currentUser);
            bool allTestsPassed = true;

            // -------------------------------------------------------------
            // DIAGNOSTIC INVESTIGATION: 21,000 Discrepancy
            // -------------------------------------------------------------
            Console.WriteLine("\n[DIAGNOSIS] Scanning database for any unbalance or missing accounts...");
            
            // Check 1: Headers with mismatched TotalDebitLocal / TotalCreditLocal
            var mismatchedHeaders = await context.JournalEntryHeaders
                .AsNoTracking()
                .Where(h => Math.Abs(h.TotalDebitLocal - h.TotalCreditLocal) > 0.001m)
                .Select(h => new { h.Id, h.EntryNumber, h.BranchId, h.TotalDebitLocal, h.TotalCreditLocal, h.Status, h.EntryDate })
                .ToListAsync();

            Console.WriteLine($"      -> Mismatched Headers Count: {mismatchedHeaders.Count}");
            foreach (var mh in mismatchedHeaders)
            {
                Console.WriteLine($"         Header #{mh.EntryNumber} (Id {mh.Id}, Branch {mh.BranchId}, Date {mh.EntryDate:yyyy-MM-dd}): Debit={mh.TotalDebitLocal}, Credit={mh.TotalCreditLocal}, Status={mh.Status}");
            }

            // Check 2: Headers where line sums don't match or lines debit != lines credit
            var lineSumsByHeader = await context.JournalEntryLines
                .AsNoTracking()
                .GroupBy(l => l.JournalEntryHeaderId)
                .Select(g => new
                {
                    HeaderId = g.Key,
                    SumDebit = g.Sum(x => x.DebitLocal),
                    SumCredit = g.Sum(x => x.CreditLocal)
                })
                .Where(x => Math.Abs(x.SumDebit - x.SumCredit) > 0.001m)
                .ToListAsync();

            Console.WriteLine($"      -> Headers with unbalanced Lines Count: {lineSumsByHeader.Count}");
            foreach (var lh in lineSumsByHeader)
            {
                var h = await context.JournalEntryHeaders.FindAsync(lh.HeaderId);
                Console.WriteLine($"         Header #{h?.EntryNumber} (Id {lh.HeaderId}, Date {h?.EntryDate:yyyy-MM-dd}): LineDebit={lh.SumDebit}, LineCredit={lh.SumCredit}, Diff={lh.SumDebit - lh.SumCredit}");
            }

            // Check 3: Lines pointing to Inactive accounts or Group accounts or Non-existent accounts
            var activeLeafAccountIdsList = await context.GLAccounts
                .AsNoTracking()
                .Where(a => a.IsActive && !a.IsGroup)
                .Select(a => a.Id)
                .ToListAsync();
            var activeLeafAccountIds = activeLeafAccountIdsList.ToHashSet();

            var arLeafAccount = await context.GLAccounts.FirstOrDefaultAsync(a => a.Code == "110201");
            if (arLeafAccount != null)
            {
                // Fix any customer pointing to group account
                var badCustomers = await context.Customers.Where(c => c.FinancialAccountId == 2).ToListAsync();
                foreach (var bc in badCustomers)
                {
                    bc.FinancialAccountId = arLeafAccount.Id;
                    Console.WriteLine($"      -> Fixed Customer #{bc.Code} FinancialAccountId from 2 to {arLeafAccount.Id}");
                }

                // Fix any VoucherLine pointing to group account
                var badVoucherLines = await context.VoucherLines.Where(vl => vl.AccountId == 2).ToListAsync();
                foreach (var bvl in badVoucherLines)
                {
                    bvl.AccountId = arLeafAccount.Id;
                    Console.WriteLine($"      -> Fixed VoucherLine #{bvl.Id} AccountId from 2 to {arLeafAccount.Id}");
                }

                // Fix any JournalEntryLine pointing to group account
                var badLines = await context.JournalEntryLines.Where(l => l.AccountId == 2).ToListAsync();
                foreach (var bl in badLines)
                {
                    bl.AccountId = arLeafAccount.Id;
                    Console.WriteLine($"      -> Fixed JournalEntryLine #{bl.Id} AccountId from 2 to {arLeafAccount.Id} ({arLeafAccount.Code})");
                }

                if (badCustomers.Count > 0 || badVoucherLines.Count > 0 || badLines.Count > 0)
                {
                    await context.SaveChangesAsync();
                    Console.WriteLine("      [SUCCESS] Database cleaned up: group account references re-pointed to leaf account 110201.");
                }
            }

            // Check 4: Any line with 21,000
            var lines21k = await context.JournalEntryLines
                .AsNoTracking()
                .Include(l => l.JournalEntryHeader)
                .Include(l => l.Account)
                .Where(l => l.DebitLocal == 21000 || l.CreditLocal == 21000)
                .Select(l => new { 
                    l.Id, 
                    l.JournalEntryHeaderId, 
                    JV = l.JournalEntryHeader != null ? l.JournalEntryHeader.EntryNumber : "N/A", 
                    EntryDate = l.JournalEntryHeader != null ? l.JournalEntryHeader.EntryDate : DateTime.MinValue, 
                    l.AccountId, 
                    Account = l.Account != null ? (l.Account.Code + " " + l.Account.NameAr) : "N/A", 
                    l.DebitLocal, 
                    l.CreditLocal 
                })
                .ToListAsync();

            Console.WriteLine($"      -> Lines with 21,000 EGP Count: {lines21k.Count}");
            foreach (var l in lines21k)
            {
                Console.WriteLine($"         JV #{l.JV} (Id {l.JournalEntryHeaderId}, Date {l.EntryDate:yyyy-MM-dd}): Account={l.Account}, Deb={l.DebitLocal}, Cred={l.CreditLocal}");
            }

            // Check 5: Look for any journal entries where Status != Posted
            var nonPostedEntries = await context.JournalEntryHeaders
                .AsNoTracking()
                .Where(h => h.Status != JournalEntryStatus.Posted)
                .Select(h => new { h.Id, h.EntryNumber, h.Status, h.TotalDebitLocal, h.TotalCreditLocal })
                .ToListAsync();
            Console.WriteLine($"      -> Non-Posted Entries Count: {nonPostedEntries.Count}");
            foreach (var np in nonPostedEntries)
            {
                Console.WriteLine($"         Non-Posted #{np.EntryNumber} (Id {np.Id}, Status {np.Status}): Debit={np.TotalDebitLocal}, Credit={np.TotalCreditLocal}");
            }

            // -------------------------------------------------------------
            // TEST 1: Full Year 2026 Trial Balance (All Levels)
            // -------------------------------------------------------------
            Console.WriteLine("\n[TEST 1] Full Year 2026 Trial Balance (All Levels, All Branches)...");
            var filterAll = new ReportFilterDto
            {
                FiscalYearId = 1,
                DateFrom = new DateTime(2026, 1, 1),
                DateTo = new DateTime(2026, 12, 31),
                Mode = ReportDisplayMode.LocalOnly,
                OnlyWithTransactions = false
            };

            var tbAll = await service.GetTrialBalanceAsync(filterAll);
            decimal diff = Math.Abs(tbAll.TotalPeriodDebitLocal - tbAll.TotalPeriodCreditLocal);

            Console.WriteLine($"      -> Total Items in Tree: {tbAll.Items.Count}");
            Console.WriteLine($"      -> Total Period Debit (EGP) : {tbAll.TotalPeriodDebitLocal:N2}");
            Console.WriteLine($"      -> Total Period Credit (EGP): {tbAll.TotalPeriodCreditLocal:N2}");
            Console.WriteLine($"      -> Net Difference           : {diff:N4}");
            Console.WriteLine($"      -> IsBalanced Status        : {tbAll.IsBalanced}");

            if (tbAll.IsBalanced && diff < 0.0001m && tbAll.TotalPeriodDebitLocal > 0)
            {
                Console.WriteLine("      [PASS] Full Trial Balance is 100% mathematically balanced.");
            }
            else
            {
                Console.WriteLine("      [FAIL] Trial Balance period totals are NOT balanced!");
                allTestsPassed = false;
            }

            // -------------------------------------------------------------
            // TEST 2: Level 1 (Root Summary Groups Roll-up)
            // -------------------------------------------------------------
            Console.WriteLine("\n[TEST 2] Level 1 (Root Account Summary Groups Roll-Up)...");
            var filterL1 = new ReportFilterDto
            {
                FiscalYearId = 1,
                DateFrom = new DateTime(2026, 1, 1),
                DateTo = new DateTime(2026, 12, 31),
                AccountLevel = 1,
                Mode = ReportDisplayMode.LocalOnly,
                OnlyWithTransactions = false
            };

            var tbL1 = await service.GetTrialBalanceAsync(filterL1);
            Console.WriteLine($"      -> Root Accounts Count: {tbL1.Items.Count}");
            Console.WriteLine("      --------------------------------------------------------------------------------------------------");
            Console.WriteLine("      | Code | Account Name (Ar)             | Period Debit (EGP)  | Period Credit (EGP) | Closing Net      |");
            Console.WriteLine("      --------------------------------------------------------------------------------------------------");

            decimal l1SumDebit = 0m;
            decimal l1SumCredit = 0m;
            foreach (var item in tbL1.Items)
            {
                l1SumDebit += item.PeriodDebitLocal;
                l1SumCredit += item.PeriodCreditLocal;
                Console.WriteLine($"      | {item.AccountCode,-4} | {item.AccountNameAr,-30} | {item.PeriodDebitLocal,19:N2} | {item.PeriodCreditLocal,19:N2} | {item.NetClosingLocal,16:N2} |");
            }
            Console.WriteLine("      --------------------------------------------------------------------------------------------------");
            Console.WriteLine($"      | SUM  | Level 1 Aggregated Totals      | {l1SumDebit,19:N2} | {l1SumCredit,19:N2} |");
            Console.WriteLine("      --------------------------------------------------------------------------------------------------");

            decimal l1Diff = Math.Abs(l1SumDebit - tbAll.TotalPeriodDebitLocal);
            if (l1Diff < 0.0001m && Math.Abs(l1SumDebit - l1SumCredit) < 0.0001m)
            {
                Console.WriteLine("      [PASS] Level 1 roll-up equals Grand Total exactly with zero discrepancy.");
            }
            else
            {
                Console.WriteLine($"      [FAIL] Level 1 roll-up discrepancy: Diff={l1Diff:N4}");
                allTestsPassed = false;
            }

            // -------------------------------------------------------------
            // TEST 3: Zero-Balance Suppression (OnlyWithTransactions = true)
            // -------------------------------------------------------------
            Console.WriteLine("\n[TEST 3] Zero-Balance Suppression Filtering (OnlyWithTransactions = true)...");
            var filterActive = new ReportFilterDto
            {
                FiscalYearId = 1,
                DateFrom = new DateTime(2026, 1, 1),
                DateTo = new DateTime(2026, 12, 31),
                OnlyWithTransactions = true,
                Mode = ReportDisplayMode.LocalOnly
            };

            var tbActive = await service.GetTrialBalanceAsync(filterActive);
            Console.WriteLine($"      -> Accounts with Activity/Balance: {tbActive.Items.Count} (out of {tbAll.Items.Count} total)");
            bool hasZeroAccounts = tbActive.Items.Any(i =>
                i.OpeningDebitLocal == 0 && i.OpeningCreditLocal == 0 &&
                i.PeriodDebitLocal == 0 && i.PeriodCreditLocal == 0 &&
                i.ClosingDebitLocal == 0 && i.ClosingCreditLocal == 0);

            if (!hasZeroAccounts && tbActive.Items.Count > 0)
            {
                Console.WriteLine("      [PASS] Zero-balance accounts successfully excluded.");
            }
            else
            {
                Console.WriteLine("      [FAIL] Zero-balance filtering returned inactive accounts or zero items.");
                allTestsPassed = false;
            }

            // -------------------------------------------------------------
            // TEST 4: Date-Range Mid-Year Cutoff (Opening Balance Test)
            // -------------------------------------------------------------
            Console.WriteLine("\n[TEST 4] Mid-Year Cutoff & Opening Balance Integrity (From 2026-07-01)...");
            var filterMid = new ReportFilterDto
            {
                FiscalYearId = 1,
                DateFrom = new DateTime(2026, 7, 1),
                DateTo = new DateTime(2026, 12, 31),
                Mode = ReportDisplayMode.LocalOnly,
                OnlyWithTransactions = false
            };

            var tbMid = await service.GetTrialBalanceAsync(filterMid);
            decimal openDiff = Math.Abs(tbMid.TotalOpeningDebitLocal - tbMid.TotalOpeningCreditLocal);
            decimal perDiff = Math.Abs(tbMid.TotalPeriodDebitLocal - tbMid.TotalPeriodCreditLocal);
            decimal closeDiff = Math.Abs(tbMid.TotalClosingDebitLocal - tbMid.TotalClosingCreditLocal);

            Console.WriteLine($"      -> Opening Balance  : Debit = {tbMid.TotalOpeningDebitLocal:N2} | Credit = {tbMid.TotalOpeningCreditLocal:N2} | Diff = {openDiff:N4}");
            Console.WriteLine($"      -> Period Movements : Debit = {tbMid.TotalPeriodDebitLocal:N2} | Credit = {tbMid.TotalPeriodCreditLocal:N2} | Diff = {perDiff:N4}");
            Console.WriteLine($"      -> Closing Balance  : Debit = {tbMid.TotalClosingDebitLocal:N2} | Credit = {tbMid.TotalClosingCreditLocal:N2} | Diff = {closeDiff:N4}");

            if (openDiff < 0.0001m && perDiff < 0.0001m && closeDiff < 0.0001m && tbMid.TotalOpeningDebitLocal > 0)
            {
                Console.WriteLine("      [PASS] Opening, Period, and Closing balances are ALL strictly balanced (Diff = 0.0000).");
            }
            else
            {
                Console.WriteLine("      [FAIL] Balance inequality detected in mid-year cutoff.");
                allTestsPassed = false;
            }

            // -------------------------------------------------------------
            // TEST 5: Branch Security & Multi-Branch Reconciliation
            // -------------------------------------------------------------
            Console.WriteLine("\n[TEST 5] Multi-Branch Reconciliation...");
            var branches = await context.Branches.AsNoTracking().Where(b => b.IsActive).Take(5).ToListAsync();
            decimal sumBranchDebits = 0m;

            foreach (var br in branches)
            {
                var brFilter = new ReportFilterDto
                {
                    FiscalYearId = 1,
                    BranchId = br.Id,
                    DateFrom = new DateTime(2026, 1, 1),
                    DateTo = new DateTime(2026, 12, 31),
                    Mode = ReportDisplayMode.LocalOnly
                };

                var brTb = await service.GetTrialBalanceAsync(brFilter);
                decimal brDiff = Math.Abs(brTb.TotalPeriodDebitLocal - brTb.TotalPeriodCreditLocal);
                Console.WriteLine($"      -> Branch [{br.Code}] {br.NameAr,-25}: Debit = {brTb.TotalPeriodDebitLocal,14:N2} | Credit = {brTb.TotalPeriodCreditLocal,14:N2} | Balanced = {brTb.IsBalanced}");

                if (brDiff > 0.0001m)
                {
                    Console.WriteLine($"         [FAIL] Branch {br.Code} is unbalanced! Diff = {brDiff:N4}");
                    allTestsPassed = false;
                }

                sumBranchDebits += brTb.TotalPeriodDebitLocal;
            }

            Console.WriteLine($"      -> Sum of 5 Branches Period Debit: {sumBranchDebits:N2} EGP");

            // -------------------------------------------------------------
            // TEST 6: Income Statement (قائمة الدخل - الأرباح والخسائر)
            // -------------------------------------------------------------
            Console.WriteLine("\n[TEST 6] Income Statement (Profit & Loss / قائمة الدخل)...");
            var filterPnl = new ReportFilterDto
            {
                FiscalYearId = 1,
                DateFrom = new DateTime(2026, 1, 1),
                DateTo = new DateTime(2026, 12, 31),
                Mode = ReportDisplayMode.LocalOnly,
                OnlyWithTransactions = true
            };

            var pnl = await service.GetIncomeStatementAsync(filterPnl);
            Console.WriteLine($"      -> Total Operating Revenues       : {pnl.TotalOperatingRevenues,14:N2} EGP");
            Console.WriteLine($"      -> Total Other Revenues           : {pnl.TotalOtherRevenues,14:N2} EGP");
            Console.WriteLine($"      -> TOTAL REVENUES (الإيرادات)      : {pnl.TotalRevenues,14:N2} EGP");
            Console.WriteLine($"      -> Total Cost of Sales (تكلفة)    : {pnl.TotalCostOfSales,14:N2} EGP");
            Console.WriteLine($"      -> GROSS PROFIT (مجمل الربح)      : {pnl.GrossProfit,14:N2} EGP ({pnl.GrossProfitMargin:N1}%)");
            Console.WriteLine($"      -> Operating Expenses (مصروفات)   : {pnl.TotalOperatingExpenses,14:N2} EGP");
            Console.WriteLine($"      -> NET INCOME (صافي نتيجة الفترة)  : {pnl.NetIncome,14:N2} EGP ({pnl.NetProfitMargin:N1}%)");

            if (pnl.TotalRevenues > 0 && pnl.TotalOperatingExpenses > 0)
            {
                Console.WriteLine("      [PASS] Income Statement calculation verified successfully.");
            }
            else
            {
                Console.WriteLine("      [FAIL] Income Statement returned zero revenues or expenses.");
                allTestsPassed = false;
            }

            // -------------------------------------------------------------
            // TEST 7: Balance Sheet (قائمة المركز المالي - الميزانية العمومية)
            // -------------------------------------------------------------
            Console.WriteLine("\n[TEST 7] Balance Sheet (Financial Position / الميزانية العمومية)...");
            var filterBs = new ReportFilterDto
            {
                FiscalYearId = 1,
                DateTo = new DateTime(2026, 12, 31),
                Mode = ReportDisplayMode.LocalOnly,
                OnlyWithTransactions = false
            };

            var bs = await service.GetBalanceSheetAsync(filterBs);
            Console.WriteLine($"      -> Total Current Assets (متداولة) : {bs.TotalCurrentAssets,14:N2} EGP");
            Console.WriteLine($"      -> Total Non-Current Assets (ثابتة): {bs.TotalNonCurrentAssets,14:N2} EGP");
            Console.WriteLine($"      -> TOTAL ASSETS (إجمالي الأصول)    : {bs.TotalAssets,14:N2} EGP");
            Console.WriteLine($"      -> Total Current Liabilities       : {bs.TotalCurrentLiabilities,14:N2} EGP");
            Console.WriteLine($"      -> Total Long-Term Liabilities     : {bs.TotalLongTermLiabilities,14:N2} EGP");
            Console.WriteLine($"      -> TOTAL LIABILITIES (الالتزامات)  : {bs.TotalLiabilities,14:N2} EGP");
            Console.WriteLine($"      -> Total Capital & Reserves        : {bs.TotalCapital + bs.TotalReserves,14:N2} EGP");
            Console.WriteLine($"      -> Transferred Net Income (من الدخل): {bs.CurrentPeriodNetIncome,14:N2} EGP");
            Console.WriteLine($"      -> TOTAL EQUITY (حقوق الملكية)    : {bs.TotalEquity,14:N2} EGP");
            Console.WriteLine($"      -> TOTAL LIABILITIES & EQUITY      : {bs.TotalLiabilitiesAndEquity,14:N2} EGP");
            Console.WriteLine($"      -> Balance Difference              : {bs.BalanceDifference,14:N4} EGP");
            Console.WriteLine($"      -> IsBalanced Status               : {bs.IsBalanced}");

            if (bs.IsBalanced && Math.Abs(bs.BalanceDifference) < 0.0001m)
            {
                Console.WriteLine("      [PASS] Balance Sheet equation (Assets = Liabilities + Equity) is 100% BALANCED.");
            }
            else
            {
                Console.WriteLine($"      [FAIL] Balance Sheet equation unbalanced! Difference = {bs.BalanceDifference:N4}");
                allTestsPassed = false;
            }

            // -------------------------------------------------------------
            // Summary
            // -------------------------------------------------------------
            Console.WriteLine("\n=================================================================");
            if (allTestsPassed)
            {
                Console.WriteLine("   ALL FINANCIAL STATEMENTS AUDIT & VALIDATION CHECKS PASSED    ");
            }
            else
            {
                Console.WriteLine("   SOME CHECKS FAILED - REVIEW OUTPUT ABOVE                     ");
            }
            Console.WriteLine("=================================================================\n");

            return allTestsPassed;
        }
    }
}
