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
using Moq;
using Xunit;

namespace ERP.Tests.Unit
{
    public class SerialNumberManagementTests
    {
        private ErpDbContext CreateDbContext(string dbName, int branchId = 1, bool isSuperAdmin = true)
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

        private async Task SeedBaseDataAsync(ErpDbContext context)
        {
            context.Branches.Add(new Branch { Id = 1, Code = "BR-01", NameAr = "فرع القاهرة", NameEn = "Cairo Branch", IsActive = true });
            context.Branches.Add(new Branch { Id = 2, Code = "BR-02", NameAr = "فرع الإسكندرية", NameEn = "Alex Branch", IsActive = true });

            context.Warehouses.Add(new Warehouse { Id = 1, BranchId = 1, Code = "WH-01", NameAr = "المستودع الرئيسي", NameEn = "Main WH", IsActive = true });
            context.Warehouses.Add(new Warehouse { Id = 2, BranchId = 2, Code = "WH-02", NameAr = "مستودع إسكندرية", NameEn = "Alex WH", IsActive = true });

            context.Items.Add(new Item
            {
                Id = 10,
                Code = "ITM-LAPTOP",
                NameAr = "لابتوب ديل",
                NameEn = "Dell Laptop",
                TrackingType = ItemTrackingType.SerialNumber,
                IsActive = true
            });

            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task RegisterReceivedSerials_CreatesSerialAndLogsMovement()
        {
            var db = CreateDbContext(nameof(RegisterReceivedSerials_CreatesSerialAndLogsMovement));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            await service.RegisterReceivedSerialsAsync(
                documentId: 101,
                docType: "PINV",
                itemId: 10,
                warehouseId: 1,
                branchId: 1,
                serialNumbers: new[] { "SN-DELL-001" },
                partyId: 50,
                partyName: "شركة التوريدات العالمية",
                docNumber: "PO-2026-001",
                userId: "user-admin");

            // Verify Serial record
            var serial = await db.ItemSerialNumbers.FirstOrDefaultAsync(s => s.SerialNumber == "SN-DELL-001");
            Assert.NotNull(serial);
            Assert.Equal(10, serial.ItemId);
            Assert.Equal(1, serial.BranchId);
            Assert.Equal(1, serial.WarehouseId);
            Assert.Equal(ItemSerialStatus.Available, serial.Status);
            Assert.Equal("Vendor", serial.CurrentPartyType);
            Assert.Equal("شركة التوريدات العالمية", serial.CurrentPartyName);

            // Verify Movement Audit Ledger
            var movement = await db.ItemSerialMovements.FirstOrDefaultAsync(m => m.SerialNumber == "SN-DELL-001");
            Assert.NotNull(movement);
            Assert.Equal(SerialMovementType.PurchaseReceipt, movement.MovementType);
            Assert.Equal(1, movement.ToBranchId);
            Assert.Equal(1, movement.ToWarehouseId);
            Assert.Equal(ItemSerialStatus.Available, movement.NewStatus);
            Assert.Equal("PO-2026-001", movement.DocumentNumber);
            Assert.Equal("user-admin", movement.UserId);
        }

        [Fact]
        public async Task ValidateSerialForReceive_RejectsDuplicateSerialForSameItem()
        {
            var db = CreateDbContext(nameof(ValidateSerialForReceive_RejectsDuplicateSerialForSameItem));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            // First purchase
            await service.RegisterReceivedSerialsAsync(
                documentId: 101, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-UNIQUE-001" });

            // Validate same serial for receive again
            var val = await service.ValidateSerialForReceiveAsync(10, "SN-UNIQUE-001");
            Assert.False(val.IsValid);
            Assert.Contains("مسجل مسبقاً", val.Message);

            // Attempting to register again must throw InvalidOperationException
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RegisterReceivedSerialsAsync(
                    documentId: 102, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                    serialNumbers: new[] { "SN-UNIQUE-001" }));
        }

        [Fact]
        public async Task ValidateSerialForIssue_EnforcesStrictGuards()
        {
            var db = CreateDbContext(nameof(ValidateSerialForIssue_EnforcesStrictGuards));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            await service.RegisterReceivedSerialsAsync(
                documentId: 101, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-GUARD-001" });

            // Guard 1: Non-existent serial
            var resNonExistent = await service.ValidateSerialForIssueAsync(10, 1, 1, "NON-EXISTENT");
            Assert.False(resNonExistent.IsValid);
            Assert.Contains("غير مسجل في النظام", resNonExistent.Message);

            // Guard 2: Wrong Branch (Try selling from Branch 2)
            var resWrongBranch = await service.ValidateSerialForIssueAsync(10, 1, 2, "SN-GUARD-001");
            Assert.False(resWrongBranch.IsValid);
            Assert.Contains("لا يتبع الفرع المحدد", resWrongBranch.Message);

            // Guard 3: Wrong Warehouse (Try selling from Warehouse 2)
            var resWrongWarehouse = await service.ValidateSerialForIssueAsync(10, 2, 1, "SN-GUARD-001");
            Assert.False(resWrongWarehouse.IsValid);
            Assert.Contains("لا يتبع المستودع المحدد", resWrongWarehouse.Message);

            // Valid condition
            var resValid = await service.ValidateSerialForIssueAsync(10, 1, 1, "SN-GUARD-001");
            Assert.True(resValid.IsValid);
        }

        [Fact]
        public async Task MarkSerialsAsSold_UpdatesStatusToSoldAndLogsMovement()
        {
            var db = CreateDbContext(nameof(MarkSerialsAsSold_UpdatesStatusToSoldAndLogsMovement));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            await service.RegisterReceivedSerialsAsync(
                documentId: 101, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-SALE-001" });

            // Sell serial
            await service.MarkSerialsAsIssuedAsync(
                documentId: 201,
                docType: "SINV",
                itemId: 10,
                warehouseId: 1,
                branchId: 1,
                serialNumbers: new[] { "SN-SALE-001" },
                partyId: 99,
                partyName: "العميل أحمد محمود",
                docNumber: "INV-2026-001",
                userId: "cashier-1",
                warrantyMonths: 12);

            var serial = await db.ItemSerialNumbers.FirstOrDefaultAsync(s => s.SerialNumber == "SN-SALE-001");
            Assert.NotNull(serial);
            Assert.Equal(ItemSerialStatus.Sold, serial.Status);
            Assert.Equal("Customer", serial.CurrentPartyType);
            Assert.Equal("العميل أحمد محمود", serial.CurrentPartyName);
            Assert.NotNull(serial.OutDate);
            Assert.Equal(12, serial.WarrantyMonths);
            Assert.NotNull(serial.WarrantyEndDate);

            // Guard: Cannot sell sold serial again
            var resSoldAgain = await service.ValidateSerialForIssueAsync(10, 1, 1, "SN-SALE-001");
            Assert.False(resSoldAgain.IsValid);
            Assert.Contains("تم بيعه مسبقاً", resSoldAgain.Message);

            // Verify Movement
            var saleMovement = await db.ItemSerialMovements.FirstOrDefaultAsync(m => m.MovementType == SerialMovementType.SalesDelivery);
            Assert.NotNull(saleMovement);
            Assert.Equal(1, saleMovement.FromBranchId);
            Assert.Equal(1, saleMovement.FromWarehouseId);
            Assert.Equal(ItemSerialStatus.Sold, saleMovement.NewStatus);
            Assert.Equal("INV-2026-001", saleMovement.DocumentNumber);
            Assert.Equal("العميل أحمد محمود", saleMovement.PartyName);
        }

        [Fact]
        public async Task RegisterSalesReturn_ReturnsSoldSerialToAvailableAtReceivingLocation()
        {
            var db = CreateDbContext(nameof(RegisterSalesReturn_ReturnsSoldSerialToAvailableAtReceivingLocation));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            // Purchase at Branch 1, WH 1
            await service.RegisterReceivedSerialsAsync(
                documentId: 101, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-RETURN-001" });

            // Sell at Branch 1, WH 1
            await service.MarkSerialsAsIssuedAsync(
                documentId: 201, docType: "SINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-RETURN-001" }, partyName: "العميل");

            // Return at Branch 2, WH 2 (returned to another branch / warehouse)
            await service.RegisterSalesReturnSerialsAsync(
                documentId: 301,
                docType: "SRET",
                itemId: 10,
                warehouseId: 2,
                branchId: 2,
                serialNumbers: new[] { "SN-RETURN-001" },
                partyName: "العميل",
                docNumber: "RET-2026-001",
                userId: "return-officer");

            var serial = await db.ItemSerialNumbers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.SerialNumber == "SN-RETURN-001");
            Assert.NotNull(serial);
            Assert.Equal(ItemSerialStatus.Available, serial.Status);
            Assert.Equal(2, serial.BranchId);
            Assert.Equal(2, serial.WarehouseId);
            Assert.Null(serial.OutDate);

            // Now it is available to be sold again from Branch 2, WH 2
            var canSellAgain = await service.ValidateSerialForIssueAsync(10, 2, 2, "SN-RETURN-001");
            Assert.True(canSellAgain.IsValid);

            // Verify return movement
            var retMovement = await db.ItemSerialMovements.FirstOrDefaultAsync(m => m.MovementType == SerialMovementType.SalesReturn);
            Assert.NotNull(retMovement);
            Assert.Equal(1, retMovement.FromWarehouseId);
            Assert.Equal(2, retMovement.ToWarehouseId);
            Assert.Equal(2, retMovement.ToBranchId);
            Assert.Equal(ItemSerialStatus.Available, retMovement.NewStatus);
        }

        [Fact]
        public async Task RegisterSalesReturn_RejectsSerialThatWasNeverSold()
        {
            var db = CreateDbContext(nameof(RegisterSalesReturn_RejectsSerialThatWasNeverSold));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            // Purchase (Available in stock, never sold)
            await service.RegisterReceivedSerialsAsync(
                documentId: 101, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-NOT-SOLD-001" });

            // Attempting to return it without selling must fail!
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RegisterSalesReturnSerialsAsync(
                    documentId: 301, docType: "SRET", itemId: 10, warehouseId: 1, branchId: 1,
                    serialNumbers: new[] { "SN-NOT-SOLD-001" }));
        }

        [Fact]
        public async Task TransferSerialsDirect_UpdatesLocationAndLogsMovement()
        {
            var db = CreateDbContext(nameof(TransferSerialsDirect_UpdatesLocationAndLogsMovement));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            await service.RegisterReceivedSerialsAsync(
                documentId: 101, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-TRF-001" });

            // Direct transfer from Branch 1, WH 1 -> Branch 2, WH 2
            await service.TransferSerialsDirectAsync(
                documentId: 401,
                docType: "TRF",
                itemId: 10,
                fromWarehouseId: 1,
                fromBranchId: 1,
                toWarehouseId: 2,
                toBranchId: 2,
                serialNumbers: new[] { "SN-TRF-001" },
                docNumber: "TRF-2026-001",
                userId: "wh-manager");

            var serial = await db.ItemSerialNumbers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.SerialNumber == "SN-TRF-001");
            Assert.NotNull(serial);
            Assert.Equal(2, serial.BranchId);
            Assert.Equal(2, serial.WarehouseId);
            Assert.Equal(ItemSerialStatus.Available, serial.Status);

            var trfMovement = await db.ItemSerialMovements.FirstOrDefaultAsync(m => m.MovementType == SerialMovementType.TransferDirect);
            Assert.NotNull(trfMovement);
            Assert.Equal(1, trfMovement.FromBranchId);
            Assert.Equal(1, trfMovement.FromWarehouseId);
            Assert.Equal(2, trfMovement.ToBranchId);
            Assert.Equal(2, trfMovement.ToWarehouseId);
        }

        [Fact]
        public async Task GetSerialDetails_ReturnsCompleteProfileAndHistoryTimeline()
        {
            var db = CreateDbContext(nameof(GetSerialDetails_ReturnsCompleteProfileAndHistoryTimeline));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            // 1. Purchase
            await service.RegisterReceivedSerialsAsync(
                101, "PINV", 10, 1, 1, new[] { "SN-LIFECYCLE-1" },
                partyId: 50, partyName: "المورد الأجنبي", docNumber: "PO-100", userId: "buyer");

            // 2. Transfer
            await service.TransferSerialsDirectAsync(
                102, "TRF", 10, 1, 1, 2, 2, new[] { "SN-LIFECYCLE-1" },
                docNumber: "TRF-100", userId: "driver");

            // 3. Sale
            await service.MarkSerialsAsIssuedAsync(
                103, "SINV", 10, 2, 2, new[] { "SN-LIFECYCLE-1" },
                partyId: 80, partyName: "العميل النهائي", docNumber: "INV-100", userId: "seller", warrantyMonths: 24);

            // Inquiry
            var details = await service.GetSerialDetailsAsync("SN-LIFECYCLE-1");
            Assert.NotNull(details);
            Assert.Equal("SN-LIFECYCLE-1", details.SerialNumber);
            Assert.Equal("ITM-LAPTOP", details.ItemCode);
            Assert.Equal(ItemSerialStatus.Sold, details.Status);
            Assert.Equal("العميل النهائي", details.CustomerName);
            Assert.Equal(24, details.WarrantyMonths);
            Assert.True(details.IsWarrantyValid);

            // Check Movements Timeline (3 movements ordered descending)
            Assert.Equal(3, details.Movements.Count);
            Assert.Equal(SerialMovementType.SalesDelivery, details.Movements[0].MovementType);
            Assert.Equal(SerialMovementType.TransferDirect, details.Movements[1].MovementType);
            Assert.Equal(SerialMovementType.PurchaseReceipt, details.Movements[2].MovementType);
        }

        [Fact]
        public async Task RevertReceivedSerials_ThrowsWhenSerialIsSold()
        {
            var db = CreateDbContext(nameof(RevertReceivedSerials_ThrowsWhenSerialIsSold));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            // 1. Purchase 2 serials
            await service.RegisterReceivedSerialsAsync(
                documentId: 201, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-PROTECT-1", "SN-PROTECT-2" },
                partyId: 50, partyName: "المورد", docNumber: "PUR-001", userId: "buyer");

            // 2. Sell 1 serial
            await service.MarkSerialsAsIssuedAsync(
                documentId: 301, docType: "SINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-PROTECT-1" },
                partyId: 80, partyName: "العميل", docNumber: "SAL-001", userId: "seller");

            // 3. Attempt to revert purchase document containing sold serial
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RevertReceivedSerialsAsync(201, "PINV"));

            Assert.Contains("SN-PROTECT-1", ex.Message);

            // Verify both serials still exist and SN-PROTECT-1 remains Sold
            var s1 = await db.ItemSerialNumbers.FirstOrDefaultAsync(s => s.SerialNumber == "SN-PROTECT-1");
            Assert.NotNull(s1);
            Assert.Equal(ItemSerialStatus.Sold, s1.Status);
        }

