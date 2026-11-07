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
    public class DocumentAdjustmentsTests
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

        private (UniversalDocumentService docService, DocumentAdjustmentService adjService, TransactionTypeService ttService)
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
            var adjService = new DocumentAdjustmentService(context);

            return (docService, adjService, ttService);
        }

        private async Task SeedDataAsync(ErpDbContext context)
        {
            if (!await context.Branches.AnyAsync())
            {
                context.Branches.Add(new Branch { Id = 1, Code = "BR01", NameAr = "الفرع الرئيسي", NameEn = "Main Branch", IsActive = true });
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
                    IsActive = true,
                    IsClosed = false
                });
            }

            if (!await context.Currencies.AnyAsync())
            {
                context.Currencies.Add(new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "EGP", ExchangeRate = 1.0m, IsActive = true });
            }

            if (!await context.GLAccounts.AnyAsync())
            {
                context.GLAccounts.AddRange(
                    new GLAccount { Id = 101, Code = "1101", NameAr = "العملاء", NameEn = "Accounts Receivable", AccountCategory = AccountCategory.Assets, AccountNature = AccountNature.Debit, BranchId = 1, IsActive = true },
                    new GLAccount { Id = 102, Code = "4101", NameAr = "المبيعات", NameEn = "Sales Revenue", AccountCategory = AccountCategory.Revenues, AccountNature = AccountNature.Credit, BranchId = 1, IsActive = true },
                    new GLAccount { Id = 103, Code = "2105", NameAr = "ضريبة القيمة المضافة", NameEn = "VAT", AccountCategory = AccountCategory.Liabilities, AccountNature = AccountNature.Credit, BranchId = 1, IsActive = true },
                    new GLAccount { Id = 104, Code = "4201", NameAr = "إيرادات شحن ونقل", NameEn = "Shipping Revenue", AccountCategory = AccountCategory.Revenues, AccountNature = AccountNature.Credit, BranchId = 1, IsActive = true },
                    new GLAccount { Id = 105, Code = "5201", NameAr = "خصم تجاري مسموح به", NameEn = "Commercial Discount Given", AccountCategory = AccountCategory.Expenses, AccountNature = AccountNature.Debit, BranchId = 1, IsActive = true }
                );
            }

            if (!await context.Warehouses.AnyAsync())
            {
                context.Warehouses.Add(new Warehouse { Id = 1, Code = "WH01", NameAr = "مخزن 1", NameEn = "WH 1", BranchId = 1, IsActive = true });
            }

            if (!await context.Units.AnyAsync())
            {
                context.Units.Add(new UnitEntity { Id = 1, Code = "PCS", NameAr = "قطعة", NameEn = "Piece", IsActive = true });
            }

            if (!await context.ItemGroups.AnyAsync())
            {
                context.ItemGroups.Add(new ItemGroup { Id = 1, Code = "G01", NameAr = "عام", NameEn = "General", IsActive = true });
            }

            if (!await context.Items.AnyAsync())
            {
                context.Items.Add(new Item
                {
                    Id = 1,
                    Code = "ITEM-001",
                    NameAr = "شاشة كمبيوتر",
                    NameEn = "Monitor",
                    ItemGroupId = 1,
                    TrackingType = ItemTrackingType.None,
                    IsActive = true
                });
            }

            if (!await context.Customers.AnyAsync())
            {
                context.Customers.Add(new Customer
                {
                    Id = 1,
                    Code = "CUST-001",
                    NameAr = "شركة الأمل",
                    NameEn = "Al Amal Co",
                    BranchId = 1,
                    FinancialAccountId = 101,
                    IsActive = true
                });
            }

            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task FlexibleChargesAndDiscounts_ConfigurationAndCalculation_Succeeds()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            var (docService, adjService, ttService) = CreateServices(context);
            await SeedDataAsync(context);

            // 1. Create Transaction Type
            var tt = new TransactionType
            {
                Code = "INV-SLS",
                NameAr = "فاتورة مبيعات قابلة للتعديل",
                NameEn = "Flexible Sales Invoice",
                Module = TransactionModule.Sales,
                BaseNature = DocumentBaseNature.Invoice,
                InventoryEffect = InventoryEffect.None,
                AccountingEffect = AccountingEffect.FullInvoiceAndCOGS,
                IsCreditAllowed = true,
                AutoPost = true,
                DefaultCreditAccountId = 102, // Revenue
                DefaultDebitAccountId = 101,  // Party/AR
                DefaultTaxAccountId = 103,    // Tax
                IsActive = true
            };
            await ttService.CreateTransactionTypeAsync(tt);

            // 2. Create Master Adjustments: Shipping (Addition) and Commercial Discount (Discount)
            var shipping = await adjService.CreateAdjustmentAsync(new CreateDocumentAdjustmentDto
            {
                Code = "CHG_SHIPPING",
                NameAr = "مصاريف شحن ونقل",
                NameEn = "Shipping Charges",
                AdjustmentType = AdjustmentType.Addition,
                CalculationMethod = AdjustmentCalculationMethod.Amount,
                DefaultValue = 150m,
                GLAccountId = 104,
                IsActive = true
            });

            var discount = await adjService.CreateAdjustmentAsync(new CreateDocumentAdjustmentDto
            {
                Code = "DISC_COMM",
                NameAr = "خصم تجاري خاص",
                NameEn = "Special Commercial Discount",
                AdjustmentType = AdjustmentType.Discount,
                CalculationMethod = AdjustmentCalculationMethod.Amount,
                DefaultValue = 50m,
                GLAccountId = 105,
                IsActive = true
            });

            // 3. Configure Transaction Type Adjustments
            await adjService.SaveTransactionTypeAdjustmentConfigAsync(new TransactionTypeAdjustmentConfigDto
            {
                TransactionTypeId = tt.Id,
                Adjustments = new List<DocumentAdjustmentSelectionDto>
                {
                    new() { DocumentAdjustmentId = shipping.Id, IsAllowed = true, DefaultValue = 150m },
                    new() { DocumentAdjustmentId = discount.Id, IsAllowed = true, DefaultValue = 50m }
                }
            });

            var allowed = await adjService.GetAllowedAdjustmentsByTransactionTypeAsync(tt.Id);
            Assert.Equal(2, allowed.Count);

            // 4. Create Document with item: Qty 2 * 1000 = 2000, Tax 14% = 280
            // Plus Shipping 150 (Addition), Minus Discount 50 (Discount)
            // Totals formula:
            // Subtotal = 2000
            // Additions = 150
            // Discounts = 50
            // Tax = 280
            // Net = 2000 - 50 + 150 + 280 = 2380
            var createDocDto = new DocumentHeaderDto
            {
                BranchId = 1,
                FiscalYearId = 1,
                TransactionTypeId = tt.Id,
                DocumentDate = DateTime.UtcNow,
                PartyType = DocumentPartyType.Customer,
                PartyId = 1,
                PaymentMethod = VoucherPaymentMethod.Cash,
                CurrencyId = 1,
                ExchangeRate = 1.0m,
                Lines = new List<DocumentLineDto>
                {
                    new()
                    {
                        LineNumber = 1,
                        ItemId = 1,
                        UnitId = 1,
                        Quantity = 2,
                        UnitPrice = 1000m,
                        TaxRate = 14m
                    }
                },
                Adjustments = new List<DocumentAdjustmentLineDto>
                {
                    new()
                    {
                        DocumentAdjustmentId = shipping.Id,
                        AdjustmentType = AdjustmentType.Addition,
                        CalculationMethod = AdjustmentCalculationMethod.Amount,
                        Amount = 150m,
                        GLAccountId = 104
                    },
                    new()
                    {
                        DocumentAdjustmentId = discount.Id,
                        AdjustmentType = AdjustmentType.Discount,
                        CalculationMethod = AdjustmentCalculationMethod.Amount,
                        Amount = 50m,
                        GLAccountId = 105
                    }
                }
            };

            var res = await docService.CreateDocumentAsync(createDocDto, "test-user-1", postImmediately: true);
            Assert.True(res.Success, res.Message);
            Assert.NotNull(res.DocumentId);

            var doc = await docService.GetDocumentByIdAsync(res.DocumentId.Value);
            Assert.NotNull(doc);
            Assert.Equal(2000m, doc.TotalAmountBeforeTax);
            Assert.Equal(50m, doc.DiscountAmount);
            Assert.Equal(150m, doc.TotalAdditions);
            Assert.Equal(280m, doc.TaxAmount);
            Assert.Equal(2380m, doc.TotalAmountNet);
            Assert.Equal(DocumentStatus.Posted, doc.Status);
            Assert.NotNull(doc.JournalEntryHeaderId);

            // 5. Verify Accounting Journal Entry
            var je = await context.JournalEntryHeaders
                .Include(j => j.Lines)
                .FirstOrDefaultAsync(j => j.Id == doc.JournalEntryHeaderId.Value);

            Assert.NotNull(je);
            Assert.Equal(JournalEntryStatus.Posted, je.Status);

            // Journal Entry Balances (Option 2 - Cash with Customer passes through customer subledger and settles immediately against cash):
            // Debit:
            //   - Customer/Party: 2380 (Net)
            //   - Cash Box: 2380 (Immediate Cash Collection)
            //   - Discount Expense (GL 105): 50
            //   Total Debit = 4810
            // Credit:
            //   - Customer/Party: 2380 (Immediate Settlement Offset)
            //   - Revenue (GL 102): 2000
            //   - Shipping Revenue (GL 104): 150
            //   - VAT (GL 103): 280
            //   Total Credit = 4810
            var sumDebit = je.Lines.Sum(l => l.DebitLocal);
            var sumCredit = je.Lines.Sum(l => l.CreditLocal);

            Assert.Equal(4810m, sumDebit);
            Assert.Equal(4810m, sumCredit);
            Assert.Equal(sumDebit, sumCredit);

            // Verify individual lines
            var customerDebitLine = je.Lines.FirstOrDefault(l => l.AccountId == 101 && l.DebitLocal > 0);
            Assert.NotNull(customerDebitLine);
            Assert.Equal(2380m, customerDebitLine.DebitLocal);
            Assert.Equal(SubLedgerType.Customer, customerDebitLine.SubLedgerType);
            Assert.Equal(1, customerDebitLine.SubLedgerId);

            var customerCreditLine = je.Lines.FirstOrDefault(l => l.AccountId == 101 && l.CreditLocal > 0);
            Assert.NotNull(customerCreditLine);
            Assert.Equal(2380m, customerCreditLine.CreditLocal);
            Assert.Equal(SubLedgerType.Customer, customerCreditLine.SubLedgerType);
            Assert.Equal(1, customerCreditLine.SubLedgerId);

            var revenueLine = je.Lines.FirstOrDefault(l => l.AccountId == 102);
            Assert.NotNull(revenueLine);
            Assert.Equal(2000m, revenueLine.CreditLocal);

            var vatLine = je.Lines.FirstOrDefault(l => l.AccountId == 103);
            Assert.NotNull(vatLine);
            Assert.Equal(280m, vatLine.CreditLocal);

            var shippingLine = je.Lines.FirstOrDefault(l => l.AccountId == 104);
            Assert.NotNull(shippingLine);
            Assert.Equal(150m, shippingLine.CreditLocal);

            var discountLine = je.Lines.FirstOrDefault(l => l.AccountId == 105);
            Assert.NotNull(discountLine);
            Assert.Equal(50m, discountLine.DebitLocal);
        }
    }
}
