using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;
using UnitEntity = ERP.Domain.Entities.Unit;

namespace ERP.Tests.Unit
{
    public class TransactionTypesAndDocumentsTests
    {
        private ErpDbContext CreateDbContext(string dbName, int branchId = 1, bool isSuperAdmin = false)
        {
            var userMock = new Mock<ICurrentUserService>();
            userMock.Setup(u => u.BranchId).Returns(branchId);
            userMock.Setup(u => u.IsSuperAdmin).Returns(isSuperAdmin);
            userMock.Setup(u => u.IsAuthenticated).Returns(true);

            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new ErpDbContext(options, userMock.Object);
        }

        private (UniversalDocumentService docService, SerialNumberService serialService, DocumentConversionService convService, TransactionTypeService ttService)
            CreateServices(ErpDbContext context, string userId = "test-user-1", int branchId = 1)
        {
            var auditMock = new Mock<IAuditService>();
            var codeSeqMock = new Mock<ICodeSequenceService>();

            var currentUserMock = new Mock<ICurrentUserContext>();
            currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
            currentUserMock.Setup(c => c.UserId).Returns(userId);
            currentUserMock.Setup(c => c.BranchId).Returns(branchId);
            currentUserMock.Setup(c => c.HasAllBranches).Returns(false);

            var cache = new MemoryCache(new MemoryCacheOptions());

            var serialService = new SerialNumberService(context);
            var ttService = new TransactionTypeService(context, cache, currentUserMock.Object, auditMock.Object);
            var fiscalPeriodService = new FiscalPeriodService(context, auditMock.Object);
            var docService = new UniversalDocumentService(context, ttService, serialService, codeSeqMock.Object, currentUserMock.Object, auditMock.Object, fiscalPeriodService);
            var convService = new DocumentConversionService(context, docService, ttService);

            return (docService, serialService, convService, ttService);
        }

