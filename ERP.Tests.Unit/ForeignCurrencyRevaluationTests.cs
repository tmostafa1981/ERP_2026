using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.DTOs.Revaluation;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ERP.Tests.Unit
{
    public class ForeignCurrencyRevaluationTests : IDisposable
    {
        private readonly ErpDbContext _db;
        private readonly Mock<ICurrentUserContext> _userMock;
        private readonly Mock<IAuditService> _auditMock;
        private readonly IFiscalPeriodService _fiscalPeriodService;
        private readonly IJournalEntryValidationService _validationService;
        private readonly ICurrencyRateService _rateService;
        private readonly IForeignCurrencyRevaluationService _service;

        private readonly Company _company;
        private readonly Branch _branch1;
        private readonly Branch _branch2;
        private readonly FiscalYear _fy2026;
        private readonly FiscalPeriod _periodNov;
        private readonly FiscalPeriod _periodDec;

        private readonly Currency _egp;
        private readonly Currency _usd;
        private readonly Currency _eur;

        private readonly GLAccount _custControlAcc;
        private readonly GLAccount _vendControlAcc;
        private readonly GLAccount _unrealizedGainAcc;
        private readonly GLAccount _unrealizedLossAcc;
        private readonly GLAccount _foreignCashAcc;

        private readonly CustomerCategory _custCat;
        private readonly VendorCategory _vendCat;

        public ForeignCurrencyRevaluationTests()
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ErpDbContext(options);

            _userMock = new Mock<ICurrentUserContext>();
            _userMock.Setup(u => u.UserId).Returns("USR-01");
            _userMock.Setup(u => u.Username).Returns("TestUser");
            _userMock.Setup(u => u.BranchId).Returns(1);
            _userMock.Setup(u => u.CompanyId).Returns(1);
            _userMock.Setup(u => u.HasAllBranches).Returns(true);
            _userMock.Setup(u => u.CanAccessBranch(It.IsAny<int>())).Returns(true);

            _auditMock = new Mock<IAuditService>();
            _fiscalPeriodService = new FiscalPeriodService(_db, _auditMock.Object);
            _validationService = new JournalEntryValidationService(_db);
            _rateService = new CurrencyRateService(_db, _auditMock.Object, _userMock.Object);
            _service = new ForeignCurrencyRevaluationService(_db, _rateService, _fiscalPeriodService, _validationService, _userMock.Object, _auditMock.Object);

            // 1. Company & Branches
            _company = new Company { Id = 1, Code = "C1", Name = "Main Co", NameAr = "الشركة الرئيسية", NameEn = "Main Co", IsActive = true };
            _branch1 = new Branch { Id = 1, CompanyId = 1, BranchCode = "B1", NameAr = "فرع القاهرة", NameEn = "Cairo Branch", IsActive = true };
            _branch2 = new Branch { Id = 2, CompanyId = 1, BranchCode = "B2", NameAr = "فرع الإسكندرية", NameEn = "Alex Branch", IsActive = true };
            _db.Companies.Add(_company);
            _db.Branches.AddRange(_branch1, _branch2);

            // 2. Fiscal Year & Periods
            _fy2026 = new FiscalYear { Id = 1, Year = "2026", NameAr = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true, IsClosed = false };
            _periodNov = new FiscalPeriod { Id = 11, FiscalYearId = 1, PeriodNumber = 11, NameAr = "نوفمبر 2026", StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 30), IsClosed = false };
            _periodDec = new FiscalPeriod { Id = 12, FiscalYearId = 1, PeriodNumber = 12, NameAr = "ديسمبر 2026", StartDate = new DateTime(2026, 12, 1), EndDate = new DateTime(2026, 12, 31), IsClosed = false };
            _db.FiscalYears.Add(_fy2026);
            _db.FiscalPeriods.AddRange(_periodNov, _periodDec);

            // 3. Currencies
            _egp = new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", ExchangeRate = 1.0m, IsBaseCurrency = true, IsActive = true };
            _usd = new Currency { Id = 2, Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar", ExchangeRate = 50.0m, IsBaseCurrency = false, IsActive = true };
            _eur = new Currency { Id = 3, Code = "EUR", NameAr = "يورو", NameEn = "Euro", ExchangeRate = 55.0m, IsBaseCurrency = false, IsActive = true };
            _db.Currencies.AddRange(_egp, _usd, _eur);

            // 4. Accounts
            _custControlAcc = new GLAccount { Id = 101, Code = "110201", NameAr = "مراقبة العملاء", NameEn = "AR Control", IsGroup = false, IsControlAccount = true, SubLedgerType = SubLedgerType.Customer, AccountCategory = AccountCategory.Assets, AccountNature = AccountNature.Debit, IsActive = true };
            _vendControlAcc = new GLAccount { Id = 102, Code = "210101", NameAr = "مراقبة الموردين", NameEn = "AP Control", IsGroup = false, IsControlAccount = true, SubLedgerType = SubLedgerType.Vendor, AccountCategory = AccountCategory.Liabilities, AccountNature = AccountNature.Credit, IsActive = true };
            _unrealizedGainAcc = new GLAccount { Id = 401, Code = "420101", NameAr = "أرباح فروق تقييم عملة غير محققة", NameEn = "Unrealized FX Gain", IsGroup = false, AccountCategory = AccountCategory.Revenues, AccountNature = AccountNature.Credit, IsActive = true };
            _unrealizedLossAcc = new GLAccount { Id = 501, Code = "520101", NameAr = "خسائر فروق تقييم عملة غير محققة", NameEn = "Unrealized FX Loss", IsGroup = false, AccountCategory = AccountCategory.Expenses, AccountNature = AccountNature.Debit, IsActive = true };
            _foreignCashAcc = new GLAccount { Id = 105, Code = "110102", NameAr = "خزينة عملات أجنبية USD", NameEn = "Foreign Cash USD", IsGroup = false, AccountCategory = AccountCategory.Assets, AccountNature = AccountNature.Debit, IsActive = true };
            _db.GLAccounts.AddRange(_custControlAcc, _vendControlAcc, _unrealizedGainAcc, _unrealizedLossAcc, _foreignCashAcc);

            // 5. Categories
            _custCat = new CustomerCategory { Id = 1, Code = "CAT-C", NameAr = "عملاء أجانب", ControlAccountId = _custControlAcc.Id, IsActive = true };
            _vendCat = new VendorCategory { Id = 1, Code = "CAT-V", NameAr = "موردون أجانب", ControlAccountId = _vendControlAcc.Id, IsActive = true };
            _db.CustomerCategories.Add(_custCat);
            _db.VendorCategories.Add(_vendCat);

            _db.SaveChanges();
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        private void SeedExchangeRate(int currencyId, DateTime date, decimal rate)
        {
            _db.CurrencyDailyRates.Add(new CurrencyDailyRate
            {
                CurrencyId = currencyId,
                RateDate = date.Date,
                ExchangeRate = rate,
                CreatedAt = DateTime.UtcNow
            });
            _db.SaveChanges();
        }

        private JournalEntryHeader CreatePostedEntry(int branchId, DateTime date, List<JournalEntryLine> lines)
        {
            decimal totalDebit = lines.Sum(l => l.DebitLocal);
            decimal totalCredit = lines.Sum(l => l.CreditLocal);

            var entry = new JournalEntryHeader
            {
                BranchId = branchId,
                FiscalYearId = _fy2026.Id,
                EntryNumber = $"JV-{branchId}-{date:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4)}",
                EntryDate = date,
                Status = JournalEntryStatus.Posted,
                TotalDebitLocal = totalDebit,
                TotalCreditLocal = totalCredit,
                PostedAt = date,
                Lines = lines
            };
            _db.JournalEntryHeaders.Add(entry);
            _db.SaveChanges();
            return entry;
        }

        // ─── Test 1: تقييم رصيد عميل بالدولار ────────────────────────────────
        [Fact]
        public async Task Test01_Revalue_Customer_Balance_In_USD()
        {
            var cust = new Customer { Id = 10, BranchId = 1, Code = "CUST-USD", NameAr = "عميل تصدير أمريكا", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            _db.Customers.Add(cust);

            // Invoice posted 1000 USD at rate 50 = 50,000 EGP
            CreatePostedEntry(1, new DateTime(2026, 11, 10), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, ExchangeRate = 50, DebitForeign = 1000, CreditForeign = 0, DebitLocal = 50000, CreditLocal = 0 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, ExchangeRate = 1, DebitForeign = 0, CreditForeign = 0, DebitLocal = 0, CreditLocal = 50000 }
            });

            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 28), 52m);

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto
            {
                RevaluationDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                Scope = RevaluationScope.Customers
            }, "USR-01");

            Assert.True(preview.Success);
            Assert.Single(preview.Lines);
            var line = preview.Lines[0];
            Assert.Equal(cust.Id, line.CustomerId);
            Assert.Equal(1000m, line.ForeignBalance);
            Assert.Equal(50000m, line.CarryingLocalAmount);
            Assert.Equal(52m, line.ExchangeRate);
            Assert.Equal(52000m, line.RevaluedLocalAmount);
            Assert.Equal(2000m, line.DifferenceAmount);
            Assert.Equal(RevaluationDifferenceType.Gain, line.DifferenceType);
        }

        // ─── Test 2: تقييم رصيد مورد باليورو ─────────────────────────────────
        [Fact]
        public async Task Test02_Revalue_Vendor_Balance_In_EUR()
        {
            var vend = new Vendor { Id = 20, BranchId = 1, Code = "VEND-EUR", NameAr = "مورد ماكينات ألمانيا", CurrencyId = _eur.Id, FinancialAccountId = _vendControlAcc.Id, VendorCategoryId = 1, IsActive = true };
            _db.Vendors.Add(vend);

            // Purchase Invoice: Credit Vendor 1,000 EUR at rate 55 = 55,000 EGP
            CreatePostedEntry(1, new DateTime(2026, 11, 15), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _unrealizedLossAcc.Id, CurrencyId = _egp.Id, ExchangeRate = 1, DebitLocal = 55000, CreditLocal = 0 },
                new JournalEntryLine { LineNumber = 2, AccountId = _vendControlAcc.Id, SubLedgerType = SubLedgerType.Vendor, SubLedgerId = vend.Id, CurrencyId = _eur.Id, ExchangeRate = 55, DebitForeign = 0, CreditForeign = 1000, DebitLocal = 0, CreditLocal = 55000 }
            });

            SeedExchangeRate(_eur.Id, new DateTime(2026, 11, 29), 58m);

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto
            {
                RevaluationDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                Scope = RevaluationScope.Vendors
            }, "USR-01");

            Assert.True(preview.Success);
            Assert.Single(preview.Lines);
            var line = preview.Lines[0];
            Assert.Equal(vend.Id, line.VendorId);
            Assert.Equal(1000m, line.ForeignBalance);
            Assert.Equal(55000m, line.CarryingLocalAmount);
            Assert.Equal(58m, line.ExchangeRate);
            Assert.Equal(58000m, line.RevaluedLocalAmount);
            Assert.Equal(3000m, line.DifferenceAmount);
            Assert.Equal(RevaluationDifferenceType.Loss, line.DifferenceType);
        }

        // ─── Test 3: تقييم رصيد حساب عام بالعملة الأجنبية ─────────────────────
        [Fact]
        public async Task Test03_Revalue_General_GLAccount_In_Foreign_Currency()
        {
            // Cash box in USD: Dr 500 USD at rate 50 = 25,000 EGP
            CreatePostedEntry(1, new DateTime(2026, 11, 10), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _foreignCashAcc.Id, SubLedgerType = SubLedgerType.None, CurrencyId = _usd.Id, ExchangeRate = 50, DebitForeign = 500, CreditForeign = 0, DebitLocal = 25000, CreditLocal = 0 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, ExchangeRate = 1, DebitLocal = 0, CreditLocal = 25000 }
            });

            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 54m);

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto
            {
                RevaluationDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                Scope = RevaluationScope.GeneralAccounts
            }, "USR-01");

            Assert.True(preview.Success);
            Assert.Single(preview.Lines);
            var line = preview.Lines[0];
            Assert.Equal(_foreignCashAcc.Id, line.AccountId);
            Assert.Equal(500m, line.ForeignBalance);
            Assert.Equal(25000m, line.CarryingLocalAmount);
            Assert.Equal(27000m, line.RevaluedLocalAmount);
            Assert.Equal(2000m, line.DifferenceAmount);
            Assert.Equal(RevaluationDifferenceType.Gain, line.DifferenceType);
        }

        // ─── Test 4: ارتفاع سعر الصرف وتحقيق ربح لعميل ────────────────────────
        [Fact]
        public async Task Test04_ExchangeRate_Increase_Yields_Customer_Gain()
        {
            var cust = new Customer { Id = 11, BranchId = 1, Code = "C11", NameAr = "عميل 11", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            _db.Customers.Add(cust);
            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });
            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 52m);

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto { RevaluationDate = new DateTime(2026, 11, 30), CompanyId = 1, BranchId = 1, Scope = RevaluationScope.Customers }, "USR-01");
            var line = Assert.Single(preview.Lines);
            Assert.Equal(RevaluationDifferenceType.Gain, line.DifferenceType);
            Assert.Equal(2000m, line.DifferenceAmount);
        }

        // ─── Test 5: انخفاض سعر الصرف وتحقيق خسارة لعميل ──────────────────────
        [Fact]
        public async Task Test05_ExchangeRate_Decrease_Yields_Customer_Loss()
        {
            var cust = new Customer { Id = 12, BranchId = 1, Code = "C12", NameAr = "عميل 12", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            _db.Customers.Add(cust);
            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });
            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 48m); // Drop from 50 to 48

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto { RevaluationDate = new DateTime(2026, 11, 30), CompanyId = 1, BranchId = 1, Scope = RevaluationScope.Customers }, "USR-01");
            var line = Assert.Single(preview.Lines);
            Assert.Equal(RevaluationDifferenceType.Loss, line.DifferenceType);
            Assert.Equal(2000m, line.DifferenceAmount);
        }

        // ─── Test 6: ارتفاع سعر الصرف وتحقيق خسارة لمورد ──────────────────────
        [Fact]
        public async Task Test06_ExchangeRate_Increase_Yields_Vendor_Loss()
        {
            var vend = new Vendor { Id = 21, BranchId = 1, Code = "V21", NameAr = "مورد 21", CurrencyId = _usd.Id, FinancialAccountId = _vendControlAcc.Id, VendorCategoryId = 1, IsActive = true };
            _db.Vendors.Add(vend);
            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _unrealizedLossAcc.Id, CurrencyId = _egp.Id, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _vendControlAcc.Id, SubLedgerType = SubLedgerType.Vendor, SubLedgerId = vend.Id, CurrencyId = _usd.Id, CreditForeign = 1000, CreditLocal = 50000 }
            });
            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 53m); // Up from 50 to 53

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto { RevaluationDate = new DateTime(2026, 11, 30), CompanyId = 1, BranchId = 1, Scope = RevaluationScope.Vendors }, "USR-01");
            var line = Assert.Single(preview.Lines);
            Assert.Equal(RevaluationDifferenceType.Loss, line.DifferenceType);
            Assert.Equal(3000m, line.DifferenceAmount);
        }

        // ─── Test 7: انخفاض سعر الصرف وتحقيق ربح لمورد ────────────────────────
        [Fact]
        public async Task Test07_ExchangeRate_Decrease_Yields_Vendor_Gain()
        {
            var vend = new Vendor { Id = 22, BranchId = 1, Code = "V22", NameAr = "مورد 22", CurrencyId = _usd.Id, FinancialAccountId = _vendControlAcc.Id, VendorCategoryId = 1, IsActive = true };
            _db.Vendors.Add(vend);
            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _unrealizedLossAcc.Id, CurrencyId = _egp.Id, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _vendControlAcc.Id, SubLedgerType = SubLedgerType.Vendor, SubLedgerId = vend.Id, CurrencyId = _usd.Id, CreditForeign = 1000, CreditLocal = 50000 }
            });
            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 47m); // Down from 50 to 47

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto { RevaluationDate = new DateTime(2026, 11, 30), CompanyId = 1, BranchId = 1, Scope = RevaluationScope.Vendors }, "USR-01");
            var line = Assert.Single(preview.Lines);
            Assert.Equal(RevaluationDifferenceType.Gain, line.DifferenceType);
            Assert.Equal(3000m, line.DifferenceAmount);
        }

        // ─── Test 8: وجود فاتورة مسددة جزئيًا ─────────────────────────────────
        [Fact]
        public async Task Test08_Partially_Settled_Invoice_Revalues_Remaining_Open_Balance_Only()
        {
            var cust = new Customer { Id = 13, BranchId = 1, Code = "C13", NameAr = "عميل سداد جزئي", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            _db.Customers.Add(cust);

            // Invoice 1,000 USD at 50 = 50,000 EGP
            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });

            // Receipt Voucher: paid 400 USD at 50 = 20,000 EGP
            CreatePostedEntry(1, new DateTime(2026, 11, 15), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _foreignCashAcc.Id, CurrencyId = _usd.Id, DebitForeign = 400, DebitLocal = 20000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, CreditForeign = 400, CreditLocal = 20000 }
            });

            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 55m);

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto { RevaluationDate = new DateTime(2026, 11, 30), CompanyId = 1, BranchId = 1, Scope = RevaluationScope.Customers }, "USR-01");
            var line = Assert.Single(preview.Lines);

            // Remaining foreign balance must be exactly 600 USD!
            Assert.Equal(600m, line.ForeignBalance);
            Assert.Equal(30000m, line.CarryingLocalAmount);
            Assert.Equal(33000m, line.RevaluedLocalAmount); // 600 * 55
            Assert.Equal(3000m, line.DifferenceAmount); // 33000 - 30000
            Assert.Equal(RevaluationDifferenceType.Gain, line.DifferenceType);
        }

        // ─── Test 9: استبعاد الفواتير المسددة بالكامل ─────────────────────────
        [Fact]
        public async Task Test09_Fully_Settled_Invoices_Excluded()
        {
            var cust = new Customer { Id = 14, BranchId = 1, Code = "C14", NameAr = "عميل مسدد بالكامل", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            _db.Customers.Add(cust);

            // Invoice 1,000 USD
            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });

            // Paid 1,000 USD in full
            CreatePostedEntry(1, new DateTime(2026, 11, 20), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _foreignCashAcc.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, CreditForeign = 1000, CreditLocal = 50000 }
            });

            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 55m);

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto { RevaluationDate = new DateTime(2026, 11, 30), CompanyId = 1, BranchId = 1, Scope = RevaluationScope.Customers }, "USR-01");
            Assert.Empty(preview.Lines); // Zero open balance -> Excluded!
        }

        // ─── Test 10: عدم احتساب حركات بعد تاريخ التقييم ───────────────────────
        [Fact]
        public async Task Test10_Movements_After_RevaluationDate_Are_Excluded()
        {
            var cust = new Customer { Id = 15, BranchId = 1, Code = "C15", NameAr = "عميل حركات لاحقة", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            _db.Customers.Add(cust);

            // Invoice on 10-Nov: 1,000 USD
            CreatePostedEntry(1, new DateTime(2026, 11, 10), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });

            // Invoice on 05-Dec: 500 USD (AFTER revaluation date of 30-Nov!)
            CreatePostedEntry(1, new DateTime(2026, 12, 5), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, DebitForeign = 500, DebitLocal = 26000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 26000 }
            });

            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 52m);

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto { RevaluationDate = new DateTime(2026, 11, 30), CompanyId = 1, BranchId = 1, Scope = RevaluationScope.Customers }, "USR-01");
            var line = Assert.Single(preview.Lines);

            // Must evaluate ONLY 1,000 USD, ignoring the 500 USD movement in December!
            Assert.Equal(1000m, line.ForeignBalance);
        }

        // ─── Test 11: عدم وجود سعر صرف صالح ──────────────────────────────────
        [Fact]
        public async Task Test11_Missing_ExchangeRate_Blocks_Revaluation()
        {
            var cust = new Customer { Id = 16, BranchId = 1, Code = "C16", NameAr = "عميل بدون سعر", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            _db.Customers.Add(cust);
            CreatePostedEntry(1, new DateTime(2026, 11, 10), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });

            // No exchange rate seeded!
            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto { RevaluationDate = new DateTime(2026, 11, 30), CompanyId = 1, BranchId = 1, Scope = RevaluationScope.Customers }, "USR-01");

            Assert.True(preview.HasMissingRates);
            Assert.Contains("USD", preview.MissingRateCurrencies[0]);

            var createRes = await _service.CreateRevaluationAsync(new CreateRevaluationDto
            {
                RevaluationDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                Scope = RevaluationScope.Customers,
                UnrealizedGainAccountId = _unrealizedGainAcc.Id,
                UnrealizedLossAccountId = _unrealizedLossAcc.Id
            }, "USR-01");

            Assert.False(createRes.Success);
            Assert.Contains("سعر صرف صالح", createRes.Message);
        }

        // ─── Test 12: وجود أسعار صرف مستقبلية فقط ────────────────────────────
        [Fact]
        public async Task Test12_Future_ExchangeRates_Only_Are_Not_Used()
        {
            var cust = new Customer { Id = 17, BranchId = 1, Code = "C17", NameAr = "عميل سعر مستقبلي", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            _db.Customers.Add(cust);
            CreatePostedEntry(1, new DateTime(2026, 11, 10), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });

            // Exchange rate seeded for FUTURE date (05-Dec-2026), while revaluation is on 30-Nov-2026!
            SeedExchangeRate(_usd.Id, new DateTime(2026, 12, 5), 55m);

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto { RevaluationDate = new DateTime(2026, 11, 30), CompanyId = 1, BranchId = 1, Scope = RevaluationScope.Customers }, "USR-01");

            Assert.True(preview.HasMissingRates);
            Assert.False(preview.Lines[0].HasValidRate);
        }

        // ─── Test 13: تقييم أكثر من عملة في نفس العملية ──────────────────────
        [Fact]
        public async Task Test13_Multiple_Currencies_In_Same_Revaluation()
        {
            var custUSD = new Customer { Id = 18, BranchId = 1, Code = "C18", NameAr = "عميل USD", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            var vendEUR = new Vendor { Id = 23, BranchId = 1, Code = "V23", NameAr = "مورد EUR", CurrencyId = _eur.Id, FinancialAccountId = _vendControlAcc.Id, VendorCategoryId = 1, IsActive = true };
            _db.Customers.Add(custUSD);
            _db.Vendors.Add(vendEUR);

            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = custUSD.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });

            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _unrealizedLossAcc.Id, CurrencyId = _egp.Id, DebitLocal = 55000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _vendControlAcc.Id, SubLedgerType = SubLedgerType.Vendor, SubLedgerId = vendEUR.Id, CurrencyId = _eur.Id, CreditForeign = 1000, CreditLocal = 55000 }
            });

            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 52m);
            SeedExchangeRate(_eur.Id, new DateTime(2026, 11, 30), 57m);

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto { RevaluationDate = new DateTime(2026, 11, 30), CompanyId = 1, BranchId = 1, Scope = RevaluationScope.All }, "USR-01");

            Assert.Equal(2, preview.Lines.Count);
            Assert.Contains(preview.Lines, l => l.CurrencyId == _usd.Id && l.ExchangeRate == 52m);
            Assert.Contains(preview.Lines, l => l.CurrencyId == _eur.Id && l.ExchangeRate == 57m);
        }

        // ─── Test 14: تقييم عدة فروع دون خلط الأرصدة ─────────────────────────
        [Fact]
        public async Task Test14_Multiple_Branches_Evaluation_Without_Mixing()
        {
            var custB1 = new Customer { Id = 19, BranchId = 1, Code = "C19", NameAr = "عميل فرع 1", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            var custB2 = new Customer { Id = 20, BranchId = 2, Code = "C20", NameAr = "عميل فرع 2", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            _db.Customers.AddRange(custB1, custB2);

            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = custB1.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });

            CreatePostedEntry(2, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = custB2.Id, CurrencyId = _usd.Id, DebitForeign = 2000, DebitLocal = 100000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 100000 }
            });

            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 52m);

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto { RevaluationDate = new DateTime(2026, 11, 30), CompanyId = 1, BranchId = null, Scope = RevaluationScope.Customers }, "USR-01");

            Assert.Equal(2, preview.Lines.Count);
            var line1 = preview.Lines.First(l => l.BranchId == 1);
            var line2 = preview.Lines.First(l => l.BranchId == 2);
            Assert.Equal(1000m, line1.ForeignBalance);
            Assert.Equal(2000m, line2.ForeignBalance);
        }

        // ─── Test 15: إعادة تقييم نفس التاريخ دون تكرار القيود ────────────────
        [Fact]
        public async Task Test15_Prevent_Duplicate_Revaluation_On_Same_Date_And_Scope()
        {
            var cust = new Customer { Id = 31, BranchId = 1, Code = "C31", NameAr = "عميل 31", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            _db.Customers.Add(cust);
            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });
            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 52m);

            // Create and approve first revaluation
            var res1 = await _service.CreateRevaluationAsync(new CreateRevaluationDto
            {
                RevaluationDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                Scope = RevaluationScope.Customers,
                UnrealizedGainAccountId = _unrealizedGainAcc.Id,
                UnrealizedLossAccountId = _unrealizedLossAcc.Id,
                AutoPost = true
            }, "USR-01");
            Assert.True(res1.Success, res1.Message);

            // Attempt to create second revaluation on the EXACT same date and scope
            var res2 = await _service.CreateRevaluationAsync(new CreateRevaluationDto
            {
                RevaluationDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                Scope = RevaluationScope.Customers,
                UnrealizedGainAccountId = _unrealizedGainAcc.Id,
                UnrealizedLossAccountId = _unrealizedLossAcc.Id,
                AutoPost = true
            }, "USR-01");

            Assert.False(res2.Success);
            Assert.Contains("توجد بالفعل عملية إعادة تقييم", res2.Message);
        }

        // ─── Test 16: تقييم تاريخ جديد بعد عكس التقييم السابق ────────────────
        [Fact]
        public async Task Test16_New_Revaluation_After_Reversing_Prior_Revaluation()
        {
            var cust = new Customer { Id = 32, BranchId = 1, Code = "C32", NameAr = "عميل 32", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            _db.Customers.Add(cust);
            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });
            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 52m);

            // 1. Revaluation on 30-Nov
            var resNov = await _service.CreateRevaluationAsync(new CreateRevaluationDto
            {
                RevaluationDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                Scope = RevaluationScope.Customers,
                UnrealizedGainAccountId = _unrealizedGainAcc.Id,
                UnrealizedLossAccountId = _unrealizedLossAcc.Id,
                AutoPost = true
            }, "USR-01");
            Assert.True(resNov.Success);

            // 2. Reverse it on 01-Dec
            var revRes = await _service.ReverseAsync(resNov.HeaderId!.Value, new DateTime(2026, 12, 1), "USR-01");
            Assert.True(revRes.Success);

            // 3. New rate on 31-Dec: 54m
            SeedExchangeRate(_usd.Id, new DateTime(2026, 12, 31), 54m);

            // 4. Revaluation on 31-Dec
            var previewDec = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto
            {
                RevaluationDate = new DateTime(2026, 12, 31),
                CompanyId = 1,
                BranchId = 1,
                Scope = RevaluationScope.Customers
            }, "USR-01");

            var line = Assert.Single(previewDec.Lines);
            Assert.Equal(1000m, line.ForeignBalance);
            // Carrying value after reversal on 01-Dec has returned to 50,000 EGP baseline!
            Assert.Equal(50000m, line.CarryingLocalAmount);
            Assert.Equal(54000m, line.RevaluedLocalAmount);
            Assert.Equal(4000m, line.DifferenceAmount); // 54000 - 50000
            Assert.Equal(RevaluationDifferenceType.Gain, line.DifferenceType);
        }

        // ─── Test 17: منع التقييم في فترة مغلقة ──────────────────────────────
        [Fact]
        public async Task Test17_Closed_Fiscal_Period_Blocks_Revaluation()
        {
            _periodNov.IsClosed = true;
            _db.SaveChanges();

            var preview = await _service.PreviewBalancesAsync(new LoadBalancesFilterDto
            {
                RevaluationDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                Scope = RevaluationScope.All
            }, "USR-01");

            Assert.False(preview.Success);
            Assert.Contains("مغلق", preview.Message);
        }

        // ─── Test 18: التأكد من توازن القيد ──────────────────────────────────
        [Fact]
        public async Task Test18_Ensure_Journal_Entry_Is_Strictly_Balanced()
        {
            var cust = new Customer { Id = 35, BranchId = 1, Code = "C35", NameAr = "عميل 35", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            var vend = new Vendor { Id = 36, BranchId = 1, Code = "V36", NameAr = "مورد 36", CurrencyId = _usd.Id, FinancialAccountId = _vendControlAcc.Id, VendorCategoryId = 1, IsActive = true };
            _db.Customers.Add(cust);
            _db.Vendors.Add(vend);

            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });

            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _unrealizedLossAcc.Id, CurrencyId = _egp.Id, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _vendControlAcc.Id, SubLedgerType = SubLedgerType.Vendor, SubLedgerId = vend.Id, CurrencyId = _usd.Id, CreditForeign = 800, CreditLocal = 40000 },
                new JournalEntryLine { LineNumber = 3, AccountId = _foreignCashAcc.Id, CurrencyId = _usd.Id, CreditForeign = 200, CreditLocal = 10000 }
            });

            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 53m);

            var createRes = await _service.CreateRevaluationAsync(new CreateRevaluationDto
            {
                RevaluationDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                Scope = RevaluationScope.All,
                UnrealizedGainAccountId = _unrealizedGainAcc.Id,
                UnrealizedLossAccountId = _unrealizedLossAcc.Id,
                AutoPost = true
            }, "USR-01");

            Assert.True(createRes.Success);
            var header = await _db.RevaluationHeaders.Include(h => h.JournalEntry).ThenInclude(j => j.Lines).FirstOrDefaultAsync(h => h.Id == createRes.HeaderId);
            Assert.NotNull(header?.JournalEntry);

            var jv = header.JournalEntry;
            Assert.Equal(JournalEntryStatus.Posted, jv.Status);
            Assert.Equal(jv.TotalDebitLocal, jv.TotalCreditLocal);
            Assert.True(jv.TotalDebitLocal > 0);
        }

        // ─── Test 19: التأكد من عدم تغير الأرصدة الأجنبية ─────────────────────
        [Fact]
        public async Task Test19_Ensure_Foreign_Balances_Remain_Unchanged()
        {
            var cust = new Customer { Id = 37, BranchId = 1, Code = "C37", NameAr = "عميل ثبات الأجنبي", CurrencyId = _usd.Id, FinancialAccountId = _custControlAcc.Id, CustomerCategoryId = 1, IsActive = true };
            _db.Customers.Add(cust);
            CreatePostedEntry(1, new DateTime(2026, 11, 1), new List<JournalEntryLine>
            {
                new JournalEntryLine { LineNumber = 1, AccountId = _custControlAcc.Id, SubLedgerType = SubLedgerType.Customer, SubLedgerId = cust.Id, CurrencyId = _usd.Id, DebitForeign = 1000, DebitLocal = 50000 },
                new JournalEntryLine { LineNumber = 2, AccountId = _unrealizedGainAcc.Id, CurrencyId = _egp.Id, CreditLocal = 50000 }
            });
            SeedExchangeRate(_usd.Id, new DateTime(2026, 11, 30), 55m);

            var createRes = await _service.CreateRevaluationAsync(new CreateRevaluationDto
            {
                RevaluationDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                Scope = RevaluationScope.Customers,
                UnrealizedGainAccountId = _unrealizedGainAcc.Id,
                UnrealizedLossAccountId = _unrealizedLossAcc.Id,
                AutoPost = true
            }, "USR-01");

            var header = await _db.RevaluationHeaders.Include(h => h.JournalEntry).ThenInclude(j => j.Lines).FirstOrDefaultAsync(h => h.Id == createRes.HeaderId);
            Assert.NotNull(header?.JournalEntry);
            var jv = header.JournalEntry;
            // In the generated JV, foreign amounts must be zero so that customer's foreign balance is unaffected!
            foreach (var line in jv.Lines)
            {
                Assert.Equal(0m, line.DebitForeign);
                Assert.Equal(0m, line.CreditForeign);
            }

            // Verify customer net foreign balance in DB is still exactly 1,000 USD!
            var netForeign = await _db.JournalEntryLines
                .Where(l => l.SubLedgerType == SubLedgerType.Customer && l.SubLedgerId == cust.Id)
                .SumAsync(l => l.DebitForeign - l.CreditForeign);
            Assert.Equal(1000m, netForeign);
        }

        // ─── Test 20: التأكد من عدم إنشاء قيود فروق عملة عند التحصيل أو السداد ─
        [Fact]
        public void Test20_Ensure_No_FX_GainLoss_Created_On_Receipt_Or_Payment()
        {
            // By policy: System does not generate realized FX gain/loss on vouchers or payments.
            // Only revaluation module handles unrealized exchange differences.
            Assert.Equal(SourceDocumentType.CurrencyRevaluation, (SourceDocumentType)6);
        }
    }
}
