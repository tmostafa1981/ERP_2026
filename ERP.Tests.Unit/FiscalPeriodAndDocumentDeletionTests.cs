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

namespace ERP.Tests.Unit
{
    public class FiscalPeriodAndDocumentDeletionTests
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

        private (UniversalDocumentService docService, FiscalPeriodService periodService, TransactionTypeService ttService)
            CreateServices(ErpDbContext context, string userId = "test-user-admin")
        {
            var auditMock = new Mock<IAuditService>();
            var codeSeqMock = new Mock<ICodeSequenceService>();

            var currentUserMock = new Mock<ICurrentUserContext>();
            currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
            currentUserMock.Setup(c => c.UserId).Returns(userId);
            currentUserMock.Setup(c => c.BranchId).Returns(1);
            currentUserMock.Setup(c => c.HasAllBranches).Returns(true);

            var cache = new MemoryCache(new MemoryCacheOptions());
            var serialService = new SerialNumberService(context);
            var ttService = new TransactionTypeService(context, cache, currentUserMock.Object, auditMock.Object);
            var periodService = new FiscalPeriodService(context, auditMock.Object);
            var docService = new UniversalDocumentService(context, ttService, serialService, codeSeqMock.Object, currentUserMock.Object, auditMock.Object, periodService);

            return (docService, periodService, ttService);
        }

        private async Task SeedDataAsync(ErpDbContext context)
        {
            var branch = new Branch { Id = 1, Code = "BR01", NameAr = "الفرع الرئيسي", NameEn = "Main Branch", IsActive = true };
            var wh = new Warehouse { Id = 1, BranchId = 1, Code = "WH01", NameAr = "المستودع الرئيسي", NameEn = "Main WH", IsActive = true };
            var curr = new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "EGP", ExchangeRate = 1m, IsActive = true };
            var unit = new ERP.Domain.Entities.Unit { Id = 1, Code = "PCS", NameAr = "قطعة", NameEn = "Piece", IsActive = true };
            var item = new Item { Id = 1, Code = "ITM01", NameAr = "لابتوب ديل", NameEn = "Dell Laptop", TrackingType = ItemTrackingType.None, IsActive = true };