        private async Task SeedBaseDataAsync(ErpDbContext context)
        {
            if (!await context.Branches.AnyAsync())
            {
                context.Branches.AddRange(
                    new Branch { Id = 1, Code = "B01", NameAr = "الفرع الرئيسي", NameEn = "Main Branch", IsActive = true },
                    new Branch { Id = 2, Code = "B02", NameAr = "فرع الإسكندرية", NameEn = "Alex Branch", IsActive = true }
                );
            }

            if (!await context.FiscalYears.AnyAsync())
            {
                context.FiscalYears.Add(new FiscalYear
                {
                    Id = 1,
                    Year = "2026",
                    Name = "2026",
                    StartDate = new DateTime(2026, 1, 1),
                    EndDate = new DateTime(2026, 12, 31),
                    IsActive = true
                });
            }

            if (!await context.Units.AnyAsync())
            {
                context.Units.Add(new UnitEntity { Id = 1, Code = "PCS", NameAr = "قطعة", NameEn = "Piece", IsActive = true });
            }

            if (!await context.Warehouses.AnyAsync())
            {
                context.Warehouses.Add(new Warehouse { Id = 1, Code = "WH01", NameAr = "المخزن الرئيسي", NameEn = "Main WH", BranchId = 1, IsActive = true });
            }

            if (!await context.Currencies.AnyAsync())
            {
                context.Currencies.Add(new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", ExchangeRate = 1.0m, IsActive = true });
            }

            if (!await context.GLAccounts.AnyAsync())
            {
                context.GLAccounts.AddRange(
                    new GLAccount { Id = 101, Code = "1101", NameAr = "الصندوق", NameEn = "Cash", AccountCategory = AccountCategory.Assets, BranchId = 1, IsActive = true },
                    new GLAccount { Id = 401, Code = "4101", NameAr = "إيرادات المبيعات", NameEn = "Sales Revenue", AccountCategory = AccountCategory.Revenues, BranchId = 1, IsActive = true }
                );
            }

            if (!await context.Customers.AnyAsync())
            {
                context.Customers.Add(new Customer
                {
                    Id = 1,
                    Code = "C001",
                    NameAr = "شركة الأمل",
                    NameEn = "Al-Amal Co",
                    BranchId = 1,
                    FinancialAccountId = 101,
                    IsActive = true
                });
            }

            if (!await context.ItemGroups.AnyAsync())
            {
                context.ItemGroups.Add(new ItemGroup { Id = 1, Code = "G01", NameAr = "أجهزة", NameEn = "Devices", IsActive = true });
            }

            if (!await context.Items.AnyAsync())
            {
                context.Items.AddRange(
                    new Item { Id = 1, Code = "ITM-NORM", NameAr = "صنف عادي", NameEn = "Normal Item", ItemGroupId = 1, TrackingType = ItemTrackingType.None, IsActive = true },
                    new Item { Id = 2, Code = "ITM-SN", NameAr = "صنف سيريال", NameEn = "Serial Item", ItemGroupId = 1, TrackingType = ItemTrackingType.SerialNumber, IsActive = true }
                );
            }

            if (!await context.TransactionTypes.AnyAsync())
            {
                context.TransactionTypes.AddRange(
                    new TransactionType
                    {
                        Id = 1,
                        Code = "SI_CASH",
                        NameAr = "فاتورة مبيعات نقدية",
                        NameEn = "Sales Invoice Cash",
                        Module = TransactionModule.Sales,
                        BaseNature = DocumentBaseNature.Invoice,
                        InventoryEffect = InventoryEffect.IssueStock,
                        AccountingEffect = AccountingEffect.FullInvoiceAndCOGS,
                        IsCashAllowed = true,
                        IsCreditAllowed = false,
                        CodeSequencePrefix = "INV-",
                        DefaultWarehouseId = 1,
                        DefaultDebitAccountId = 101,
                        DefaultCreditAccountId = 401,
                        IsActive = true
                    },
                    new TransactionType
                    {
                        Id = 2,
                        Code = "QUOTATION",
                        NameAr = "عرض سعر مبيعات",
                        NameEn = "Sales Quotation",
                        Module = TransactionModule.Sales,
                        BaseNature = DocumentBaseNature.Quotation,
                        InventoryEffect = InventoryEffect.None,
                        AccountingEffect = AccountingEffect.None,
                        IsCashAllowed = true,
                        IsCreditAllowed = true,
                        CodeSequencePrefix = "QUO-",
                        IsActive = true
                    }
                );

                context.TransactionTypeConversions.Add(new TransactionTypeConversion
                {
                    SourceTransactionTypeId = 2, // Quotation
                    TargetTransactionTypeId = 1, // Sales Invoice
                    AllowPartialQuantity = true,
                    AllowMultipleSources = true,
                    IsActive = true
                });
            }

            await context.SaveChangesAsync();
        }

        // ==========================================
        // 1. TransactionTypeService Tests
        // ==========================================
        [Fact]
        public async Task TransactionTypeService_FiltersPermissionsByUser()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var (_, _, _, ttService) = CreateServices(context);

            // Grant permission only to SI_CASH for "restricted_user"
            await ttService.SaveUserPermissionsAsync("restricted_user", new List<TransactionTypeDto>
            {
                new TransactionTypeDto { Id = 1, CanCreate = true, CanView = true },
                new TransactionTypeDto { Id = 2, CanCreate = false, CanView = false }
            });

            var allowed = await ttService.GetAllowedTypesAsync("restricted_user", 1, TransactionModule.Sales);

            Assert.Single(allowed);
            Assert.Equal("SI_CASH", allowed[0].Code);
        }

        // ==========================================
        // 2. SerialNumberService Tests
        // ==========================================
        [Fact]
        public async Task SerialNumberService_ValidatesAndPreventsDuplicateOrCrossBranchIssue()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var (_, serialService, _, _) = CreateServices(context);

            // Receive serial SN-1001 in Branch 1, Warehouse 1
            await serialService.RegisterReceivedSerialsAsync(
                documentId: 10,
                docType: "GR-001",
                itemId: 2,
                warehouseId: 1,
                branchId: 1,
                serialNumbers: new List<string> { "SN-1001" });

