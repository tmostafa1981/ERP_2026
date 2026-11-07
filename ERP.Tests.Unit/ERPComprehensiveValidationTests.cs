using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.DTOs.Lookups;
using ERP.Application.DTOs.Reports;
using ERP.Application.Interfaces;
using ERP.Application.Models;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using ERP.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Xunit.Abstractions;
using UnitEntity = ERP.Domain.Entities.Unit;

namespace ERP.Tests.Unit
{
    public class ERPComprehensiveValidationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly List<ErpDbContext> _activeContexts = new();

        public ERPComprehensiveValidationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
            foreach (var ctx in _activeContexts)
            {
                ctx.Dispose();
            }
        }

        private ErpDbContext CreateDbContext(string dbName, int branchId = 1, bool isSuperAdmin = false, string userId = "test-user-1")
        {
            var userMock = new Mock<ICurrentUserService>();
            userMock.Setup(u => u.BranchId).Returns(branchId);
            userMock.Setup(u => u.IsSuperAdmin).Returns(isSuperAdmin);
            userMock.Setup(u => u.IsAuthenticated).Returns(true);
            userMock.Setup(u => u.UserId).Returns(userId);

            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var ctx = new ErpDbContext(options, userMock.Object);
            _activeContexts.Add(ctx);
            return ctx;
        }

        private ICurrentUserContext CreateUserContext(string userId = "test-user-1", int branchId = 1, bool hasAllBranches = false)
        {
            var mock = new Mock<ICurrentUserContext>();
            mock.Setup(c => c.IsAuthenticated).Returns(true);
            mock.Setup(c => c.UserId).Returns(userId);
            mock.Setup(c => c.Username).Returns(userId);
            mock.Setup(c => c.BranchId).Returns(branchId);
            mock.Setup(c => c.BranchName).Returns($"فرع {branchId}");
            mock.Setup(c => c.FiscalYearId).Returns(1);
            mock.Setup(c => c.FiscalYearName).Returns("2026");
            mock.Setup(c => c.HasAllBranches).Returns(hasAllBranches);
            mock.Setup(c => c.CanAccessBranch(It.IsAny<int>())).Returns((int b) => hasAllBranches || b == branchId);
            mock.Setup(c => c.CanAccessCompany(It.IsAny<int>())).Returns(true);
            return mock.Object;
        }

        private async Task SeedMasterDataAsync(ErpDbContext context)
        {
            // Branches
            var b1 = new Branch { Id = 1, Code = "B1", Name = "Branch 1", NameAr = "الفرع الرئيسي", IsActive = true };
            var b2 = new Branch { Id = 2, Code = "B2", Name = "Branch 2", NameAr = "فرع الإسكندرية", IsActive = true };
            var b3 = new Branch { Id = 3, Code = "B3", Name = "Branch 3", NameAr = "فرع طنطا", IsActive = true };
            context.Branches.AddRange(b1, b2, b3);

            // Currencies
            var egp = new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", ExchangeRate = 1.0m, IsActive = true };
            var usd = new Currency { Id = 2, Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar", ExchangeRate = 50.0m, IsActive = true };
            context.Currencies.AddRange(egp, usd);

            // Fiscal Year & Periods (2026)
            var fy2026 = new FiscalYear
            {
                Id = 1,
                Year = "2026",
                Name = "السنة المالية 2026",
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 12, 31),
                IsActive = true,
                IsClosed = false
            };
            context.FiscalYears.Add(fy2026);

            // Month 1: Jan 2026 (Closed for testing)
            var p1 = new FiscalPeriod
            {
                Id = 1,
                FiscalYearId = 1,
                PeriodNumber = 1,
                NameAr = "يناير 2026",
                NameEn = "Jan 2026",
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 1, 31),
                IsClosed = true
            };
            // Month 2: Feb 2026 (Open)
            var p2 = new FiscalPeriod
            {
                Id = 2,
                FiscalYearId = 1,
                PeriodNumber = 2,
                NameAr = "فبراير 2026",
                NameEn = "Feb 2026",
                StartDate = new DateTime(2026, 2, 1),
                EndDate = new DateTime(2026, 2, 28),
                IsClosed = false
            };
            // Month 3: Mar 2026 (Open)
            var p3 = new FiscalPeriod
            {
                Id = 3,
                FiscalYearId = 1,
                PeriodNumber = 3,
                NameAr = "مارس 2026",
                NameEn = "Mar 2026",
                StartDate = new DateTime(2026, 3, 1),
                EndDate = new DateTime(2026, 3, 31),
                IsClosed = false
            };
            context.FiscalPeriods.AddRange(p1, p2, p3);

            // GL Accounts
            var accCust = new GLAccount { Id = 101, Code = "110301", NameAr = "العملاء المحليين", NameEn = "Local Customers", SubLedgerType = SubLedgerType.Customer, AccountNature = AccountNature.Debit, IsActive = true, CurrencyId = 1 };
            var accVend = new GLAccount { Id = 102, Code = "210101", NameAr = "الموردين المحليين", NameEn = "Local Vendors", SubLedgerType = SubLedgerType.Vendor, AccountNature = AccountNature.Credit, IsActive = true, CurrencyId = 1 };
            var accCash = new GLAccount { Id = 103, Code = "110101", NameAr = "الصندوق الرئيسي", NameEn = "Main Cash", SubLedgerType = SubLedgerType.None, AccountNature = AccountNature.Debit, IsActive = true, CurrencyId = 1 };
            var accBankUSD = new GLAccount { Id = 104, Code = "110202", NameAr = "بنك القاهرة دولار", NameEn = "Bank USD", SubLedgerType = SubLedgerType.Bank, AccountNature = AccountNature.Debit, IsActive = true, CurrencyId = 2 };
            var accSales = new GLAccount { Id = 105, Code = "410101", NameAr = "إيرادات المبيعات", NameEn = "Sales Revenues", AccountNature = AccountNature.Credit, IsActive = true, CurrencyId = 1 };
            var accPurch = new GLAccount { Id = 106, Code = "510101", NameAr = "المشتريات", NameEn = "Purchases", AccountNature = AccountNature.Debit, IsActive = true, CurrencyId = 1 };
            var accVat = new GLAccount { Id = 107, Code = "210501", NameAr = "ضريبة القيمة المضافة", NameEn = "VAT", AccountNature = AccountNature.Credit, IsActive = true, CurrencyId = 1 };
            context.GLAccounts.AddRange(accCust, accVend, accCash, accBankUSD, accSales, accPurch, accVat);

            // Categories
            var catCust = new CustomerCategory { Id = 1, BranchId = 1, Code = "CCAT-01", NameAr = "عملاء تجزئة", NameEn = "Retail Customers", IsActive = true };
            var catVend = new VendorCategory { Id = 1, BranchId = 1, Code = "VCAT-01", NameAr = "موردو خامات", NameEn = "Raw Vendors", IsActive = true };
            context.CustomerCategories.Add(catCust);
            context.VendorCategories.Add(catVend);

            // Customers & Vendors
            var c1 = new Customer { Id = 1, BranchId = 1, Code = "CUS-001", NameAr = "شركة الأمل للتجارة", NameEn = "Al Amal Trading", CustomerCategoryId = 1, FinancialAccountId = 101, CurrencyId = 1, IsActive = true };
            var c2 = new Customer { Id = 2, BranchId = 2, Code = "CUS-002", NameAr = "مؤسسة النور فرع 2", NameEn = "Al Noor Branch 2", CustomerCategoryId = 1, FinancialAccountId = 101, CurrencyId = 1, IsActive = true };
            var v1 = new Vendor { Id = 1, BranchId = 1, Code = "VEN-001", NameAr = "شركة التوريدات العالمية", NameEn = "Global Supplies", VendorCategoryId = 1, FinancialAccountId = 102, CurrencyId = 1, IsActive = true };
            var v2 = new Vendor { Id = 2, BranchId = 2, Code = "VEN-002", NameAr = "مورد الإسكندرية", NameEn = "Alexandria Supplier", VendorCategoryId = 1, FinancialAccountId = 102, CurrencyId = 1, IsActive = true };
            context.Customers.AddRange(c1, c2);
            context.Vendors.AddRange(v1, v2);

            // Warehouses
            var wh1 = new Warehouse { Id = 1, BranchId = 1, Code = "WH-01", NameAr = "المستودع الرئيسي", NameEn = "Main Warehouse", IsActive = true };
            var wh2 = new Warehouse { Id = 2, BranchId = 2, Code = "WH-02", NameAr = "مستودع الإسكندرية", NameEn = "Alex Warehouse", IsActive = true };
            context.Warehouses.AddRange(wh1, wh2);

            // Salesmen / Representatives
            var s1 = new Salesman { Id = 1, BranchId = 1, Code = "SLM-001", NameAr = "أحمد محمود", NameEn = "Ahmed Mahmoud", IsActive = true };
            var s2 = new Salesman { Id = 2, BranchId = 2, Code = "SLM-002", NameAr = "مصطفى كامل", NameEn = "Mostafa Kamel", IsActive = true };
            context.Salesmen.AddRange(s1, s2);

            // Item Groups & Items
            var grp1 = new ItemGroup { Id = 1, Code = "GRP-01", NameAr = "أجهزة كهربائية", NameEn = "Appliances", IsActive = true };
            context.ItemGroups.Add(grp1);

            var u1 = new UnitEntity { Id = 1, Code = "PCS", NameAr = "قطعة", NameEn = "Piece", IsActive = true };
            var itm1 = new Item { Id = 1, Code = "ITM-001", NameAr = "شاشة سامسونج 55", NameEn = "Samsung TV 55", ItemGroupId = 1, TrackingType = ItemTrackingType.SerialNumber, IsActive = true };
            var itm2 = new Item { Id = 2, Code = "ITM-002", NameAr = "كابل HDMI", NameEn = "HDMI Cable", ItemGroupId = 1, TrackingType = ItemTrackingType.None, IsActive = true };
            context.Units.Add(u1);
            context.Items.AddRange(itm1, itm2);

            // Transaction Types
            var ttSales = new TransactionType
            {
                Id = 1,
                Code = "SI_CREDIT",
                Module = TransactionModule.Sales,
                BaseNature = DocumentBaseNature.Invoice,
                NameAr = "فاتورة مبيعات آجلة",
                NameEn = "Sales Invoice Credit",
                CodeSequencePrefix = "SI-",
                AccountingEffect = AccountingEffect.FullInvoiceAndCOGS,
                InventoryEffect = InventoryEffect.IssueStock,
                DefaultDebitAccountId = 101,
                DefaultCreditAccountId = 105,
                DefaultTaxAccountId = 107,
                IsCreditAllowed = true,
                IsCashAllowed = true,
                DefaultWarehouseId = 1,
                IsActive = true
            };
            var ttPurch = new TransactionType
            {
                Id = 2,
                Code = "PI_CREDIT",
                Module = TransactionModule.Purchase,
                BaseNature = DocumentBaseNature.Invoice,
                NameAr = "فاتورة مشتريات آجلة",
                NameEn = "Purchase Invoice Credit",
                CodeSequencePrefix = "PI-",
                AccountingEffect = AccountingEffect.FullInvoiceAndCOGS,
                InventoryEffect = InventoryEffect.ReceiveStock,
                DefaultDebitAccountId = 106,
                DefaultCreditAccountId = 102,
                DefaultTaxAccountId = 107,
                IsCreditAllowed = true,
                IsCashAllowed = true,
                DefaultWarehouseId = 1,
                IsActive = true
            };
            context.TransactionTypes.AddRange(ttSales, ttPurch);

            await context.SaveChangesAsync();
        }

        #region Domain 1: Customer Statement Testing
        [Fact]
        public async Task Test1_CustomerStatement_Validates_Opening_Debit_Credit_Running_And_ClosingBalances()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName, branchId: 1, isSuperAdmin: true);
            await SeedMasterDataAsync(context);

            var userCtx = CreateUserContext(branchId: 1, hasAllBranches: false);
            var reportService = new AccountingReportService(context, userCtx);

            // 1. Pre-period Opening Journal Entry (2026-01-15 -> Prior to DateFrom: 2026-02-01)
            var openingJV = new JournalEntryHeader
            {
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-OPENING-01",
                EntryDate = new DateTime(2026, 1, 15),
                Status = JournalEntryStatus.Posted,
                Description = "رصيد افتتاحي للعميل",
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine
                    {
                        LineNumber = 1,
                        AccountId = 101, // Customers
                        SubLedgerType = SubLedgerType.Customer,
                        SubLedgerId = 1, // Customer 1
                        DebitLocal = 5000m,
                        CreditLocal = 0m,
                        DebitForeign = 5000m,
                        CreditForeign = 0m,
                        CurrencyId = 1,
                        ExchangeRate = 1.0m,
                        Description = "رصيد افتتاحي مدين"
                    },
                    new JournalEntryLine
                    {
                        LineNumber = 2,
                        AccountId = 105,
                        DebitLocal = 0m,
                        CreditLocal = 5000m,
                        DebitForeign = 0m,
                        CreditForeign = 5000m,
                        CurrencyId = 1,
                        ExchangeRate = 1.0m
                    }
                }
            };
            context.JournalEntryHeaders.Add(openingJV);

            // 2. Period Journal Entry: Sales movement (Debit 3,000) on 2026-02-05
            var periodJV1 = new JournalEntryHeader
            {
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-PERIOD-01",
                EntryDate = new DateTime(2026, 2, 5),
                Status = JournalEntryStatus.Posted,
                Description = "مبيعات خدمات إضافية",
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine
                    {
                        LineNumber = 1,
                        AccountId = 101,
                        SubLedgerType = SubLedgerType.Customer,
                        SubLedgerId = 1,
                        DebitLocal = 3000m,
                        CreditLocal = 0m,
                        DebitForeign = 3000m,
                        CreditForeign = 0m,
                        CurrencyId = 1,
                        ExchangeRate = 1.0m,
                        Description = "إثبات خدمات"
                    },
                    new JournalEntryLine
                    {
                        LineNumber = 2,
                        AccountId = 105,
                        DebitLocal = 0m,
                        CreditLocal = 3000m,
                        DebitForeign = 0m,
                        CreditForeign = 3000m,
                        CurrencyId = 1,
                        ExchangeRate = 1.0m
                    }
                }
            };
            context.JournalEntryHeaders.Add(periodJV1);

            // 3. Receipt Voucher: Customer payment (Credit 2,000) on 2026-02-10
            var periodJV2 = new JournalEntryHeader
            {
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-RV-001",
                EntryDate = new DateTime(2026, 2, 10),
                Status = JournalEntryStatus.Posted,
                Description = "سند قبض رقم RV-001",
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine
                    {
                        LineNumber = 1,
                        AccountId = 103, // Cash
                        DebitLocal = 2000m,
                        CreditLocal = 0m,
                        DebitForeign = 2000m,
                        CreditForeign = 0m,
                        CurrencyId = 1,
                        ExchangeRate = 1.0m
                    },
                    new JournalEntryLine
                    {
                        LineNumber = 2,
                        AccountId = 101, // Customers
                        SubLedgerType = SubLedgerType.Customer,
                        SubLedgerId = 1,
                        DebitLocal = 0m,
                        CreditLocal = 2000m,
                        DebitForeign = 0m,
                        CreditForeign = 2000m,
                        CurrencyId = 1,
                        ExchangeRate = 1.0m,
                        Description = "سداد نقدي من العميل"
                    }
                }
            };
            context.JournalEntryHeaders.Add(periodJV2);

            // 4. Payment Voucher (Customer Refund: Debit 500) on 2026-02-15
            var periodJV3 = new JournalEntryHeader
            {
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-PV-001",
                EntryDate = new DateTime(2026, 2, 15),
                Status = JournalEntryStatus.Posted,
                Description = "سند صرف استرداد رقم PV-001",
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine
                    {
                        LineNumber = 1,
                        AccountId = 101, // Customers
                        SubLedgerType = SubLedgerType.Customer,
                        SubLedgerId = 1,
                        DebitLocal = 500m,
                        CreditLocal = 0m,
                        DebitForeign = 500m,
                        CreditForeign = 0m,
                        CurrencyId = 1,
                        ExchangeRate = 1.0m,
                        Description = "استرداد نقدي للعميل"
                    },
                    new JournalEntryLine
                    {
                        LineNumber = 2,
                        AccountId = 103, // Cash
                        DebitLocal = 0m,
                        CreditLocal = 500m,
                        DebitForeign = 0m,
                        CreditForeign = 500m,
                        CurrencyId = 1,
                        ExchangeRate = 1.0m
                    }
                }
            };
            context.JournalEntryHeaders.Add(periodJV3);

            await context.SaveChangesAsync();

            // Execute Customer Statement Query
            var filter = new ReportFilterDto
            {
                CustomerId = 1,
                BranchId = 1,
                FiscalYearId = 1,
                DateFrom = new DateTime(2026, 2, 1),
                DateTo = new DateTime(2026, 2, 28)
            };

            var statement = await reportService.GetCustomerStatementAsync(filter);

            // Assertions
            Assert.NotNull(statement);
            Assert.Equal("CUS-001", statement.Code);
            Assert.Equal(5000m, statement.OpeningBalanceLocal);
            Assert.Equal(3, statement.Lines.Count);

            // Movement Totals
            // Debits: 3000 + 500 = 3500
            // Credits: 2000
            Assert.Equal(3500m, statement.TotalPeriodDebitLocal);
            Assert.Equal(2000m, statement.TotalPeriodCreditLocal);

            // Running Balance calculations check
            // Line 1 (2026-02-05): 5000 + 3000 = 8000
            Assert.Equal(8000m, statement.Lines[0].BalanceLocal);
            // Line 2 (2026-02-10): 8000 - 2000 = 6000
            Assert.Equal(6000m, statement.Lines[1].BalanceLocal);
            // Line 3 (2026-02-15): 6000 + 500 = 6500
            Assert.Equal(6500m, statement.Lines[2].BalanceLocal);

            // Closing Balance check
            Assert.Equal(6500m, statement.ClosingBalanceLocal);
        }
        #endregion

        #region Domain 2: Vendor Statement Testing
        [Fact]
        public async Task Test2_VendorStatement_Validates_Opening_Debit_Credit_And_VendorIsolation()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName, branchId: 1, isSuperAdmin: true);
            await SeedMasterDataAsync(context);

            var userCtx = CreateUserContext(branchId: 1, hasAllBranches: false);
            var reportService = new AccountingReportService(context, userCtx);

            // Pre-period Opening for Vendor 1 (Credit 10,000) on 2026-01-10
            context.JournalEntryHeaders.Add(new JournalEntryHeader
            {
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-VOPEN-01",
                EntryDate = new DateTime(2026, 1, 10),
                Status = JournalEntryStatus.Posted,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine { LineNumber = 1, AccountId = 106, DebitLocal = 10000m, CurrencyId = 1, ExchangeRate = 1m },
                    new JournalEntryLine { LineNumber = 2, AccountId = 102, SubLedgerType = SubLedgerType.Vendor, SubLedgerId = 1, CreditLocal = 10000m, CurrencyId = 1, ExchangeRate = 1m }
                }
            });

            // Purchase movement for Vendor 1 (Credit 4,000) on 2026-02-05
            context.JournalEntryHeaders.Add(new JournalEntryHeader
            {
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-VPERIOD-01",
                EntryDate = new DateTime(2026, 2, 5),
                Status = JournalEntryStatus.Posted,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine { LineNumber = 1, AccountId = 106, DebitLocal = 4000m, CurrencyId = 1, ExchangeRate = 1m },
                    new JournalEntryLine { LineNumber = 2, AccountId = 102, SubLedgerType = SubLedgerType.Vendor, SubLedgerId = 1, CreditLocal = 4000m, CurrencyId = 1, ExchangeRate = 1m }
                }
            });

            // Payment to Vendor 1 (Debit 7,000) on 2026-02-12
            context.JournalEntryHeaders.Add(new JournalEntryHeader
            {
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-VPAY-01",
                EntryDate = new DateTime(2026, 2, 12),
                Status = JournalEntryStatus.Posted,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine { LineNumber = 1, AccountId = 102, SubLedgerType = SubLedgerType.Vendor, SubLedgerId = 1, DebitLocal = 7000m, CurrencyId = 1, ExchangeRate = 1m },
                    new JournalEntryLine { LineNumber = 2, AccountId = 103, CreditLocal = 7000m, CurrencyId = 1, ExchangeRate = 1m }
                }
            });

            // Separate movement for Vendor 2 (Should NOT appear in Vendor 1 statement)
            context.JournalEntryHeaders.Add(new JournalEntryHeader
            {
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-V2-01",
                EntryDate = new DateTime(2026, 2, 14),
                Status = JournalEntryStatus.Posted,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine { LineNumber = 1, AccountId = 106, DebitLocal = 9999m, CurrencyId = 1, ExchangeRate = 1m },
                    new JournalEntryLine { LineNumber = 2, AccountId = 102, SubLedgerType = SubLedgerType.Vendor, SubLedgerId = 2, CreditLocal = 9999m, CurrencyId = 1, ExchangeRate = 1m }
                }
            });

            await context.SaveChangesAsync();

            var filter = new ReportFilterDto
            {
                VendorId = 1,
                BranchId = 1,
                FiscalYearId = 1,
                DateFrom = new DateTime(2026, 2, 1),
                DateTo = new DateTime(2026, 2, 28)
            };

            var statement = await reportService.GetVendorStatementAsync(filter);

            Assert.NotNull(statement);
            Assert.Equal("VEN-001", statement.Code);
            // Vendor Opening: Credit 10,000 -> OpeningBalance = Debit(0) - Credit(10000) = -10000
            Assert.Equal(-10000m, statement.OpeningBalanceLocal);
            Assert.Equal(2, statement.Lines.Count); // Only Vendor 1 lines, Vendor 2 excluded!

            Assert.Equal(7000m, statement.TotalPeriodDebitLocal);
            Assert.Equal(4000m, statement.TotalPeriodCreditLocal);

            // Closing Balance: -10000 + 7000 - 4000 = -7000
            Assert.Equal(-7000m, statement.ClosingBalanceLocal);
        }
        #endregion

        #region Domain 3: General Account Statement Testing
        [Fact]
        public async Task Test3_GeneralAccountStatement_Calculates_Foreign_And_Local_Balances_Accurately()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName, branchId: 1, isSuperAdmin: true);
            await SeedMasterDataAsync(context);

            var userCtx = CreateUserContext(branchId: 1, hasAllBranches: false);
            var reportService = new AccountingReportService(context, userCtx);

            // Account 104 is Bank USD (Exchange rate 50 EGP per USD)
            // JV 1: Deposit $1,000 USD (= 50,000 EGP)
            context.JournalEntryHeaders.Add(new JournalEntryHeader
            {
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-USD-01",
                EntryDate = new DateTime(2026, 2, 1),
                Status = JournalEntryStatus.Posted,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine
                    {
                        LineNumber = 1,
                        AccountId = 104, // Bank USD
                        DebitForeign = 1000m,
                        CreditForeign = 0m,
                        DebitLocal = 50000m,
                        CreditLocal = 0m,
                        CurrencyId = 2,
                        ExchangeRate = 50m,
                        Description = "إيداع دولاري"
                    },
                    new JournalEntryLine
                    {
                        LineNumber = 2,
                        AccountId = 105,
                        DebitForeign = 0m,
                        CreditForeign = 1000m,
                        DebitLocal = 0m,
                        CreditLocal = 50000m,
                        CurrencyId = 2,
                        ExchangeRate = 50m
                    }
                }
            });

            // JV 2: Transfer out $300 USD (= 15,000 EGP)
            context.JournalEntryHeaders.Add(new JournalEntryHeader
            {
                BranchId = 1,
                FiscalYearId = 1,
                EntryNumber = "JV-USD-02",
                EntryDate = new DateTime(2026, 2, 10),
                Status = JournalEntryStatus.Posted,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine
                    {
                        LineNumber = 1,
                        AccountId = 103, // Cash
                        DebitForeign = 0m,
                        CreditForeign = 0m,
                        DebitLocal = 15000m,
                        CreditLocal = 0m,
                        CurrencyId = 1,
                        ExchangeRate = 1m
                    },
                    new JournalEntryLine
                    {
                        LineNumber = 2,
                        AccountId = 104, // Bank USD
                        DebitForeign = 0m,
                        CreditForeign = 300m,
                        DebitLocal = 0m,
                        CreditLocal = 15000m,
                        CurrencyId = 2,
                        ExchangeRate = 50m,
                        Description = "تحويل دولاري"
                    }
                }
            });

            await context.SaveChangesAsync();

            var filter = new ReportFilterDto
            {
                AccountId = 104,
                BranchId = 1,
                FiscalYearId = 1,
                DateFrom = new DateTime(2026, 2, 1),
                DateTo = new DateTime(2026, 2, 28)
            };

            var accountStatement = await reportService.GetAccountStatementAsync(filter);

            Assert.NotNull(accountStatement);
            Assert.Equal("110202", accountStatement.AccountCode);
            Assert.Equal(2, accountStatement.Lines.Count);

            // Foreign Currency (USD)
            Assert.Equal(1000m, accountStatement.TotalPeriodDebitForeign);
            Assert.Equal(300m, accountStatement.TotalPeriodCreditForeign);
            Assert.Equal(700m, accountStatement.ClosingBalanceForeign);

            // Local Currency (EGP)
            Assert.Equal(50000m, accountStatement.TotalPeriodDebitLocal);
            Assert.Equal(15000m, accountStatement.TotalPeriodCreditLocal);
            Assert.Equal(35000m, accountStatement.ClosingBalanceLocal);
        }
        #endregion

        #region Domain 4 & 5: Multi-Branch Testing and Cross-Branch Validation
        [Fact]
        public async Task Test4_5_MultiBranch_Isolation_And_CrossBranch_NoDataLeak()
        {
            var dbName = Guid.NewGuid().ToString();
            using var adminCtx = CreateDbContext(dbName, branchId: 1, isSuperAdmin: true);
            await SeedMasterDataAsync(adminCtx);

            // Add branch-specific documents
            adminCtx.JournalEntryHeaders.AddRange(
                new JournalEntryHeader { Id = 101, BranchId = 1, FiscalYearId = 1, EntryNumber = "JV-B1-01", Status = JournalEntryStatus.Posted },
                new JournalEntryHeader { Id = 102, BranchId = 2, FiscalYearId = 1, EntryNumber = "JV-B2-01", Status = JournalEntryStatus.Posted },
                new JournalEntryHeader { Id = 103, BranchId = 3, FiscalYearId = 1, EntryNumber = "JV-B3-01", Status = JournalEntryStatus.Posted }
            );
            await adminCtx.SaveChangesAsync();

            // 1. Querying as Branch 1 user through DbContext Query Filters
            using var userB1Ctx = CreateDbContext(dbName, branchId: 1, isSuperAdmin: false);
            var b1Customers = await userB1Ctx.Customers.ToListAsync();
            var b1Vendors = await userB1Ctx.Vendors.ToListAsync();
            var b1Warehouses = await userB1Ctx.Warehouses.ToListAsync();
            var b1JVs = await userB1Ctx.JournalEntryHeaders.ToListAsync();

            Assert.All(b1Customers, c => Assert.Equal(1, c.BranchId));
            Assert.All(b1Vendors, v => Assert.Equal(1, v.BranchId));
            Assert.All(b1Warehouses, w => Assert.Equal(1, w.BranchId));
            Assert.All(b1JVs, j => Assert.Equal(1, j.BranchId));
            Assert.DoesNotContain(b1Customers, c => c.BranchId == 2);
            Assert.DoesNotContain(b1Warehouses, w => w.BranchId == 2);

            // 2. Querying as Branch 2 user
            using var userB2Ctx = CreateDbContext(dbName, branchId: 2, isSuperAdmin: false);
            var b2Customers = await userB2Ctx.Customers.ToListAsync();
            Assert.All(b2Customers, c => Assert.Equal(2, c.BranchId));
            Assert.DoesNotContain(b2Customers, c => c.BranchId == 1);

            // 3. Cross-Branch Lookup Testing: Branch 1 lookup should not return Branch 2 warehouse
            var userMock = new Mock<ICurrentUserService>();
            userMock.Setup(u => u.BranchId).Returns(1);
            userMock.Setup(u => u.IsSuperAdmin).Returns(false);

            var lookupService = new LookupService(userB1Ctx, userMock.Object);
            var whLookup = await lookupService.SearchAsync(new LookupSearchRequest
            {
                EntityType = "warehouse",
                BranchId = 1
            });

            Assert.NotEmpty(whLookup.Items);
            Assert.All(whLookup.Items, item => Assert.NotEqual("Alex Warehouse", item.NameEn));
            Assert.Contains(whLookup.Items, item => item.NameEn == "Main Warehouse");
        }
        #endregion

        #region Domain 6: Searchable Lookup Testing
        [Fact]
        public async Task Test6_SearchableLookups_SearchByCode_NameAr_NameEn_AndDynamicRefresh()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName, branchId: 1, isSuperAdmin: true);
            await SeedMasterDataAsync(context);

            var userMock = new Mock<ICurrentUserService>();
            userMock.Setup(u => u.BranchId).Returns(1);
            userMock.Setup(u => u.IsSuperAdmin).Returns(true);

            var lookupService = new LookupService(context, userMock.Object);

            // 1. Search Salesman by Code: "SLM-001"
            var resCode = await lookupService.SearchAsync(new LookupSearchRequest { EntityType = "salesman", Query = "SLM-001" });
            Assert.Single(resCode.Items);
            Assert.Equal("SLM-001", resCode.Items[0].Code);

            // 2. Search Salesman by NameAr: "أحمد"
            var resAr = await lookupService.SearchAsync(new LookupSearchRequest { EntityType = "salesman", Query = "أحمد" });
            Assert.Contains(resAr.Items, s => s.NameAr.Contains("أحمد"));

            // 3. Search Salesman by NameEn: "Ahmed"
            var resEn = await lookupService.SearchAsync(new LookupSearchRequest { EntityType = "salesman", Query = "Ahmed" });
            Assert.Contains(resEn.Items, s => s.NameEn != null && s.NameEn.Contains("Ahmed"));

            // 4. Dynamic Refresh Test:
            // Create a brand new Representative right now
            var newRep = new Salesman
            {
                BranchId = 1,
                Code = "SLM-NEW-99",
                NameAr = "مندوب جديد فوري",
                NameEn = "Instant New Rep",
                IsActive = true
            };
            context.Salesmen.Add(newRep);
            await context.SaveChangesAsync();

            // Immediately search without restart
            var freshSearch = await lookupService.SearchAsync(new LookupSearchRequest { EntityType = "salesman", Query = "SLM-NEW-99" });
            Assert.Single(freshSearch.Items);
            Assert.Equal("SLM-NEW-99", freshSearch.Items[0].Code);
            Assert.Equal("مندوب جديد فوري", freshSearch.Items[0].NameAr);
        }
        #endregion

        #region Domain 7: Concurrency Testing
        [Fact]
        public async Task Test7_Concurrency_Simulate20ConcurrentUsers_UniqueNumbering_NoDeadlocks()
        {
            var dbName = Guid.NewGuid().ToString();
            using var seedCtx = CreateDbContext(dbName, branchId: 1, isSuperAdmin: true);
            await SeedMasterDataAsync(seedCtx);

            int concurrentUsers = 20;
            var generatedEntryNumbers = new ConcurrentBag<string>();
            var generatedVoucherNumbers = new ConcurrentBag<string>();
            var exceptions = new ConcurrentBag<Exception>();

            var tasks = new List<Task>();

            // All 20 simulated concurrent users hit Branch 1 simultaneously
            for (int i = 1; i <= concurrentUsers; i++)
            {
                int userIdIdx = i;
                int branchId = 1;

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var threadCtx = CreateDbContext(dbName, branchId: branchId, isSuperAdmin: true, userId: $"user-{userIdIdx}");
                        var codeSeqService = new CodeSequenceService(threadCtx);

                        // Test Journal Entry sequential number generation
                        var jvCode = await codeSeqService.GetNextCodeAsync("JournalEntry", branchId);
                        generatedEntryNumbers.Add(jvCode);

                        // Test Voucher sequential number generation
                        var rvCode = await codeSeqService.GetNextCodeAsync("ReceiptVoucher", branchId);
                        generatedVoucherNumbers.Add(rvCode);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assertions
            Assert.Empty(exceptions);
            Assert.Equal(concurrentUsers, generatedEntryNumbers.Count);
            Assert.Equal(concurrentUsers, generatedVoucherNumbers.Count);

            // Verify absolute uniqueness per branch
            var distinctEntries = generatedEntryNumbers.Distinct().ToList();
            Assert.Equal(concurrentUsers, distinctEntries.Count);

            var distinctVouchers = generatedVoucherNumbers.Distinct().ToList();
            Assert.Equal(concurrentUsers, distinctVouchers.Count);
        }
        #endregion

        #region Domain 8: Audit Trail Validation
        [Fact]
        public async Task Test8_AuditTrail_Logs_Create_Edit_Delete_Cancel_With_Full_AuditData()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName, branchId: 1, isSuperAdmin: true);
            await SeedMasterDataAsync(context);

            var userContextMock = new Mock<ICurrentUserContext>();
            userContextMock.Setup(x => x.UserId).Returns("user-admin");
            userContextMock.Setup(x => x.Username).Returns("admin");
            userContextMock.Setup(x => x.EmployeeCode).Returns("105");
            userContextMock.Setup(x => x.CompanyId).Returns(1);
            userContextMock.Setup(x => x.BranchId).Returns(1);
            userContextMock.Setup(x => x.FiscalYearId).Returns(1);

            var httpContextMock = new Mock<IHttpContextAccessor>();
            httpContextMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

            var services = new ServiceCollection();
            services.AddSingleton(context);
            var sp = services.BuildServiceProvider();

            var auditService = new AuditService(sp, userContextMock.Object, httpContextMock.Object, NullLogger<AuditService>.Instance);

            // 1. Audit Create
            await auditService.LogCreateAsync(
                screenCode: "SI",
                screenName: "SalesInvoices",
                moduleName: "Sales",
                entityName: "DocumentHeader",
                entityId: "1",
                newValues: new { TotalAmount = 5000m, CustomerId = 1 }
            );

            // 2. Audit Update
            await auditService.LogUpdateAsync(
                screenCode: "SI",
                screenName: "SalesInvoices",
                moduleName: "Sales",
                entityName: "DocumentHeader",
                entityId: "1",
                oldValues: new { TotalAmount = 5000m },
                newValues: new { TotalAmount = 5500m }
            );

            // 3. Audit Delete
            await auditService.LogDeleteAsync(
                screenCode: "SI",
                screenName: "SalesInvoices",
                moduleName: "Sales",
                entityName: "DocumentHeader",
                entityId: "1",
                oldValues: new { TotalAmount = 5500m }
            );

            // 4. Audit Cancel
            await auditService.LogAsync(new AuditLogEntry
            {
                ScreenCode = "RV",
                ScreenName = "CashBankVouchers",
                ModuleName = "Cash",
                EntityName = "VoucherHeader",
                EntityId = "1",
                Action = AuditAction.Cancel,
                OldValues = new { Status = "Posted" },
                NewValues = new { Status = "Cancelled" }
            });

            var logs = await context.AuditLogs.OrderBy(a => a.Id).ToListAsync();

            Assert.Equal(4, logs.Count);

            // Verify Create log
            var createLog = logs[0];
            Assert.Equal("Create", createLog.Action);
            Assert.Equal("user-admin", createLog.UserId);
            Assert.Equal(1, createLog.BranchId);
            Assert.NotNull(createLog.NewValues);
            Assert.Contains("5000", createLog.NewValues);

            // Verify Edit log
            var editLog = logs[1];
            Assert.Equal("Update", editLog.Action);
            Assert.NotNull(editLog.OldValues);
            Assert.NotNull(editLog.NewValues);

            // Verify Delete log
            var deleteLog = logs[2];
            Assert.Equal("Delete", deleteLog.Action);
            Assert.NotNull(deleteLog.OldValues);
            Assert.Null(deleteLog.NewValues);

            // Verify Cancel log
            var cancelLog = logs[3];
            Assert.Equal("Cancel", cancelLog.Action);
            Assert.Contains("Cancelled", cancelLog.NewValues);
        }
        #endregion

        #region Domain 9: Fiscal Period Validation
        [Fact]
        public async Task Test9_FiscalPeriod_OpenAllows_ClosedBlocks_BasedOnDocumentDate()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName, branchId: 1, isSuperAdmin: true);
            await SeedMasterDataAsync(context);

            var auditMock = new Mock<IAuditService>();
            var fiscalPeriodService = new FiscalPeriodService(context, auditMock.Object);

            // Jan 2026 is CLOSED (defined in SeedMasterDataAsync)
            var closedDate = new DateTime(2026, 1, 15);
            var closedValidation = await fiscalPeriodService.ValidateDatePeriodAsync(closedDate);

            Assert.False(closedValidation.IsAllowed);
            Assert.Contains("مغلق", closedValidation.ErrorMessage);

            // Feb 2026 is OPEN
            var openDate = new DateTime(2026, 2, 10);
            var openValidation = await fiscalPeriodService.ValidateDatePeriodAsync(openDate);

            Assert.True(openValidation.IsAllowed);
            Assert.Null(openValidation.ErrorMessage);

            // Date outside any active Fiscal Year
            var outOfRangeDate = new DateTime(2025, 12, 31);
            var outOfRangeValidation = await fiscalPeriodService.ValidateDatePeriodAsync(outOfRangeDate);

            Assert.False(outOfRangeValidation.IsAllowed);
            Assert.Contains("لا توجد سنة مالية معرّفة", outOfRangeValidation.ErrorMessage);
        }
        #endregion

        #region Domain 10: Automatic Posting Validation
        [Fact]
        public async Task Test10_AutomaticPosting_ImmediateAccountingAndInventoryEffects_WithoutManualPost()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName, branchId: 1, isSuperAdmin: true);
            await SeedMasterDataAsync(context);

            var auditMock = new Mock<IAuditService>();
            var codeSeqMock = new Mock<ICodeSequenceService>();
            var userCtx = CreateUserContext(branchId: 1);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var serialService = new SerialNumberService(context);
            var ttService = new TransactionTypeService(context, cache, userCtx, auditMock.Object);
            var fiscalPeriodService = new FiscalPeriodService(context, auditMock.Object);
            var docService = new UniversalDocumentService(context, ttService, serialService, codeSeqMock.Object, userCtx, auditMock.Object, fiscalPeriodService);

            // Pre-seed serial in warehouse
            await serialService.RegisterReceivedSerialsAsync(
                documentId: 99,
                docType: "PI",
                itemId: 1,
                warehouseId: 1,
                branchId: 1,
                serialNumbers: new List<string> { "SN-AUTO-1001" });

            var docDto = new DocumentHeaderDto
            {
                BranchId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 1, // SI_CREDIT
                DocumentDate = new DateTime(2026, 2, 20), // Open Period
                PaymentMethod = VoucherPaymentMethod.Other, // Credit / On-account
                PartyType = DocumentPartyType.Customer,
                PartyId = 1,
                WarehouseId = 1,
                Lines = new List<DocumentLineDto>
                {
                    new DocumentLineDto
                    {
                        LineNumber = 1,
                        ItemId = 1, // Serialized TV
                        Quantity = 1,
                        UnitPrice = 12000m,
                        WarehouseId = 1,
                        TrackingType = ItemTrackingType.SerialNumber,
                        SerialNumbers = new List<string> { "SN-AUTO-1001" }
                    }
                }
            };

            // Save Document
            var createRes = await docService.CreateDocumentAsync(docDto, "user-admin");

            Assert.True(createRes.Success, createRes.Message);
            Assert.NotNull(createRes.DocumentId);

            // 1. Verify Document Status is Posted immediately
            var savedDoc = await context.DocumentHeaders
                .Include(d => d.Lines)
                .FirstOrDefaultAsync(d => d.Id == createRes.DocumentId.Value);

            Assert.NotNull(savedDoc);
            Assert.Equal(DocumentStatus.Posted, savedDoc.Status);
            Assert.NotNull(savedDoc.JournalEntryHeaderId);

            // 2. Verify Journal Entry was created and posted immediately
            var jv = await context.JournalEntryHeaders
                .Include(j => j.Lines)
                .FirstOrDefaultAsync(j => j.Id == savedDoc.JournalEntryHeaderId.Value);

            Assert.NotNull(jv);
            Assert.Equal(JournalEntryStatus.Posted, jv.Status);
            Assert.Equal(12000m, jv.TotalDebitLocal);
            Assert.Equal(12000m, jv.TotalCreditLocal);

            // Check that customer line is tagged with SubLedger
            var custLine = jv.Lines.FirstOrDefault(l => l.AccountId == 101);
            Assert.NotNull(custLine);
            Assert.Equal(SubLedgerType.Customer, custLine.SubLedgerType);
            Assert.Equal(1, custLine.SubLedgerId);

            // 3. Verify Serial Number was marked as Issued immediately
            var serialEntity = await context.ItemSerialNumbers.FirstOrDefaultAsync(s => s.SerialNumber == "SN-AUTO-1001");
            Assert.NotNull(serialEntity);
            Assert.Equal(ItemSerialStatus.Issued, serialEntity.Status);
        }
        #endregion

        #region Domain 11: Performance Testing & Benchmarking
        [Fact]
        public async Task Test11_PerformanceBenchmark_MeasuresExecutionSpeed_OnVolume()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName, branchId: 1, isSuperAdmin: true);
            await SeedMasterDataAsync(context);

            var userCtx = CreateUserContext(branchId: 1, hasAllBranches: false);
            var reportService = new AccountingReportService(context, userCtx);

            var sw = new Stopwatch();

            // 1. Generate 100 Customers and 50 Vendors
            var customers = new List<Customer>();
            for (int i = 10; i < 110; i++)
            {
                customers.Add(new Customer
                {
                    Id = i,
                    BranchId = 1,
                    Code = $"CUS-BENCH-{i}",
                    NameAr = $"عميل رقم {i}",
                    NameEn = $"Customer {i}",
                    CustomerCategoryId = 1,
                    FinancialAccountId = 101,
                    CurrencyId = 1,
                    IsActive = true
                });
            }
            context.Customers.AddRange(customers);

            var vendors = new List<Vendor>();
            for (int i = 10; i < 60; i++)
            {
                vendors.Add(new Vendor
                {
                    Id = i,
                    BranchId = 1,
                    Code = $"VEN-BENCH-{i}",
                    NameAr = $"مورد رقم {i}",
                    NameEn = $"Vendor {i}",
                    VendorCategoryId = 1,
                    FinancialAccountId = 102,
                    CurrencyId = 1,
                    IsActive = true
                });
            }
            context.Vendors.AddRange(vendors);

            // 2. Generate 1,000 Journal Entries (distributed across Customers & Accounts)
            var jvs = new List<JournalEntryHeader>();
            for (int i = 1; i <= 1000; i++)
            {
                int targetCustId = 10 + (i % 100);
                var jv = new JournalEntryHeader
                {
                    BranchId = 1,
                    FiscalYearId = 1,
                    EntryNumber = $"JV-LOAD-{i:D5}",
                    EntryDate = new DateTime(2026, 2, (i % 25) + 1),
                    Status = JournalEntryStatus.Posted,
                    Description = $"قيد تجريبي رقم {i}",
                    TotalDebitLocal = 1500m,
                    TotalCreditLocal = 1500m,
                    Lines = new List<JournalEntryLine>
                    {
                        new JournalEntryLine
                        {
                            LineNumber = 1,
                            AccountId = 101,
                            SubLedgerType = SubLedgerType.Customer,
                            SubLedgerId = targetCustId,
                            DebitLocal = 1500m,
                            CreditLocal = 0m,
                            DebitForeign = 1500m,
                            CreditForeign = 0m,
                            CurrencyId = 1,
                            ExchangeRate = 1.0m,
                            Description = "حركة مدينة"
                        },
                        new JournalEntryLine
                        {
                            LineNumber = 2,
                            AccountId = 105,
                            DebitLocal = 0m,
                            CreditLocal = 1500m,
                            DebitForeign = 0m,
                            CreditForeign = 1500m,
                            CurrencyId = 1,
                            ExchangeRate = 1.0m,
                            Description = "إيرادات"
                        }
                    }
                };
                jvs.Add(jv);
            }
            context.JournalEntryHeaders.AddRange(jvs);

            // 3. Generate 500 Receipt Vouchers & 500 Payment Vouchers
            var vHeaders = new List<VoucherHeader>();
            for (int i = 1; i <= 500; i++)
            {
                int targetCustId = 10 + (i % 100);
                vHeaders.Add(new VoucherHeader
                {
                    BranchId = 1,
                    FiscalYearId = 1,
                    VoucherType = VoucherType.Receipt,
                    VoucherNumber = $"RV-LOAD-{i:D5}",
                    VoucherDate = new DateTime(2026, 2, (i % 25) + 1),
                    Status = VoucherStatus.Posted,
                    TotalAmountForeign = 800m,
                    TotalAmountLocal = 800m,
                    CurrencyId = 1,
                    ExchangeRate = 1m
                });

                int targetVendId = 10 + (i % 50);
                vHeaders.Add(new VoucherHeader
                {
                    BranchId = 1,
                    FiscalYearId = 1,
                    VoucherType = VoucherType.Payment,
                    VoucherNumber = $"PV-LOAD-{i:D5}",
                    VoucherDate = new DateTime(2026, 2, (i % 25) + 1),
                    Status = VoucherStatus.Posted,
                    TotalAmountForeign = 600m,
                    TotalAmountLocal = 600m,
                    CurrencyId = 1,
                    ExchangeRate = 1m
                });
            }
            context.VoucherHeaders.AddRange(vHeaders);

            await context.SaveChangesAsync();

            // Benchmark Statement Loading Speed
            sw.Restart();
            var testCustStatement = await reportService.GetCustomerStatementAsync(new ReportFilterDto
            {
                CustomerId = 15,
                BranchId = 1,
                FiscalYearId = 1,
                DateFrom = new DateTime(2026, 2, 1),
                DateTo = new DateTime(2026, 2, 28)
            });
            sw.Stop();
            long statementTimeMs = sw.ElapsedMilliseconds;

            // Benchmark Search Speed
            var userMock = new Mock<ICurrentUserService>();
            userMock.Setup(u => u.BranchId).Returns(1);
            userMock.Setup(u => u.IsSuperAdmin).Returns(true);
            var lookupService = new LookupService(context, userMock.Object);

            sw.Restart();
            var searchResult = await lookupService.SearchAsync(new LookupSearchRequest
            {
                EntityType = "customer",
                Query = "CUS-BENCH-4"
            });
            sw.Stop();
            long searchTimeMs = sw.ElapsedMilliseconds;

            // Benchmark Sequential Numbering (Posting Speed element)
            var codeSeqService = new CodeSequenceService(context);
            sw.Restart();
            for (int i = 0; i < 50; i++)
            {
                await codeSeqService.GetNextCodeAsync("ReceiptVoucher", 1);
            }
            sw.Stop();
            long numberingTimeMs = sw.ElapsedMilliseconds;

            _output.WriteLine($"[PERFORMANCE BENCHMARK RESULTS]");
            _output.WriteLine($"Customer Statement (with 1000 JEs in DB): {statementTimeMs} ms (Target: < 500ms)");
            _output.WriteLine($"Lookup Search (over 100 customers): {searchTimeMs} ms (Target: < 100ms)");
            _output.WriteLine($"50 Sequential Number Generations: {numberingTimeMs} ms (Average: {numberingTimeMs / 50.0:F2} ms/code)");

            Assert.True(statementTimeMs < 1000, $"Statement generation took {statementTimeMs}ms, exceeded 1000ms SLA");
            Assert.True(searchTimeMs < 200, $"Lookup search took {searchTimeMs}ms, exceeded 200ms SLA");
            Assert.NotEmpty(searchResult.Items);
            Assert.NotNull(testCustStatement);
        }
        #endregion

        #region Domain 12: Cash Invoices Option 2 - Subledger Movement & Statement Integrity
        [Fact]
        public async Task Test12_CashInvoicesOption2_SubledgerMovementAndStatementIntegrity()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName, branchId: 1, isSuperAdmin: true);
            await SeedMasterDataAsync(context);

            var auditMock = new Mock<IAuditService>();
            var codeSeqMock = new Mock<ICodeSequenceService>();
            var userCtx = CreateUserContext(branchId: 1);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var serialService = new SerialNumberService(context);
            var ttService = new TransactionTypeService(context, cache, userCtx, auditMock.Object);
            var fiscalPeriodService = new FiscalPeriodService(context, auditMock.Object);
            var docService = new UniversalDocumentService(context, ttService, serialService, codeSeqMock.Object, userCtx, auditMock.Object, fiscalPeriodService);
            var reportService = new AccountingReportService(context, userCtx);

            // ==========================================
            // 1. CASH SALES INVOICE WITH CUSTOMER (Option 2)
            // ==========================================
            var cashSalesDoc = new DocumentHeaderDto
            {
                BranchId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 1, // SI
                DocumentDate = new DateTime(2026, 2, 10),
                PaymentMethod = VoucherPaymentMethod.Cash,
                PartyType = DocumentPartyType.Customer,
                PartyId = 1,
                WarehouseId = 1,
                CashBankAccountId = 103, // Safe / Cash
                Lines = new List<DocumentLineDto>
                {
                    new DocumentLineDto
                    {
                        LineNumber = 1,
                        ItemId = 2, // Standard Item (No serial required)
                        Quantity = 2,
                        UnitPrice = 2500m,
                        WarehouseId = 1,
                        TrackingType = ItemTrackingType.None
                    }
                }
            };

            var salesResult = await docService.CreateDocumentAsync(cashSalesDoc, "user-cash-test");
            Assert.True(salesResult.Success, salesResult.Message);
            Assert.NotNull(salesResult.DocumentId);

            // Verify Journal Entry
            var salesDoc = await context.DocumentHeaders.FindAsync(salesResult.DocumentId.Value);
            Assert.NotNull(salesDoc);
            Assert.NotNull(salesDoc.JournalEntryHeaderId);

            var salesJv = await context.JournalEntryHeaders
                .Include(j => j.Lines)
                .FirstOrDefaultAsync(j => j.Id == salesDoc.JournalEntryHeaderId.Value);

            Assert.NotNull(salesJv);
            Assert.Equal(JournalEntryStatus.Posted, salesJv.Status);
            // Sum of Debits == Sum of Credits
            Assert.Equal(salesJv.TotalDebitLocal, salesJv.TotalCreditLocal);

            // Customer Subledger lines check:
            // 1. Customer Debit (Invoice net amount)
            var custDebit = salesJv.Lines.FirstOrDefault(l => l.SubLedgerType == SubLedgerType.Customer && l.SubLedgerId == 1 && l.DebitLocal > 0);
            Assert.NotNull(custDebit);
            Assert.Equal(5000m, custDebit.DebitLocal);

            // 2. Customer Credit (Immediate Cash Settlement)
            var custCredit = salesJv.Lines.FirstOrDefault(l => l.SubLedgerType == SubLedgerType.Customer && l.SubLedgerId == 1 && l.CreditLocal > 0);
            Assert.NotNull(custCredit);
            Assert.Equal(5000m, custCredit.CreditLocal);

            // 3. Cash Account Debit
            var cashDebit = salesJv.Lines.FirstOrDefault(l => l.AccountId == 103 && l.DebitLocal > 0);
            Assert.NotNull(cashDebit);
            Assert.Equal(5000m, cashDebit.DebitLocal);

            // Verify in Customer Statement
            var custStatement = await reportService.GetCustomerStatementAsync(new ReportFilterDto
            {
                CustomerId = 1,
                BranchId = 1,
                FiscalYearId = 1,
                DateFrom = new DateTime(2026, 2, 1),
                DateTo = new DateTime(2026, 2, 28)
            });

            Assert.NotNull(custStatement);
            // Both movements must be present in statement lines
            Assert.True(custStatement.Lines.Any(l => l.DebitLocal == 5000m), "Customer statement must show Cash Invoice Debit");
            Assert.True(custStatement.Lines.Any(l => l.CreditLocal == 5000m), "Customer statement must show Cash Invoice Credit Settlement");
            // Net closing balance effect is zero
            Assert.Equal(0m, custStatement.ClosingBalanceLocal);

            // ==========================================
            // 2. CASH PURCHASE INVOICE WITH VENDOR (Option 2)
            // ==========================================
            var cashPurchDoc = new DocumentHeaderDto
            {
                BranchId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 2, // PI
                DocumentDate = new DateTime(2026, 2, 12),
                PaymentMethod = VoucherPaymentMethod.Cash,
                PartyType = DocumentPartyType.Vendor,
                PartyId = 1,
                WarehouseId = 1,
                CashBankAccountId = 103, // Safe / Cash
                Lines = new List<DocumentLineDto>
                {
                    new DocumentLineDto
                    {
                        LineNumber = 1,
                        ItemId = 2,
                        Quantity = 1,
                        UnitPrice = 3000m,
                        WarehouseId = 1,
                        TrackingType = ItemTrackingType.None
                    }
                }
            };

            var purchResult = await docService.CreateDocumentAsync(cashPurchDoc, "user-cash-test");
            Assert.True(purchResult.Success, purchResult.Message);
            Assert.NotNull(purchResult.DocumentId);

            // Verify Journal Entry
            var purchDoc = await context.DocumentHeaders.FindAsync(purchResult.DocumentId.Value);
            Assert.NotNull(purchDoc);
            Assert.NotNull(purchDoc.JournalEntryHeaderId);

            var purchJv = await context.JournalEntryHeaders
                .Include(j => j.Lines)
                .FirstOrDefaultAsync(j => j.Id == purchDoc.JournalEntryHeaderId.Value);

            Assert.NotNull(purchJv);
            Assert.Equal(JournalEntryStatus.Posted, purchJv.Status);
            // Sum of Debits == Sum of Credits
            Assert.Equal(purchJv.TotalDebitLocal, purchJv.TotalCreditLocal);

            // Vendor Subledger lines check:
            // 1. Vendor Credit (Invoice net amount)
            var vendCredit = purchJv.Lines.FirstOrDefault(l => l.SubLedgerType == SubLedgerType.Vendor && l.SubLedgerId == 1 && l.CreditLocal > 0);
            Assert.NotNull(vendCredit);
            Assert.Equal(3000m, vendCredit.CreditLocal);

            // 2. Vendor Debit (Immediate Cash Payment)
            var vendDebit = purchJv.Lines.FirstOrDefault(l => l.SubLedgerType == SubLedgerType.Vendor && l.SubLedgerId == 1 && l.DebitLocal > 0);
            Assert.NotNull(vendDebit);
            Assert.Equal(3000m, vendDebit.DebitLocal);

            // 3. Cash Account Credit
            var cashCredit = purchJv.Lines.FirstOrDefault(l => l.AccountId == 103 && l.CreditLocal > 0);
            Assert.NotNull(cashCredit);
            Assert.Equal(3000m, cashCredit.CreditLocal);

            // Verify in Vendor Statement
            var vendStatement = await reportService.GetVendorStatementAsync(new ReportFilterDto
            {
                VendorId = 1,
                BranchId = 1,
                FiscalYearId = 1,
                DateFrom = new DateTime(2026, 2, 1),
                DateTo = new DateTime(2026, 2, 28)
            });

            Assert.NotNull(vendStatement);
            // Both movements must be present in statement lines
            Assert.True(vendStatement.Lines.Any(l => l.CreditLocal == 3000m), "Vendor statement must show Cash Invoice Credit");
            Assert.True(vendStatement.Lines.Any(l => l.DebitLocal == 3000m), "Vendor statement must show Cash Invoice Debit Settlement");
            // Net closing balance effect is zero
            Assert.Equal(0m, vendStatement.ClosingBalanceLocal);
        }
        #endregion
    }
}
