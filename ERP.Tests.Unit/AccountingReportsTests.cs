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
using Moq;
using Xunit;

namespace ERP.Tests.Unit
{
    public class AccountingReportsTests
    {
        private (ErpDbContext context, AccountingReportService service) SetupTestEnvironment(string dbName)
        {
            var userMock = new Mock<ICurrentUserService>();
            userMock.Setup(u => u.BranchId).Returns(1);
            userMock.Setup(u => u.IsSuperAdmin).Returns(true);
            userMock.Setup(u => u.IsAuthenticated).Returns(true);

            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            var context = new ErpDbContext(options, userMock.Object);

            var userContextMock = new Mock<ICurrentUserContext>();
            userContextMock.Setup(c => c.BranchId).Returns(1);
            userContextMock.Setup(c => c.BranchName).Returns("الفرع الرئيسي");
            userContextMock.Setup(c => c.FiscalYearId).Returns(1);
            userContextMock.Setup(c => c.FiscalYearName).Returns("2026");
            userContextMock.Setup(c => c.HasAllBranches).Returns(true);

            var service = new AccountingReportService(context, userContextMock.Object);

            return (context, service);
        }

        [Fact]
        public async Task AccountStatement_Calculates_Opening_And_Running_Balances_Correctly()
        {
            var dbName = Guid.NewGuid().ToString();
            var (context, service) = SetupTestEnvironment(dbName);

            // 1. Seed Branch, Fiscal Year, Currency, GLAccount
            var branch = new Branch { Id = 1, Code = "B1", Name = "Main Branch", NameAr = "الفرع الرئيسي" };
            var fy = new FiscalYear { Id = 1, Year = "2026", Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31) };
            var curUSD = new Currency { Id = 1, Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar", ExchangeRate = 50m };
            var account = new GLAccount { Id = 101, Code = "10101", NameAr = "الصندوق بالدولار", NameEn = "Cash USD", CurrencyId = 1, AccountNature = AccountNature.Debit };

            context.Branches.Add(branch);
            context.FiscalYears.Add(fy);
            context.Currencies.Add(curUSD);
            context.GLAccounts.Add(account);
            await context.SaveChangesAsync();

            // 2. Add an Opening Transaction (dated 2026-01-10) before period start (2026-02-01)
            var jv1 = new JournalEntryHeader
            {
                Id = 1,
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-001",
                EntryDate = new DateTime(2026, 1, 10),
                Status = JournalEntryStatus.Posted,
                Description = "قيد افتتاحي"
            };
            var jv1Line = new JournalEntryLine
            {
                Id = 1,
                JournalEntryHeaderId = 1,
                AccountId = 101,
                CurrencyId = 1,
                ExchangeRate = 50m,
                DebitForeign = 1000m,
                CreditForeign = 0m,
                DebitLocal = 50000m,
                CreditLocal = 0m,
                Description = "رصيد افتتاحي"
            };
            context.JournalEntryHeaders.Add(jv1);
            context.JournalEntryLines.Add(jv1Line);

            // 3. Add Period Transactions (dated 2026-02-05 and 2026-02-15)
            var jv2 = new JournalEntryHeader
            {
                Id = 2,
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-002",
                EntryDate = new DateTime(2026, 2, 5),
                Status = JournalEntryStatus.Posted,
                Description = "إيداع نقدي"
            };
            var jv2Line = new JournalEntryLine
            {
                Id = 2,
                JournalEntryHeaderId = 2,
                AccountId = 101,
                CurrencyId = 1,
                ExchangeRate = 50m,
                DebitForeign = 500m,
                CreditForeign = 0m,
                DebitLocal = 25000m,
                CreditLocal = 0m,
                Description = "إيداع"
            };

            var jv3 = new JournalEntryHeader
            {
                Id = 3,
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-003",
                EntryDate = new DateTime(2026, 2, 15),
                Status = JournalEntryStatus.Posted,
                Description = "سحب نقدي"
            };
            var jv3Line = new JournalEntryLine
            {
                Id = 3,
                JournalEntryHeaderId = 3,
                AccountId = 101,
                CurrencyId = 1,
                ExchangeRate = 50m,
                DebitForeign = 0m,
                CreditForeign = 300m,
                DebitLocal = 0m,
                CreditLocal = 15000m,
                Description = "سحب"
            };

            context.JournalEntryHeaders.AddRange(jv2, jv3);
            context.JournalEntryLines.AddRange(jv2Line, jv3Line);
            await context.SaveChangesAsync();

            // Act: Request statement for February 2026
            var filter = new ReportFilterDto
            {
                AccountId = 101,
                BranchId = 1,
                FiscalYearId = 1,
                DateFrom = new DateTime(2026, 2, 1),
                DateTo = new DateTime(2026, 2, 28),
                Mode = ReportDisplayMode.Both
            };

            var result = await service.GetAccountStatementAsync(filter);

            // Assert
            // Opening Balances
            Assert.Equal(1000m, result.OpeningBalanceForeign);
            Assert.Equal(50000m, result.OpeningBalanceLocal);

            // 2 period lines
            Assert.Equal(2, result.Lines.Count);

            // Line 1 running balances
            Assert.Equal(1500m, result.Lines[0].RunningBalanceForeign); // 1000 + 500
            Assert.Equal(75000m, result.Lines[0].RunningBalanceLocal);  // 50000 + 25000

            // Line 2 running balances
            Assert.Equal(1200m, result.Lines[1].RunningBalanceForeign); // 1500 - 300
            Assert.Equal(60000m, result.Lines[1].RunningBalanceLocal);  // 75000 - 15000

            // Final Closing Balances
            Assert.Equal(1200m, result.ClosingBalanceForeign);
            Assert.Equal(60000m, result.ClosingBalanceLocal);
            Assert.Equal(500m, result.TotalPeriodDebitForeign);
            Assert.Equal(300m, result.TotalPeriodCreditForeign);
            Assert.Equal(25000m, result.TotalPeriodDebitLocal);
            Assert.Equal(15000m, result.TotalPeriodCreditLocal);
        }

