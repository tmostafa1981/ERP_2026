using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.DTOs.InventoryCosting;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using UnitEntity = ERP.Domain.Entities.Unit;

namespace ERP.Tests.Unit
{
    public class InventoryCostingTests
    {
        private ErpDbContext CreateDbContext(string dbName)
        {
            var userMock = new Mock<ICurrentUserService>();
            userMock.Setup(u => u.BranchId).Returns(1);
            userMock.Setup(u => u.IsSuperAdmin).Returns(true);
            userMock.Setup(u => u.IsAuthenticated).Returns(true);

            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new ErpDbContext(options, userMock.Object);
        }

        private Mock<ICurrentUserContext> CreateUserContextMock(int branchId = 1)
        {
            var mock = new Mock<ICurrentUserContext>();
            mock.Setup(u => u.BranchId).Returns(branchId);
            mock.Setup(u => u.HasAllBranches).Returns(true);
            mock.Setup(u => u.UserId).Returns("test-user");
            return mock;
        }

        private async Task SeedBaseDataAsync(ErpDbContext context)
        {
            var branch = new Branch { Id = 1, Code = "BR-01", NameAr = "الفرع الرئيسي", NameEn = "Main Branch", IsActive = true };
            context.Branches.Add(branch);

            var wh1 = new Warehouse { Id = 1, BranchId = 1, Code = "WH-01", NameAr = "المستودع الرئيسي", NameEn = "Main WH", IsActive = true };
            var wh2 = new Warehouse { Id = 2, BranchId = 1, Code = "WH-02", NameAr = "مستودع فرعي", NameEn = "Sub WH", IsActive = true };
            context.Warehouses.AddRange(wh1, wh2);

            var unitPcs = new UnitEntity { Id = 1, Code = "PCS", NameAr = "قطعة", NameEn = "Piece", IsActive = true };
            context.Units.Add(unitPcs);

            var group = new ItemGroup { Id = 1, Code = "GRP-01", NameAr = "بضائع عامة", NameEn = "General Goods", IsActive = true };
            context.ItemGroups.Add(group);

            var egp = new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", IsBaseCurrency = true, ExchangeRate = 1m, IsActive = true };
            context.Currencies.Add(egp);

            var fy2026 = new FiscalYear { Id = 1, Year = "2026", Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true, IsClosed = false };
            context.FiscalYears.Add(fy2026);

            for (int m = 1; m <= 12; m++)
            {
                context.FiscalPeriods.Add(new FiscalPeriod
                {
                    Id = m,
                    FiscalYearId = 1,
                    PeriodNumber = m,
                    NameAr = $"فترة {m}",
                    NameEn = $"Period {m}",
                    StartDate = new DateTime(2026, m, 1),
                    EndDate = new DateTime(2026, m, DateTime.DaysInMonth(2026, m)),
                    IsClosed = false
                });
            }

            // GL Accounts:
            // 110301 Finished Goods Inventory
            // 510101 Cost of Goods Sold
            // 410101 Sales Revenue
            // 110101 Cash
            // 110201 Customers Control
            // 210101 Vendors Control
            var accInv = new GLAccount { Id = 101, Code = "110301", NameAr = "مخزون البضائع", NameEn = "Inventory", AccountNature = AccountNature.Debit, IsActive = true, CurrencyId = 1 };
            var accCogs = new GLAccount { Id = 102, Code = "510101", NameAr = "تكلفة البضاعة المباعة", NameEn = "COGS", AccountNature = AccountNature.Debit, IsActive = true, CurrencyId = 1 };
            var accRev = new GLAccount { Id = 103, Code = "410101", NameAr = "إيرادات المبيعات", NameEn = "Sales Revenue", AccountNature = AccountNature.Credit, IsActive = true, CurrencyId = 1 };
            var accCash = new GLAccount { Id = 104, Code = "110101", NameAr = "الصندوق", NameEn = "Cash", AccountNature = AccountNature.Debit, IsActive = true, CurrencyId = 1 };
            var accCust = new GLAccount { Id = 105, Code = "110201", NameAr = "العملاء", NameEn = "Customers", AccountNature = AccountNature.Debit, IsActive = true, CurrencyId = 1 };
            var accVend = new GLAccount { Id = 106, Code = "210101", NameAr = "الموردين", NameEn = "Vendors", AccountNature = AccountNature.Credit, IsActive = true, CurrencyId = 1 };
            context.GLAccounts.AddRange(accInv, accCogs, accRev, accCash, accCust, accVend);

            // Customers & Vendors
            var cust = new Customer { Id = 1, BranchId = 1, Code = "CUST-01", NameAr = "شركة الأمل", NameEn = "Al-Amal Co", FinancialAccountId = 105, IsActive = true };
            var vend = new Vendor { Id = 1, BranchId = 1, Code = "VEND-01", NameAr = "مؤسسة التوريد", NameEn = "Supply Est", FinancialAccountId = 106, IsActive = true };
            context.Customers.Add(cust);
            context.Vendors.Add(vend);

            // Transaction Types
            var ttPurch = new TransactionType
            {
                Id = 1,
                Code = "PINV",
                NameAr = "فاتورة مشتريات",
                NameEn = "Purchase Invoice",
                Module = TransactionModule.Purchase,
                BaseNature = DocumentBaseNature.Invoice,
                InventoryEffect = InventoryEffect.ReceiveStock,
                AccountingEffect = AccountingEffect.FullInvoiceAndCOGS,
                DefaultDebitAccountId = 101, // Inventory
                DefaultCreditAccountId = 106, // Vendor
                IsActive = true
            };

            var ttSale = new TransactionType
            {
                Id = 2,
                Code = "SINV",
                NameAr = "فاتورة مبيعات",
                NameEn = "Sales Invoice",
                Module = TransactionModule.Sales,
                BaseNature = DocumentBaseNature.Invoice,
                InventoryEffect = InventoryEffect.IssueStock,
                AccountingEffect = AccountingEffect.FullInvoiceAndCOGS,
                DefaultDebitAccountId = 105, // Customer
                DefaultCreditAccountId = 103, // Sales Revenue
                DefaultCogsAccountId = 102, // COGS
                DefaultInventoryAccountId = 101, // Inventory
                IsActive = true
            };

            var ttSaleRet = new TransactionType
            {
                Id = 3,
                Code = "SRET",
                NameAr = "مردود مبيعات",
                NameEn = "Sales Return",
                Module = TransactionModule.Sales,
                BaseNature = DocumentBaseNature.Return,
                InventoryEffect = InventoryEffect.ReceiveStock,
                AccountingEffect = AccountingEffect.FullInvoiceAndCOGS,
                DefaultDebitAccountId = 103,
                DefaultCreditAccountId = 105,
                DefaultCogsAccountId = 102,
                DefaultInventoryAccountId = 101,
                IsActive = true
            };

            var ttPurchRet = new TransactionType
            {
                Id = 4,
                Code = "PRET",
                NameAr = "مردود مشتريات",
                NameEn = "Purchase Return",
                Module = TransactionModule.Purchase,
                BaseNature = DocumentBaseNature.Return,
                InventoryEffect = InventoryEffect.IssueStock,
                AccountingEffect = AccountingEffect.FullInvoiceAndCOGS,
                DefaultDebitAccountId = 106,
                DefaultCreditAccountId = 101,
                IsActive = true
            };

            var ttTransfer = new TransactionType
            {
                Id = 5,
                Code = "TRNS",
                NameAr = "تحويل مخزني",
                NameEn = "Stock Transfer",
                Module = TransactionModule.Transfer,
                BaseNature = DocumentBaseNature.Transfer,
                InventoryEffect = InventoryEffect.None,
                AccountingEffect = AccountingEffect.None,
                IsActive = true
            };

            context.TransactionTypes.AddRange(ttPurch, ttSale, ttSaleRet, ttPurchRet, ttTransfer);

            // Items
            var item1 = new Item
            {
                Id = 1,
                Code = "ITM-01",
                NameAr = "شاشة سامسونج",
                NameEn = "Samsung Monitor",
                ItemGroupId = 1,
                IsService = false,
                TrackingType = ItemTrackingType.None,
                IsActive = true
            };

            var itemService = new Item
            {
                Id = 2,
                Code = "SRV-01",
                NameAr = "خدمة تركيب وتوصيل",
                NameEn = "Installation Service",
                ItemGroupId = 1,
                IsService = true,
                TrackingType = ItemTrackingType.None,
                IsActive = true
            };

            var itemSerial = new Item
            {
                Id = 3,
                Code = "ITM-SER",
                NameAr = "لابتوب ديل",
                NameEn = "Dell Laptop",
                ItemGroupId = 1,
                IsService = false,
                TrackingType = ItemTrackingType.SerialNumber,
                IsActive = true
            };

            context.Items.AddRange(item1, itemService, itemSerial);
            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task MovingAverage_PurchasesAtDifferentPrices_CalculatesCorrectAverageCost()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var auditMock = new Mock<IAuditService>();
            var fiscalService = new FiscalPeriodService(context, auditMock.Object);
            var costingService = new InventoryCostingService(context, auditMock.Object, fiscalService);

            // Purchase 1: 100 units @ 100 EGP
            var doc1 = new DocumentHeader
            {
                Id = 1,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 1,
                DocumentNumber = "PINV-001",
                DocumentDate = new DateTime(2026, 1, 10),
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TransactionType = await context.TransactionTypes.FindAsync(1),
                Lines = new List<DocumentLine>
                {
                    new DocumentLine
                    {
                        Id = 1,
                        ItemId = 1,
                        Quantity = 100,
                        UnitPrice = 100m,
                        UnitId = 1,
                        ConversionRate = 1.0m
                    }
                }
            };
            context.DocumentHeaders.Add(doc1);
            await context.SaveChangesAsync();

            var res1 = await costingService.ProcessReceiptCostAsync(doc1);
            Assert.True(res1.Success);

            var cost1 = await costingService.GetCostDetailsAsync(1, 1, 1);
            Assert.NotNull(cost1);
            Assert.Equal(100m, cost1.CurrentQuantity);
            Assert.Equal(10000m, cost1.TotalCost);
            Assert.Equal(100m, cost1.MovingAverageCost);

            // Purchase 2: 100 units @ 120 EGP
            var doc2 = new DocumentHeader
            {
                Id = 2,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 1,
                DocumentNumber = "PINV-002",
                DocumentDate = new DateTime(2026, 1, 15),
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TransactionType = await context.TransactionTypes.FindAsync(1),
                Lines = new List<DocumentLine>
                {
                    new DocumentLine
                    {
                        Id = 2,
                        ItemId = 1,
                        Quantity = 100,
                        UnitPrice = 120m,
                        UnitId = 1,
                        ConversionRate = 1.0m
                    }
                }
            };
            context.DocumentHeaders.Add(doc2);
            await context.SaveChangesAsync();

            var res2 = await costingService.ProcessReceiptCostAsync(doc2);
            Assert.True(res2.Success);

            var cost2 = await costingService.GetCostDetailsAsync(1, 1, 1);
            Assert.NotNull(cost2);
            Assert.Equal(200m, cost2.CurrentQuantity);
            Assert.Equal(22000m, cost2.TotalCost);
            // Moving Weighted Average: 22,000 / 200 = 110.0000
            Assert.Equal(110m, cost2.MovingAverageCost);
        }