        [Fact]
        public async Task RevertReceivedSerials_SucceedsWhenAllSerialsAreAvailable()
        {
            var db = CreateDbContext(nameof(RevertReceivedSerials_SucceedsWhenAllSerialsAreAvailable));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            // 1. Purchase 2 serials
            await service.RegisterReceivedSerialsAsync(
                documentId: 202, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-AVAIL-1", "SN-AVAIL-2" },
                partyId: 50, partyName: "المورد", docNumber: "PUR-002", userId: "buyer");

            // 2. Revert when both are available
            await service.RevertReceivedSerialsAsync(202, "PINV");

            // Verify serials and movements were cleaned up
            var count = await db.ItemSerialNumbers.CountAsync(s => s.SourceDocId == 202);
            Assert.Equal(0, count);

            var movCount = await db.ItemSerialMovements.CountAsync(m => m.SerialNumber == "SN-AVAIL-1" || m.SerialNumber == "SN-AVAIL-2");
            Assert.Equal(0, movCount);
        }

        [Fact]
        public async Task Case1_Paste100SerialsFromExcel_ValidatesAndAddsSuccessfully()
        {
            var db = CreateDbContext(nameof(Case1_Paste100SerialsFromExcel_ValidatesAndAddsSuccessfully));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            // Simulate Excel paste with 100 serials separated by Newlines and Tabs
            var excelLines = Enumerable.Range(1, 100)
                .Select(i => $"00{i:D6}")
                .ToList();
            var excelPasteBlock = string.Join("\r\n",
                excelLines.Chunk(4).Select(row => string.Join("\t", row)));

            var validationRes = await service.ValidateBatchForReceiveAsync(new SerialBatchValidationRequestDto
            {
                ItemId = 10,
                WarehouseId = 1,
                BranchId = 1,
                RawSerials = new List<string> { excelPasteBlock }
            });

            Assert.Equal(100, validationRes.TotalCount);
            Assert.Equal(100, validationRes.ValidCount);
            Assert.Equal(0, validationRes.DuplicateCount);
            Assert.Equal(0, validationRes.ExistingCount);
            Assert.Equal(0, validationRes.InvalidCount);
            Assert.All(validationRes.Results, r =>
            {
                Assert.True(r.IsValid);
                Assert.Equal(SerialPasteValidationStatus.Valid, r.Status);
                Assert.Equal("صالح للإضافة", r.StatusTextAr);
                Assert.StartsWith("00", r.SerialNumber); // Leading zeros preserved
            });

            var validSerialsToSave = validationRes.Results.Where(r => r.IsValid).Select(r => r.SerialNumber).ToList();
            await service.RegisterReceivedSerialsAsync(
                documentId: 501, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: validSerialsToSave, docNumber: "PO-EXCEL-100");

            var savedCount = await db.ItemSerialNumbers.CountAsync(s => s.ItemId == 10 && s.SourceDocId == 501);
            Assert.Equal(100, savedCount);
            var firstSaved = await db.ItemSerialNumbers.FirstOrDefaultAsync(s => s.SerialNumber == "00000001");
            Assert.NotNull(firstSaved);
        }