        [Fact]
        public async Task CustomerStatement_Tracks_SubLedger_Transactions()
        {
            var dbName = Guid.NewGuid().ToString();
            var (context, service) = SetupTestEnvironment(dbName);

            var cur = new Currency { Id = 2, Code = "EUR", NameAr = "يورو", NameEn = "Euro", ExchangeRate = 55m };
            var customer = new Customer { Id = 10, BranchId = 1, Code = "CUST-001", NameAr = "شركة الأمل", NameEn = "Al-Amal Co", CurrencyId = 2 };
            var account = new GLAccount { Id = 201, Code = "120101", NameAr = "العملاء", NameEn = "Accounts Receivable", AccountNature = AccountNature.Debit };

            context.Currencies.Add(cur);
            context.Customers.Add(customer);
            context.GLAccounts.Add(account);
            await context.SaveChangesAsync();

            var jv = new JournalEntryHeader
            {
                Id = 10,
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-INV-01",
                EntryDate = new DateTime(2026, 3, 1),
                Status = JournalEntryStatus.Posted,
                Description = "فاتورة مبيعات باليورو"
            };
            var jvLine = new JournalEntryLine
            {
                Id = 100,
                JournalEntryHeaderId = 10,
                AccountId = 201,
                SubLedgerType = SubLedgerType.Customer,
                SubLedgerId = 10,
                CurrencyId = 2,
                ExchangeRate = 55m,
                DebitForeign = 200m,
                CreditForeign = 0m,
                DebitLocal = 11000m,
                CreditLocal = 0m,
                Description = "فاتورة بيع"
            };
            context.JournalEntryHeaders.Add(jv);
            context.JournalEntryLines.Add(jvLine);
            await context.SaveChangesAsync();

            var filter = new ReportFilterDto
            {
                CustomerId = 10,
                DateFrom = new DateTime(2026, 1, 1),
                DateTo = new DateTime(2026, 12, 31)
            };

            var statement = await service.GetCustomerStatementAsync(filter);

            Assert.Equal("CUST-001", statement.Code);
            Assert.Equal("شركة الأمل", statement.NameAr);
            Assert.Single(statement.Lines);
            Assert.Equal(200m, statement.ClosingBalanceForeign);
            Assert.Equal(11000m, statement.ClosingBalanceLocal);
        }

