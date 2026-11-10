using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using DomainUnit = ERP.Domain.Entities.Unit;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using ERP.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ERP.Tests.Unit
{
    public class MasterDataTests : IDisposable
    {
        private readonly ErpDbContext _db;
        private readonly Mock<ICurrentUserContext> _userContextMock;
        private readonly Mock<IAuditService> _auditMock;
        private readonly ICodeSequenceService _sequenceService;
        private readonly IPartyGroupService _partyGroupService;

        public MasterDataTests()
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new ErpDbContext(options);
            _userContextMock = new Mock<ICurrentUserContext>();
            _auditMock = new Mock<IAuditService>();
            _sequenceService = new CodeSequenceService(_db);
            _partyGroupService = new PartyGroupService(_db, _sequenceService, _userContextMock.Object, _auditMock.Object);

            // Setup default user context: Branch 1, Company 1, Headquarter
            _userContextMock.Setup(u => u.UserId).Returns("user-test-1");
            _userContextMock.Setup(u => u.Username).Returns("testadmin");
            _userContextMock.Setup(u => u.BranchId).Returns(1);
            _userContextMock.Setup(u => u.CompanyId).Returns(1);
            _userContextMock.Setup(u => u.HasAllBranches).Returns(true);
            _userContextMock.Setup(u => u.CanAccessBranch(It.IsAny<int>())).Returns((int bId) => bId == 1 || bId == 2);
            _userContextMock.Setup(u => u.CanAccessCompany(It.IsAny<int>())).Returns(true);

            // Seed Branches 1 and 2
            _db.Branches.AddRange(
                new Branch { Id = 1, Code = "CAI", BranchCode = "CAI", Name = "Cairo", NameAr = "فرع القاهرة", NameEn = "Cairo Branch", CompanyId = 1, IsActive = true },
                new Branch { Id = 2, Code = "GIZ", BranchCode = "GIZ", Name = "Giza", NameAr = "فرع الجيزة", NameEn = "Giza Branch", CompanyId = 1, IsActive = true }
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
            var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
            return new ControllerContext
            {
                HttpContext = httpContext
            };
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 1. Structural Verification: Item has NO BranchId, NO WarehouseLocationId
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public void Item_HasNoBranchId_And_NoWarehouseLocationId()
        {
            var itemType = typeof(Item);

            var branchIdProp = itemType.GetProperty("BranchId");
            var warehouseLocProp = itemType.GetProperty("WarehouseLocationId");
            var storageLocProp = itemType.GetProperty("StorageLocation");

            Assert.Null(branchIdProp);
            Assert.Null(warehouseLocProp);
            Assert.NotNull(storageLocProp);
            Assert.Equal(typeof(string), storageLocProp.PropertyType);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 2. Global Uniqueness Tests
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Item_CodeIsGloballyUnique()
        {
            var group = new ItemGroup { Code = "GRP-01", NameAr = "مجموعة", NameEn = "Group" };
            var unit = new DomainUnit { Code = "UNT-01", NameAr = "وحدة", NameEn = "Unit" };
            _db.ItemGroups.Add(group);
            _db.Units.Add(unit);
            await _db.SaveChangesAsync();

            var controller = new ItemController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // First item
            var item1 = new Item { Code = "ITM-UNIQUE", NameAr = "صنف 1", NameEn = "Item 1", ItemGroupId = group.Id };
            var res1 = await controller.Create(item1, unit.Id, null, null);
            var redir1 = Assert.IsType<RedirectToActionResult>(res1);
            Assert.Equal("Index", redir1.ActionName);

            // Duplicate item code
            var item2 = new Item { Code = "ITM-UNIQUE", NameAr = "صنف 2", NameEn = "Item 2", ItemGroupId = group.Id };
            var res2 = await controller.Create(item2, unit.Id, null, null);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("Code"));
        }

        [Fact]
        public async Task Currency_CodeIsGloballyUnique()
        {
            var controller = new CurrencyController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            var cur1 = new Currency { Code = "EGP", NameAr = "جنيه", NameEn = "Pound", ExchangeRate = 1.0m };
            await controller.Create(cur1);

            var cur2 = new Currency { Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", ExchangeRate = 1.0m };
            await controller.Create(cur2);

            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("Code"));
        }

        [Fact]
        public async Task Unit_CodeIsGloballyUnique()
        {
            var controller = new UnitController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            var u1 = new DomainUnit { Code = "PCS", NameAr = "قطعة", NameEn = "Piece" };
            await controller.Create(u1);

            var u2 = new DomainUnit { Code = "PCS", NameAr = "قطعة ثانية", NameEn = "Piece 2" };
            await controller.Create(u2);

            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("Code"));
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 3. Branch-Based Isolation & Composite Uniqueness Tests
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Warehouse_AllowsSameCodeInDifferentBranches_RejectsSameBranchDuplicate()
        {
            var controller = new WarehouseController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // Branch 1 - WH-01
            var wh1 = new Warehouse { BranchId = 1, Code = "WH-01", NameAr = "مخزن 1 فرع 1", NameEn = "WH 1 Branch 1" };
            var res1 = await controller.Create(wh1);
            Assert.IsType<RedirectToActionResult>(res1);

            // Branch 2 - WH-01 (Same code, different branch: ALLOWED)
            var wh2 = new Warehouse { BranchId = 2, Code = "WH-01", NameAr = "مخزن 1 فرع 2", NameEn = "WH 1 Branch 2" };
            var res2 = await controller.Create(wh2);
            Assert.IsType<RedirectToActionResult>(res2);

            // Branch 1 - WH-01 (Duplicate within same branch: REJECTED)
            var wh3 = new Warehouse { BranchId = 1, Code = "WH-01", NameAr = "مخزن مكرر", NameEn = "Duplicate WH" };
            await controller.Create(wh3);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("Code"));
        }

        [Fact]
        public async Task Customer_AllowsSameCodeInDifferentBranches_RejectsSameBranchDuplicate()
        {
            var cat1 = new CustomerCategory { BranchId = 1, Code = "CAT-01", NameAr = "فئة فرع 1", NameEn = "Category B1" };
            var cat2 = new CustomerCategory { BranchId = 2, Code = "CAT-02", NameAr = "فئة فرع 2", NameEn = "Category B2" };
            var gov = new Governorate { Code = "CAI", NameAr = "القاهرة", NameEn = "Cairo" };
            _db.CustomerCategories.AddRange(cat1, cat2);
            _db.Governorates.Add(gov);
            await _db.SaveChangesAsync();

            var city = new City { Code = "NASR", GovernorateId = gov.Id, NameAr = "مدينة نصر", NameEn = "Nasr City" };
            _db.Cities.Add(city);
            await _db.SaveChangesAsync();

            var controller = new CustomerController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService, _partyGroupService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // Branch 1
            var cus1 = new Customer
            {
                BranchId = 1,
                Code = "CUS-001",
                NameAr = "عميل 1",
                NameEn = "Customer 1",
                CustomerCategoryId = cat1.Id,
                GovernorateId = gov.Id,
                CityId = city.Id
            };
            var res1 = await controller.Create(cus1);
            Assert.IsType<RedirectToActionResult>(res1);

            // Branch 2 (same code: ALLOWED)
            var cus2 = new Customer
            {
                BranchId = 2,
                Code = "CUS-001",
                NameAr = "عميل 2 فرع 2",
                NameEn = "Customer 2 Branch 2",
                CustomerCategoryId = cat2.Id,
                GovernorateId = gov.Id,
                CityId = city.Id
            };
            var res2 = await controller.Create(cus2);
            Assert.IsType<RedirectToActionResult>(res2);

            // Branch 1 again (same code, same branch: REJECTED)
            var cus3 = new Customer
            {
                BranchId = 1,
                Code = "CUS-001",
                NameAr = "عميل مكرر",
                NameEn = "Duplicate Customer",
                CustomerCategoryId = cat1.Id,
                GovernorateId = gov.Id,
                CityId = city.Id
            };
            await controller.Create(cus3);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("Code"));
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 4. Branch Security Isolation Tests
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Customer_CannotBeCreatedInUnauthorizedBranch()
        {
            var branch3 = new Branch { Id = 3, Code = "ALX", BranchCode = "ALX", Name = "Alex", NameAr = "الإسكندرية", NameEn = "Alexandria", CompanyId = 1, IsActive = true };
            _db.Branches.Add(branch3);

            var cat = new CustomerCategory { BranchId = 3, Code = "CAT-SEC", NameAr = "فئة", NameEn = "Category" };
            _db.CustomerCategories.Add(cat);
            await _db.SaveChangesAsync();

            // Restrict user to branch 1 only
            _userContextMock.Setup(u => u.CanAccessBranch(3)).Returns(false);

            var controller = new CustomerController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService, _partyGroupService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            var cus = new Customer
            {
                BranchId = 3, // Unauthorized branch
                Code = "CUS-SEC-01",
                NameAr = "عميل محظور",
                NameEn = "Forbidden Customer",
                CustomerCategoryId = cat.Id
            };

            await controller.Create(cus);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("BranchId"));
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 5. Cross-Entity Consistency Validations
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Customer_RejectsSalesmanFromDifferentBranch()
        {
            var cat = new CustomerCategory { BranchId = 1, Code = "CAT-SM", NameAr = "فئة", NameEn = "Category" };
            var smBranch2 = new Salesman { BranchId = 2, Code = "SM-02", NameAr = "مندوب فرع 2", NameEn = "Salesman Branch 2" };
            _db.CustomerCategories.Add(cat);
            _db.Salesmen.Add(smBranch2);
            await _db.SaveChangesAsync();

            var controller = new CustomerController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService, _partyGroupService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            var cus = new Customer
            {
                BranchId = 1, // Branch 1
                SalesmanId = smBranch2.Id, // Salesman belongs to Branch 2!
                Code = "CUS-SM-01",
                NameAr = "عميل بمندوب فرع مختلف",
                NameEn = "Customer with wrong salesman branch",
                CustomerCategoryId = cat.Id
            };

            await controller.Create(cus);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("SalesmanId"));
        }

        [Fact]
        public async Task Customer_RejectsCityNotBelongingToSelectedGovernorate()
        {
            var cat = new CustomerCategory { BranchId = 1, Code = "CAT-GOV", NameAr = "فئة", NameEn = "Category" };
            var govCairo = new Governorate { Code = "CAI-T", NameAr = "القاهرة", NameEn = "Cairo" };
            var govAlex = new Governorate { Code = "ALX-T", NameAr = "الإسكندرية", NameEn = "Alexandria" };
            _db.CustomerCategories.Add(cat);
            _db.Governorates.AddRange(govCairo, govAlex);
            await _db.SaveChangesAsync();

            var cityAlex = new City { Code = "SMRH", GovernorateId = govAlex.Id, NameAr = "سموحة", NameEn = "Smouha" };
            _db.Cities.Add(cityAlex);
            await _db.SaveChangesAsync();

            var controller = new CustomerController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService, _partyGroupService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            var cus = new Customer
            {
                BranchId = 1,
                Code = "CUS-CITY-01",
                NameAr = "عميل بمدينة غير تابعة للمحافظة",
                NameEn = "Customer with mismatched city",
                CustomerCategoryId = cat.Id,
                GovernorateId = govCairo.Id, // Cairo
                CityId = cityAlex.Id // Smouha (Alexandria!)
            };

            await controller.Create(cus);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("CityId"));
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 6. ItemUnit Business Rules
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task ItemUnit_RequiresPositiveConversionFactor()
        {
            var group = new ItemGroup { Code = "GRP-U", NameAr = "مجموعة", NameEn = "Group" };
            var unit = new DomainUnit { Code = "BOX", NameAr = "كرتونة", NameEn = "Box" };
            _db.ItemGroups.Add(group);
            _db.Units.Add(unit);
            await _db.SaveChangesAsync();

            var item = new Item { Code = "ITM-U", NameAr = "صنف", NameEn = "Item", ItemGroupId = group.Id };
            _db.Items.Add(item);
            await _db.SaveChangesAsync();

            var controller = new ItemUnitController(_db, _userContextMock.Object, _auditMock.Object)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            var itemUnit = new ItemUnit
            {
                ItemId = item.Id,
                UnitId = unit.Id,
                ConversionFactor = 0 // Invalid factor
            };

            await controller.Create(itemUnit);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("ConversionFactor"));
        }

        [Fact]
        public async Task ItemUnit_DefaultSalesExclusivity_ResetsPreviousDefault()
        {
            var group = new ItemGroup { Code = "GRP-DEF", NameAr = "مجموعة", NameEn = "Group" };
            var unit1 = new DomainUnit { Code = "PCS-D", NameAr = "قطعة", NameEn = "Piece" };
            var unit2 = new DomainUnit { Code = "BOX-D", NameAr = "كرتونة", NameEn = "Box" };
            _db.ItemGroups.Add(group);
            _db.Units.AddRange(unit1, unit2);
            await _db.SaveChangesAsync();

            var item = new Item { Code = "ITM-DEF", NameAr = "صنف افتراضي", NameEn = "Default Item", ItemGroupId = group.Id };
            _db.Items.Add(item);
            await _db.SaveChangesAsync();

            var controller = new ItemUnitController(_db, _userContextMock.Object, _auditMock.Object)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // First unit is default sales
            var iu1 = new ItemUnit
            {
                ItemId = item.Id,
                UnitId = unit1.Id,
                ConversionFactor = 1.0m,
                IsDefaultSales = true
            };
            await controller.Create(iu1);

            // Second unit also requested as default sales
            var iu2 = new ItemUnit
            {
                ItemId = item.Id,
                UnitId = unit2.Id,
                ConversionFactor = 12.0m,
                IsDefaultSales = true
            };
            await controller.Create(iu2);

            // Re-fetch from DB
            var dbIu1 = await _db.ItemUnits.FirstAsync(iu => iu.UnitId == unit1.Id && iu.ItemId == item.Id);
            var dbIu2 = await _db.ItemUnits.FirstAsync(iu => iu.UnitId == unit2.Id && iu.ItemId == item.Id);

            Assert.False(dbIu1.IsDefaultSales, "Previous default unit should be cleared");
            Assert.True(dbIu2.IsDefaultSales, "New unit should be set as default sales");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 7. Audit Service Verification
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Warehouse_CreateAndToggleStatus_LogsAuditEvents()
        {
            var controller = new WarehouseController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            var wh = new Warehouse { BranchId = 1, Code = "WH-AUDIT", NameAr = "مخزن تدقيق", NameEn = "Audit Warehouse" };
            await controller.Create(wh);

            _auditMock.Verify(a => a.LogCreateAsync(
                "WH001",
                "Warehouses",
                "MasterData",
                "Warehouse",
                wh.Id.ToString(),
                It.IsAny<object>(),
                null), Times.Once);

            await controller.ToggleStatus(wh.Id);

            _auditMock.Verify(a => a.LogUpdateAsync(
                "WH001",
                "Warehouses",
                "MasterData",
                "Warehouse",
                wh.Id.ToString(),
                It.IsAny<object>(),
                It.IsAny<object>(),
                null), Times.Once);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 8. Master Data Coding Standard: Concurrency, Formats & Branch Isolation
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task CodeSequence_HighConcurrency_GeneratesUniqueSequentialCodes()
        {
            var sharedDbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: sharedDbName)
                .Options;

            // Simulate 50 concurrent requests for Item codes (each with its own scoped DbContext as in production)
            int concurrentCount = 50;
            var tasks = new List<Task<string>>();

            for (int i = 0; i < concurrentCount; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    using var scopeDb = new ErpDbContext(options);
                    var service = new CodeSequenceService(scopeDb);
                    return await service.GetNextCodeAsync("Item");
                }));
            }

            var results = await Task.WhenAll(tasks);

            // Assert: Exactly 50 results
            Assert.Equal(concurrentCount, results.Length);

            // Assert: All 50 codes are distinct (NO duplicates under concurrency!)
            var distinctCodes = results.Distinct().ToList();
            Assert.Equal(concurrentCount, distinctCodes.Count);

            // Assert: Format is ITM000001 ... ITM000050
            Assert.All(results, code =>
            {
                Assert.StartsWith("ITM", code);
                Assert.Equal(9, code.Length); // ITM + 6 digits
                var numericPart = code.Substring(3);
                Assert.True(int.TryParse(numericPart, out int num));
                Assert.InRange(num, 1, 50);
            });
        }

        [Fact]
        public async Task CodeSequence_BranchIsolation_MaintainsSeparateCountersPerBranch()
        {
            // Branch 1 (CAI) generates two customer codes -> CAI-C000001, CAI-C000002
            var codeB1_1 = await _sequenceService.GetNextCodeAsync("Customer", branchId: 1);
            var codeB1_2 = await _sequenceService.GetNextCodeAsync("Customer", branchId: 1);

            // Branch 2 (GIZ) generates two customer codes -> GIZ-C000001, GIZ-C000002
            var codeB2_1 = await _sequenceService.GetNextCodeAsync("Customer", branchId: 2);
            var codeB2_2 = await _sequenceService.GetNextCodeAsync("Customer", branchId: 2);

            Assert.Equal("CAI-C000001", codeB1_1);
            Assert.Equal("CAI-C000002", codeB1_2);

            // Branch 2 starts at GIZ-C000001 independently!
            Assert.Equal("GIZ-C000001", codeB2_1);
            Assert.Equal("GIZ-C000002", codeB2_2);
        }

        [Fact]
        public async Task CodeSequence_GlobalEntity_MaintainsSingleGlobalSequence()
        {
            var code1 = await _sequenceService.GetNextCodeAsync("Governorate");
            var code2 = await _sequenceService.GetNextCodeAsync("Governorate");
            var code3 = await _sequenceService.GetNextCodeAsync("Currency");

            Assert.Equal("GOV000001", code1);
            Assert.Equal("GOV000002", code2);
            Assert.Equal("CUR000001", code3);
        }

        [Fact]
        public async Task Customer_Create_AutoGeneratesCode_WhenCodeIsEmpty()
        {
            var cat = new CustomerCategory { BranchId = 1, Code = "CAT-AUTO", NameAr = "فئة", NameEn = "Category" };
            _db.CustomerCategories.Add(cat);
            await _db.SaveChangesAsync();

            var controller = new CustomerController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService, _partyGroupService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // Customer created with empty code in Branch 1 -> should auto-generate CAI-C000001
            var cus = new Customer
            {
                BranchId = 1,
                Code = "", // empty
                NameAr = "عميل تلقائي الكود",
                NameEn = "Auto Code Customer",
                CustomerCategoryId = cat.Id
            };

            var res = await controller.Create(cus);
            Assert.IsType<RedirectToActionResult>(res);

            // Verify customer in DB has auto-generated code CAI-C000001
            var createdCustomer = await _db.Customers.FirstOrDefaultAsync(c => c.NameEn == "Auto Code Customer");
            Assert.NotNull(createdCustomer);
            Assert.Equal("CAI-C000001", createdCustomer.Code);
        }

        [Fact]
        public async Task Customer_Edit_LogsOldCodeAndNewCodeInAuditTrail()
        {
            var cat = new CustomerCategory { BranchId = 1, Code = "CAT-AUD", NameAr = "فئة", NameEn = "Category" };
            _db.CustomerCategories.Add(cat);
            await _db.SaveChangesAsync();

            var cus = new Customer
            {
                BranchId = 1,
                Code = "CAI-C000010",
                NameAr = "عميل تعديل",
                NameEn = "Edit Customer",
                CustomerCategoryId = cat.Id,
                CreatedAt = DateTime.UtcNow
            };
            _db.Customers.Add(cus);
            await _db.SaveChangesAsync();

            var controller = new CustomerController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService, _partyGroupService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            cus.Code = "CAI-C000099"; // modified code
            await controller.Edit(cus);

            // Verify audit logged update with OldCode and NewCode
            _auditMock.Verify(a => a.LogUpdateAsync(
                "CUS001",
                "Customers",
                "MasterData",
                "Customer",
                cus.Id.ToString(),
                It.Is<object>(o => o.ToString()!.Contains("OldCode")),
                It.Is<object>(o => o.ToString()!.Contains("NewCode")),
                null), Times.Once);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 9. New Requirements: BranchCode & Branch-Based Customer/Vendor Categories
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Branch_Create_AutomaticallyCreatesDefaultCustomerAndVendorCategories()
        {
            var controller = new BranchController(_db, _userContextMock.Object, _auditMock.Object)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            var newBranch = new Branch
            {
                BranchCode = "ALX",
                Code = "ALX",
                NameAr = "فرع الإسكندرية",
                NameEn = "Alexandria Branch",
                CompanyId = 1
            };

            var res = await controller.Create(newBranch);
            Assert.IsType<RedirectToActionResult>(res);

            // Verify Branch created
            var createdBranch = await _db.Branches.FirstOrDefaultAsync(b => b.BranchCode == "ALX");
            Assert.NotNull(createdBranch);
            Assert.Equal("ALX", createdBranch.BranchCode);

            // Verify Default Customer Category auto-created
            var defaultCustCat = await _db.CustomerCategories
                .FirstOrDefaultAsync(cc => cc.BranchId == createdBranch.Id && cc.IsDefault);
            Assert.NotNull(defaultCustCat);
            Assert.Equal("EGP-MAC", defaultCustCat.Code);
            Assert.Equal("عملاء مصري ماكينات", defaultCustCat.NameAr);
            Assert.True(defaultCustCat.IsDefault);

            // Verify Default Vendor Category auto-created
            var defaultVendCat = await _db.VendorCategories
                .FirstOrDefaultAsync(vc => vc.BranchId == createdBranch.Id && vc.IsDefault);
            Assert.NotNull(defaultVendCat);
            Assert.Equal("ALX", defaultVendCat.Code);
            Assert.Equal("فرع الإسكندرية", defaultVendCat.NameAr);
            Assert.Equal("Alexandria Branch", defaultVendCat.NameEn);
            Assert.True(defaultVendCat.IsDefault);
        }

        [Fact]
        public async Task Branch_BranchCode_IsRequiredAndMustBeUnique()
        {
            var controller = new BranchController(_db, _userContextMock.Object, _auditMock.Object)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // Missing BranchCode
            var branchEmptyCode = new Branch
            {
                BranchCode = "",
                NameAr = "فرع بدون كود",
                NameEn = "No Code Branch",
                CompanyId = 1
            };
            await controller.Create(branchEmptyCode);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("BranchCode"));

            // Duplicate BranchCode (CAI already exists)
            controller.ModelState.Clear();
            var branchDupCode = new Branch
            {
                BranchCode = "CAI",
                NameAr = "فرع مكرر",
                NameEn = "Duplicate Branch",
                CompanyId = 1
            };
            await controller.Create(branchDupCode);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("BranchCode"));
        }

        [Fact]
        public async Task CustomerCategory_DefaultCategoryCannotBeDeleted()
        {
            var defaultCat = new CustomerCategory
            {
                BranchId = 1,
                Code = "CAI-DEF",
                NameAr = "فئة القاهرة الافتراضية",
                NameEn = "Cairo Default Category",
                IsDefault = true,
                IsActive = true
            };
            _db.CustomerCategories.Add(defaultCat);
            await _db.SaveChangesAsync();

            var controller = new CustomerCategoryController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            var res = await controller.Delete(defaultCat.Id);
            Assert.IsType<RedirectToActionResult>(res);

            // Category must NOT be deleted
            var exists = await _db.CustomerCategories.AnyAsync(cc => cc.Id == defaultCat.Id);
            Assert.True(exists);
            Assert.NotNull(controller.TempData["ErrorMessage"]);
        }

        [Fact]
        public async Task VendorCategory_DefaultCategoryCannotBeDeleted()
        {
            var defaultCat = new VendorCategory
            {
                BranchId = 1,
                Code = "CAI-DEF",
                NameAr = "فئة موردين القاهرة الافتراضية",
                NameEn = "Cairo Default Vendor Category",
                IsDefault = true,
                IsActive = true
            };
            _db.VendorCategories.Add(defaultCat);
            await _db.SaveChangesAsync();

            var controller = new VendorCategoryController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            var res = await controller.Delete(defaultCat.Id);
            Assert.IsType<RedirectToActionResult>(res);

            // Category must NOT be deleted
            var exists = await _db.VendorCategories.AnyAsync(vc => vc.Id == defaultCat.Id);
            Assert.True(exists);
            Assert.NotNull(controller.TempData["ErrorMessage"]);
        }

        [Fact]
        public async Task Customer_CodeGeneration_FollowsBranchPattern()
        {
            // Cairo Branch (CAI)
            var cairoCust1 = await _sequenceService.GetNextCodeAsync("Customer", branchId: 1);
            var cairoCust2 = await _sequenceService.GetNextCodeAsync("Customer", branchId: 1);

            // Giza Branch (GIZ)
            var gizaCust1 = await _sequenceService.GetNextCodeAsync("Customer", branchId: 2);

            Assert.Equal("CAI-C000001", cairoCust1);
            Assert.Equal("CAI-C000002", cairoCust2);
            Assert.Equal("GIZ-C000001", gizaCust1);
        }

        [Fact]
        public async Task Vendor_CodeGeneration_FollowsBranchPattern()
        {
            // Cairo Branch (CAI)
            var cairoVen1 = await _sequenceService.GetNextCodeAsync("Vendor", branchId: 1);
            var cairoVen2 = await _sequenceService.GetNextCodeAsync("Vendor", branchId: 1);

            // Giza Branch (GIZ)
            var gizaVen1 = await _sequenceService.GetNextCodeAsync("Vendor", branchId: 2);

            Assert.Equal("CAI-V000001", cairoVen1);
            Assert.Equal("CAI-V000002", cairoVen2);
            Assert.Equal("GIZ-V000001", gizaVen1);
        }

        [Fact]
        public async Task Customer_RejectsCategoryFromDifferentBranch()
        {
            // Category belongs to Branch 2 (Giza)
            var catBranch2 = new CustomerCategory
            {
                BranchId = 2,
                Code = "GIZ-CAT",
                NameAr = "فئة الجيزة",
                NameEn = "Giza Category",
                IsActive = true
            };
            _db.CustomerCategories.Add(catBranch2);
            await _db.SaveChangesAsync();

            var controller = new CustomerController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService, _partyGroupService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // Attempt to create customer in Branch 1 using Branch 2's category
            var cus = new Customer
            {
                BranchId = 1, // Cairo
                CustomerCategoryId = catBranch2.Id, // Belongs to Giza!
                Code = "CAI-C000001",
                NameAr = "عميل تجربة",
                NameEn = "Test Customer"
            };

            await controller.Create(cus);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("CustomerCategoryId"));
        }

        [Fact]
        public async Task Vendor_RejectsCategoryFromDifferentBranch()
        {
            // Category belongs to Branch 2 (Giza)
            var catBranch2 = new VendorCategory
            {
                BranchId = 2,
                Code = "GIZ-VCAT",
                NameAr = "فئة موردين الجيزة",
                NameEn = "Giza Vendor Category",
                IsActive = true
            };
            _db.VendorCategories.Add(catBranch2);
            await _db.SaveChangesAsync();

            var controller = new VendorController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService, _partyGroupService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // Attempt to create vendor in Branch 1 using Branch 2's category
            var ven = new Vendor
            {
                BranchId = 1, // Cairo
                VendorCategoryId = catBranch2.Id, // Belongs to Giza!
                Code = "CAI-V000001",
                NameAr = "مورد تجربة",
                NameEn = "Test Vendor"
            };

            await controller.Create(ven);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("VendorCategoryId"));
        }

        [Fact]
        public async Task Branch_Edit_LogsOldBranchCodeAndNewBranchCodeInAuditTrail()
        {
            var branch = await _db.Branches.FindAsync(1);
            Assert.NotNull(branch);

            var controller = new BranchController(_db, _userContextMock.Object, _auditMock.Object)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            branch.BranchCode = "CAI-NEW";
            branch.Code = "CAI-NEW";
            await controller.Edit(branch);

            _auditMock.Verify(a => a.LogUpdateAsync(
                "BRN001",
                "Branches",
                "Organization",
                "Branch",
                branch.Id.ToString(),
                It.Is<object>(o => o.ToString()!.Contains("OldBranchCode")),
                It.Is<object>(o => o.ToString()!.Contains("NewBranchCode")),
                null), Times.Once);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 13. Auto Sequence Sync & Non-Duplication Tests
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Item_Sequence_SynchronizesWithExistingDbRecords_And_NeverDuplicates()
        {
            var group = new ItemGroup { Code = "GRP-SEQ", NameAr = "مجموعة التسلسل", NameEn = "Seq Group" };
            var unit = new DomainUnit { Code = "UNT-SEQ", NameAr = "وحدة تسلسل", NameEn = "Seq Unit" };
            _db.ItemGroups.Add(group);
            _db.Units.Add(unit);
            await _db.SaveChangesAsync();

            // Pre-seed items with codes ITM000001 and ITM000002 directly in DB
            // (simulating existing records before sequence service initialized or when sequence was out of sync)
            _db.Items.AddRange(
                new Item { Code = "ITM000001", NameAr = "صنف 1", NameEn = "Item 1", ItemGroupId = group.Id },
                new Item { Code = "ITM000002", NameAr = "صنف 2", NameEn = "Item 2", ItemGroupId = group.Id }
            );
            await _db.SaveChangesAsync();

            // 1. Sequence service must recognize ITM000001 and ITM000002 exist, and preview ITM000003
            var previewCode = await _sequenceService.PeekNextCodeAsync("Item");
            Assert.Equal("ITM000003", previewCode);

            var controller = new ItemController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // 2. Create item with isManualCode = false (simulating user submitting Add Item modal with auto code)
            // Even if client form sent the stale/initial code "ITM000001", controller should assign the fresh sequence code "ITM000003"
            var item3 = new Item { Code = "ITM000001", NameAr = "صنف 3", NameEn = "Item 3", ItemGroupId = group.Id };
            var res3 = await controller.Create(item3, unit.Id, null, null, isManualCode: false);
            var redir3 = Assert.IsType<RedirectToActionResult>(res3);
            Assert.Equal("Index", redir3.ActionName);

            // Assert item3 code in DB is ITM000003
            var savedItem3 = await _db.Items.FirstOrDefaultAsync(i => i.NameAr == "صنف 3");
            Assert.NotNull(savedItem3);
            Assert.Equal("ITM000003", savedItem3.Code);

            // 3. Next preview must now be ITM000004
            var nextPreview = await _sequenceService.PeekNextCodeAsync("Item");
            Assert.Equal("ITM000004", nextPreview);

            // 4. Create another item
            var item4 = new Item { Code = "ITM000004", NameAr = "صنف 4", NameEn = "Item 4", ItemGroupId = group.Id };
            var res4 = await controller.Create(item4, unit.Id, null, null, isManualCode: false);
            var redir4 = Assert.IsType<RedirectToActionResult>(res4);
            Assert.Equal("Index", redir4.ActionName);

            var savedItem4 = await _db.Items.FirstOrDefaultAsync(i => i.NameAr == "صنف 4");
            Assert.NotNull(savedItem4);
            Assert.Equal("ITM000004", savedItem4.Code);
        }

        [Fact]
        public async Task Item_ManualCode_IsRespected_WhenIsManualCodeTrue()
        {
            var group = new ItemGroup { Code = "GRP-MAN", NameAr = "مجموعة اليدوي", NameEn = "Manual Group" };
            var unit = new DomainUnit { Code = "UNT-MAN", NameAr = "وحدة يدوي", NameEn = "Manual Unit" };
            _db.ItemGroups.Add(group);
            _db.Units.Add(unit);
            await _db.SaveChangesAsync();

            var controller = new ItemController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            var customItem = new Item { Code = "CUSTOM-ITEM-999", NameAr = "صنف كود يدوي", NameEn = "Manual Code Item", ItemGroupId = group.Id };
            var res = await controller.Create(customItem, unit.Id, null, null, isManualCode: true);
            var redir = Assert.IsType<RedirectToActionResult>(res);
            Assert.Equal("Index", redir.ActionName);

            var saved = await _db.Items.FirstOrDefaultAsync(i => i.Code == "CUSTOM-ITEM-999");
            Assert.NotNull(saved);
            Assert.Equal("CUSTOM-ITEM-999", saved.Code);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 14. Phase 3 Critical Fixes Tests
        // ─────────────────────────────────────────────────────────────────────────────
        [Fact]
        public async Task Item_Barcode_AllowsEmpty_And_RejectsDuplicates()
        {
            var group = new ItemGroup { Code = "GRP-BC", NameAr = "مجموعة باركود", NameEn = "Barcode Group" };
            _db.ItemGroups.Add(group);
            var unit = new DomainUnit { Code = "UNT-BC", NameAr = "حبة", NameEn = "Piece" };
            _db.Units.Add(unit);
            await _db.SaveChangesAsync();

            var controller = new ItemController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // 1. Items with null or empty barcode should be accepted
            var itemNullBc = new Item { Code = "ITM-BC-01", NameAr = "صنف 1", NameEn = "Item 1", ItemGroupId = group.Id, Barcode = null };
            var resNull = await controller.Create(itemNullBc, unit.Id, null, null);
            Assert.IsType<RedirectToActionResult>(resNull);

            var itemEmptyBc = new Item { Code = "ITM-BC-02", NameAr = "صنف 2", NameEn = "Item 2", ItemGroupId = group.Id, Barcode = "   " };
            var resEmpty = await controller.Create(itemEmptyBc, unit.Id, null, null);
            Assert.IsType<RedirectToActionResult>(resEmpty);

            // 2. Item with valid barcode
            var itemWithBc = new Item { Code = "ITM-BC-03", NameAr = "صنف 3", NameEn = "Item 3", ItemGroupId = group.Id, Barcode = "6221234567890" };
            var resBc = await controller.Create(itemWithBc, unit.Id, null, null);
            Assert.IsType<RedirectToActionResult>(resBc);

            // 3. Attempting to create duplicate barcode
            var itemDupBc = new Item { Code = "ITM-BC-04", NameAr = "صنف 4", NameEn = "Item 4", ItemGroupId = group.Id, Barcode = "6221234567890" };
            var resDup = await controller.Create(itemDupBc, unit.Id, null, null);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("Barcode"));

            // 4. Attempting to edit itemNullBc to duplicate barcode
            controller.ModelState.Clear();
            itemNullBc.Barcode = "6221234567890";
            var resEditDup = await controller.Edit(itemNullBc, unit.Id, null, null);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("Barcode"));
        }

        [Fact]
        public async Task Item_RequiresGroupAndUnit_ToSave_OnCreateAndEdit()
        {
            var group = new ItemGroup { Code = "GRP-REQ", NameAr = "مجموعة مطلوبة", NameEn = "Req Group" };
            var unit = new DomainUnit { Code = "UNT-REQ", NameAr = "قطعة", NameEn = "Piece" };
            _db.ItemGroups.Add(group);
            _db.Units.Add(unit);
            await _db.SaveChangesAsync();

            var controller = new ItemController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // 1. Create without group should fail
            var itemNoGroup = new Item { Code = "ITM-NG", NameAr = "صنف بدون مجموعة", NameEn = "No Group Item", ItemGroupId = 0 };
            await controller.Create(itemNoGroup, unit.Id, null, null);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("ItemGroupId"));

            // 2. Create without unit should fail
            controller.ModelState.Clear();
            var itemNoUnit = new Item { Code = "ITM-NU", NameAr = "صنف بدون وحدة", NameEn = "No Unit Item", ItemGroupId = group.Id };
            await controller.Create(itemNoUnit, null, null, null);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("baseUnitId"));

            // 3. Create with group and unit (prices omitted) should succeed and default prices to 0
            controller.ModelState.Clear();
            var itemValid = new Item { Code = "ITM-OK", NameAr = "صنف صالح", NameEn = "Valid Item", ItemGroupId = group.Id };
            var createRes = await controller.Create(itemValid, unit.Id, null, null);
            Assert.IsType<RedirectToActionResult>(createRes);

            var createdItem = await _db.Items.Include(i => i.ItemUnits).FirstOrDefaultAsync(i => i.Code == "ITM-OK");
            Assert.NotNull(createdItem);
            Assert.Single(createdItem.ItemUnits);
            var baseIu = createdItem.ItemUnits.First();
            Assert.Equal(unit.Id, baseIu.UnitId);
            Assert.Equal(0m, baseIu.SalesPrice);
            Assert.Equal(0m, baseIu.PurchasePrice);
            Assert.Equal(1.0m, baseIu.ConversionFactor);

            // 4. Edit without unit should fail
            controller.ModelState.Clear();
            await controller.Edit(createdItem, null, 10m, 5m);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("baseUnitId"));

            // 5. Edit with new unit and prices should update base unit
            var unit2 = new DomainUnit { Code = "UNT-REQ2", NameAr = "كرتونة", NameEn = "Carton" };
            _db.Units.Add(unit2);
            await _db.SaveChangesAsync();

            controller.ModelState.Clear();
            var editRes = await controller.Edit(createdItem, unit2.Id, 150m, 120m);
            Assert.IsType<RedirectToActionResult>(editRes);

            var updatedItem = await _db.Items.Include(i => i.ItemUnits).FirstOrDefaultAsync(i => i.Id == createdItem.Id);
            Assert.NotNull(updatedItem);
            var updatedBaseIu = updatedItem.ItemUnits.FirstOrDefault(u => u.ConversionFactor == 1.0m);
            Assert.NotNull(updatedBaseIu);
            Assert.Equal(unit2.Id, updatedBaseIu.UnitId);
            Assert.Equal(150m, updatedBaseIu.SalesPrice);
            Assert.Equal(120m, updatedBaseIu.PurchasePrice);
        }

        [Fact]
        public async Task ItemGroup_CircularHierarchy_IsBlockedAtArbitraryDepth()
        {
            var controller = new ItemGroupController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // Setup hierarchy: Food -> Drinks -> Juice
            var food = new ItemGroup { Code = "GRP-FOOD", NameAr = "أغذية", NameEn = "Food", ParentId = null };
            _db.ItemGroups.Add(food);
            await _db.SaveChangesAsync();

            var drinks = new ItemGroup { Code = "GRP-DRINKS", NameAr = "مشروبات", NameEn = "Drinks", ParentId = food.Id };
            _db.ItemGroups.Add(drinks);
            await _db.SaveChangesAsync();

            var juice = new ItemGroup { Code = "GRP-JUICE", NameAr = "عصائر", NameEn = "Juice", ParentId = drinks.Id };
            _db.ItemGroups.Add(juice);
            await _db.SaveChangesAsync();

            // Attempt circular assignment: Set Food.ParentId = Juice.Id
            food.ParentId = juice.Id;
            var resCircular = await controller.Edit(food);

            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("ParentId"));

            // Self-referencing check: Set Juice.ParentId = Juice.Id
            controller.ModelState.Clear();
            juice.ParentId = juice.Id;
            var resSelf = await controller.Edit(juice);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("ParentId"));
        }

        [Fact]
        public async Task City_DeleteProtection_BlocksWhenReferencedByCustomer()
        {
            var gov = new Governorate { Code = "GOV-DEL", NameAr = "محافظة تجربة", NameEn = "Test Gov" };
            _db.Governorates.Add(gov);
            await _db.SaveChangesAsync();

            var city = new City { Code = "CIT-DEL", NameAr = "مدينة تجربة", NameEn = "Test City", GovernorateId = gov.Id };
            _db.Cities.Add(city);
            await _db.SaveChangesAsync();

            var cat = new CustomerCategory { BranchId = 1, Code = "CAT-CDEL", NameAr = "فئة", NameEn = "Cat" };
            _db.CustomerCategories.Add(cat);
            await _db.SaveChangesAsync();

            var cus = new Customer
            {
                BranchId = 1,
                Code = "CUS-CIT-DEL",
                NameAr = "عميل في المدينة",
                NameEn = "Customer In City",
                CustomerCategoryId = cat.Id,
                GovernorateId = gov.Id,
                CityId = city.Id
            };
            _db.Customers.Add(cus);
            await _db.SaveChangesAsync();

            var controller = new CityController(_db, _userContextMock.Object, _auditMock.Object, _sequenceService)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            var res = await controller.Delete(city.Id);
            Assert.IsType<RedirectToActionResult>(res);

            // Assert city was NOT deleted
            var exists = await _db.Cities.AnyAsync(c => c.Id == city.Id);
            Assert.True(exists);
            Assert.NotNull(controller.TempData["ErrorMessage"]);
        }

        [Fact]
        public async Task Customer_And_Vendor_Save_Mobile2_And_Notes_Correctly()
        {
            var custCat = new CustomerCategory { BranchId = 1, Code = "CC-TEST", NameAr = "فئة تجريبية", NameEn = "Test Cat" };
            var vendCat = new VendorCategory { BranchId = 1, Code = "VC-TEST", NameAr = "فئة موردين", NameEn = "Vend Cat" };
            _db.CustomerCategories.Add(custCat);
            _db.VendorCategories.Add(vendCat);
            await _db.SaveChangesAsync();

            // 1. Create Customer
            var customer = new Customer
            {
                BranchId = 1,
                Code = "CUST-CONT-01",
                NameAr = "عميل اتصالات",
                NameEn = "Contact Customer",
                CustomerCategoryId = custCat.Id,
                Mobile = "01000000001",
                Mobile2 = "01100000002",
                Email = "customer@example.com",
                Notes = "ملاحظات هامة للعميل"
            };
            _db.Customers.Add(customer);

            // 2. Create Vendor
            var vendor = new Vendor
            {
                BranchId = 1,
                Code = "VEND-CONT-01",
                NameAr = "مورد اتصالات",
                NameEn = "Contact Vendor",
                VendorCategoryId = vendCat.Id,
                Mobile = "01200000001",
                Mobile2 = "01500000002",
                Email = "vendor@example.com",
                Notes = "ملاحظات خاصة بالمورد"
            };
            _db.Vendors.Add(vendor);
            await _db.SaveChangesAsync();

            // Assert from DB
            var savedCust = await _db.Customers.FirstOrDefaultAsync(c => c.Code == "CUST-CONT-01");
            Assert.NotNull(savedCust);
            Assert.Equal("01000000001", savedCust.Mobile);
            Assert.Equal("01000000001", savedCust.Mobile1);
            Assert.Equal("01100000002", savedCust.Mobile2);
            Assert.Equal("customer@example.com", savedCust.Email);
            Assert.Equal("ملاحظات هامة للعميل", savedCust.Notes);

            var savedVend = await _db.Vendors.FirstOrDefaultAsync(v => v.Code == "VEND-CONT-01");
            Assert.NotNull(savedVend);
            Assert.Equal("01200000001", savedVend.Mobile);
            Assert.Equal("01200000001", savedVend.Mobile1);
            Assert.Equal("01500000002", savedVend.Mobile2);
            Assert.Equal("vendor@example.com", savedVend.Email);
            Assert.Equal("ملاحظات خاصة بالمورد", savedVend.Notes);
        }
    }
}