        [Fact]
        public async Task Case2_PasteSerialsWithEmptyRowsSpacesAndLeadingZeros_ExtractsCleanly()
        {
            var db = CreateDbContext(nameof(Case2_PasteSerialsWithEmptyRowsSpacesAndLeadingZeros_ExtractsCleanly));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            var rawPaste = "\r\n   \t  \r\n  00098765  \t   00100200  \r\n\r\n  SN-ABC-01 , SN-ABC-02 ;  00005555  \n   ";

            var res = await service.ValidateBatchForReceiveAsync(new SerialBatchValidationRequestDto
            {
                ItemId = 10,
                WarehouseId = 1,
                BranchId = 1,
                RawSerials = new List<string> { rawPaste }
            });

            Assert.Equal(5, res.TotalCount);
            Assert.Equal(5, res.ValidCount);
            Assert.Equal("00098765", res.Results[0].SerialNumber);
            Assert.Equal("00100200", res.Results[1].SerialNumber);
            Assert.Equal("SN-ABC-01", res.Results[2].SerialNumber);
            Assert.Equal("SN-ABC-02", res.Results[3].SerialNumber);
            Assert.Equal("00005555", res.Results[4].SerialNumber);
        }

        [Fact]
        public async Task Case3_And_Case4_PasteSerialsWithDuplicatesAndAlreadyExisting_ClassifiesAccurately()
        {
            var db = CreateDbContext(nameof(Case3_And_Case4_PasteSerialsWithDuplicatesAndAlreadyExisting_ClassifiesAccurately));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            // Seed an already existing serial in DB
            await service.RegisterReceivedSerialsAsync(
                documentId: 601, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-EXIST-001" });

            var rawPaste = "SN-NEW-001\r\nSN-NEW-002\tSN-NEW-001\r\nSN-EXIST-001\r\nSN-IN-LINE-001";

            var res = await service.ValidateBatchForReceiveAsync(new SerialBatchValidationRequestDto
            {
                ItemId = 10,
                WarehouseId = 1,
                BranchId = 1,
                RawSerials = new List<string> { rawPaste },
                ExistingLineSerials = new List<string> { "SN-IN-LINE-001" }
            });

            Assert.Equal(5, res.TotalCount);
            Assert.Equal(2, res.ValidCount);      // SN-NEW-001 (first), SN-NEW-002
            Assert.Equal(2, res.DuplicateCount);  // SN-NEW-001 (second), SN-IN-LINE-001
            Assert.Equal(1, res.ExistingCount);   // SN-EXIST-001

            Assert.Equal(SerialPasteValidationStatus.Valid, res.Results[0].Status);
            Assert.Equal("صالح للإضافة", res.Results[0].StatusTextAr);

            Assert.Equal(SerialPasteValidationStatus.Valid, res.Results[1].Status);

            Assert.Equal(SerialPasteValidationStatus.DuplicateInPaste, res.Results[2].Status);
            Assert.Equal("مكرر في البيانات الملصقة", res.Results[2].StatusTextAr);

            Assert.Equal(SerialPasteValidationStatus.AlreadyExists, res.Results[3].Status);
            Assert.Equal("موجود مسبقًا", res.Results[3].StatusTextAr);
        }

        [Fact]
        public async Task Case5_And_Case6_GetAvailableSerialsPaged_FiltersByWarehouseStatusReservationAndSupportsSearch()
        {
            var db = CreateDbContext(nameof(Case5_And_Case6_GetAvailableSerialsPaged_FiltersByWarehouseStatusReservationAndSupportsSearch));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            // Add serials in Warehouse 1 (Branch 1)
            await service.RegisterReceivedSerialsAsync(
                documentId: 701, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-WH1-001", "SN-WH1-002", "SN-WH1-003", "SN-WH1-SOLD", "SN-WH1-RSV" });

            // Add serials in Warehouse 2 (Branch 2)
            await service.RegisterReceivedSerialsAsync(
                documentId: 702, docType: "PINV", itemId: 10, warehouseId: 2, branchId: 2,
                serialNumbers: new[] { "SN-WH2-001" });

            // Mark SN-WH1-SOLD as Sold
            await service.MarkSerialsAsIssuedAsync(
                documentId: 703, docType: "SINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-WH1-SOLD" });

            // Mark SN-WH1-RSV as Reserved
            var rsv = await db.ItemSerialNumbers.FirstAsync(s => s.SerialNumber == "SN-WH1-RSV");
            rsv.Status = ItemSerialStatus.Reserved;
            rsv.ReservedDocId = 999;
            await db.SaveChangesAsync();

            // Case 5: Query available serials for Item 10, Warehouse 1, Branch 1
            var pageRes = await service.GetAvailableSerialsPagedAsync(new AvailableSerialsQueryDto
            {
                ItemId = 10,
                WarehouseId = 1,
                BranchId = 1,
                PageNumber = 1,
                PageSize = 50
            });

            // Only the 3 truly available serials in WH 1 should appear (excludes WH2, Sold, and Reserved)
            Assert.Equal(3, pageRes.TotalCount);
            Assert.Equal(3, pageRes.Items.Count);
            Assert.DoesNotContain(pageRes.Items, x => x.SerialNumber == "SN-WH2-001");
            Assert.DoesNotContain(pageRes.Items, x => x.SerialNumber == "SN-WH1-SOLD");
            Assert.DoesNotContain(pageRes.Items, x => x.SerialNumber == "SN-WH1-RSV");

            // Case 6: Server-side search by partial serial code
            var searchRes = await service.GetAvailableSerialsPagedAsync(new AvailableSerialsQueryDto
            {
                ItemId = 10,
                WarehouseId = 1,
                BranchId = 1,
                Search = "002",
                PageNumber = 1,
                PageSize = 50
            });

            Assert.Equal(1, searchRes.TotalCount);
            Assert.Single(searchRes.Items);
            Assert.Equal("SN-WH1-002", searchRes.Items[0].SerialNumber);
        }

        [Fact]
        public async Task Case7_Case8_Case9_IssueMultipleSerials_AndRejectWrongWarehouseOrAlreadyIssued()
        {
            var db = CreateDbContext(nameof(Case7_Case8_Case9_IssueMultipleSerials_AndRejectWrongWarehouseOrAlreadyIssued));
            await SeedBaseDataAsync(db);
            var service = new SerialNumberService(db);

            await service.RegisterReceivedSerialsAsync(
                documentId: 801, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-MULTI-1", "SN-MULTI-2", "SN-MULTI-3" });

            await service.RegisterReceivedSerialsAsync(
                documentId: 802, docType: "PINV", itemId: 10, warehouseId: 2, branchId: 1,
                serialNumbers: new[] { "SN-OTHER-WH" });

            // Case 7: Select multiple serials and issue them
            await service.MarkSerialsAsIssuedAsync(
                documentId: 803, docType: "SINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-MULTI-1", "SN-MULTI-2" });

            var availAfter = await service.GetAvailableSerialsPagedAsync(new AvailableSerialsQueryDto
            {
                ItemId = 10, WarehouseId = 1, BranchId = 1
            });
            Assert.Equal(1, availAfter.TotalCount);
            Assert.Equal("SN-MULTI-3", availAfter.Items[0].SerialNumber);

            // Case 8: Attempt to issue a serial from a different warehouse
            var exWrongWh = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.MarkSerialsAsIssuedAsync(
                    documentId: 804, docType: "SINV", itemId: 10, warehouseId: 1, branchId: 1,
                    serialNumbers: new[] { "SN-OTHER-WH" }));
            Assert.Contains("SN-OTHER-WH", exWrongWh.Message);
            Assert.Contains("مستودع", exWrongWh.Message);

            // Case 9: Attempt to issue a serial that was already issued
            var exAlreadySold = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.MarkSerialsAsIssuedAsync(
                    documentId: 805, docType: "SINV", itemId: 10, warehouseId: 1, branchId: 1,
                    serialNumbers: new[] { "SN-MULTI-1" }));
            Assert.Contains("SN-MULTI-1", exAlreadySold.Message);
            Assert.Contains("مسبقاً", exAlreadySold.Message);
        }

