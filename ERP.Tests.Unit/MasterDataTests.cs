using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using DomainUnit = ERP.Domain.Entities.Unit;
using ERP.Infrastructure.Data;
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

        public MasterDataTests()
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new ErpDbContext(options);
            _userContextMock = new Mock<ICurrentUserContext>();
            _auditMock = new Mock<IAuditService>();

            // Setup default user context: Branch 1, Company 1, Headquarter
            _userContextMock.Setup(u => u.UserId).Returns("user-test-1");
            _userContextMock.Setup(u => u.Username).Returns("testadmin");
            _userContextMock.Setup(u => u.BranchId).Returns(1);
            _userContextMock.Setup(u => u.CompanyId).Returns(1);
            _userContextMock.Setup(u => u.HasAllBranches).Returns(true);
            _userContextMock.Setup(u => u.CanAccessBranch(It.IsAny<int>())).Returns((int bId) => bId == 1 || bId == 2);
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
            _db.ItemGroups.Add(group);
            await _db.SaveChangesAsync();

            var controller = new ItemController(_db, _userContextMock.Object, _auditMock.Object)
            {
                ControllerContext = CreateControllerContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
            };

            // First item
            var item1 = new Item { Code = "ITM-UNIQUE", NameAr = "صنف 1", NameEn = "Item 1", ItemGroupId = group.Id };
            var res1 = await controller.Create(item1, null, null, null);
            var redir1 = Assert.IsType<RedirectToActionResult>(res1);
            Assert.Equal("Index", redir1.ActionName);

            // Duplicate item code
            var item2 = new Item { Code = "ITM-UNIQUE", NameAr = "صنف 2", NameEn = "Item 2", ItemGroupId = group.Id };
            var res2 = await controller.Create(item2, null, null, null);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("Code"));
        }

        [Fact]
        public async Task Currency_CodeIsGloballyUnique()
        {
            var controller = new CurrencyController(_db, _userContextMock.Object, _auditMock.Object)
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
            var controller = new UnitController(_db, _userContextMock.Object, _auditMock.Object)
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
            var controller = new WarehouseController(_db, _userContextMock.Object, _auditMock.Object)
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
            var cat = new CustomerCategory { Code = "CAT-01", NameAr = "فئة", NameEn = "Category" };
            var gov = new Governorate { Code = "CAI", NameAr = "القاهرة", NameEn = "Cairo" };
            _db.CustomerCategories.Add(cat);
            _db.Governorates.Add(gov);
            await _db.SaveChangesAsync();

            var city = new City { Code = "NASR", GovernorateId = gov.Id, NameAr = "مدينة نصر", NameEn = "Nasr City" };
            _db.Cities.Add(city);
            await _db.SaveChangesAsync();

            var controller = new CustomerController(_db, _userContextMock.Object, _auditMock.Object)
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
                CustomerCategoryId = cat.Id,
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
                CustomerCategoryId = cat.Id,
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
                CustomerCategoryId = cat.Id,
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
            // Restrict user to branch 1 only
            _userContextMock.Setup(u => u.CanAccessBranch(3)).Returns(false);

            var cat = new CustomerCategory { Code = "CAT-SEC", NameAr = "فئة", NameEn = "Category" };
            _db.CustomerCategories.Add(cat);
            await _db.SaveChangesAsync();

            var controller = new CustomerController(_db, _userContextMock.Object, _auditMock.Object)
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
            var cat = new CustomerCategory { Code = "CAT-SM", NameAr = "فئة", NameEn = "Category" };
            var smBranch2 = new Salesman { BranchId = 2, Code = "SM-02", NameAr = "مندوب فرع 2", NameEn = "Salesman Branch 2" };
            _db.CustomerCategories.Add(cat);
            _db.Salesmen.Add(smBranch2);
            await _db.SaveChangesAsync();

            var controller = new CustomerController(_db, _userContextMock.Object, _auditMock.Object)
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
            var cat = new CustomerCategory { Code = "CAT-GOV", NameAr = "فئة", NameEn = "Category" };
            var govCairo = new Governorate { Code = "CAI-T", NameAr = "القاهرة", NameEn = "Cairo" };
            var govAlex = new Governorate { Code = "ALX-T", NameAr = "الإسكندرية", NameEn = "Alexandria" };
            _db.CustomerCategories.Add(cat);
            _db.Governorates.AddRange(govCairo, govAlex);
            await _db.SaveChangesAsync();

            var cityAlex = new City { Code = "SMRH", GovernorateId = govAlex.Id, NameAr = "سموحة", NameEn = "Smouha" };
            _db.Cities.Add(cityAlex);
            await _db.SaveChangesAsync();

            var controller = new CustomerController(_db, _userContextMock.Object, _auditMock.Object)
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
            var controller = new WarehouseController(_db, _userContextMock.Object, _auditMock.Object)
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
    }
}