            // 1. Trying to validate SN-1001 for Branch 2 (Cross-branch violation) must fail
            var crossBranchVal = await serialService.ValidateSerialForIssueAsync(
                itemId: 2,
                warehouseId: 1,
                branchId: 2, // Wrong Branch!
                serialNumber: "SN-1001");

            Assert.False(crossBranchVal.IsValid);

            // 2. Trying to validate a non-existent serial must fail
            var nonExistentVal = await serialService.ValidateSerialForIssueAsync(
                itemId: 2,
                warehouseId: 1,
                branchId: 1,
                serialNumber: "SN-9999");

            Assert.False(nonExistentVal.IsValid);

            // 3. Validating in same Branch 1 and Warehouse 1 must succeed
            var validVal = await serialService.ValidateSerialForIssueAsync(
                itemId: 2,
                warehouseId: 1,
                branchId: 1,
                serialNumber: "SN-1001");

            Assert.True(validVal.IsValid);

            // 4. Issue SN-1001
            await serialService.MarkSerialsAsIssuedAsync(
                documentId: 20,
                docType: "SI-001",
                itemId: 2,
                warehouseId: 1,
                branchId: 1,
                serialNumbers: new List<string> { "SN-1001" });

            // 5. Trying to validate SN-1001 for issue again must fail (status is now Issued)
            var duplicateVal = await serialService.ValidateSerialForIssueAsync(
                itemId: 2,
                warehouseId: 1,
                branchId: 1,
                serialNumber: "SN-1001");

            Assert.False(duplicateVal.IsValid);
        }

        // ==========================================
        // 3. UniversalDocumentService Tests
        // ==========================================
        [Fact]
        public async Task UniversalDocumentService_CreateAndPost_UpdatesSerialsAndGeneratesJournalEntry()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var (docService, serialService, _, _) = CreateServices(context);

            // Receive serial SN-777 first
            await serialService.RegisterReceivedSerialsAsync(1, "SEED-01", 2, 1, 1, new List<string> { "SN-777" });

            // Create Cash Sales Invoice with serial SN-777
            var docDto = new DocumentHeaderDto
            {
                BranchId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 1, // SI_CASH
                DocumentDate = DateTime.Today,
                PaymentMethod = VoucherPaymentMethod.Cash,
                CashBankAccountId = 101,
                WarehouseId = 1,
                Lines = new List<DocumentLineDto>
                {
                    new DocumentLineDto
                    {
                        LineNumber = 1,
                        ItemId = 2,
                        UnitId = 1,
                        Quantity = 1,
                        UnitPrice = 500,
                        TrackingType = ItemTrackingType.SerialNumber,
                        WarehouseId = 1,
                        SerialNumbers = new List<string> { "SN-777" }
                    }
                }
            };

            var createRes = await docService.CreateDocumentAsync(docDto, "test-user");
            Assert.True(createRes.Success);
            Assert.NotNull(createRes.DocumentId);

            // Verify Document Status is Posted immediately upon Save
            var postedDoc = await docService.GetDocumentByIdAsync(createRes.DocumentId.Value);
            Assert.NotNull(postedDoc);
            Assert.Equal(DocumentStatus.Posted, postedDoc.Status);
            Assert.NotNull(postedDoc.JournalEntryHeaderId);