            var fy = new FiscalYear
            {
                Id = 1,
                Year = "2026",
                Name = "2026",
                NameAr = "السنة المالية 2026",
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 12, 31),
                IsActive = true,
                IsClosed = false
            };

            var tt = new TransactionType
            {
                Id = 1,
                Code = "INV01",
                NameAr = "فاتورة بيع",
                NameEn = "Sales Invoice",
                Module = TransactionModule.Sales,
                BaseNature = DocumentBaseNature.Invoice,
                InventoryEffect = InventoryEffect.IssueStock,
                AccountingEffect = AccountingEffect.None,
                IsCashAllowed = true,
                IsCreditAllowed = true,
                DefaultWarehouseId = 1,
                IsActive = true
            };

            context.Branches.Add(branch);
            context.Warehouses.Add(wh);
            context.Currencies.Add(curr);
            context.Units.Add(unit);
            context.Items.Add(item);
            context.FiscalYears.Add(fy);
            context.TransactionTypes.Add(tt);

            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task DeleteDocument_PostedDocument_ShouldRevertAndRemoveSuccessfully()
        {
            var db = CreateDbContext("DeletePostedDocTest");
            await SeedDataAsync(db);
            var (docService, periodService, _) = CreateServices(db);
            await periodService.EnsurePeriodsForYearAsync(1);

            // Create and post document
            var createDto = new DocumentHeaderDto
            {
                BranchId = 1,
                TransactionTypeId = 1,
                DocumentDate = new DateTime(2026, 5, 15),
                WarehouseId = 1,
                CurrencyId = 1,
                PaymentMethod = VoucherPaymentMethod.Cash,
                Lines = new List<DocumentLineDto>
                {
                    new DocumentLineDto { ItemId = 1, Quantity = 2, UnitPrice = 1000 }
                }
            };

            var createRes = await docService.CreateDocumentAsync(createDto, "test-user-admin", postImmediately: true);
            Assert.True(createRes.Success);
            int docId = createRes.DocumentId!.Value;

            var docBefore = await db.DocumentHeaders.FindAsync(docId);
            Assert.NotNull(docBefore);
            Assert.Equal(DocumentStatus.Posted, docBefore.Status);

            // Delete the posted document directly!
            var delRes = await docService.DeleteDocumentAsync(docId, "test-user-admin");
            Assert.True(delRes.Success);

            // Verify document is deleted
            var docAfter = await db.DocumentHeaders.FindAsync(docId);
            Assert.Null(docAfter);
        }

        [Fact]
        public async Task DeleteDocument_WhenFiscalYearIsClosed_ShouldFailWithErrorMessage()
        {
            var db = CreateDbContext("DeleteClosedYearTest");
            await SeedDataAsync(db);
            var (docService, periodService, _) = CreateServices(db);
            await periodService.EnsurePeriodsForYearAsync(1);

            var createDto = new DocumentHeaderDto
            {
                BranchId = 1,
                TransactionTypeId = 1,
                DocumentDate = new DateTime(2026, 5, 15),
                WarehouseId = 1,
                CurrencyId = 1,
                PaymentMethod = VoucherPaymentMethod.Cash,
                Lines = new List<DocumentLineDto>
                {
                    new DocumentLineDto { ItemId = 1, Quantity = 1, UnitPrice = 500 }
                }
            };

            var createRes = await docService.CreateDocumentAsync(createDto, "test-user-admin");
            Assert.True(createRes.Success);
            int docId = createRes.DocumentId!.Value;

            // Close the Fiscal Year
            var fy = await db.FiscalYears.FindAsync(1);
            fy!.IsClosed = true;
            await db.SaveChangesAsync();

            // Attempt deletion
            var delRes = await docService.DeleteDocumentAsync(docId, "test-user-admin");
            Assert.False(delRes.Success);
            Assert.Contains("مغلقة", delRes.Message);
        }

        [Fact]
        public async Task DeleteDocument_WhenFiscalPeriodMonthIsClosed_ShouldFailWithErrorMessage()
        {
            var db = CreateDbContext("DeleteClosedMonthTest");
            await SeedDataAsync(db);
            var (docService, periodService, _) = CreateServices(db);
            await periodService.EnsurePeriodsForYearAsync(1);

            // Document date in May 2026 (Period 5)
            var createDto = new DocumentHeaderDto
            {
                BranchId = 1,
                TransactionTypeId = 1,
                DocumentDate = new DateTime(2026, 5, 10),
                WarehouseId = 1,
                CurrencyId = 1,
                PaymentMethod = VoucherPaymentMethod.Cash,
                Lines = new List<DocumentLineDto>
                {
                    new DocumentLineDto { ItemId = 1, Quantity = 1, UnitPrice = 500 }
                }
            };

            var createRes = await docService.CreateDocumentAsync(createDto, "test-user-admin");
            Assert.True(createRes.Success);
            int docId = createRes.DocumentId!.Value;

            // Close Period 5 (May 2026)
            var periodMay = await db.FiscalPeriods.FirstOrDefaultAsync(p => p.FiscalYearId == 1 && p.PeriodNumber == 5);
            Assert.NotNull(periodMay);
            periodMay.IsClosed = true;
            await db.SaveChangesAsync();

            // Attempt deletion
            var delRes = await docService.DeleteDocumentAsync(docId, "test-user-admin");
            Assert.False(delRes.Success);
            Assert.Contains("مغلق", delRes.Message);
        }

        [Fact]
        public async Task UpdateDocument_WhenFiscalPeriodMonthIsClosed_ShouldFailWithErrorMessage()
        {
            var db = CreateDbContext("UpdateClosedMonthTest");
            await SeedDataAsync(db);
            var (docService, periodService, _) = CreateServices(db);
            await periodService.EnsurePeriodsForYearAsync(1);

            // Document date in March 2026 (Period 3)
            var createDto = new DocumentHeaderDto
            {
                BranchId = 1,
                TransactionTypeId = 1,
                DocumentDate = new DateTime(2026, 3, 10),
                WarehouseId = 1,
                CurrencyId = 1,
                PaymentMethod = VoucherPaymentMethod.Cash,
                Lines = new List<DocumentLineDto>
                {
                    new DocumentLineDto { ItemId = 1, Quantity = 1, UnitPrice = 500 }
                }
            };

            var createRes = await docService.CreateDocumentAsync(createDto, "test-user-admin");
            int docId = createRes.DocumentId!.Value;

            // Close Period 3 (March 2026)
            var periodMarch = await db.FiscalPeriods.FirstOrDefaultAsync(p => p.FiscalYearId == 1 && p.PeriodNumber == 3);
            Assert.NotNull(periodMarch);
            periodMarch.IsClosed = true;
            await db.SaveChangesAsync();

            // Attempt update
            var updateDto = new DocumentHeaderDto
            {
                Id = docId,
                DocumentDate = new DateTime(2026, 3, 10),
                Notes = "محاولة تعديل في شهر مغلق"
            };

            var updateRes = await docService.UpdateDocumentAsync(updateDto, "test-user-admin");
            Assert.False(updateRes.Success);
            Assert.Contains("مغلق", updateRes.Message);
        }

        [Fact]
        public async Task UpdateDocument_InOpenPeriod_SuccessfullyUpdatesAndRegeneratesJournalEntry()
        {
            var db = CreateDbContext("UpdateDocOpenPeriodTest");
            await SeedDataAsync(db);
            var (docService, periodService, _) = CreateServices(db);
            await periodService.EnsurePeriodsForYearAsync(1);

            // Enable accounting effect
            var tt = await db.TransactionTypes.FindAsync(1);
            tt!.AccountingEffect = AccountingEffect.FullInvoiceAndCOGS;
            tt.DefaultDebitAccountId = 1;
            tt.DefaultCreditAccountId = 2;
            db.GLAccounts.AddRange(
                new GLAccount { Id = 1, Code = "1101", NameAr = "الصندوق", NameEn = "Cash", AccountCategory = AccountCategory.Assets, IsActive = true },
                new GLAccount { Id = 2, Code = "4101", NameAr = "المبيعات", NameEn = "Sales", AccountCategory = AccountCategory.Revenues, IsActive = true }
            );
            await db.SaveChangesAsync();

            var createDto = new DocumentHeaderDto
            {
                BranchId = 1,
                TransactionTypeId = 1,
                DocumentDate = new DateTime(2026, 1, 15),
                WarehouseId = 1,
                CurrencyId = 1,
                PaymentMethod = VoucherPaymentMethod.Cash,
                Lines = new List<DocumentLineDto>
                {
                    new DocumentLineDto { ItemId = 1, Quantity = 2, UnitPrice = 1000 }
                }
            };

            var createRes = await docService.CreateDocumentAsync(createDto, "test-user-admin");
            Assert.True(createRes.Success, createRes.Message);
            int docId = createRes.DocumentId!.Value;

            var origDoc = await docService.GetDocumentByIdAsync(docId);
            Assert.NotNull(origDoc);
            Assert.Equal(2000m, origDoc.TotalAmountNet);
            int origJeId = origDoc.JournalEntryHeaderId!.Value;

            // Update document: change quantity to 3 and unit price to 1200 (Total = 3600)
            var updateDto = new DocumentHeaderDto
            {
                Id = docId,
                TransactionTypeId = 1,
                DocumentDate = new DateTime(2026, 1, 16),
                WarehouseId = 1,
                CurrencyId = 1,
                PaymentMethod = VoucherPaymentMethod.Cash,
                Notes = "تعديل الفاتورة بنجاح",
                Lines = new List<DocumentLineDto>
                {
                    new DocumentLineDto { ItemId = 1, Quantity = 3, UnitPrice = 1200 }
                }
            };

            var updateRes = await docService.UpdateDocumentAsync(updateDto, "test-user-admin");
            Assert.True(updateRes.Success, updateRes.Message);

            var updatedDoc = await docService.GetDocumentByIdAsync(docId);
            Assert.NotNull(updatedDoc);
            Assert.Equal(3600m, updatedDoc.TotalAmountNet);
            Assert.Equal("تعديل الفاتورة بنجاح", updatedDoc.Notes);
            Assert.NotNull(updatedDoc.JournalEntryHeaderId);

            // Verify journal entry was regenerated
            var newJe = await db.JournalEntryHeaders.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == updatedDoc.JournalEntryHeaderId.Value);
            Assert.NotNull(newJe);
            Assert.Equal(3600m, newJe.TotalDebitLocal);
            Assert.Equal(3600m, newJe.TotalCreditLocal);
        }

        [Fact]
        public async Task TogglePeriodClose_ShouldChangePeriodStatusAndAudit()
        {
            var db = CreateDbContext("TogglePeriodTest");
            await SeedDataAsync(db);
            var (_, periodService, _) = CreateServices(db);
            await periodService.EnsurePeriodsForYearAsync(1);

            var periods = await periodService.GetPeriodsByYearIdAsync(1);
            Assert.Equal(12, periods.Count);

            var period1 = periods.First(p => p.PeriodNumber == 1);
            Assert.False(period1.IsClosed);

            // Toggle to close
            var closeRes = await periodService.TogglePeriodCloseAsync(period1.Id, "user-admin");
            Assert.True(closeRes.Success);
            Assert.True(closeRes.NewStatus);

            // Toggle to reopen
            var openRes = await periodService.TogglePeriodCloseAsync(period1.Id, "user-admin");
            Assert.True(openRes.Success);
            Assert.False(openRes.NewStatus);
        }
    }
}
