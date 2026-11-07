using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using ERP.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ERP.Tests.Unit
{
    public class CustomerVendorMultiCurrencyGroupTests : IDisposable
    {
        private readonly ErpDbContext _db;
        private readonly Mock<ICurrentUserContext> _userContextMock;
        private readonly Mock<IAuditService> _auditMock;
        private readonly ICodeSequenceService _sequenceService;
        private readonly IPartyGroupService _partyGroupService;

        private readonly Currency _egp;
        private readonly Currency _usd;
        private readonly Currency _eur;
        private readonly Currency _cny;
        private readonly GLAccount _custControlAcc;
        private readonly GLAccount _vendControlAcc;
        private readonly CustomerCategory _custCat;
        private readonly VendorCategory _vendCat;

        public CustomerVendorMultiCurrencyGroupTests()
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ErpDbContext(options);
            _userContextMock = new Mock<ICurrentUserContext>();
            _auditMock = new Mock<IAuditService>();
            _sequenceService = new CodeSequenceService(_db);
            _partyGroupService = new PartyGroupService(_db, _sequenceService, _userContextMock.Object, _auditMock.Object);

            // Setup default user context: Branch 1, Company 1, HasAllBranches = true, all permissions granted
            _userContextMock.Setup(u => u.UserId).Returns("admin-usr-1");
            _userContextMock.Setup(u => u.Username).Returns("admin");
            _userContextMock.Setup(u => u.BranchId).Returns(1);
            _userContextMock.Setup(u => u.CompanyId).Returns(1);
            _userContextMock.Setup(u => u.HasAllBranches).Returns(true);
            _userContextMock.Setup(u => u.CanAccessBranch(It.IsAny<int>())).Returns(true);
            _userContextMock.Setup(u => u.CanAccessCompany(It.IsAny<int>())).Returns(true);
            _userContextMock.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(true);

            // Seed Branches 1 and 2
            var b1 = new Branch { Id = 1, Code = "B001", BranchCode = "B001", Name = "Main Cairo", NameAr = "فرع القاهرة", NameEn = "Cairo Branch", CompanyId = 1, IsActive = true };
            var b2 = new Branch { Id = 2, Code = "B002", BranchCode = "B002", Name = "Alexandria", NameAr = "فرع الإسكندرية", NameEn = "Alex Branch", CompanyId = 1, IsActive = true };
            _db.Branches.AddRange(b1, b2);

            // Seed Currencies (EGP, USD, EUR, CNY)
            _egp = new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", Symbol = "ج.م", ExchangeRate = 1.0m, IsActive = true };
            _usd = new Currency { Id = 2, Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar", Symbol = "$", ExchangeRate = 50.0m, IsActive = true };
            _eur = new Currency { Id = 3, Code = "EUR", NameAr = "يورو", NameEn = "Euro", Symbol = "€", ExchangeRate = 54.0m, IsActive = true };
            _cny = new Currency { Id = 4, Code = "CNY", NameAr = "يوان صيني", NameEn = "Chinese Yuan", Symbol = "¥", ExchangeRate = 7.0m, IsActive = true };
            _db.Currencies.AddRange(_egp, _usd, _eur, _cny);

            // Seed Financial Accounts
            _custControlAcc = new GLAccount { Id = 101, Code = "110201", NameAr = "مراقبة العملاء", NameEn = "AR Control", IsGroup = false, SubLedgerType = SubLedgerType.Customer, IsActive = true };
            _vendControlAcc = new GLAccount { Id = 201, Code = "210101", NameAr = "مراقبة الموردين", NameEn = "AP Control", IsGroup = false, SubLedgerType = SubLedgerType.Vendor, IsActive = true };
            _db.GLAccounts.AddRange(_custControlAcc, _vendControlAcc);

            // Seed Categories
            _custCat = new CustomerCategory { Id = 1, BranchId = 1, Code = "CCAT-01", NameAr = "عملاء محليون", NameEn = "Local Customers", ControlAccountId = _custControlAcc.Id, IsDefault = true, IsActive = true };
            _vendCat = new VendorCategory { Id = 1, BranchId = 1, Code = "VCAT-01", NameAr = "موردون محليون", NameEn = "Local Vendors", ControlAccountId = _vendControlAcc.Id, IsDefault = true, IsActive = true };
            _db.CustomerCategories.Add(_custCat);
            _db.VendorCategories.Add(_vendCat);

            // Seed Code Sequences
            _db.CodeSequences.AddRange(
                new CodeSequence { EntityName = "Customer", BranchId = 1, Prefix = "B001-C", LastNumber = 0, PaddingLength = 6, IsAuto = true, IsManualAllowed = true },
                new CodeSequence { EntityName = "Vendor", BranchId = 1, Prefix = "B001-V", LastNumber = 0, PaddingLength = 6, IsAuto = true, IsManualAllowed = true },
                new CodeSequence { EntityName = "Customer", BranchId = 2, Prefix = "B002-C", LastNumber = 0, PaddingLength = 6, IsAuto = true, IsManualAllowed = true },
                new CodeSequence { EntityName = "Vendor", BranchId = 2, Prefix = "B002-V", LastNumber = 0, PaddingLength = 6, IsAuto = true, IsManualAllowed = true }
            );

            _db.SaveChanges();
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        private static ControllerContext CreateControllerContext()
        {
            var httpContext = new DefaultHttpContext();
            return new ControllerContext { HttpContext = httpContext };
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 1. إنشاء عميل بالجنيه فقط (Scenario 1)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario01_CreateCustomer_WithEgpOnly()
        {
            var req = new CreateCustomerGroupRequestDto
            {
                BaseCustomer = new Customer
                {
                    BranchId = 1,
                    NameAr = "شركة الأمل للتجارة",
                    NameEn = "Al-Amal Trading Co.",
                    CustomerCategoryId = _custCat.Id,
                    FinancialAccountId = _custControlAcc.Id,
                    CurrencyId = _egp.Id,
                    IsActive = true
                },
                CustomerCurrencyIds = new List<int> { _egp.Id }
            };

            var result = await _partyGroupService.CreateCustomerGroupAsync(req);

            Assert.True(result.Success);
            Assert.Single(result.CreatedCustomers);
            Assert.Empty(result.CreatedVendors);

            var created = result.CreatedCustomers[0];
            Assert.Equal("B001-C000001", created.Code);
            Assert.Equal(_egp.Id, created.CurrencyId);
            Assert.Contains("مصري", created.NameAr);
            Assert.NotNull(created.PartyGroupId);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 2. إنشاء عميل بالجنيه والدولار (Scenario 2)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario02_CreateCustomer_WithEgpAndUsd()
        {
            var req = new CreateCustomerGroupRequestDto
            {
                BaseCustomer = new Customer
                {
                    BranchId = 1,
                    NameAr = "شركة النور",
                    NameEn = "Al-Noor Co.",
                    CustomerCategoryId = _custCat.Id,
                    FinancialAccountId = _custControlAcc.Id,
                    IsActive = true
                },
                CustomerCurrencyIds = new List<int> { _egp.Id, _usd.Id }
            };

            var result = await _partyGroupService.CreateCustomerGroupAsync(req);

            Assert.True(result.Success);
            Assert.Equal(2, result.CreatedCustomers.Count);
            Assert.NotNull(result.PartyGroupId);

            var egpCust = result.CreatedCustomers.First(c => c.CurrencyId == _egp.Id);
            var usdCust = result.CreatedCustomers.First(c => c.CurrencyId == _usd.Id);

            Assert.Equal("B001-C000001", egpCust.Code);
            Assert.Equal("B001-C000002", usdCust.Code);
            Assert.Equal(egpCust.PartyGroupId, usdCust.PartyGroupId);

            Assert.Equal("شركة النور - مصري", egpCust.NameAr);
            Assert.Equal("Al-Noor Co. - EGP", egpCust.NameEn);
            Assert.Equal("شركة النور - دولار", usdCust.NameAr);
            Assert.Equal("Al-Noor Co. - Dollar", usdCust.NameEn);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 3. إنشاء عميل بأربع عملات دفعة واحدة EGP, USD, EUR, CNY (Scenario 3)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario03_CreateCustomer_With4CurrenciesAtOnce()
        {
            var req = new CreateCustomerGroupRequestDto
            {
                BaseCustomer = new Customer
                {
                    BranchId = 1,
                    NameAr = "مجموعة الصفا الدولية",
                    NameEn = "Al-Safa International",
                    CustomerCategoryId = _custCat.Id,
                    FinancialAccountId = _custControlAcc.Id,
                    IsActive = true
                },
                CustomerCurrencyIds = new List<int> { _egp.Id, _usd.Id, _eur.Id, _cny.Id }
            };

            var result = await _partyGroupService.CreateCustomerGroupAsync(req);

            Assert.True(result.Success);
            Assert.Equal(4, result.CreatedCustomers.Count);

            var codes = result.CreatedCustomers.Select(c => c.Code).Distinct().ToList();
            Assert.Equal(4, codes.Count);

            var groupIds = result.CreatedCustomers.Select(c => c.PartyGroupId).Distinct().ToList();
            Assert.Single(groupIds);

            var cnyCust = result.CreatedCustomers.First(c => c.CurrencyId == _cny.Id);
            Assert.Contains("يوان صيني", cnyCust.NameAr);
            Assert.Contains("Chinese Yuan", cnyCust.NameEn);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 4. إنشاء عميل ومورد مرتبط في نفس المجموعة (Scenario 4)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario04_CreateCustomer_WithLinkedVendor()
        {
            var req = new CreateCustomerGroupRequestDto
            {
                BaseCustomer = new Customer
                {
                    BranchId = 1,
                    NameAr = "شركة الأهرام للتجارة والصناعة",
                    NameEn = "Al-Ahram Trading & Industry",
                    CustomerCategoryId = _custCat.Id,
                    FinancialAccountId = _custControlAcc.Id,
                    IsActive = true
                },
                CustomerCurrencyIds = new List<int> { _egp.Id },
                CreateLinkedVendor = true,
                VendorCurrencyIds = new List<int> { _egp.Id },
                LinkedVendorCategoryId = _vendCat.Id,
                LinkedVendorFinancialAccountId = _vendControlAcc.Id
            };

            var result = await _partyGroupService.CreateCustomerGroupAsync(req);

            Assert.True(result.Success);
            Assert.Single(result.CreatedCustomers);
            Assert.Single(result.CreatedVendors);

            var cust = result.CreatedCustomers[0];
            var vend = result.CreatedVendors[0];

            Assert.Equal(cust.PartyGroupId, vend.PartyGroupId);
            Assert.Equal("B001-C000001", cust.Code);
            Assert.Equal("B001-V000001", vend.Code);
            Assert.Equal(_custControlAcc.Id, cust.FinancialAccountId);
            Assert.Equal(_vendControlAcc.Id, vend.FinancialAccountId);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 5. إنشاء 4 عملاء و4 موردين دفعة واحدة بنفس العملية (Scenario 5)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario05_Create4CustomersAnd4Vendors_InSameOperation()
        {
            var req = new CreateCustomerGroupRequestDto
            {
                BaseCustomer = new Customer
                {
                    BranchId = 1,
                    NameAr = "مؤسسة النيل للتجارة الدولية",
                    NameEn = "Nile Global Trading",
                    CustomerCategoryId = _custCat.Id,
                    FinancialAccountId = _custControlAcc.Id,
                    IsActive = true
                },
                CustomerCurrencyIds = new List<int> { _egp.Id, _usd.Id, _eur.Id, _cny.Id },
                CreateLinkedVendor = true,
                VendorCurrencyIds = new List<int> { _egp.Id, _usd.Id, _eur.Id, _cny.Id }
            };

            var result = await _partyGroupService.CreateCustomerGroupAsync(req);

            Assert.True(result.Success);
            Assert.Equal(4, result.CreatedCustomers.Count);
            Assert.Equal(4, result.CreatedVendors.Count);

            int sharedGroupId = result.PartyGroupId!.Value;
            Assert.All(result.CreatedCustomers, c => Assert.Equal(sharedGroupId, c.PartyGroupId));
            Assert.All(result.CreatedVendors, v => Assert.Equal(sharedGroupId, v.PartyGroupId));

            // Verify independent codes
            var allCodes = result.CreatedCustomers.Select(c => c.Code)
                .Concat(result.CreatedVendors.Select(v => v.Code))
                .ToList();
            Assert.Equal(8, allCodes.Distinct().Count());
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 6. اختبار الاسم الإنجليزي الفارغ: استخدام الاسم العربي وتذييله بالعملة (Scenario 6)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario06_EmptyEnglishName_FallsBackToNameAr_WithCurrencySuffix()
        {
            var req = new CreateCustomerGroupRequestDto
            {
                BaseCustomer = new Customer
                {
                    BranchId = 1,
                    NameAr = "شركة البركة",
                    NameEn = "", // Empty English name
                    CustomerCategoryId = _custCat.Id,
                    FinancialAccountId = _custControlAcc.Id,
                    IsActive = true
                },
                CustomerCurrencyIds = new List<int> { _usd.Id, _cny.Id }
            };

            var result = await _partyGroupService.CreateCustomerGroupAsync(req);

            Assert.True(result.Success);
            var usdCust = result.CreatedCustomers.First(c => c.CurrencyId == _usd.Id);
            var cnyCust = result.CreatedCustomers.First(c => c.CurrencyId == _cny.Id);

            // Never saved empty
            Assert.False(string.IsNullOrWhiteSpace(usdCust.NameEn));
            Assert.False(string.IsNullOrWhiteSpace(cnyCust.NameEn));

            // Falls back to NameAr + English currency suffix
            Assert.Equal("شركة البركة - Dollar", usdCust.NameEn);
            Assert.Equal("شركة البركة - Chinese Yuan", cnyCust.NameEn);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 7. توليد كود مستقل لكل سجل عميل ومورد (Scenario 7)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario07_IndependentCodeGeneration_ForEachCreatedRecord()
        {
            var req = new CreateCustomerGroupRequestDto
            {
                BaseCustomer = new Customer
                {
                    BranchId = 1,
                    NameAr = "سيريال تست",
                    NameEn = "Serial Test",
                    CustomerCategoryId = _custCat.Id,
                    FinancialAccountId = _custControlAcc.Id,
                    IsActive = true
                },
                CustomerCurrencyIds = new List<int> { _egp.Id, _usd.Id, _eur.Id }
            };

            var result = await _partyGroupService.CreateCustomerGroupAsync(req);

            Assert.True(result.Success);
            Assert.Equal("B001-C000001", result.CreatedCustomers[0].Code);
            Assert.Equal("B001-C000002", result.CreatedCustomers[1].Code);
            Assert.Equal("B001-C000003", result.CreatedCustomers[2].Code);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 8. منع تكرار العملة لنفس الطرف داخل نفس المجموعة (Scenario 8)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario08_PreventDuplicateCurrency_WithinSameGroup()
        {
            var req = new CreateCustomerGroupRequestDto
            {
                BaseCustomer = new Customer
                {
                    BranchId = 1,
                    NameAr = "شركة التكرار",
                    CustomerCategoryId = _custCat.Id,
                    FinancialAccountId = _custControlAcc.Id,
                    IsActive = true
                },
                // Intentionally passing duplicate USD currency IDs
                CustomerCurrencyIds = new List<int> { _usd.Id, _usd.Id }
            };

            var result = await _partyGroupService.CreateCustomerGroupAsync(req);

            Assert.False(result.Success);
            Assert.Contains("لا يمكن تكرار نفس العملة", result.Message);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 9. ربط عميل قديم غير مرتبط بعميل آخر يتبع مجموعة جديدة تلقائياً (Scenario 9)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario09_LinkExistingLegacyCustomer_ToAnotherCustomer()
        {
            var cust1 = new Customer
            {
                BranchId = 1,
                Code = "B001-C000010",
                NameAr = "عميل قديم 1 - مصري",
                NameEn = "Old Customer 1",
                CustomerCategoryId = _custCat.Id,
                FinancialAccountId = _custControlAcc.Id,
                CurrencyId = _egp.Id,
                PartyGroupId = null // Legacy unlinked
            };
            var cust2 = new Customer
            {
                BranchId = 1,
                Code = "B001-C000020",
                NameAr = "عميل قديم 2 - دولار",
                NameEn = "Old Customer 2",
                CustomerCategoryId = _custCat.Id,
                FinancialAccountId = _custControlAcc.Id,
                CurrencyId = _usd.Id,
                PartyGroupId = null // Legacy unlinked
            };
            _db.Customers.AddRange(cust1, cust2);
            await _db.SaveChangesAsync();

            var linkReq = new LinkPartyRecordRequestDto
            {
                SourcePartyType = "Customer",
                SourcePartyId = cust1.Id,
                TargetPartyType = "Customer",
                TargetPartyId = cust2.Id
            };

            var linkRes = await _partyGroupService.LinkRecordsAsync(linkReq);

            Assert.True(linkRes.Success);
            Assert.NotNull(linkRes.PartyGroupId);

            var reloaded1 = await _db.Customers.FindAsync(cust1.Id);
            var reloaded2 = await _db.Customers.FindAsync(cust2.Id);

            Assert.Equal(reloaded1!.PartyGroupId, reloaded2!.PartyGroupId);
            Assert.Equal("B001-C000010", reloaded1.Code); // Unchanged code
            Assert.Equal("B001-C000020", reloaded2.Code); // Unchanged code
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 10. ربط مورد قديم بمجموعة عميل قائمة (Scenario 10)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario10_LinkExistingLegacyVendor_ToCustomerGroup()
        {
            var group = new PartyGroup { Code = "PG-000055", NameAr = "مجموعة الشروق", NameEn = "Al-Shorouk Group", BranchId = 1 };
            _db.PartyGroups.Add(group);
            await _db.SaveChangesAsync();

            var cust = new Customer
            {
                BranchId = 1,
                Code = "B001-C000030",
                NameAr = "عميل الشروق",
                CustomerCategoryId = _custCat.Id,
                FinancialAccountId = _custControlAcc.Id,
                CurrencyId = _egp.Id,
                PartyGroupId = group.Id
            };

            var vend = new Vendor
            {
                BranchId = 1,
                Code = "B001-V000030",
                NameAr = "مورد الشروق للتوريدات",
                VendorCategoryId = _vendCat.Id,
                FinancialAccountId = _vendControlAcc.Id,
                CurrencyId = _usd.Id,
                PartyGroupId = null // Not linked yet
            };

            _db.Customers.Add(cust);
            _db.Vendors.Add(vend);
            await _db.SaveChangesAsync();

            var linkReq = new LinkPartyRecordRequestDto
            {
                SourcePartyType = "Customer",
                SourcePartyId = cust.Id,
                TargetPartyType = "Vendor",
                TargetPartyId = vend.Id
            };

            var linkRes = await _partyGroupService.LinkRecordsAsync(linkReq);

            Assert.True(linkRes.Success);

            var reloadedVend = await _db.Vendors.FindAsync(vend.Id);
            Assert.Equal(group.Id, reloadedVend!.PartyGroupId);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 11. فك ارتباط سجل دون حذف السجل نفسه (Scenario 11)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario11_UnlinkRecord_WithoutDeletingTheRecordItself()
        {
            var group = new PartyGroup { Code = "PG-000077", NameAr = "مجموعة السلام", NameEn = "Al-Salam Group", BranchId = 1 };
            _db.PartyGroups.Add(group);
            await _db.SaveChangesAsync();

            var cust = new Customer
            {
                BranchId = 1,
                Code = "B001-C000040",
                NameAr = "عميل السلام",
                CustomerCategoryId = _custCat.Id,
                FinancialAccountId = _custControlAcc.Id,
                PartyGroupId = group.Id
            };
            _db.Customers.Add(cust);
            await _db.SaveChangesAsync();

            var unlinkRes = await _partyGroupService.UnlinkRecordAsync("Customer", cust.Id);

            Assert.True(unlinkRes.Success);

            var reloadedCust = await _db.Customers.FindAsync(cust.Id);
            Assert.NotNull(reloadedCust); // Record NOT deleted
            Assert.Null(reloadedCust.PartyGroupId); // Only PartyGroupId unlinked
            Assert.Equal("B001-C000040", reloadedCust.Code);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 12. التحقق من عدم تأثر الحركات والأرصدة السابقة بالربط وفك الربط (Scenario 12)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario12_HistoricalBalancesAndTransactions_UntouchedByLinkingAndUnlinking()
        {
            var cust = new Customer
            {
                BranchId = 1,
                Code = "B001-C000050",
                NameAr = "عميل الحركات المالية",
                CustomerCategoryId = _custCat.Id,
                FinancialAccountId = _custControlAcc.Id,
                CurrencyId = _usd.Id
            };
            _db.Customers.Add(cust);
            await _db.SaveChangesAsync();

            // Post a journal entry for this customer
            var header = new JournalEntryHeader
            {
                BranchId = 1,
                EntryNumber = "JV-001",
                EntryDate = DateTime.UtcNow,
                Status = JournalEntryStatus.Posted
            };
            _db.JournalEntryHeaders.Add(header);
            await _db.SaveChangesAsync();

            var line1 = new JournalEntryLine
            {
                JournalEntryHeaderId = header.Id,
                LineNumber = 1,
                AccountId = _custControlAcc.Id,
                SubLedgerType = SubLedgerType.Customer,
                SubLedgerId = cust.Id,
                CurrencyId = _usd.Id,
                ExchangeRate = 50.0m,
                DebitForeign = 500.0m,
                DebitLocal = 25000.0m
            };
            var line2 = new JournalEntryLine
            {
                JournalEntryHeaderId = header.Id,
                LineNumber = 2,
                AccountId = _custControlAcc.Id,
                SubLedgerType = SubLedgerType.Customer,
                SubLedgerId = cust.Id,
                CurrencyId = _usd.Id,
                ExchangeRate = 50.0m,
                CreditForeign = 200.0m,
                CreditLocal = 10000.0m
            };
            _db.JournalEntryLines.AddRange(line1, line2);
            await _db.SaveChangesAsync();

            // 1. Check details before link
            var detailsBefore = await _partyGroupService.GetPartyGroupDetailsAsync("Customer", cust.Id);
            Assert.Single(detailsBefore.Records);
            Assert.Equal(300.0m, detailsBefore.Records[0].BalanceInCurrency); // 500 - 200 = 300 USD

            // 2. Link to a group
            var newGroup = new PartyGroup { Code = "PG-BAL-01", NameAr = "مجموعة الأرصدة", NameEn = "Balance Group", BranchId = 1 };
            _db.PartyGroups.Add(newGroup);
            cust.PartyGroupId = newGroup.Id;
            await _db.SaveChangesAsync();

            var detailsAfterLink = await _partyGroupService.GetPartyGroupDetailsAsync("Customer", cust.Id);
            Assert.Equal(300.0m, detailsAfterLink.Records.First(r => r.PartyId == cust.Id).BalanceInCurrency);

            // 3. Unlink
            await _partyGroupService.UnlinkRecordAsync("Customer", cust.Id);
            var detailsAfterUnlink = await _partyGroupService.GetPartyGroupDetailsAsync("Customer", cust.Id);
            Assert.Equal(300.0m, detailsAfterUnlink.Records.First(r => r.PartyId == cust.Id).BalanceInCurrency);

            // Verify journal entries lines count remained intact
            var totalLinesInDb = await _db.JournalEntryLines.CountAsync(l => l.SubLedgerId == cust.Id);
            Assert.Equal(2, totalLinesInDb);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 13. البحث وظهور جميع السجلات المرتبطة بالمجموعة في شاشة العرض والبحث (Scenario 13)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario13_SearchAndGroupQueries_ReturnAllLinkedRecords()
        {
            var group = new PartyGroup { Code = "PG-SEARCH-99", NameAr = "مجموعة البحث الموحد", NameEn = "Unified Search Group", BranchId = 1 };
            _db.PartyGroups.Add(group);
            await _db.SaveChangesAsync();

            var custEgp = new Customer
            {
                BranchId = 1,
                Code = "B001-C000091",
                NameAr = "شركة البحث - مصري",
                CustomerCategoryId = _custCat.Id,
                FinancialAccountId = _custControlAcc.Id,
                CurrencyId = _egp.Id,
                PartyGroupId = group.Id
            };
            var custUsd = new Customer
            {
                BranchId = 1,
                Code = "B001-C000092",
                NameAr = "شركة البحث - دولار",
                CustomerCategoryId = _custCat.Id,
                FinancialAccountId = _custControlAcc.Id,
                CurrencyId = _usd.Id,
                PartyGroupId = group.Id
            };
            _db.Customers.AddRange(custEgp, custUsd);
            await _db.SaveChangesAsync();

            var controller = new CustomerController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService, _partyGroupService)
            {
                ControllerContext = CreateControllerContext()
            };

            // Search by group code "PG-SEARCH-99" -> should return both linked customers
            var viewResult = await controller.Index(searchString: "PG-SEARCH-99", branchId: 1, categoryId: null);
            var view = Assert.IsType<ViewResult>(viewResult);
            var modelList = Assert.IsAssignableFrom<IEnumerable<Customer>>(view.Model).ToList();

            Assert.Equal(2, modelList.Count);
            Assert.Contains(modelList, c => c.Code == "B001-C000091");
            Assert.Contains(modelList, c => c.Code == "B001-C000092");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 14. اختبار عزل الفروع وصلاحيات المستخدمين (Scenario 14)
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Scenario14_BranchIsolationAndPermissions_Enforced()
        {
            // 1. Without MasterData.Vendors.Add permission -> cannot create linked vendor
            _userContextMock.Setup(u => u.HasPermission("MasterData.Vendors.Add")).Returns(false);

            var reqWithVendor = new CreateCustomerGroupRequestDto
            {
                BaseCustomer = new Customer
                {
                    BranchId = 1,
                    NameAr = "عميل بدون صلاحية مورد",
                    CustomerCategoryId = _custCat.Id,
                    FinancialAccountId = _custControlAcc.Id
                },
                CustomerCurrencyIds = new List<int> { _egp.Id },
                CreateLinkedVendor = true
            };

            var resVendorDenied = await _partyGroupService.CreateCustomerGroupAsync(reqWithVendor);
            Assert.False(resVendorDenied.Success);
            Assert.Contains("MasterData.Vendors.Add", resVendorDenied.Message);

            // Reset permission
            _userContextMock.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(true);

            // 2. Cannot link records from different branches
            var custBranch1 = new Customer
            {
                BranchId = 1,
                Code = "B001-C000095",
                NameAr = "عميل فرع 1",
                CustomerCategoryId = _custCat.Id,
                FinancialAccountId = _custControlAcc.Id,
                CurrencyId = _egp.Id
            };
            var custBranch2 = new Customer
            {
                BranchId = 2,
                Code = "B002-C000095",
                NameAr = "عميل فرع 2",
                CustomerCategoryId = _custCat.Id,
                FinancialAccountId = _custControlAcc.Id,
                CurrencyId = _usd.Id
            };
            _db.Customers.AddRange(custBranch1, custBranch2);
            await _db.SaveChangesAsync();

            var linkCrossBranchReq = new LinkPartyRecordRequestDto
            {
                SourcePartyType = "Customer",
                SourcePartyId = custBranch1.Id,
                TargetPartyType = "Customer",
                TargetPartyId = custBranch2.Id
            };

            var crossBranchRes = await _partyGroupService.LinkRecordsAsync(linkCrossBranchReq);
            Assert.False(crossBranchRes.Success);
            Assert.Contains("لا يمكن ربط سجلات تتبع فروعًا مختلفة", crossBranchRes.Message);
        }
    }
}