        [Fact]
        public async Task PurchaseAcquisitionCost_AllocatesDiscountsAndHeaderAdditions()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var auditMock = new Mock<IAuditService>();
            var fiscalService = new FiscalPeriodService(context, auditMock.Object);
            var costingService = new InventoryCostingService(context, auditMock.Object, fiscalService);

            // Purchase invoice with:
            // Line price = 100, line discount = 10 (Net = 90)
            // Header Additions = 20 (allocated to this line)
            // Total Acquisition Cost = 90 + 20 = 110
            var doc = new DocumentHeader
            {
                Id = 1,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 1,
                DocumentNumber = "PINV-003",
                DocumentDate = new DateTime(2026, 1, 10),
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TotalAdditions = 20m,
                TransactionType = await context.TransactionTypes.FindAsync(1),
                Lines = new List<DocumentLine>
                {
                    new DocumentLine
                    {
                        Id = 1,
                        ItemId = 1,
                        Quantity = 1,
                        UnitPrice = 100m,
                        DiscountAmount = 10m,
                        UnitId = 1,
                        ConversionRate = 1.0m
                    }
                }
            };
            context.DocumentHeaders.Add(doc);
            await context.SaveChangesAsync();

            var res = await costingService.ProcessReceiptCostAsync(doc);
            Assert.True(res.Success);