        [Fact]
        public async Task TrialBalance_Calculates_Balanced_Debit_Credit_Totals()
        {
            var dbName = Guid.NewGuid().ToString();
            var (context, service) = SetupTestEnvironment(dbName);

            var accDebit = new GLAccount { Id = 301, Code = "101", NameAr = "نقدية", Level = 1, IsGroup = false, AccountNature = AccountNature.Debit, IsActive = true };
            var accCredit = new GLAccount { Id = 302, Code = "401", NameAr = "إيرادات", Level = 1, IsGroup = false, AccountNature = AccountNature.Credit, IsActive = true };

            context.GLAccounts.AddRange(accDebit, accCredit);
            await context.SaveChangesAsync();

            var jv = new JournalEntryHeader
            {
                Id = 20,
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-BAL-01",
                EntryDate = new DateTime(2026, 4, 1),
                Status = JournalEntryStatus.Posted,
                Description = "قيد إيراد نقدي متزن"
            };
            var lDebit = new JournalEntryLine
            {
                Id = 201,
                JournalEntryHeaderId = 20,
                AccountId = 301,
                ExchangeRate = 1m,
                DebitLocal = 5000m,
                CreditLocal = 0m,
                Description = "نقدية"
            };
            var lCredit = new JournalEntryLine
            {
                Id = 202,
                JournalEntryHeaderId = 20,
                AccountId = 302,
                ExchangeRate = 1m,
                DebitLocal = 0m,
                CreditLocal = 5000m,
                Description = "إيرادات"
            };

            context.JournalEntryHeaders.Add(jv);
            context.JournalEntryLines.AddRange(lDebit, lCredit);
            await context.SaveChangesAsync();

            var filter = new ReportFilterDto
            {
                DateFrom = new DateTime(2026, 1, 1),
                DateTo = new DateTime(2026, 12, 31),
                Mode = ReportDisplayMode.LocalOnly
            };

            var tb = await service.GetTrialBalanceAsync(filter);

            Assert.True(tb.IsBalanced);
            Assert.Equal(5000m, tb.TotalPeriodDebitLocal);
            Assert.Equal(5000m, tb.TotalPeriodCreditLocal);
        }

        [Fact]
        public async Task TrialBalance_RollsUp_Child_Account_Balances_To_Parent_Groups_Correctly()
        {
            var dbName = Guid.NewGuid().ToString();
            var (context, service) = SetupTestEnvironment(dbName);

            // Hierarchy:
            // 1 (Assets, Level 1, Group)
            //   -> 11 (Current Assets, Level 2, Group)
            //        -> 1101 (Cash, Level 3, Leaf)
            //        -> 1102 (Bank, Level 3, Leaf)
            // 4 (Revenues, Level 1, Group)
            //   -> 41 (Operating Revenue, Level 2, Group)
            //        -> 4101 (Sales, Level 3, Leaf)
            var acc1 = new GLAccount { Id = 1, Code = "1", NameAr = "الأصول", Level = 1, IsGroup = true, IsActive = true, AccountNature = AccountNature.Debit };
            var acc11 = new GLAccount { Id = 11, ParentId = 1, Code = "11", NameAr = "الأصول المتداولة", Level = 2, IsGroup = true, IsActive = true, AccountNature = AccountNature.Debit };
            var acc1101 = new GLAccount { Id = 1101, ParentId = 11, Code = "1101", NameAr = "الصندوق", Level = 3, IsGroup = false, IsActive = true, AccountNature = AccountNature.Debit };
            var acc1102 = new GLAccount { Id = 1102, ParentId = 11, Code = "1102", NameAr = "البنك", Level = 3, IsGroup = false, IsActive = true, AccountNature = AccountNature.Debit };

            var acc4 = new GLAccount { Id = 4, Code = "4", NameAr = "الإيرادات", Level = 1, IsGroup = true, IsActive = true, AccountNature = AccountNature.Credit };
            var acc41 = new GLAccount { Id = 41, ParentId = 4, Code = "41", NameAr = "إيرادات النشاط", Level = 2, IsGroup = true, IsActive = true, AccountNature = AccountNature.Credit };
            var acc4101 = new GLAccount { Id = 4101, ParentId = 41, Code = "4101", NameAr = "مبيعات عامة", Level = 3, IsGroup = false, IsActive = true, AccountNature = AccountNature.Credit };

            context.GLAccounts.AddRange(acc1, acc11, acc1101, acc1102, acc4, acc41, acc4101);
            await context.SaveChangesAsync();

            // Journal Entry: Debit 1101 (3000), Debit 1102 (2000), Credit 4101 (5000)
            var jv = new JournalEntryHeader
            {
                Id = 50,
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-ROLL-01",
                EntryDate = new DateTime(2026, 5, 10),
                Status = JournalEntryStatus.Posted,
                Description = "قيد اختبار التجميع الشجري"
            };

            var line1 = new JournalEntryLine { Id = 501, JournalEntryHeaderId = 50, AccountId = 1101, ExchangeRate = 1m, DebitLocal = 3000m, CreditLocal = 0m };
            var line2 = new JournalEntryLine { Id = 502, JournalEntryHeaderId = 50, AccountId = 1102, ExchangeRate = 1m, DebitLocal = 2000m, CreditLocal = 0m };
            var line3 = new JournalEntryLine { Id = 503, JournalEntryHeaderId = 50, AccountId = 4101, ExchangeRate = 1m, DebitLocal = 0m, CreditLocal = 5000m };

            context.JournalEntryHeaders.Add(jv);
            context.JournalEntryLines.AddRange(line1, line2, line3);
            await context.SaveChangesAsync();

            // Request Trial Balance filtered to Level 2
            var filter = new ReportFilterDto
            {
                AccountLevel = 2,
                DateFrom = new DateTime(2026, 1, 1),
                DateTo = new DateTime(2026, 12, 31),
                Mode = ReportDisplayMode.LocalOnly
            };

            var tb = await service.GetTrialBalanceAsync(filter);

            // Check that only Level 1 and Level 2 accounts are present
            Assert.All(tb.Items, item => Assert.True(item.Level <= 2));

            // Level 2 group (11 - الأصول المتداولة) must have rolled-up Debit = 3000 + 2000 = 5000
            var item11 = tb.Items.FirstOrDefault(i => i.AccountCode == "11");
            Assert.NotNull(item11);
            Assert.Equal(5000m, item11.PeriodDebitLocal);
            Assert.Equal(0m, item11.PeriodCreditLocal);
            Assert.Equal(5000m, item11.ClosingDebitLocal);

            // Level 1 root group (1 - الأصول) must have rolled-up Debit = 5000
            var item1 = tb.Items.FirstOrDefault(i => i.AccountCode == "1");
            Assert.NotNull(item1);
            Assert.Equal(5000m, item1.PeriodDebitLocal);
            Assert.Equal(5000m, item1.ClosingDebitLocal);

            // Level 2 group (41 - إيرادات النشاط) must have rolled-up Credit = 5000
            var item41 = tb.Items.FirstOrDefault(i => i.AccountCode == "41");
            Assert.NotNull(item41);
            Assert.Equal(5000m, item41.PeriodCreditLocal);
            Assert.Equal(5000m, item41.ClosingCreditLocal);

            // Grand Totals must reflect the full trial balance (5000 Debit / 5000 Credit) and be balanced
            Assert.True(tb.IsBalanced);
            Assert.Equal(5000m, tb.TotalPeriodDebitLocal);
            Assert.Equal(5000m, tb.TotalPeriodCreditLocal);
            Assert.Equal(5000m, tb.TotalClosingDebitLocal);
            Assert.Equal(5000m, tb.TotalClosingCreditLocal);
        }

