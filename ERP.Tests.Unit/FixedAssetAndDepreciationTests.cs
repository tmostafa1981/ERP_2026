using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs.FixedAssets;
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
    public class FixedAssetAndDepreciationTests : IDisposable
    {
        private readonly ErpDbContext _db;
        private readonly Mock<ICurrentUserContext> _userMock;
        private readonly Mock<IAuditService> _auditMock;
        private readonly IFiscalPeriodService _fiscalPeriodService;
        private readonly IJournalEntryValidationService _validationService;
        private readonly IFixedAssetCategoryService _categoryService;
        private readonly IFixedAssetService _assetService;
        private readonly IAssetDepreciationService _depreciationService;

        private readonly Company _company;
        private readonly Branch _branch1;
        private readonly Branch _branch2;
        private readonly FiscalYear _fy2026;
        private readonly FiscalPeriod _periodNov;
        private readonly FiscalPeriod _periodDec;
        private readonly Currency _egp;
        private readonly Currency _usd;

        private readonly GLAccount _costAcc;
        private readonly GLAccount _accDepAcc;
        private readonly GLAccount _depExpAcc;
        private readonly GLAccount _disposalGainAcc;
        private readonly GLAccount _disposalLossAcc;
        private readonly GLAccount _cipAcc;
        private readonly GLAccount _cashAcc;

        private readonly CostCenter _costCenter1;
        private AssetCategory _category;

        public FixedAssetAndDepreciationTests()
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ErpDbContext(options);

            _userMock = new Mock<ICurrentUserContext>();
            _userMock.Setup(u => u.UserId).Returns("USR-01");
            _userMock.Setup(u => u.Username).Returns("AssetAccountant");
            _userMock.Setup(u => u.BranchId).Returns(1);
            _userMock.Setup(u => u.CompanyId).Returns(1);
            _userMock.Setup(u => u.HasAllBranches).Returns(true);
            _userMock.Setup(u => u.CanAccessBranch(It.IsAny<int>())).Returns(true);

            _auditMock = new Mock<IAuditService>();
            _fiscalPeriodService = new FiscalPeriodService(_db, _auditMock.Object);
            _validationService = new JournalEntryValidationService(_db);

            _categoryService = new FixedAssetCategoryService(_db, _auditMock.Object);
            _assetService = new FixedAssetService(_db, _fiscalPeriodService, _validationService, _auditMock.Object, _userMock.Object);
            _depreciationService = new AssetDepreciationService(_db, _fiscalPeriodService, _validationService, _auditMock.Object, _userMock.Object);

            // 1. Company & Branches
            _company = new Company { Id = 1, Code = "C1", Name = "Main Co", NameAr = "الشركة الرئيسية", NameEn = "Main Co", IsActive = true };
            _branch1 = new Branch { Id = 1, CompanyId = 1, BranchCode = "B1", NameAr = "فرع القاهرة", NameEn = "Cairo Branch", IsActive = true };
            _branch2 = new Branch { Id = 2, CompanyId = 1, BranchCode = "B2", NameAr = "فرع الإسكندرية", NameEn = "Alex Branch", IsActive = true };
            _db.Companies.Add(_company);
            _db.Branches.AddRange(_branch1, _branch2);

            // 2. Fiscal Year & Periods (Nov & Dec 2026)
            _fy2026 = new FiscalYear { Id = 1, Year = "2026", NameAr = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true, IsClosed = false };
            _periodNov = new FiscalPeriod { Id = 11, FiscalYearId = 1, PeriodNumber = 11, NameAr = "نوفمبر 2026", StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 30), IsClosed = false };
            _periodDec = new FiscalPeriod { Id = 12, FiscalYearId = 1, PeriodNumber = 12, NameAr = "ديسمبر 2026", StartDate = new DateTime(2026, 12, 1), EndDate = new DateTime(2026, 12, 31), IsClosed = false };
            _db.FiscalYears.Add(_fy2026);
            _db.FiscalPeriods.AddRange(_periodNov, _periodDec);

            // 3. Currencies
            _egp = new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", ExchangeRate = 1.0m, IsBaseCurrency = true, IsActive = true };
            _usd = new Currency { Id = 2, Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar", ExchangeRate = 50.0m, IsBaseCurrency = false, IsActive = true };
            _db.Currencies.AddRange(_egp, _usd);

            // 4. Accounts
            _costAcc = new GLAccount { Id = 101, Code = "120101", NameAr = "الآلات والمعدات", NameEn = "Machinery & Equipment", IsGroup = false, AccountCategory = AccountCategory.Assets, AccountNature = AccountNature.Debit, IsActive = true };
            _accDepAcc = new GLAccount { Id = 102, Code = "120102", NameAr = "مجمع إهلاك الآلات والمعدات", NameEn = "Acc Dep - Machinery", IsGroup = false, AccountCategory = AccountCategory.Liabilities, AccountNature = AccountNature.Credit, IsActive = true };
            _depExpAcc = new GLAccount { Id = 501, Code = "520201", NameAr = "مصروف إهلاك الآلات والمعدات", NameEn = "Dep Expense - Machinery", IsGroup = false, AccountCategory = AccountCategory.Expenses, AccountNature = AccountNature.Debit, IsActive = true };
            _disposalGainAcc = new GLAccount { Id = 401, Code = "420201", NameAr = "أرباح استبعاد الأصول", NameEn = "Gain on Asset Disposal", IsGroup = false, AccountCategory = AccountCategory.Revenues, AccountNature = AccountNature.Credit, IsActive = true };
            _disposalLossAcc = new GLAccount { Id = 502, Code = "520202", NameAr = "خسائر استبعاد الأصول", NameEn = "Loss on Asset Disposal", IsGroup = false, AccountCategory = AccountCategory.Expenses, AccountNature = AccountNature.Debit, IsActive = true };
            _cipAcc = new GLAccount { Id = 103, Code = "120201", NameAr = "مشروعات تحت التنفيذ", NameEn = "Construction in Progress", IsGroup = false, AccountCategory = AccountCategory.Assets, AccountNature = AccountNature.Debit, IsActive = true };
            _cashAcc = new GLAccount { Id = 104, Code = "110101", NameAr = "الصندوق الرئيسي / البنك", NameEn = "Cash & Bank", IsGroup = false, AccountCategory = AccountCategory.Assets, AccountNature = AccountNature.Debit, IsActive = true };
            _db.GLAccounts.AddRange(_costAcc, _accDepAcc, _depExpAcc, _disposalGainAcc, _disposalLossAcc, _cipAcc, _cashAcc);

            // 5. Cost Center
            _costCenter1 = new CostCenter { Id = 1, Code = "CC-PROD", NameAr = "مركز تكلفة الإنتاج", NameEn = "Production Cost Center", IsActive = true };
            _db.CostCenters.Add(_costCenter1);

            // 6. Asset Category
            _category = new AssetCategory
            {
                Id = 1,
                Code = "CAT-MACH",
                NameAr = "آلات وخطوط إنتاج",
                NameEn = "Machinery",
                DefaultUsefulLifeMonths = 60,
                DefaultDepreciationMethod = DepreciationMethod.StraightLine,
                DefaultResidualValueRate = 0m,
                AssetCostAccountId = _costAcc.Id,
                AccumulatedDepreciationAccountId = _accDepAcc.Id,
                DepreciationExpenseAccountId = _depExpAcc.Id,
                DisposalGainAccountId = _disposalGainAcc.Id,
                DisposalLossAccountId = _disposalLossAcc.Id,
                CipAccountId = _cipAcc.Id,
                IsActive = true
            };
            _db.AssetCategories.Add(_category);

            _db.SaveChanges();
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        // ─── Test 1: إنشاء تصنيف أصل جديد ────────────────────────────────────
        [Fact]
        public async Task Test01_Create_Asset_Category_Successfully()
        {
            var dto = new CreateAssetCategoryDto
            {
                Code = "CAT-VEH",
                NameAr = "السيارات ووسائل النقل",
                NameEn = "Vehicles",
                DefaultUsefulLifeMonths = 60,
                DefaultDepreciationMethod = DepreciationMethod.StraightLine,
                AssetCostAccountId = _costAcc.Id,
                AccumulatedDepreciationAccountId = _accDepAcc.Id,
                DepreciationExpenseAccountId = _depExpAcc.Id,
                DisposalGainAccountId = _disposalGainAcc.Id,
                DisposalLossAccountId = _disposalLossAcc.Id
            };

            var res = await _categoryService.CreateAsync(dto, "USR-01");
            Assert.True(res.Success);
            Assert.NotNull(res.CategoryId);

            var created = await _db.AssetCategories.FindAsync(res.CategoryId.Value);
            Assert.NotNull(created);
            Assert.Equal("CAT-VEH", created.Code);
            Assert.Equal(60, created.DefaultUsefulLifeMonths);
        }

        // ─── Test 2: إنشاء أصل ثابت جديد وتوليد كود تلقائي ─────────────────────
        [Fact]
        public async Task Test02_Create_New_Fixed_Asset_With_Auto_Code()
        {
            var dto = new CreateFixedAssetDto
            {
                NameAr = "ماكينة قص ليزر صناعية",
                AssetCategoryId = _category.Id,
                BranchId = 1,
                PurchaseDate = new DateTime(2026, 11, 1),
                PlacedInServiceDate = new DateTime(2026, 11, 1),
                DepreciationStartDate = new DateTime(2026, 11, 1),
                InitialCostLocal = 120000m,
                AdditionalExpensesLocal = 0m,
                UsefulLifeMonths = 60,
                DepreciationMethod = DepreciationMethod.StraightLine
            };

            var res = await _assetService.CreateAssetAsync(dto, "USR-01");
            Assert.True(res.Success, res.Message);
            Assert.NotNull(res.AssetId);
            Assert.StartsWith("AST-1-2026-", res.AssetCode);

            var asset = await _db.FixedAssets.FindAsync(res.AssetId.Value);
            Assert.NotNull(asset);
            Assert.Equal(120000m, asset.CurrentCostLocal);
            Assert.Equal(120000m, asset.CurrentBookValue);
            Assert.Equal(0m, asset.CurrentAccumulatedDepreciation);
            Assert.Equal(AssetStatus.InService, asset.Status);
        }

        // ─── Test 3: حساب الإهلاك بالقسط الثابت ───────────────────────────────
        [Fact]
        public void Test03_Straight_Line_Depreciation_Calculation()
        {
            // Cost: 60,000 | Useful Life: 60 months | Residual: 0 => Monthly = 1,000
            var asset = new FixedAsset
            {
                Status = AssetStatus.InService,
                CurrentCostLocal = 60000m,
                CurrentBookValue = 60000m,
                ResidualValue = 0m,
                UsefulLifeMonths = 60,
                DepreciationMethod = DepreciationMethod.StraightLine,
                DepreciationStartDate = new DateTime(2026, 1, 1)
            };

            decimal dep = _depreciationService.CalculatePeriodDepreciation(asset, new DateTime(2026, 11, 30), new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));
            Assert.Equal(1000m, dep);
        }

        // ─── Test 4: حساب الإهلاك بالقسط المتناقص ────────────────────────────
        [Fact]
        public void Test04_Declining_Balance_Depreciation_Calculation()
        {
            // Book value: 100,000 | Annual Rate: 20% => Monthly Rate = (20 / 100) / 12 = 0.0166667 => Monthly = 1,666.6667
            var asset = new FixedAsset
            {
                Status = AssetStatus.InService,
                CurrentCostLocal = 100000m,
                CurrentBookValue = 100000m,
                ResidualValue = 5000m,
                DepreciationRate = 20m,
                DepreciationMethod = DepreciationMethod.DecliningBalance,
                DepreciationStartDate = new DateTime(2026, 1, 1)
            };

            decimal dep = _depreciationService.CalculatePeriodDepreciation(asset, new DateTime(2026, 11, 30), new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));
            decimal expected = Math.Round(100000m * (0.20m / 12m), 4);
            Assert.Equal(expected, dep);
        }

        // ─── Test 5: أصل بدون إهلاك (مثل الأراضي) ───────────────────────────
        [Fact]
        public void Test05_No_Depreciation_Asset_Should_Yield_Zero()
        {
            var landAsset = new FixedAsset
            {
                Status = AssetStatus.InService,
                CurrentCostLocal = 5000000m,
                CurrentBookValue = 5000000m,
                DepreciationMethod = DepreciationMethod.NoDepreciation,
                DepreciationStartDate = new DateTime(2026, 1, 1)
            };

            decimal dep = _depreciationService.CalculatePeriodDepreciation(landAsset, new DateTime(2026, 11, 30), new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));
            Assert.Equal(0m, dep);
        }

        // ─── Test 6: أصل في منتصف الشهر وسياسة الأيام النسبية (Pro Rata) ────
        [Fact]
        public void Test06_Mid_Month_Addition_ProRata_Policy()
        {
            // November has 30 days. Asset placed in service on 16-Nov => 15 active days out of 30 => exactly 50%
            // Full monthly = 60,000 / 60 = 1,000 => Pro rata = 1,000 * (15/30) = 500
            var asset = new FixedAsset
            {
                Status = AssetStatus.InService,
                CurrentCostLocal = 60000m,
                CurrentBookValue = 60000m,
                ResidualValue = 0m,
                UsefulLifeMonths = 60,
                DepreciationMethod = DepreciationMethod.StraightLine,
                DepreciationStartDate = new DateTime(2026, 11, 16),
                FirstMonthPolicy = FirstMonthPolicy.ProRataDays
            };

            decimal dep = _depreciationService.CalculatePeriodDepreciation(asset, new DateTime(2026, 11, 30), new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));
            Assert.Equal(500m, dep);
        }

        // ─── Test 7: أصل له قيمة تخريدية وعدم تجاوزها ──────────────────────
        [Fact]
        public void Test07_Residual_Value_Cap_Prevention()
        {
            // Cost: 60,000 | Residual: 10,000 | Depreciable Basis: 50,000 | Current Book Value: 10,200
            // Normal monthly: 50,000 / 50 = 1,000 | Max allowed: 10,200 - 10,000 = 200
            var asset = new FixedAsset
            {
                Status = AssetStatus.InService,
                CurrentCostLocal = 60000m,
                CurrentBookValue = 10200m,
                ResidualValue = 10000m,
                UsefulLifeMonths = 50,
                DepreciationMethod = DepreciationMethod.StraightLine,
                DepreciationStartDate = new DateTime(2026, 1, 1)
            };

            decimal dep = _depreciationService.CalculatePeriodDepreciation(asset, new DateTime(2026, 11, 30), new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));
            Assert.Equal(200m, dep); // Capped at exactly remaining amount before residual value!
        }

        // ─── Test 8: أصل قديم برصيد افتتاحي ────────────────────────────────
        [Fact]
        public async Task Test08_Create_Old_Asset_With_Opening_Accumulated_Depreciation()
        {
            var dto = new CreateFixedAssetDto
            {
                NameAr = "مولد كهربائي قديم",
                AssetCategoryId = _category.Id,
                BranchId = 1,
                PurchaseDate = new DateTime(2024, 1, 1),
                PlacedInServiceDate = new DateTime(2024, 1, 1),
                DepreciationStartDate = new DateTime(2026, 1, 1),
                InitialCostLocal = 100000m,
                IsOpeningAsset = true,
                OpeningAccumulatedDepreciation = 40000m,
                UsefulLifeMonths = 60,
                DepreciationMethod = DepreciationMethod.StraightLine
            };

            var res = await _assetService.CreateAssetAsync(dto, "USR-01");
            Assert.True(res.Success);

            var asset = await _db.FixedAssets.FindAsync(res.AssetId.Value);
            Assert.NotNull(asset);
            Assert.True(asset.IsOpeningAsset);
            Assert.Equal(100000m, asset.CurrentCostLocal);
            Assert.Equal(40000m, asset.OpeningAccumulatedDepreciation);
            Assert.Equal(40000m, asset.CurrentAccumulatedDepreciation);
            Assert.Equal(60000m, asset.CurrentBookValue); // 100,000 - 40,000 = 60,000
        }

        // ─── Test 9: تشغيل الإهلاك الدوري واعتماد القيد المحاسبي ──────────────
        [Fact]
        public async Task Test09_Execute_And_Approve_Monthly_Depreciation_Run()
        {
            // Seed asset: 60,000 cost / 60 months = 1,000/mo
            var createRes = await _assetService.CreateAssetAsync(new CreateFixedAssetDto
            {
                NameAr = "ضاغط هواء مصنع",
                AssetCategoryId = _category.Id,
                BranchId = 1,
                CostCenterId = _costCenter1.Id,
                PurchaseDate = new DateTime(2026, 11, 1),
                PlacedInServiceDate = new DateTime(2026, 11, 1),
                DepreciationStartDate = new DateTime(2026, 11, 1),
                InitialCostLocal = 60000m,
                UsefulLifeMonths = 60,
                DepreciationMethod = DepreciationMethod.StraightLine
            }, "USR-01");

            var runRes = await _depreciationService.ExecuteDepreciationRunAsync(new ExecuteDepreciationRunDto
            {
                RunDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                AutoPost = true
            }, "USR-01");

            Assert.True(runRes.Success, runRes.Message);
            Assert.NotNull(runRes.RunId);
            Assert.NotNull(runRes.JournalEntryId);

            var run = await _db.AssetDepreciationRuns.Include(r => r.Histories).FirstOrDefaultAsync(r => r.Id == runRes.RunId);
            Assert.NotNull(run);
            Assert.Equal(DepreciationRunStatus.Approved, run.Status);
            Assert.Equal(1000m, run.TotalDepreciationAmount);

            // Asset balances updated
            var asset = await _db.FixedAssets.FindAsync(createRes.AssetId.Value);
            Assert.NotNull(asset);
            Assert.Equal(1000m, asset.CurrentAccumulatedDepreciation);
            Assert.Equal(59000m, asset.CurrentBookValue);
            Assert.Equal(new DateTime(2026, 11, 30), asset.LastDepreciationDate);
        }

        // ─── Test 10: منع تكرار إهلاك نفس الأصل لنفس الفترة المالية ──────────
        [Fact]
        public async Task Test10_Prevent_Duplicate_Depreciation_In_Same_Period()
        {
            await _assetService.CreateAssetAsync(new CreateFixedAssetDto
            {
                NameAr = "مخرطة صناعية",
                AssetCategoryId = _category.Id,
                BranchId = 1,
                PurchaseDate = new DateTime(2026, 11, 1),
                DepreciationStartDate = new DateTime(2026, 11, 1),
                InitialCostLocal = 60000m,
                UsefulLifeMonths = 60
            }, "USR-01");

            // 1. First run in Nov
            var run1 = await _depreciationService.ExecuteDepreciationRunAsync(new ExecuteDepreciationRunDto
            {
                RunDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                AutoPost = true
            }, "USR-01");
            Assert.True(run1.Success);

            // 2. Second attempt in same period (Nov)
            var run2 = await _depreciationService.ExecuteDepreciationRunAsync(new ExecuteDepreciationRunDto
            {
                RunDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                AutoPost = true
            }, "USR-01");

            Assert.False(run2.Success);
            Assert.Contains("لا توجد أي أصول مؤهلة", run2.Message);
        }

        // ─── Test 11: التحقق من توازن قيد الإهلاك المحاسبي وحساباته ─────────
        [Fact]
        public async Task Test11_Ensure_Depreciation_Journal_Entry_Is_Balanced_And_Mapped()
        {
            await _assetService.CreateAssetAsync(new CreateFixedAssetDto
            {
                NameAr = "شاحنة توزيع",
                AssetCategoryId = _category.Id,
                BranchId = 1,
                CostCenterId = _costCenter1.Id,
                PurchaseDate = new DateTime(2026, 11, 1),
                DepreciationStartDate = new DateTime(2026, 11, 1),
                InitialCostLocal = 120000m,
                UsefulLifeMonths = 60
            }, "USR-01");

            var runRes = await _depreciationService.ExecuteDepreciationRunAsync(new ExecuteDepreciationRunDto
            {
                RunDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                AutoPost = true
            }, "USR-01");

            var jv = await _db.JournalEntryHeaders.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == runRes.JournalEntryId);
            Assert.NotNull(jv);
            Assert.Equal(JournalEntryStatus.Posted, jv.Status);
            Assert.Equal(SourceDocumentType.AssetDepreciation, jv.SourceDocumentType);

            // Strict Debit == Credit check
            Assert.Equal(jv.TotalDebitLocal, jv.TotalCreditLocal);
            Assert.Equal(2000m, jv.TotalDebitLocal); // 120,000 / 60 = 2,000

            // Check Debit is Expense and Credit is Accumulated Depreciation
            var debitLine = jv.Lines.First(l => l.DebitLocal > 0);
            var creditLine = jv.Lines.First(l => l.CreditLocal > 0);

            Assert.Equal(_depExpAcc.Id, debitLine.AccountId);
            Assert.Equal(_costCenter1.Id, debitLine.CostCenterId);
            Assert.Equal(_accDepAcc.Id, creditLine.AccountId);
        }

        // ─── Test 12: نقل الأصل بين فرعين وتحديث المسؤولية والعهدة ───────────
        [Fact]
        public async Task Test12_Transfer_Asset_Between_Branches()
        {
            var createRes = await _assetService.CreateAssetAsync(new CreateFixedAssetDto
            {
                NameAr = "سيرفر رئيسي",
                AssetCategoryId = _category.Id,
                BranchId = 1,
                CustodianEmployeeName = "مهندس محمد",
                Department = "تكنولوجيا المعلومات",
                PurchaseDate = new DateTime(2026, 11, 1),
                InitialCostLocal = 50000m
            }, "USR-01");

            var transferRes = await _assetService.TransferAssetAsync(new AssetTransferDto
            {
                AssetId = createRes.AssetId!.Value,
                TransferDate = new DateTime(2026, 11, 15),
                ToBranchId = 2,
                ToDepartment = "فرع الإسكندرية",
                ToCustodian = "مهندس علي",
                Reason = "دعم فرع الإسكندرية بسيرفر جديد"
            }, "USR-01");

            Assert.True(transferRes.Success);

            var asset = await _db.FixedAssets.Include(a => a.Transactions).FirstOrDefaultAsync(a => a.Id == createRes.AssetId.Value);
            Assert.NotNull(asset);
            Assert.Equal(2, asset.BranchId);
            Assert.Equal("مهندس علي", asset.CustodianEmployeeName);
            Assert.Equal("فرع الإسكندرية", asset.Department);

            var tx = asset.Transactions.FirstOrDefault(t => t.TransactionType == AssetTransactionType.Transfer);
            Assert.NotNull(tx);
            Assert.Equal(1, tx.FromBranchId);
            Assert.Equal(2, tx.ToBranchId);
        }

        // ─── Test 13: إضافة تكلفة لاحقة ورسملتها على الأصل ──────────────────
        [Fact]
        public async Task Test13_Add_Capitalized_Subsequent_Cost_Increases_Asset_Cost()
        {
            var createRes = await _assetService.CreateAssetAsync(new CreateFixedAssetDto
            {
                NameAr = "خط إنتاج تعبئة",
                AssetCategoryId = _category.Id,
                BranchId = 1,
                PurchaseDate = new DateTime(2026, 11, 1),
                InitialCostLocal = 100000m
            }, "USR-01");

            var addRes = await _assetService.AddAdditionalCostAsync(new AssetAdditionalCostDto
            {
                AssetId = createRes.AssetId!.Value,
                TransactionDate = new DateTime(2026, 11, 10),
                Amount = 25000m,
                IsCapitalized = true,
                CreateJournalEntry = true,
                Reason = "إضافة وحدة تحكم رقمية PLC متطورة"
            }, "USR-01");

            Assert.True(addRes.Success);
            Assert.NotNull(addRes.JournalEntryId);

            var asset = await _db.FixedAssets.FindAsync(createRes.AssetId.Value);
            Assert.NotNull(asset);
            Assert.Equal(125000m, asset.CurrentCostLocal); // 100,000 + 25,000
            Assert.Equal(125000m, asset.CurrentBookValue);

            var jv = await _db.JournalEntryHeaders.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == addRes.JournalEntryId);
            Assert.NotNull(jv);
            Assert.Equal(25000m, jv.TotalDebitLocal);
            Assert.Equal(25000m, jv.TotalCreditLocal);
        }

        // ─── Test 14: استبعاد الأصل مع تحقيق أرباح رأسمالية ─────────────────
        [Fact]
        public async Task Test14_Asset_Disposal_With_Capital_Gain()
        {
            // Cost: 100,000 | AccDep: 70,000 | NetBook: 30,000 | Sold for: 35,000 => Gain = 5,000
            var createRes = await _assetService.CreateAssetAsync(new CreateFixedAssetDto
            {
                NameAr = "سيارة مدير مبيعات",
                AssetCategoryId = _category.Id,
                BranchId = 1,
                PurchaseDate = new DateTime(2024, 1, 1),
                InitialCostLocal = 100000m,
                IsOpeningAsset = true,
                OpeningAccumulatedDepreciation = 70000m
            }, "USR-01");

            var dispRes = await _assetService.DisposeAssetAsync(new AssetDisposalDto
            {
                AssetId = createRes.AssetId!.Value,
                DisposalDate = new DateTime(2026, 11, 20),
                ProceedsAmount = 35000m,
                Reason = "بيع سيارة مستعملة لانتهاء الاستخدام",
                AutoPostJournalEntry = true
            }, "USR-01");

            Assert.True(dispRes.Success);
            Assert.NotNull(dispRes.JournalEntryId);

            var asset = await _db.FixedAssets.FindAsync(createRes.AssetId.Value);
            Assert.NotNull(asset);
            Assert.Equal(AssetStatus.Disposed, asset.Status);
            Assert.Equal(0m, asset.CurrentBookValue);

            var jv = await _db.JournalEntryHeaders.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == dispRes.JournalEntryId);
            Assert.NotNull(jv);
            Assert.Equal(jv.TotalDebitLocal, jv.TotalCreditLocal);
            // Total debit: 35,000 (Cash) + 70,000 (AccDep) = 105,000
            // Total credit: 100,000 (Asset Cost) + 5,000 (Gain) = 105,000
            Assert.Equal(105000m, jv.TotalDebitLocal);

            var gainLine = jv.Lines.FirstOrDefault(l => l.AccountId == _disposalGainAcc.Id);
            Assert.NotNull(gainLine);
            Assert.Equal(5000m, gainLine.CreditLocal);
        }

        // ─── Test 15: استبعاد الأصل مع تحقيق خسائر رأسمالية ─────────────────
        [Fact]
        public async Task Test15_Asset_Disposal_With_Capital_Loss()
        {
            // Cost: 100,000 | AccDep: 70,000 | NetBook: 30,000 | Sold for: 20,000 => Loss = 10,000
            var createRes = await _assetService.CreateAssetAsync(new CreateFixedAssetDto
            {
                NameAr = "معدة قديمة",
                AssetCategoryId = _category.Id,
                BranchId = 1,
                PurchaseDate = new DateTime(2024, 1, 1),
                InitialCostLocal = 100000m,
                IsOpeningAsset = true,
                OpeningAccumulatedDepreciation = 70000m
            }, "USR-01");

            var dispRes = await _assetService.DisposeAssetAsync(new AssetDisposalDto
            {
                AssetId = createRes.AssetId!.Value,
                DisposalDate = new DateTime(2026, 11, 20),
                ProceedsAmount = 20000m,
                Reason = "بيع خردة بخسارة",
                AutoPostJournalEntry = true
            }, "USR-01");

            Assert.True(dispRes.Success);
            var jv = await _db.JournalEntryHeaders.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == dispRes.JournalEntryId);
            Assert.NotNull(jv);
            Assert.Equal(jv.TotalDebitLocal, jv.TotalCreditLocal);
            // Total debit: 20,000 (Cash) + 70,000 (AccDep) + 10,000 (Loss) = 100,000
            // Total credit: 100,000 (Asset Cost)
            Assert.Equal(100000m, jv.TotalDebitLocal);

            var lossLine = jv.Lines.FirstOrDefault(l => l.AccountId == _disposalLossAcc.Id);
            Assert.NotNull(lossLine);
            Assert.Equal(10000m, lossLine.DebitLocal);
        }

        // ─── Test 16: أصل تحت الإنشاء (CIP) وتشغيله وتحويله لأصل في الخدمة ──
        [Fact]
        public async Task Test16_Construction_In_Progress_Capitalization()
        {
            // 1. Create asset under construction
            var createRes = await _assetService.CreateAssetAsync(new CreateFixedAssetDto
            {
                NameAr = "بناء جملون تخزين جديد",
                AssetCategoryId = _category.Id,
                BranchId = 1,
                PurchaseDate = new DateTime(2026, 11, 1),
                InitialCostLocal = 200000m,
                Status = AssetStatus.UnderConstruction
            }, "USR-01");

            var cipAsset = await _db.FixedAssets.FindAsync(createRes.AssetId!.Value);
            Assert.NotNull(cipAsset);
            Assert.Equal(AssetStatus.UnderConstruction, cipAsset.Status);

            // 2. Ensure CIP cannot be depreciated before placed in service
            decimal depBefore = _depreciationService.CalculatePeriodDepreciation(cipAsset, new DateTime(2026, 11, 30), new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));
            Assert.Equal(0m, depBefore);

            // 3. Capitalize and place in service
            var capRes = await _assetService.CapitalizeCipAssetAsync(new AssetCapitalizeCipDto
            {
                AssetId = cipAsset.Id,
                CapitalizationDate = new DateTime(2026, 11, 25),
                DepreciationStartDate = new DateTime(2026, 12, 1),
                UsefulLifeMonths = 120,
                ResidualValue = 10000m,
                CreateReclassificationEntry = true
            }, "USR-01");

            Assert.True(capRes.Success);
            Assert.NotNull(capRes.JournalEntryId);

            var activatedAsset = await _db.FixedAssets.FindAsync(cipAsset.Id);
            Assert.NotNull(activatedAsset);
            Assert.Equal(AssetStatus.InService, activatedAsset.Status);
            Assert.Equal(new DateTime(2026, 12, 1), activatedAsset.DepreciationStartDate);
            Assert.Equal(120, activatedAsset.UsefulLifeMonths);

            // Reclassification JV: Debit Fixed Assets (120101) / Credit CIP (120201)
            var jv = await _db.JournalEntryHeaders.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == capRes.JournalEntryId);
            Assert.NotNull(jv);
            Assert.Equal(200000m, jv.TotalDebitLocal);
            Assert.Equal(200000m, jv.TotalCreditLocal);
        }

        // ─── Test 17: منع تشغيل الإهلاك في فترة مالية مغلقة ─────────────────
        [Fact]
        public async Task Test17_Prevent_Depreciation_Run_In_Closed_Fiscal_Period()
        {
            // Close period Nov 2026
            _periodNov.IsClosed = true;
            await _db.SaveChangesAsync();

            var runRes = await _depreciationService.ExecuteDepreciationRunAsync(new ExecuteDepreciationRunDto
            {
                RunDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1
            }, "USR-01");

            Assert.False(runRes.Success);
            Assert.Contains("مغلق", runRes.Message);
        }

        // ─── Test 18: عكس عملية الإهلاك وإرجاع أرصدة الأصول دفترياً ───────────
        [Fact]
        public async Task Test18_Reverse_Depreciation_Run_Restores_Asset_Balances()
        {
            var createRes = await _assetService.CreateAssetAsync(new CreateFixedAssetDto
            {
                NameAr = "ماكينة طباعة",
                AssetCategoryId = _category.Id,
                BranchId = 1,
                PurchaseDate = new DateTime(2026, 11, 1),
                DepreciationStartDate = new DateTime(2026, 11, 1),
                InitialCostLocal = 60000m,
                UsefulLifeMonths = 60
            }, "USR-01");

            var runRes = await _depreciationService.ExecuteDepreciationRunAsync(new ExecuteDepreciationRunDto
            {
                RunDate = new DateTime(2026, 11, 30),
                CompanyId = 1,
                BranchId = 1,
                AutoPost = true
            }, "USR-01");

            Assert.True(runRes.Success);

            // Reverse in Dec
            var revRes = await _depreciationService.ReverseDepreciationRunAsync(runRes.RunId!.Value, new DateTime(2026, 12, 1), "USR-01");
            Assert.True(revRes.Success);
            Assert.NotNull(revRes.ReversalJournalEntryId);

            var run = await _db.AssetDepreciationRuns.FindAsync(runRes.RunId.Value);
            Assert.NotNull(run);
            Assert.Equal(DepreciationRunStatus.Reversed, run.Status);

            // Asset balances restored to original
            var asset = await _db.FixedAssets.FindAsync(createRes.AssetId!.Value);
            Assert.NotNull(asset);
            Assert.Equal(0m, asset.CurrentAccumulatedDepreciation);
            Assert.Equal(60000m, asset.CurrentBookValue);
        }
    }
}