            Assert.Equal(110m, doc.Lines.First().UnitCost);
            Assert.Equal(110m, doc.Lines.First().TotalCost);

            var cost = await costingService.GetCostDetailsAsync(1, 1, 1);
            Assert.Equal(110m, cost!.MovingAverageCost);
        }

        [Fact]
        public async Task SalesIssue_DeductsCostAtMovingAverage_AndPreservesHistoricalCost()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var auditMock = new Mock<IAuditService>();
            var fiscalService = new FiscalPeriodService(context, auditMock.Object);
            var costingService = new InventoryCostingService(context, auditMock.Object, fiscalService);

            // Seed stock: 200 units @ 110 = 22,000
            context.ItemWarehouseCosts.Add(new ItemWarehouseCost
            {
                BranchId = 1,
                WarehouseId = 1,
                ItemId = 1,
                CurrentQuantity = 200m,
                TotalCost = 22000m,
                MovingAverageCost = 110m,
                LastUpdatedDate = new DateTime(2026, 1, 1)
            });
            await context.SaveChangesAsync();

            // Sell 50 units
            var docSale = new DocumentHeader
            {
                Id = 10,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 2,
                DocumentNumber = "SINV-001",
                DocumentDate = new DateTime(2026, 1, 20),
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TransactionType = await context.TransactionTypes.FindAsync(2),
                Lines = new List<DocumentLine>
                {
                    new DocumentLine
                    {
                        Id = 101,
                        ItemId = 1,
                        Quantity = 50,
                        UnitPrice = 150m,
                        UnitId = 1,
                        ConversionRate = 1.0m
                    }
                }
            };
            context.DocumentHeaders.Add(docSale);
            await context.SaveChangesAsync();