            // Verify Serial is now Issued
            var serial = await context.ItemSerialNumbers.FirstOrDefaultAsync(s => s.SerialNumber == "SN-777");
            Assert.NotNull(serial);
            Assert.Equal(ItemSerialStatus.Issued, serial.Status);
        }

        // ==========================================
        // 4. DocumentConversionService Tests
        // ==========================================
        [Fact]
        public async Task DocumentConversionService_PartialConversion_TracksQuantitiesAndStatus()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);

            var (docService, _, convService, _) = CreateServices(context);

            // 1. Create and Post Quotation for 100 units of Normal Item
            var quoteDto = new DocumentHeaderDto
            {
                BranchId = 1,
                FiscalYearId = 1,
                TransactionTypeId = 2, // QUOTATION
                DocumentDate = DateTime.Today,
                PaymentMethod = VoucherPaymentMethod.Cash,
                WarehouseId = 1,
                Lines = new List<DocumentLineDto>
                {
                    new DocumentLineDto
                    {
                        LineNumber = 1,
                        ItemId = 1,
                        UnitId = 1,
                        Quantity = 100,
                        UnitPrice = 25,
                        WarehouseId = 1
                    }
                }
            };

            var createQuote = await docService.CreateDocumentAsync(quoteDto, "test-user");
            Assert.True(createQuote.Success);
            int quoteId = createQuote.DocumentId!.Value;

            // Post quotation
            await docService.PostDocumentAsync(quoteId, "test-user");

            var quoteDoc = await docService.GetDocumentByIdAsync(quoteId);
            int lineId = quoteDoc!.Lines[0].Id;

            // 2. Partial Conversion: Convert 60 units to Sales Invoice
            var convReq = new ConvertDocumentRequestDto
            {
                SourceDocumentIds = new List<int> { quoteId },
                TargetTransactionTypeId = 1, // SI_CASH
                SelectedLines = new List<ConvertDocumentLineSelectionDto>
                {
                    new ConvertDocumentLineSelectionDto
                    {
                        SourceDocumentLineId = lineId,
                        QuantityToConvert = 60
                    }
                }
            };

            var convRes1 = await convService.ConvertDocumentsAsync(convReq, "test-user", 1);
            Assert.True(convRes1.Success);

            // Check Quote status -> PartiallyConverted
            var quoteAfter1 = await docService.GetDocumentByIdAsync(quoteId);
            Assert.Equal(DocumentConversionStatus.PartiallyConverted, quoteAfter1!.ConversionStatus);
            Assert.Equal(60, quoteAfter1.Lines[0].ConvertedQuantity);
            Assert.Equal(40, quoteAfter1.Lines[0].RemainingQuantity);

            // 3. Over-conversion attempt: Try to convert 50 units (Remaining is only 40) -> Must Fail!
            var overConvReq = new ConvertDocumentRequestDto
            {
                SourceDocumentIds = new List<int> { quoteId },
                TargetTransactionTypeId = 1,
                SelectedLines = new List<ConvertDocumentLineSelectionDto>
                {
                    new ConvertDocumentLineSelectionDto
                    {
                        SourceDocumentLineId = lineId,
                        QuantityToConvert = 50 // Exceeds 40!
                    }
                }
            };

            var overRes = await convService.ConvertDocumentsAsync(overConvReq, "test-user", 1);
            Assert.False(overRes.Success);

            // 4. Convert Remaining 40 units -> Status should become FullyConverted
            var convReq2 = new ConvertDocumentRequestDto
            {
                SourceDocumentIds = new List<int> { quoteId },
                TargetTransactionTypeId = 1,
                SelectedLines = new List<ConvertDocumentLineSelectionDto>
                {
                    new ConvertDocumentLineSelectionDto
                    {
                        SourceDocumentLineId = lineId,
                        QuantityToConvert = 40
                    }
                }
            };

            var convRes2 = await convService.ConvertDocumentsAsync(convReq2, "test-user", 1);
            Assert.True(convRes2.Success);

            var quoteAfter2 = await docService.GetDocumentByIdAsync(quoteId);
            Assert.Equal(DocumentConversionStatus.FullyConverted, quoteAfter2!.ConversionStatus);
            Assert.Equal(100, quoteAfter2.Lines[0].ConvertedQuantity);
            Assert.Equal(0, quoteAfter2.Lines[0].RemainingQuantity);
        }

        [Fact]
        public async Task ConversionService_Should_Return_Allowed_Target_Types()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            await SeedBaseDataAsync(context);
            var (_, _, convService, _) = CreateServices(context);

            context.TransactionTypeConversions.Add(new TransactionTypeConversion
            {
                SourceTransactionTypeId = 1,
                TargetTransactionTypeId = 2,
                IsActive = true
            });
            await context.SaveChangesAsync();

            var targets = await convService.GetAllowedTargetTypesAsync(1);
            Assert.NotEmpty(targets);
            Assert.Contains(targets, t => t.Id == 2);
        }
    }
}