        [Fact]
        public async Task GetSubLedgerSummaryAsync_Aggregates_Customers_And_Vendors_With_Opening_And_Period_Movements()
        {
            var dbName = Guid.NewGuid().ToString();
            var (context, service) = SetupTestEnvironment(dbName);

            // 1. Master Data
            var branch = new Branch { Id = 1, Code = "B1", Name = "Main Branch", NameAr = "الفرع الرئيسي" };
            var fy = new FiscalYear { Id = 1, Year = "2026", Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31) };
            var curEGP = new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", ExchangeRate = 1m };
            var curUSD = new Currency { Id = 2, Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar", ExchangeRate = 50m };

            var glCustomer = new GLAccount { Id = 1201, Code = "1201", NameAr = "حساب مراقبة العملاء", NameEn = "Customers Control", CurrencyId = 1 };
            var glVendor = new GLAccount { Id = 2101, Code = "2101", NameAr = "حساب مراقبة الموردين", NameEn = "Vendors Control", CurrencyId = 1 };

            var customer = new Customer
            {
                Id = 10,
                Code = "CUST-001",
                NameAr = "شركة الأمل للتجارة",
                NameEn = "Al Amal Trading",
                BranchId = 1,
                CurrencyId = 1,
                FinancialAccountId = 1201,
                IsActive = true
            };

            var vendor = new Vendor
            {
                Id = 20,
                Code = "VEND-001",
                NameAr = "مؤسسة النصر للتوريدات",
                NameEn = "Al Nasr Supplies",
                BranchId = 1,
                CurrencyId = 1,
                FinancialAccountId = 2101,
                IsActive = true
            };

            context.Branches.Add(branch);
            context.FiscalYears.Add(fy);
            context.Currencies.AddRange(curEGP, curUSD);
            context.GLAccounts.AddRange(glCustomer, glVendor);
            context.Customers.Add(customer);
            context.Vendors.Add(vendor);
            await context.SaveChangesAsync();

            // 2. Opening Balance (EntryDate = 2026-01-10) before period start (2026-02-01)
            // Customer owes 5,000 EGP (Debit)
            // Vendor has 8,000 EGP credit balance
            var jvOpening = new JournalEntryHeader
            {
                Id = 1,
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-OP-01",
                EntryDate = new DateTime(2026, 1, 10),
                Status = JournalEntryStatus.Posted
            };
            var jvOpLine1 = new JournalEntryLine
            {
                Id = 1,
                JournalEntryHeaderId = 1,
                JournalEntryHeader = jvOpening,
                AccountId = 1201,
                SubLedgerType = SubLedgerType.Customer,
                SubLedgerId = 10,
                DebitLocal = 5000m,
                CreditLocal = 0m,
                CurrencyId = 1,
                ExchangeRate = 1m
            };
            var jvOpLine2 = new JournalEntryLine
            {
                Id = 2,
                JournalEntryHeaderId = 1,
                JournalEntryHeader = jvOpening,
                AccountId = 2101,
                SubLedgerType = SubLedgerType.Vendor,
                SubLedgerId = 20,
                DebitLocal = 0m,
                CreditLocal = 8000m,
                CurrencyId = 1,
                ExchangeRate = 1m
            };
            context.JournalEntryHeaders.Add(jvOpening);
            context.JournalEntryLines.AddRange(jvOpLine1, jvOpLine2);

            // 3. Period Transactions (EntryDate = 2026-02-15)
            // Customer was invoiced 3,000 EGP (Debit) and paid 2,000 EGP (Credit) -> Net Period = +1,000 EGP -> Closing = +6,000 EGP
            // Vendor invoiced 4,000 EGP (Credit) and paid 3,000 EGP (Debit) -> Net Period = -1,000 EGP -> Closing = -9,000 EGP (Credit)
            var jvPeriod = new JournalEntryHeader
            {
                Id = 2,
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-PR-01",
                EntryDate = new DateTime(2026, 2, 15),
                Status = JournalEntryStatus.Posted
            };
            var jvPrLine1 = new JournalEntryLine
            {
                Id = 3,
                JournalEntryHeaderId = 2,
                JournalEntryHeader = jvPeriod,
                AccountId = 1201,
                SubLedgerType = SubLedgerType.Customer,
                SubLedgerId = 10,
                DebitLocal = 3000m,
                CreditLocal = 2000m,
                CurrencyId = 1,
                ExchangeRate = 1m
            };
            var jvPrLine2 = new JournalEntryLine
            {
                Id = 4,
                JournalEntryHeaderId = 2,
                JournalEntryHeader = jvPeriod,
                AccountId = 2101,
                SubLedgerType = SubLedgerType.Vendor,
                SubLedgerId = 20,
                DebitLocal = 3000m,
                CreditLocal = 4000m,
                CurrencyId = 1,
                ExchangeRate = 1m
            };
            context.JournalEntryHeaders.Add(jvPeriod);
            context.JournalEntryLines.AddRange(jvPrLine1, jvPrLine2);
            await context.SaveChangesAsync();

            // 4. Execute Service Call
            var filter = new SubLedgerSummaryFilterDto
            {
                FiscalYearId = 1,
                BranchId = 1,
                PartyType = "All",
                DateFrom = new DateTime(2026, 2, 1),
                DateTo = new DateTime(2026, 2, 28)
            };

            var result = await service.GetSubLedgerSummaryAsync(filter);

            // 5. Assertions
            Assert.NotNull(result);
            Assert.Equal(1, result.TotalCustomersCount);
            Assert.Equal(1, result.TotalVendorsCount);
            Assert.Equal(2, result.Items.Count);

            // Customer Item
            var custItem = result.Items.FirstOrDefault(i => i.PartyType == "Customer" && i.PartyId == 10);
            Assert.NotNull(custItem);
            Assert.Equal(5000m, custItem.OpeningDebitLocal);
            Assert.Equal(0m, custItem.OpeningCreditLocal);
            Assert.Equal(5000m, custItem.OpeningBalanceLocal);
            Assert.Equal(3000m, custItem.PeriodDebitLocal);
            Assert.Equal(2000m, custItem.PeriodCreditLocal);
            Assert.Equal(1000m, custItem.PeriodNetLocal);
            Assert.Equal(8000m, custItem.ClosingDebitLocal);
            Assert.Equal(2000m, custItem.ClosingCreditLocal);
            Assert.Equal(6000m, custItem.ClosingBalanceLocal);
            Assert.Equal("مدين (لنا)", custItem.BalanceStatusText);

            // Vendor Item
            var vendItem = result.Items.FirstOrDefault(i => i.PartyType == "Vendor" && i.PartyId == 20);
            Assert.NotNull(vendItem);
            Assert.Equal(0m, vendItem.OpeningDebitLocal);
            Assert.Equal(8000m, vendItem.OpeningCreditLocal);
            Assert.Equal(-8000m, vendItem.OpeningBalanceLocal);
            Assert.Equal(3000m, vendItem.PeriodDebitLocal);
            Assert.Equal(4000m, vendItem.PeriodCreditLocal);
            Assert.Equal(-1000m, vendItem.PeriodNetLocal);
            Assert.Equal(3000m, vendItem.ClosingDebitLocal);
            Assert.Equal(12000m, vendItem.ClosingCreditLocal);
            Assert.Equal(-9000m, vendItem.ClosingBalanceLocal);
            Assert.Equal("دائن (علينا)", vendItem.BalanceStatusText);

            // Totals and KPIs
            Assert.Equal(6000m, result.TotalCustomerReceivablesLocal);
            Assert.Equal(9000m, result.TotalVendorPayablesLocal);
            Assert.Equal(6000m - 9000m, result.NetOverallBalanceLocal); // -3000 EGP net liability
        }