        [Fact]
        public async Task Case10_ConcurrentUsersIssuingSameSerial_PreventsRaceCondition()
        {
            var dbName = nameof(Case10_ConcurrentUsersIssuingSameSerial_PreventsRaceCondition);
            var seedDb = CreateDbContext(dbName);
            await SeedBaseDataAsync(seedDb);
            var seedService = new SerialNumberService(seedDb);

            await seedService.RegisterReceivedSerialsAsync(
                documentId: 901, docType: "PINV", itemId: 10, warehouseId: 1, branchId: 1,
                serialNumbers: new[] { "SN-RACE-001" });

            // Two independent DbContexts simulating two concurrent users trying to issue SN-RACE-001 at the same time
            var dbUser1 = CreateDbContext(dbName);
            var dbUser2 = CreateDbContext(dbName);

            // Both contexts pre-load the serial while it's still Available
            var pre1 = await dbUser1.ItemSerialNumbers.FirstAsync(s => s.SerialNumber == "SN-RACE-001");
            var pre2 = await dbUser2.ItemSerialNumbers.FirstAsync(s => s.SerialNumber == "SN-RACE-001");
            Assert.Equal(ItemSerialStatus.Available, pre1.Status);
            Assert.Equal(ItemSerialStatus.Available, pre2.Status);

            var serviceUser1 = new SerialNumberService(dbUser1);
            var serviceUser2 = new SerialNumberService(dbUser2);

            var task1 = Task.Run(async () =>
            {
                await serviceUser1.MarkSerialsAsIssuedAsync(
                    documentId: 902, docType: "SINV", itemId: 10, warehouseId: 1, branchId: 1,
                    serialNumbers: new[] { "SN-RACE-001" }, userId: "user-1");
            });

            var task2 = Task.Run(async () =>
            {
                await serviceUser2.MarkSerialsAsIssuedAsync(
                    documentId: 903, docType: "SINV", itemId: 10, warehouseId: 1, branchId: 1,
                    serialNumbers: new[] { "SN-RACE-001" }, userId: "user-2");
            });

            var results = await Task.WhenAll(
                task1.ContinueWith(t => t.Exception?.InnerException),
                task2.ContinueWith(t => t.Exception?.InnerException));

            // Exactly one must succeed (null exception) and one must fail with InvalidOperationException naming SN-RACE-001
            var successes = results.Count(ex => ex == null);
            var failures = results.Where(ex => ex != null).ToList();

            Assert.Equal(1, successes);
            Assert.Single(failures);
            Assert.IsType<InvalidOperationException>(failures[0]);
            Assert.Contains("SN-RACE-001", failures[0]!.Message);
        }
    }
}