            var (success, msg, totalCogs) = await costingService.ProcessIssueCostAsync(docSale);
            Assert.True(success);
            Assert.Equal(5500m, totalCogs); // 50 * 110 = 5500

            var line = docSale.Lines.First();
            Assert.Equal(110m, line.UnitCost);
            Assert.Equal(5500m, line.TotalCost);

            var cost = await costingService.GetCostDetailsAsync(1, 1, 1);
            Assert.NotNull(cost);
            Assert.Equal(150m, cost.CurrentQuantity);
            Assert.Equal(16500m, cost.TotalCost);
            Assert.Equal(110m, cost.MovingAverageCost); // Moving average unchanged on sale
        }

        [Fact]
        public async Task SalesInvoice_GeneratesBalancedCOGSJournalEntry()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            // Pre-seed stock: 100 units @ 80 = 8000
            context.ItemWarehouseCosts.Add(new ItemWarehouseCost
            {
                BranchId = 1,
                WarehouseId = 1,
                ItemId = 1,
                CurrentQuantity = 100m,
                TotalCost = 8000m,
                MovingAverageCost = 80m,
                LastUpdatedDate = new DateTime(2026, 1, 1)
            });
            await context.SaveChangesAsync();

            var userContext = CreateUserContextMock(1);
            var ttMock = new Mock<ITransactionTypeService>();
            ttMock.Setup(t => t.HasPermissionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var serialService = new SerialNumberService(context);
            var codeSeqMock = new Mock<ICodeSequenceService>();
            codeSeqMock.Setup(c => c.GetNextCodeAsync(It.IsAny<string>(), It.IsAny<int?>()))
                .ReturnsAsync("SINV-2026-0001");

            var auditMock = new Mock<IAuditService>();
            var fiscalService = new FiscalPeriodService(context, auditMock.Object);

            var docService = new UniversalDocumentService(
                context,
                ttMock.Object,
                serialService,
                codeSeqMock.Object,
                userContext.Object,
                auditMock.Object,
                fiscalService);

            var dto = new DocumentHeaderDto
            {
                BranchId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 2, // SINV
                DocumentDate = new DateTime(2026, 1, 25),
                PartyType = DocumentPartyType.Customer,
                PartyId = 1,
                PaymentMethod = VoucherPaymentMethod.Other,
                WarehouseId = 1,
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TotalAmountBeforeTax = 1200m,
                TotalAmountNet = 1200m,
                Lines = new List<DocumentLineDto>
                {
                    new DocumentLineDto
                    {
                        ItemId = 1,
                        Quantity = 10,
                        UnitPrice = 120m,
                        UnitId = 1,
                        ConversionRate = 1.0m
                    }
                }
            };

            var (createSuccess, createMsg, docId, docNum) = await docService.CreateDocumentAsync(dto, "user-1", postImmediately: true);
            Assert.True(createSuccess, createMsg);

            var doc = await context.DocumentHeaders
                .Include(d => d.Lines)
                .Include(d => d.JournalEntryHeader).ThenInclude(j => j!.Lines)
                .FirstOrDefaultAsync(d => d.Id == docId!.Value);

            Assert.NotNull(doc);
            Assert.NotNull(doc.JournalEntryHeader);

            var jv = doc.JournalEntryHeader!;
            // Check balanced invariant
            Assert.Equal(jv.TotalDebitLocal, jv.TotalCreditLocal);

            // COGS check: 10 units @ 80 = 800 EGP
            // Debit: COGS (510101) = 800
            // Credit: Inventory (110301) = 800
            var cogsLine = jv.Lines.FirstOrDefault(l => l.AccountId == 102); // COGS
            var invLine = jv.Lines.FirstOrDefault(l => l.AccountId == 101); // Inventory

            Assert.NotNull(cogsLine);
            Assert.Equal(800m, cogsLine.DebitLocal);
            Assert.Equal(0m, cogsLine.CreditLocal);

            Assert.NotNull(invLine);
            Assert.Equal(0m, invLine.DebitLocal);
            Assert.Equal(800m, invLine.CreditLocal);

            // Gross Balanced Sum: Revenue (1200) + COGS (800) = 2000
            Assert.Equal(2000m, jv.TotalDebitLocal);
            Assert.Equal(2000m, jv.TotalCreditLocal);
        }

        [Fact]
        public async Task SalesReturn_RestoresHistoricalCost()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var auditMock = new Mock<IAuditService>();
            var fiscalService = new FiscalPeriodService(context, auditMock.Object);
            var costingService = new InventoryCostingService(context, auditMock.Object, fiscalService);

            // Current cost is 150 (due to some other transactions), but original sale line had UnitCost = 100
            context.ItemWarehouseCosts.Add(new ItemWarehouseCost
            {
                BranchId = 1,
                WarehouseId = 1,
                ItemId = 1,
                CurrentQuantity = 50m,
                TotalCost = 7500m,
                MovingAverageCost = 150m,
                LastUpdatedDate = new DateTime(2026, 1, 10)
            });

            // Original sales document
            var origSaleDoc = new DocumentHeader
            {
                Id = 50,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 2,
                DocumentNumber = "SINV-ORIG",
                DocumentDate = new DateTime(2026, 1, 10),
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                Lines = new List<DocumentLine>
                {
                    new DocumentLine
                    {
                        Id = 500,
                        ItemId = 1,
                        WarehouseId = 1,
                        Quantity = 20,
                        UnitPrice = 200m,
                        UnitCost = 100m, // Historical cost
                        TotalCost = 2000m
                    }
                }
            };
            context.DocumentHeaders.Add(origSaleDoc);

            var link = new DocumentLineLink
            {
                SourceDocumentLineId = 500,
                TargetDocumentLineId = 600,
                ItemId = 1,
                ConvertedQuantity = 10
            };
            context.DocumentLineLinks.Add(link);
            await context.SaveChangesAsync();

            // Sales Return of 10 units linked to original line 500
            var retLine = new DocumentLine
            {
                Id = 600,
                DocumentHeaderId = 60,
                ItemId = 1,
                WarehouseId = 1,
                Quantity = 10,
                UnitPrice = 200m,
                UnitId = 1,
                ConversionRate = 1.0m,
                SourceLineLinks = new List<DocumentLineLink> { link }
            };

            var docRet = new DocumentHeader
            {
                Id = 60,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 3, // SRET
                DocumentNumber = "SRET-001",
                DocumentDate = new DateTime(2026, 1, 28),
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TransactionType = await context.TransactionTypes.FindAsync(3),
                Lines = new List<DocumentLine> { retLine }
            };
            context.DocumentHeaders.Add(docRet);
            await context.SaveChangesAsync();

            var (success, _) = await costingService.ProcessReceiptCostAsync(docRet);
            Assert.True(success);

            // Unit cost must be recovered from original sale line (100) instead of current average (150)
            Assert.Equal(100m, retLine.UnitCost);
            Assert.Equal(1000m, retLine.TotalCost); // 10 * 100 = 1000

            // Updated stock: (7500 + 1000) / (50 + 10) = 8500 / 60 = 141.6667
            var cost = await costingService.GetCostDetailsAsync(1, 1, 1);
            Assert.Equal(60m, cost!.CurrentQuantity);
            Assert.Equal(8500m, cost.TotalCost);
            Assert.Equal(141.6667m, cost.MovingAverageCost);
        }

        [Fact]
        public async Task InternalTransfer_MovesStockValuationWithoutProfitOrLoss()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var auditMock = new Mock<IAuditService>();
            var fiscalService = new FiscalPeriodService(context, auditMock.Object);
            var costingService = new InventoryCostingService(context, auditMock.Object, fiscalService);

            // WH1 has 100 units @ 50 = 5000
            context.ItemWarehouseCosts.Add(new ItemWarehouseCost
            {
                BranchId = 1,
                WarehouseId = 1,
                ItemId = 1,
                CurrentQuantity = 100m,
                TotalCost = 5000m,
                MovingAverageCost = 50m,
                LastUpdatedDate = new DateTime(2026, 1, 1)
            });

            // WH2 has 50 units @ 80 = 4000
            context.ItemWarehouseCosts.Add(new ItemWarehouseCost
            {
                BranchId = 1,
                WarehouseId = 2,
                ItemId = 1,
                CurrentQuantity = 50m,
                TotalCost = 4000m,
                MovingAverageCost = 80m,
                LastUpdatedDate = new DateTime(2026, 1, 1)
            });
            await context.SaveChangesAsync();

            // Transfer 20 units from WH1 to WH2 (PartyId = 2 indicates target warehouse in this module)
            var docTransfer = new DocumentHeader
            {
                Id = 80,
                BranchId = 1,
                WarehouseId = 1, // Source WH
                PartyId = 2, // Destination WH
                FiscalYearId = 1,
                TransactionTypeId = 5,
                DocumentNumber = "TRNS-001",
                DocumentDate = new DateTime(2026, 1, 20),
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TransactionType = await context.TransactionTypes.FindAsync(5),
                Lines = new List<DocumentLine>
                {
                    new DocumentLine
                    {
                        Id = 801,
                        ItemId = 1,
                        Quantity = 20,
                        UnitPrice = 0m,
                        UnitId = 1,
                        ConversionRate = 1.0m
                    }
                }
            };
            context.DocumentHeaders.Add(docTransfer);
            await context.SaveChangesAsync();

            var (success, _) = await costingService.ProcessTransferCostAsync(docTransfer);
            Assert.True(success);

            // WH1: 100 - 20 = 80 units @ 50 = 4000
            var wh1Cost = await costingService.GetCostDetailsAsync(1, 1, 1);
            Assert.Equal(80m, wh1Cost!.CurrentQuantity);
            Assert.Equal(4000m, wh1Cost.TotalCost);
            Assert.Equal(50m, wh1Cost.MovingAverageCost);

            // WH2: 50 + 20 = 70 units; Total cost = 4000 + (20 * 50) = 5000; New average = 5000 / 70 = 71.4286
            var wh2Cost = await costingService.GetCostDetailsAsync(1, 2, 1);
            Assert.Equal(70m, wh2Cost!.CurrentQuantity);
            Assert.Equal(5000m, wh2Cost.TotalCost);
            Assert.Equal(71.4286m, wh2Cost.MovingAverageCost);
        }

        [Fact]
        public async Task SerializedItem_TracksSpecificIdentificationCost()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var auditMock = new Mock<IAuditService>();
            var fiscalService = new FiscalPeriodService(context, auditMock.Object);
            var costingService = new InventoryCostingService(context, auditMock.Object, fiscalService);

            // Receive 2 laptops with serials SN-A1 and SN-A2 @ 1500 EGP each
            var linePurch = new DocumentLine
            {
                Id = 901,
                ItemId = 3,
                Quantity = 2,
                UnitPrice = 1500m,
                UnitId = 1,
                ConversionRate = 1.0m,
                Serials = new List<DocumentLineSerial>
                {
                    new DocumentLineSerial { SerialNumber = "SN-A1" },
                    new DocumentLineSerial { SerialNumber = "SN-A2" }
                }
            };

            context.ItemSerialNumbers.AddRange(
                new ItemSerialNumber { ItemId = 3, SerialNumber = "SN-A1", BranchId = 1, WarehouseId = 1, Status = ItemSerialStatus.Available },
                new ItemSerialNumber { ItemId = 3, SerialNumber = "SN-A2", BranchId = 1, WarehouseId = 1, Status = ItemSerialStatus.Available }
            );

            var docPurch = new DocumentHeader
            {
                Id = 90,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 1,
                DocumentNumber = "PINV-SER-01",
                DocumentDate = new DateTime(2026, 1, 10),
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TransactionType = await context.TransactionTypes.FindAsync(1),
                Lines = new List<DocumentLine> { linePurch }
            };
            context.DocumentHeaders.Add(docPurch);
            await context.SaveChangesAsync();

            await costingService.ProcessReceiptCostAsync(docPurch);

            var sn1 = await context.ItemSerialNumbers.FirstOrDefaultAsync(s => s.SerialNumber == "SN-A1");
            Assert.Equal(1500m, sn1!.UnitCost);

            // Sell 1 laptop with serial SN-A1
            var lineSale = new DocumentLine
            {
                Id = 951,
                ItemId = 3,
                Quantity = 1,
                UnitPrice = 2000m,
                UnitId = 1,
                ConversionRate = 1.0m,
                Serials = new List<DocumentLineSerial>
                {
                    new DocumentLineSerial { SerialNumber = "SN-A1" }
                }
            };

            var docSale = new DocumentHeader
            {
                Id = 95,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 2,
                DocumentNumber = "SINV-SER-01",
                DocumentDate = new DateTime(2026, 1, 15),
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TransactionType = await context.TransactionTypes.FindAsync(2),
                Lines = new List<DocumentLine> { lineSale }
            };
            context.DocumentHeaders.Add(docSale);
            await context.SaveChangesAsync();

            var (success, _, totalCogs) = await costingService.ProcessIssueCostAsync(docSale);
            Assert.True(success);
            Assert.Equal(1500m, totalCogs);
            Assert.Equal(1500m, lineSale.UnitCost);
        }

        [Fact]
        public async Task ServiceItem_BypassesInventoryCostingAndCOGS()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var auditMock = new Mock<IAuditService>();
            var fiscalService = new FiscalPeriodService(context, auditMock.Object);
            var costingService = new InventoryCostingService(context, auditMock.Object, fiscalService);

            var docSale = new DocumentHeader
            {
                Id = 99,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 2,
                DocumentNumber = "SINV-SRV-01",
                DocumentDate = new DateTime(2026, 1, 20),
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TransactionType = await context.TransactionTypes.FindAsync(2),
                Lines = new List<DocumentLine>
                {
                    new DocumentLine
                    {
                        Id = 991,
                        ItemId = 2, // Service Item
                        Quantity = 5,
                        UnitPrice = 250m,
                        UnitId = 1,
                        ConversionRate = 1.0m
                    }
                }
            };
            context.DocumentHeaders.Add(docSale);
            await context.SaveChangesAsync();

            var (success, _, totalCogs) = await costingService.ProcessIssueCostAsync(docSale);
            Assert.True(success);
            Assert.Equal(0m, totalCogs); // 0 COGS for services

            var cost = await costingService.GetCostDetailsAsync(2, 1, 1);
            Assert.Null(cost); // No inventory cost record created for service item
        }

        [Fact]
        public async Task PurchaseQuantityReduction_PreventedWhenConsumedInSales()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var auditMock = new Mock<IAuditService>();
            var fiscalService = new FiscalPeriodService(context, auditMock.Object);
            var costingService = new InventoryCostingService(context, auditMock.Object, fiscalService);

            // Purchase 100 units
            var docPurch = new DocumentHeader
            {
                Id = 100,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 1,
                DocumentNumber = "PINV-100",
                DocumentDate = new DateTime(2026, 1, 1),
                Status = DocumentStatus.Posted,
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TransactionType = await context.TransactionTypes.FindAsync(1),
                Lines = new List<DocumentLine>
                {
                    new DocumentLine
                    {
                        Id = 1001,
                        ItemId = 1,
                        Quantity = 100,
                        UnitPrice = 100m,
                        UnitId = 1,
                        ConversionRate = 1.0m
                    }
                }
            };
            context.DocumentHeaders.Add(docPurch);
            await context.SaveChangesAsync();
            await costingService.ProcessReceiptCostAsync(docPurch);

            // Sell 60 units
            var docSale = new DocumentHeader
            {
                Id = 101,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 2,
                DocumentNumber = "SINV-101",
                DocumentDate = new DateTime(2026, 1, 5),
                Status = DocumentStatus.Posted,
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TransactionType = await context.TransactionTypes.FindAsync(2),
                Lines = new List<DocumentLine>
                {
                    new DocumentLine
                    {
                        Id = 1011,
                        ItemId = 1,
                        Quantity = 60,
                        UnitPrice = 150m,
                        UnitId = 1,
                        ConversionRate = 1.0m
                    }
                }
            };
            context.DocumentHeaders.Add(docSale);
            await context.SaveChangesAsync();
            await costingService.ProcessIssueCostAsync(docSale);

            // Stock now = 40 units
            // Reducing purchase to 70 units should be ALLOWED (new stock would be 10)
            var (can70, _) = await costingService.ValidatePurchaseQuantityReductionAsync(100, 1, 1, 70m);
            Assert.True(can70);

            // Reducing purchase to 50 units should be REJECTED (new stock would be -10, below 0)
            var (can50, error50) = await costingService.ValidatePurchaseQuantityReductionAsync(100, 1, 1, 50m);
            Assert.False(can50);
            Assert.NotNull(error50);
        }

        [Fact]
        public async Task PurchaseModification_ReplaysChronologically_AndGeneratesAdjustmentJVForClosedPeriod()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var auditMock = new Mock<IAuditService>();

            // Setup: Period 1 (January) is CLOSED, Period 2 (February) is OPEN
            var p1 = await context.FiscalPeriods.FindAsync(1);
            p1!.IsClosed = true;
            await context.SaveChangesAsync();

            var fiscalService = new FiscalPeriodService(context, auditMock.Object);
            var costingService = new InventoryCostingService(context, auditMock.Object, fiscalService);

            // Initial Purchase on Jan 5: 100 units @ 100 EGP
            var docPurch = new DocumentHeader
            {
                Id = 200,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 1,
                DocumentNumber = "PINV-200",
                DocumentDate = new DateTime(2026, 1, 5),
                Status = DocumentStatus.Posted,
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TransactionType = await context.TransactionTypes.FindAsync(1),
                Lines = new List<DocumentLine>
                {
                    new DocumentLine
                    {
                        Id = 2001,
                        ItemId = 1,
                        Quantity = 100,
                        UnitPrice = 100m,
                        UnitId = 1,
                        ConversionRate = 1.0m
                    }
                }
            };
            context.DocumentHeaders.Add(docPurch);
            await context.SaveChangesAsync();
            await costingService.ProcessReceiptCostAsync(docPurch);

            // Sale in January (Period 1, closed): 50 units @ 150 (COGS = 50 * 100 = 5000)
            var docSale = new DocumentHeader
            {
                Id = 201,
                BranchId = 1,
                WarehouseId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 2,
                DocumentNumber = "SINV-201",
                DocumentDate = new DateTime(2026, 1, 15),
                Status = DocumentStatus.Posted,
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                TransactionType = await context.TransactionTypes.FindAsync(2),
                Lines = new List<DocumentLine>
                {
                    new DocumentLine
                    {
                        Id = 2011,
                        ItemId = 1,
                        Quantity = 50,
                        UnitPrice = 150m,
                        UnitId = 1,
                        ConversionRate = 1.0m
                    }
                }
            };
            context.DocumentHeaders.Add(docSale);
            await context.SaveChangesAsync();
            await costingService.ProcessIssueCostAsync(docSale);

            // Now, purchase price is adjusted retroactively from 100 to 120 (+20 per unit)
            docPurch.Lines.First().UnitPrice = 120m;
            await context.SaveChangesAsync();

            // Run recalculation engine
            var (recalcSuccess, recalcMsg, summary) = await costingService.RecalculateCostsAfterPurchaseModificationAsync(200, "admin");
            Assert.True(recalcSuccess, recalcMsg);
            Assert.NotNull(summary);
            Assert.True(summary.AffectedSalesInvoicesCount > 0);

            // Since Period 1 is closed:
            // 1. An adjustment record must exist
            var adjRecord = await context.CostAdjustmentRecords.FirstOrDefaultAsync(r => r.AffectedDocumentHeaderId == 201);
            Assert.NotNull(adjRecord);
            Assert.True(adjRecord.IsClosedPeriod);
            Assert.Equal(1000m, adjRecord.CostDifference); // (120 - 100) * 50 = +1000 COGS difference
            Assert.True(adjRecord.AdjustmentJournalEntryId.HasValue);

            // Check the generated adjustment JV
            var adjJv = await context.JournalEntryHeaders
                .Include(j => j.Lines)
                .FirstOrDefaultAsync(j => j.Id == adjRecord.AdjustmentJournalEntryId.Value);

            Assert.NotNull(adjJv);
            // Must be strictly balanced
            Assert.Equal(adjJv.TotalDebitLocal, adjJv.TotalCreditLocal);
            Assert.Equal(1000m, adjJv.TotalDebitLocal);
        }
    }
}