        [Fact]
        public async Task GetSubLedgerSummaryAsync_Filters_Correctly_By_PartyType_And_Balances()
        {
            var dbName = Guid.NewGuid().ToString();
            var (context, service) = SetupTestEnvironment(dbName);

            var branch = new Branch { Id = 1, Code = "B1", Name = "Main Branch", NameAr = "الفرع الرئيسي" };
            var fy = new FiscalYear { Id = 1, Year = "2026", Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31) };
            var curEGP = new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", ExchangeRate = 1m };

            var customer1 = new Customer { Id = 1, Code = "C1", NameAr = "عميل نشط", BranchId = 1, CurrencyId = 1, IsActive = true };
            var customer2 = new Customer { Id = 2, Code = "C2", NameAr = "عميل بدون رصيد", BranchId = 1, CurrencyId = 1, IsActive = true };
            var vendor1 = new Vendor { Id = 1, Code = "V1", NameAr = "مورد 1", BranchId = 1, CurrencyId = 1, IsActive = true };

            context.Branches.Add(branch);
            context.FiscalYears.Add(fy);
            context.Currencies.Add(curEGP);
            context.Customers.AddRange(customer1, customer2);
            context.Vendors.Add(vendor1);

            // C1 has balance 1000 EGP
            var jv = new JournalEntryHeader { Id = 1, BranchId = 1, FiscalYearId = 1, EntryNumber = "JV-1", EntryDate = new DateTime(2026, 1, 15), Status = JournalEntryStatus.Posted };
            var line1 = new JournalEntryLine { Id = 1, JournalEntryHeaderId = 1, JournalEntryHeader = jv, SubLedgerType = SubLedgerType.Customer, SubLedgerId = 1, DebitLocal = 1000m, CurrencyId = 1 };
            context.JournalEntryHeaders.Add(jv);
            context.JournalEntryLines.Add(line1);
            await context.SaveChangesAsync();

            // Test 1: Only Customers
            var resCust = await service.GetSubLedgerSummaryAsync(new SubLedgerSummaryFilterDto { FiscalYearId = 1, BranchId = 1, PartyType = "Customer" });
            Assert.Equal(2, resCust.Items.Count);
            Assert.All(resCust.Items, i => Assert.Equal("Customer", i.PartyType));

            // Test 2: Only Vendors
            var resVend = await service.GetSubLedgerSummaryAsync(new SubLedgerSummaryFilterDto { FiscalYearId = 1, BranchId = 1, PartyType = "Vendor" });
            Assert.Single(resVend.Items);
            Assert.Equal("Vendor", resVend.Items[0].PartyType);

            // Test 3: Only with balances
            var resBal = await service.GetSubLedgerSummaryAsync(new SubLedgerSummaryFilterDto { FiscalYearId = 1, BranchId = 1, PartyType = "Customer", OnlyWithBalances = true });
            Assert.Single(resBal.Items);
            Assert.Equal(1, resBal.Items[0].PartyId);
            Assert.Equal(1000m, resBal.Items[0].ClosingBalanceLocal);
        }
    }
}
