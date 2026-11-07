using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs.Lookups;
using ERP.Application.DTOs.Reports;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using ERP.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using UnitEntity = ERP.Domain.Entities.Unit;

namespace ERP.Tests.Unit
{
    public class InventoryAvailabilityAndReportsTests
    {
        private ErpDbContext CreateDbContext(string dbName, int branchId = 0, bool isSuperAdmin = true)
        {
            var userMock = new Mock<ICurrentUserService>();
            userMock.Setup(u => u.BranchId).Returns(branchId);
            userMock.Setup(u => u.IsSuperAdmin).Returns(isSuperAdmin);
            userMock.Setup(u => u.IsAuthenticated).Returns(true);

            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new ErpDbContext(options, userMock.Object);
        }

        private Mock<ICurrentUserContext> CreateUserContextMock(int branchId = 1, bool hasAllBranches = true)
        {
            var mock = new Mock<ICurrentUserContext>();
            mock.Setup(u => u.BranchId).Returns(branchId);
            mock.Setup(u => u.HasAllBranches).Returns(hasAllBranches);
            mock.Setup(u => u.UserId).Returns("test-user");
            return mock;
        }

        private async Task SeedDataAsync(ErpDbContext context)
        {
            var branch = new Branch { Id = 1, Code = "BR-01", NameAr = "الفرع الرئيسي", NameEn = "Main Branch", IsActive = true };
            context.Branches.Add(branch);

            var wh = new Warehouse { Id = 1, BranchId = 1, Code = "WH-01", NameAr = "المستودع الرئيسي", NameEn = "Main WH", IsActive = true };
            context.Warehouses.Add(wh);

            var unitPcs = new UnitEntity { Id = 1, Code = "PCS", NameAr = "قطعة", NameEn = "Piece", IsActive = true };
            var unitBox = new UnitEntity { Id = 2, Code = "BOX", NameAr = "كرتونة", NameEn = "Box", IsActive = true };
            context.Units.AddRange(unitPcs, unitBox);

            var group = new ItemGroup { Id = 1, Code = "GRP-01", NameAr = "إلكترونيات", NameEn = "Electronics", IsActive = true };
            context.ItemGroups.Add(group);

            var currency = new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", IsActive = true };
            context.Currencies.Add(currency);

            var item = new Item
            {
                Id = 100,
                Code = "ITM-01",
                NameAr = "شاشة سامسونج",
                NameEn = "Samsung Monitor",
                TrackingType = ItemTrackingType.None,
                IsActive = true,
                ItemGroupId = 1,
                ItemGroup = group,
                ItemUnits = new List<ItemUnit>
                {
                    new ItemUnit { ItemId = 100, UnitId = 1, ConversionFactor = 1.0m, IsActive = true, IsDefaultSales = true },
                    new ItemUnit { ItemId = 100, UnitId = 2, ConversionFactor = 10.0m, IsActive = true }
                }
            };

            var serviceItem = new Item
            {
                Id = 200,
                Code = "SRV-01",
                NameAr = "خدمة صيانة",
                NameEn = "Maintenance Service",
                IsService = true,
                ItemGroupId = 1,
                ItemGroup = group,
                IsActive = true
            };

            context.Items.AddRange(item, serviceItem);

            var custCat = new CustomerCategory { Id = 1, Code = "CAT-01", NameAr = "فئة عامة", NameEn = "General Category", BranchId = 1, IsActive = true };
            var vendCat = new VendorCategory { Id = 1, Code = "VCAT-01", NameAr = "فئة عامة", NameEn = "General Category", BranchId = 1, IsActive = true };
            context.CustomerCategories.Add(custCat);
            context.VendorCategories.Add(vendCat);

            var cust = new Customer { Id = 10, Code = "CUST-01", NameAr = "عميل تجريبي", NameEn = "Test Customer", BranchId = 1, CustomerCategoryId = 1, IsActive = true };
            var vend = new Vendor { Id = 20, Code = "VEND-01", NameAr = "مورد رئيسي", NameEn = "Main Vendor", BranchId = 1, VendorCategoryId = 1, IsActive = true };
            context.Customers.Add(cust);
            context.Vendors.Add(vend);

            // Transaction Types
            var ttPurchase = new TransactionType
            {
                Id = 1,
                Code = "PINV",
                NameAr = "فاتورة مشتريات",
                NameEn = "Purchase Invoice",
                Module = TransactionModule.Purchase,
                BaseNature = DocumentBaseNature.Invoice,
                InventoryEffect = InventoryEffect.ReceiveStock
            };

            var ttSale = new TransactionType
            {
                Id = 2,
                Code = "SINV",
                NameAr = "فاتورة مبيعات",
                NameEn = "Sales Invoice",
                Module = TransactionModule.Sales,
                BaseNature = DocumentBaseNature.Invoice,
                InventoryEffect = InventoryEffect.IssueStock
            };

            var ttSaleOrder = new TransactionType
            {
                Id = 3,
                Code = "SORD",
                NameAr = "أمر بيع حجز",
                NameEn = "Sales Order",
                Module = TransactionModule.Sales,
                BaseNature = DocumentBaseNature.Order,
                InventoryEffect = InventoryEffect.ReserveStock
            };

            var ttSaleReturn = new TransactionType
            {
                Id = 4,
                Code = "SRET",
                NameAr = "مرتجع مبيعات",
                NameEn = "Sales Return",
                Module = TransactionModule.Sales,
                BaseNature = DocumentBaseNature.Return,
                InventoryEffect = InventoryEffect.ReceiveStock
            };

            context.TransactionTypes.AddRange(ttPurchase, ttSale, ttSaleOrder, ttSaleReturn);
            await context.SaveChangesAsync();

            // Document 1: Received 100 pcs via Purchase Invoice (Posted)
            var docReceipt = new DocumentHeader
            {
                Id = 1001,
                DocumentNumber = "PINV-001",
                TransactionTypeId = 1,
                TransactionType = ttPurchase,
                BranchId = 1,
                Branch = branch,
                WarehouseId = 1,
                Warehouse = wh,
                PartyType = DocumentPartyType.Vendor,
                PartyId = 20,
                DocumentDate = DateTime.Today.AddDays(-10),
                Status = DocumentStatus.Posted,
                InventoryEffect = InventoryEffect.ReceiveStock,
                TotalAmountBeforeTax = 100000m,
                TotalAmountNet = 100000m
            };

            var lineReceipt = new DocumentLine
            {
                Id = 1,
                DocumentHeaderId = 1001,
                DocumentHeader = docReceipt,
                ItemId = 100,
                Item = item,
                UnitId = 1,
                Unit = unitPcs,
                WarehouseId = 1,
                Quantity = 100m,
                ConversionRate = 1.0m,
                UnitPrice = 1000m,
                LineTotal = 100000m
            };
            docReceipt.Lines.Add(lineReceipt);

            // Document 2: Reserved 25 pcs via Sales Order (Posted, Unconverted)
            var docOrder = new DocumentHeader
            {
                Id = 1002,
                DocumentNumber = "SORD-001",
                TransactionTypeId = 3,
                TransactionType = ttSaleOrder,
                BranchId = 1,
                Branch = branch,
                WarehouseId = 1,
                Warehouse = wh,
                PartyType = DocumentPartyType.Customer,
                PartyId = 10,
                DocumentDate = DateTime.Today.AddDays(-5),
                Status = DocumentStatus.Posted,
                InventoryEffect = InventoryEffect.ReserveStock,
                ConversionStatus = DocumentConversionStatus.Unconverted,
                TotalAmountBeforeTax = 30000m,
                TotalAmountNet = 30000m
            };

            var lineOrder = new DocumentLine
            {
                Id = 2,
                DocumentHeaderId = 1002,
                DocumentHeader = docOrder,
                ItemId = 100,
                Item = item,
                UnitId = 1,
                Unit = unitPcs,
                WarehouseId = 1,
                Quantity = 25m,
                ConvertedQuantity = 0m,
                ConversionRate = 1.0m,
                UnitPrice = 1200m,
                LineTotal = 30000m
            };
            docOrder.Lines.Add(lineOrder);

            // Document 3: Sold 10 pcs via Sales Invoice (Posted)
            var docSale = new DocumentHeader
            {
                Id = 1003,
                DocumentNumber = "SINV-001",
                TransactionTypeId = 2,
                TransactionType = ttSale,
                BranchId = 1,
                Branch = branch,
                WarehouseId = 1,
                Warehouse = wh,
                PartyType = DocumentPartyType.Customer,
                PartyId = 10,
                DocumentDate = DateTime.Today.AddDays(-2),
                Status = DocumentStatus.Posted,
                InventoryEffect = InventoryEffect.IssueStock,
                TotalAmountBeforeTax = 12000m,
                TotalAmountNet = 12000m
            };

            var lineSale = new DocumentLine
            {
                Id = 3,
                DocumentHeaderId = 1003,
                DocumentHeader = docSale,
                ItemId = 100,
                Item = item,
                UnitId = 1,
                Unit = unitPcs,
                WarehouseId = 1,
                Quantity = 10m,
                ConversionRate = 1.0m,
                UnitPrice = 1200m,
                LineTotal = 12000m
            };
            docSale.Lines.Add(lineSale);

            context.DocumentHeaders.AddRange(docReceipt, docOrder, docSale);
            context.DocumentLines.AddRange(docReceipt.Lines);
            context.DocumentLines.AddRange(docOrder.Lines);
            context.DocumentLines.AddRange(docSale.Lines);
            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task InventoryAvailabilityService_CalculatesOnHandAndReservedCorrectly()
        {
            var db = CreateDbContext(nameof(InventoryAvailabilityService_CalculatesOnHandAndReservedCorrectly));
            await SeedDataAsync(db);
            var service = new InventoryAvailabilityService(db);

            // 100 received - 10 sold = 90 on-hand.
            // 25 reserved in open order.
            // Available = 90 - 25 = 65 pcs.
            var result = await service.GetAvailabilityAsync(
                itemId: 100,
                branchId: 1,
                warehouseId: 1,
                unitId: 1,
                requestedQuantity: 50m);

            Assert.Equal(90m, result.OnHandQuantity);
            Assert.Equal(25m, result.ReservedQuantity);
            Assert.Equal(65m, result.AvailableQuantity);
            Assert.True(result.IsAvailable);
            Assert.Equal(15m, result.RemainingAfterRequest);
        }

        [Fact]
        public async Task InventoryAvailabilityService_DetectsShortageWhenRequestExceedsAvailable()
        {
            var db = CreateDbContext(nameof(InventoryAvailabilityService_DetectsShortageWhenRequestExceedsAvailable));
            await SeedDataAsync(db);
            var service = new InventoryAvailabilityService(db);

            // Available is 65 pcs. Requesting 70 pcs.
            var result = await service.GetAvailabilityAsync(
                itemId: 100,
                branchId: 1,
                warehouseId: 1,
                unitId: 1,
                requestedQuantity: 70m);

            Assert.Equal(65m, result.AvailableQuantity);
            Assert.False(result.IsAvailable);
            Assert.Equal(-5m, result.RemainingAfterRequest);
            Assert.Contains("تتجاوز المتاح", result.Message);
        }

        [Fact]
        public async Task InventoryAvailabilityService_HandlesUnitConversionCorrectly()
        {
            var db = CreateDbContext(nameof(InventoryAvailabilityService_HandlesUnitConversionCorrectly));
            await SeedDataAsync(db);
            var service = new InventoryAvailabilityService(db);

            // 65 pcs available = 6.5 boxes (box = 10 pcs).
            // Requesting 6 boxes (60 pcs) -> Available (6.5 >= 6).
            var result6Boxes = await service.GetAvailabilityAsync(
                itemId: 100,
                branchId: 1,
                warehouseId: 1,
                unitId: 2, // BOX
                requestedQuantity: 6m);

            Assert.True(result6Boxes.IsAvailable);
            Assert.Equal(6.5m, result6Boxes.AvailableQuantity);
            Assert.Equal(0.5m, result6Boxes.RemainingAfterRequest);

            // Requesting 7 boxes (70 pcs) -> Deficit: RemainingAfterRequest is -0.5
            var result7Boxes = await service.GetAvailabilityAsync(
                itemId: 100,
                branchId: 1,
                warehouseId: 1,
                unitId: 2, // BOX
                requestedQuantity: 7m);

            Assert.False(result7Boxes.IsAvailable);
            Assert.Equal(-0.5m, result7Boxes.RemainingAfterRequest);
        }

        [Fact]
        public async Task InventoryAvailabilityService_AlwaysAllowsServiceItems()
        {
            var db = CreateDbContext(nameof(InventoryAvailabilityService_AlwaysAllowsServiceItems));
            await SeedDataAsync(db);
            var service = new InventoryAvailabilityService(db);

            var result = await service.GetAvailabilityAsync(
                itemId: 200, // Service item
                branchId: 1,
                warehouseId: 1,
                requestedQuantity: 500m);

            Assert.True(result.IsAvailable);
            Assert.True(result.IsService);
            Assert.True(result.RemainingAfterRequest > 0m);
        }

        [Fact]
        public async Task SalesReportService_GeneratesSalesSummaryAndDetails()
        {
            var db = CreateDbContext(nameof(SalesReportService_GeneratesSalesSummaryAndDetails));
            await SeedDataAsync(db);
            var userMock = CreateUserContextMock();
            var service = new SalesReportService(db, userMock.Object);

            var summary = await service.GetSalesSummaryReportAsync(new SalesReportFilterDto());
            Assert.NotNull(summary);
            Assert.Single(summary.Items);
            Assert.Equal(12000m, summary.GrandTotalSales);

            var details = await service.GetSalesInvoiceDetailsReportAsync(new SalesReportFilterDto());
            Assert.NotNull(details);
            Assert.Single(details.Items);
            Assert.Equal("SINV-001", details.Items[0].DocumentNumber);
            Assert.Equal(12000m, details.Items[0].TotalNet);
        }

        [Fact]
        public async Task PurchaseReportService_GeneratesPurchaseSummary()
        {
            var db = CreateDbContext(nameof(PurchaseReportService_GeneratesPurchaseSummary));
            await SeedDataAsync(db);
            var userMock = CreateUserContextMock();
            var service = new PurchaseReportService(db, userMock.Object);

            var summary = await service.GetPurchaseSummaryReportAsync(new PurchaseReportFilterDto());
            Assert.NotNull(summary);
            Assert.Single(summary.Items);
            Assert.Equal(100000m, summary.GrandTotalPurchases);
        }

        [Fact]
        public async Task InventoryReportService_GeneratesStockBalancesAndStockCard()
        {
            var db = CreateDbContext(nameof(InventoryReportService_GeneratesStockBalancesAndStockCard));
            await SeedDataAsync(db);
            var userMock = CreateUserContextMock();
            var service = new InventoryReportService(db, userMock.Object);

            // Stock balances report
            var balances = await service.GetStockBalancesReportAsync(new InventoryReportFilterDto { ItemId = 100 });
            Assert.NotNull(balances);
            Assert.Single(balances.Items);
            Assert.Equal(90m, balances.Items[0].OnHandQuantity);
            Assert.Equal(25m, balances.Items[0].ReservedQuantity);
            Assert.Equal(65m, balances.Items[0].AvailableQuantity);

            // Item Stock Card report (inward 100, outward 10, running balance 90)
            var card = await service.GetItemStockCardReportAsync(new InventoryReportFilterDto { ItemId = 100 });
            Assert.NotNull(card);
            Assert.Equal(2, card.Movements.Count);
            Assert.Equal(100m, card.TotalIn);
            Assert.Equal(10m, card.TotalOut);
            Assert.Equal(90m, card.Movements.Last().BalanceAfter);
        }

        [Fact]
        public async Task SalesReportsController_WhenNotSubmitted_DoesNotQueryService()
        {
            var db = CreateDbContext(nameof(SalesReportsController_WhenNotSubmitted_DoesNotQueryService));
            await SeedDataAsync(db);
            var userMock = CreateUserContextMock();
            var salesServiceMock = new Mock<ISalesReportService>();

            var controller = new SalesReportsController(salesServiceMock.Object, db, userMock.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            var result = await controller.SalesSummary(new SalesReportFilterDto());
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<SalesSummaryReportDto>(viewResult.Model);

            Assert.Empty(model.Items);
            Assert.False(controller.ViewBag.IsRequested);
            salesServiceMock.Verify(s => s.GetSalesSummaryReportAsync(It.IsAny<SalesReportFilterDto>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SalesReportsController_WhenSubmitted_QueriesService()
        {
            var db = CreateDbContext(nameof(SalesReportsController_WhenSubmitted_QueriesService));
            await SeedDataAsync(db);
            var userMock = CreateUserContextMock();
            var salesServiceMock = new Mock<ISalesReportService>();
            salesServiceMock.Setup(s => s.GetSalesSummaryReportAsync(It.IsAny<SalesReportFilterDto>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new SalesSummaryReportDto
                {
                    Items = new List<SalesSummaryReportItemDto>
                    {
                        new SalesSummaryReportItemDto { BranchName = "Main Branch", InvoiceCount = 1, TotalNetSales = 500 }
                    }
                });

            var httpContext = new DefaultHttpContext();
            httpContext.Request.QueryString = new QueryString("?submitted=true");

            var controller = new SalesReportsController(salesServiceMock.Object, db, userMock.Object);
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var result = await controller.SalesSummary(new SalesReportFilterDto());
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<SalesSummaryReportDto>(viewResult.Model);

            Assert.Single(model.Items);
            Assert.True(controller.ViewBag.IsRequested);
            salesServiceMock.Verify(s => s.GetSalesSummaryReportAsync(It.IsAny<SalesReportFilterDto>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PurchaseReportsController_WhenNotSubmitted_DoesNotQueryService()
        {
            var db = CreateDbContext(nameof(PurchaseReportsController_WhenNotSubmitted_DoesNotQueryService));
            await SeedDataAsync(db);
            var userMock = CreateUserContextMock();
            var purchaseServiceMock = new Mock<IPurchaseReportService>();

            var controller = new PurchaseReportsController(purchaseServiceMock.Object, db, userMock.Object);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            var result = await controller.PurchaseSummary(new PurchaseReportFilterDto());
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<PurchaseSummaryReportDto>(viewResult.Model);

            Assert.Empty(model.Items);
            Assert.False(controller.ViewBag.IsRequested);
            purchaseServiceMock.Verify(p => p.GetPurchaseSummaryReportAsync(It.IsAny<PurchaseReportFilterDto>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task InventoryReportsController_WhenNotSubmitted_DoesNotQueryService()
        {
            var db = CreateDbContext(nameof(InventoryReportsController_WhenNotSubmitted_DoesNotQueryService));
            await SeedDataAsync(db);
            var userMock = CreateUserContextMock();
            var invServiceMock = new Mock<IInventoryReportService>();

            var controller = new InventoryReportsController(invServiceMock.Object, db, userMock.Object);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            var result = await controller.StockBalances(new InventoryReportFilterDto());
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<StockBalanceReportDto>(viewResult.Model);

            Assert.Empty(model.Items);
            Assert.False(controller.ViewBag.IsRequested);
            invServiceMock.Verify(i => i.GetStockBalancesReportAsync(It.IsAny<InventoryReportFilterDto>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task LookupService_SearchCustomers_ByCodeNameAndBranch()
        {
            var db = CreateDbContext(nameof(LookupService_SearchCustomers_ByCodeNameAndBranch));
            await SeedDataAsync(db);
            var userMock = CreateUserContextMock(branchId: 1, hasAllBranches: true);
            var lookupService = new LookupService(db, null, userMock.Object);

            // Search by Code
            var resCode = await lookupService.SearchAsync(new LookupSearchRequest { EntityType = "customer", Query = "CUST-01" });
            Assert.NotEmpty(resCode.Items);
            Assert.Equal("CUST-01", resCode.Items[0].Code);

            // Search by Arabic Name
            var resNameAr = await lookupService.SearchAsync(new LookupSearchRequest { EntityType = "customer", Query = "تجريبي" });
            Assert.NotEmpty(resNameAr.Items);
            Assert.Equal(10, resNameAr.Items[0].Id);

            // Search by English Name
            var resNameEn = await lookupService.SearchAsync(new LookupSearchRequest { EntityType = "customer", Query = "Test Customer" });
            Assert.NotEmpty(resNameEn.Items);
            Assert.Equal(10, resNameEn.Items[0].Id);

            // Branch Authorization: user restricted to branch 2 cannot access branch 1 customer
            var restrictedUser = CreateUserContextMock(branchId: 2, hasAllBranches: false);
            restrictedUser.Setup(u => u.CanAccessBranch(1)).Returns(false);
            var restrictedService = new LookupService(db, null, restrictedUser.Object);

            var resBlocked = await restrictedService.SearchAsync(new LookupSearchRequest { EntityType = "customer", BranchId = 1 });
            Assert.Empty(resBlocked.Items);
        }

        [Fact]
        public async Task LookupService_SearchVendors_ByCodeAndName()
        {
            var db = CreateDbContext(nameof(LookupService_SearchVendors_ByCodeAndName));
            await SeedDataAsync(db);
            var userMock = CreateUserContextMock(branchId: 1, hasAllBranches: true);
            var lookupService = new LookupService(db, null, userMock.Object);

            // Search by Code
            var resCode = await lookupService.SearchAsync(new LookupSearchRequest { EntityType = "vendor", Query = "VEND-01" });
            Assert.NotEmpty(resCode.Items);
            Assert.Equal("VEND-01", resCode.Items[0].Code);

            // Search by Arabic Name
            var resNameAr = await lookupService.SearchAsync(new LookupSearchRequest { EntityType = "vendor", Query = "رئيسي" });
            Assert.NotEmpty(resNameAr.Items);
            Assert.Equal(20, resNameAr.Items[0].Id);

            // Search by English Name
            var resNameEn = await lookupService.SearchAsync(new LookupSearchRequest { EntityType = "vendor", Query = "Main Vendor" });
            Assert.NotEmpty(resNameEn.Items);
            Assert.Equal(20, resNameEn.Items[0].Id);
        }

        [Fact]
        public async Task LookupService_SearchItems_ByCodeBarcodeAndServiceFilter()
        {
            var db = CreateDbContext(nameof(LookupService_SearchItems_ByCodeBarcodeAndServiceFilter));
            await SeedDataAsync(db);
            var userMock = CreateUserContextMock(branchId: 1, hasAllBranches: true);
            var lookupService = new LookupService(db, null, userMock.Object);

            // Search by Code
            var resCode = await lookupService.SearchAsync(new LookupSearchRequest { EntityType = "item", Query = "ITM-01" });
            Assert.NotEmpty(resCode.Items);
            Assert.Equal("ITM-01", resCode.Items[0].Code);
            Assert.Contains("قطعة", resCode.Items[0].SubTitle ?? "");

            // Search with inventoryOnly = true (excludes service item)
            var resInventoryOnly = await lookupService.SearchAsync(new LookupSearchRequest
            {
                EntityType = "item",
                Filters = new Dictionary<string, string> { { "inventoryOnly", "true" } }
            });
            Assert.All(resInventoryOnly.Items, i => Assert.NotEqual("SRV-01", i.Code));
            Assert.Contains(resInventoryOnly.Items, i => i.Code == "ITM-01");

            // Search with serviceOnly = true
            var resServiceOnly = await lookupService.SearchAsync(new LookupSearchRequest
            {
                EntityType = "item",
                Filters = new Dictionary<string, string> { { "serviceOnly", "true" } }
            });
            Assert.Single(resServiceOnly.Items);
            Assert.Equal("SRV-01", resServiceOnly.Items[0].Code);
            Assert.Equal("صنف خدمي", resServiceOnly.Items[0].BadgeText);
        }

        [Fact]
        public async Task LookupService_GetById_ReturnsEnrichedItemAndParty()
        {
            var db = CreateDbContext(nameof(LookupService_GetById_ReturnsEnrichedItemAndParty));
            await SeedDataAsync(db);
            var userMock = CreateUserContextMock(branchId: 1, hasAllBranches: true);
            var lookupService = new LookupService(db, null, userMock.Object);

            // Get Item by ID
            var itemDto = await lookupService.GetByIdAsync("item", 100);
            Assert.NotNull(itemDto);
            Assert.Equal("ITM-01", itemDto.Code);
            Assert.Contains("قطعة", itemDto.SubTitle ?? "");

            // Get Customer by ID
            var custDto = await lookupService.GetByIdAsync("customer", 10);
            Assert.NotNull(custDto);
            Assert.Equal("CUST-01", custDto.Code);

            // Get Vendor by ID
            var vendDto = await lookupService.GetByIdAsync("vendor", 20);
            Assert.NotNull(vendDto);
            Assert.Equal("VEND-01", vendDto.Code);
        }
    }
}
