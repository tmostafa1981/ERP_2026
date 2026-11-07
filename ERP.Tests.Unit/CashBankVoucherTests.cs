using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Helpers;
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
    public class CashBankVoucherTests
    {
        private ErpDbContext CreateDbContext(int branchId, bool isSuperAdmin, string dbName)
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

        [Fact]
        public void BranchIsolation_VoucherHeader_OnlySeesOwnBranch()
        {
            var dbName = Guid.NewGuid().ToString();

            // Seed vouchers across two branches as SuperAdmin
            using (var adminDb = CreateDbContext(branchId: 0, isSuperAdmin: true, dbName))
            {
                adminDb.VoucherHeaders.AddRange(
                    new VoucherHeader
                    {
                        Id = 1,
                        BranchId = 1,
                        FiscalYearId = 1,
                        VoucherType = VoucherType.Receipt,
                        VoucherNumber = "RV-0001",
                        VoucherDate = DateTime.Today,
                        PaymentMethod = VoucherPaymentMethod.Cash,
                        CashBankAccountId = 1,
                        CurrencyId = 1,
                        ExchangeRate = 1m,
                        TotalAmountForeign = 500m,
                        TotalAmountLocal = 500m,
                        Status = VoucherStatus.Draft
                    },
                    new VoucherHeader
                    {
                        Id = 2,
                        BranchId = 2,
                        FiscalYearId = 1,
                        VoucherType = VoucherType.Payment,
                        VoucherNumber = "PV-0001",
                        VoucherDate = DateTime.Today,
                        PaymentMethod = VoucherPaymentMethod.BankTransfer,
                        CashBankAccountId = 2,
                        CurrencyId = 1,
                        ExchangeRate = 1m,
                        TotalAmountForeign = 1000m,
                        TotalAmountLocal = 1000m,
                        Status = VoucherStatus.Draft
                    }
                );
                adminDb.SaveChanges();
            }

            // User in Branch 1 must only see Voucher 1
            using (var userDb = CreateDbContext(branchId: 1, isSuperAdmin: false, dbName))
            {
                var vouchers = userDb.VoucherHeaders.ToList();
                Assert.Single(vouchers);
                Assert.Equal("RV-0001", vouchers[0].VoucherNumber);
                Assert.Equal(1, vouchers[0].BranchId);
            }
        }

        [Fact]
        public async Task ReceiptVoucher_PostingCreatesValidDoubleEntry()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(branchId: 1, isSuperAdmin: true, dbName);

            // Seed Fiscal Year, Cash Account, and Customer Control Account
            db.FiscalYears.Add(new FiscalYear { Id = 1, Year = "2026", Name = "FY 2026", IsClosed = false, IsActive = true });
            db.GLAccounts.AddRange(
                new GLAccount { Id = 101, Code = "10101", NameAr = "صندوق الفرع", NameEn = "Branch Cash", IsGroup = false, IsActive = true },
                new GLAccount { Id = 201, Code = "110201", NameAr = "مراقبة عملاء تجاريين", NameEn = "AR Control", IsGroup = false, IsActive = true }
            );
            await db.SaveChangesAsync();

            // Create Receipt Voucher
            var voucher = new VoucherHeader
            {
                Id = 10,
                BranchId = 1,
                FiscalYearId = 1,
                VoucherType = VoucherType.Receipt,
                VoucherNumber = "RV-2026-0001",
                VoucherDate = DateTime.Today,
                PaymentMethod = VoucherPaymentMethod.Cash,
                CashBankAccountId = 101,
                CurrencyId = 1,
                ExchangeRate = 1m,
                TotalAmountForeign = 1500m,
                TotalAmountLocal = 1500m,
                Status = VoucherStatus.Draft,
                Lines = new List<VoucherLine>
                {
                    new VoucherLine
                    {
                        LineNumber = 1,
                        LineType = VoucherLineType.Customer,
                        SubLedgerId = 5,
                        AccountId = 201, // Auto-resolved control account
                        AmountForeign = 1500m,
                        AmountLocal = 1500m,
                        Notes = "سداد دفعة نقدية"
                    }
                }
            };
            db.VoucherHeaders.Add(voucher);
            await db.SaveChangesAsync();

            // Simulate Posting Logic
            var jvHeader = new JournalEntryHeader
            {
                BranchId = voucher.BranchId,
                FiscalYearId = voucher.FiscalYearId,
                EntryDate = voucher.VoucherDate,
                SourceDocumentType = SourceDocumentType.ReceiptVoucher,
                SourceDocumentId = voucher.Id,
                Notes = $"قيد سند قبض {voucher.VoucherNumber}",
                Lines = new List<JournalEntryLine>()
            };

            // Receipt: Debit Cash/Bank
            jvHeader.Lines.Add(new JournalEntryLine
            {
                AccountId = voucher.CashBankAccountId,
                DebitForeign = voucher.TotalAmountForeign,
                DebitLocal = voucher.TotalAmountLocal,
                CreditForeign = 0m,
                CreditLocal = 0m,
                CurrencyId = voucher.CurrencyId,
                ExchangeRate = voucher.ExchangeRate,
                Description = voucher.Notes ?? voucher.VoucherNumber
            });

            // Receipt: Credit Detail Lines
            foreach (var line in voucher.Lines)
            {
                jvHeader.Lines.Add(new JournalEntryLine
                {
                    AccountId = line.AccountId,
                    DebitForeign = 0m,
                    DebitLocal = 0m,
                    CreditForeign = line.AmountForeign,
                    CreditLocal = line.AmountLocal,
                    CurrencyId = voucher.CurrencyId,
                    ExchangeRate = voucher.ExchangeRate,
                    Description = line.Notes ?? voucher.VoucherNumber
                });
            }

            // Validate Double Entry via JournalEntryValidationService
            var validationService = new JournalEntryValidationService(db);
            await validationService.ValidateAsync(jvHeader);

            db.JournalEntryHeaders.Add(jvHeader);
            await db.SaveChangesAsync();

            voucher.Status = VoucherStatus.Posted;
            voucher.JournalEntryHeaderId = jvHeader.Id;
            await db.SaveChangesAsync();

            // Assertions
            Assert.Equal(VoucherStatus.Posted, voucher.Status);
            Assert.NotNull(voucher.JournalEntryHeaderId);
            Assert.Equal(2, jvHeader.Lines.Count);
            Assert.Equal(1500m, jvHeader.Lines.Sum(l => l.DebitLocal));
            Assert.Equal(1500m, jvHeader.Lines.Sum(l => l.CreditLocal));
            Assert.Equal(SourceDocumentType.ReceiptVoucher, jvHeader.SourceDocumentType);
        }

        [Fact]
        public async Task PaymentVoucher_PostingCreatesValidDoubleEntry()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(branchId: 1, isSuperAdmin: true, dbName);

            db.FiscalYears.Add(new FiscalYear { Id = 1, Year = "2026", Name = "FY 2026", IsClosed = false, IsActive = true });
            db.GLAccounts.AddRange(
                new GLAccount { Id = 102, Code = "10201", NameAr = "البنك الأهلي", NameEn = "NBE Bank", IsGroup = false, IsActive = true },
                new GLAccount { Id = 301, Code = "210101", NameAr = "مراقبة موردين تجاريين", NameEn = "AP Control", IsGroup = false, IsActive = true }
            );
            await db.SaveChangesAsync();

            // Create Payment Voucher
            var voucher = new VoucherHeader
            {
                Id = 20,
                BranchId = 1,
                FiscalYearId = 1,
                VoucherType = VoucherType.Payment,
                VoucherNumber = "PV-2026-0001",
                VoucherDate = DateTime.Today,
                PaymentMethod = VoucherPaymentMethod.BankTransfer,
                CashBankAccountId = 102,
                CurrencyId = 1,
                ExchangeRate = 1m,
                TotalAmountForeign = 8000m,
                TotalAmountLocal = 8000m,
                Status = VoucherStatus.Draft,
                Lines = new List<VoucherLine>
                {
                    new VoucherLine
                    {
                        LineNumber = 1,
                        LineType = VoucherLineType.Vendor,
                        SubLedgerId = 12,
                        AccountId = 301,
                        AmountForeign = 8000m,
                        AmountLocal = 8000m,
                        Notes = "سداد مستحقات توريد"
                    }
                }
            };
            db.VoucherHeaders.Add(voucher);
            await db.SaveChangesAsync();

            // Simulate Posting Logic: Payment = Debit Lines, Credit Bank
            var jvHeader = new JournalEntryHeader
            {
                BranchId = voucher.BranchId,
                FiscalYearId = voucher.FiscalYearId,
                EntryDate = voucher.VoucherDate,
                SourceDocumentType = SourceDocumentType.PaymentVoucher,
                SourceDocumentId = voucher.Id,
                Notes = $"قيد سند صرف {voucher.VoucherNumber}",
                Lines = new List<JournalEntryLine>()
            };

            foreach (var line in voucher.Lines)
            {
                jvHeader.Lines.Add(new JournalEntryLine
                {
                    AccountId = line.AccountId,
                    DebitForeign = line.AmountForeign,
                    DebitLocal = line.AmountLocal,
                    CreditForeign = 0m,
                    CreditLocal = 0m,
                    CurrencyId = voucher.CurrencyId,
                    ExchangeRate = voucher.ExchangeRate,
                    Description = line.Notes ?? voucher.VoucherNumber
                });
            }

            jvHeader.Lines.Add(new JournalEntryLine
            {
                AccountId = voucher.CashBankAccountId,
                DebitForeign = 0m,
                DebitLocal = 0m,
                CreditForeign = voucher.TotalAmountForeign,
                CreditLocal = voucher.TotalAmountLocal,
                CurrencyId = voucher.CurrencyId,
                ExchangeRate = voucher.ExchangeRate,
                Description = voucher.Notes ?? voucher.VoucherNumber
            });

            var validationService = new JournalEntryValidationService(db);
            await validationService.ValidateAsync(jvHeader);

            db.JournalEntryHeaders.Add(jvHeader);
            await db.SaveChangesAsync();

            voucher.Status = VoucherStatus.Posted;
            voucher.JournalEntryHeaderId = jvHeader.Id;
            await db.SaveChangesAsync();

            Assert.Equal(VoucherStatus.Posted, voucher.Status);
            Assert.Equal(8000m, jvHeader.Lines.Sum(l => l.DebitLocal));
            Assert.Equal(8000m, jvHeader.Lines.Sum(l => l.CreditLocal));
            Assert.Equal(SourceDocumentType.PaymentVoucher, jvHeader.SourceDocumentType);
        }

        [Fact]
        public async Task Voucher_Unposting_RemovesJournalEntry_AndResetsStatus()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(branchId: 1, isSuperAdmin: true, dbName);

            var jv = new JournalEntryHeader
            {
                Id = 99,
                BranchId = 1,
                FiscalYearId = 1,
                SourceDocumentType = SourceDocumentType.ReceiptVoucher,
                SourceDocumentId = 55,
                EntryDate = DateTime.Today,
                Lines = new List<JournalEntryLine>()
            };
            db.JournalEntryHeaders.Add(jv);

            var voucher = new VoucherHeader
            {
                Id = 55,
                BranchId = 1,
                FiscalYearId = 1,
                VoucherType = VoucherType.Receipt,
                VoucherNumber = "RV-0055",
                VoucherDate = DateTime.Today,
                PaymentMethod = VoucherPaymentMethod.Cash,
                CashBankAccountId = 1,
                TotalAmountForeign = 500m,
                TotalAmountLocal = 500m,
                Status = VoucherStatus.Posted,
                JournalEntryHeaderId = 99
            };
            db.VoucherHeaders.Add(voucher);
            await db.SaveChangesAsync();

            // Unpost operation
            db.JournalEntryHeaders.Remove(jv);
            voucher.JournalEntryHeaderId = null;
            voucher.Status = VoucherStatus.Draft;
            await db.SaveChangesAsync();

            // Verify
            var updatedVoucher = await db.VoucherHeaders.FindAsync(55);
            var deletedJv = await db.JournalEntryHeaders.FindAsync(99);

            Assert.NotNull(updatedVoucher);
            Assert.Equal(VoucherStatus.Draft, updatedVoucher.Status);
            Assert.Null(updatedVoucher.JournalEntryHeaderId);
            Assert.Null(deletedJv);
        }

        [Theory]
        [InlineData(1250.50, "EGP", true, "ألف")]
        [InlineData(1250.50, "EGP", true, "مئتان")]
        [InlineData(500, "USD", true, "خمسمائة")]
        [InlineData(100.25, "SAR", true, "مائة")]
        [InlineData(0, "EGP", true, "صفر")]
        public void CurrencyTafqeetHelper_ConvertToWords_FormatsCorrectly(decimal amount, string currencyCode, bool isArabic, string expectedSub)
        {
            var words = CurrencyTafqeetHelper.ConvertToWords(amount, currencyCode, isArabic);
            Assert.NotNull(words);
            Assert.Contains(expectedSub, words);
        }

        [Fact]
        public void Voucher_MultiCurrency_ExchangeRateCalculation()
        {
            decimal foreignAmount = 150m;
            decimal exchangeRate = 48.50m;

            var line = new VoucherLine
            {
                AmountForeign = foreignAmount,
                AmountLocal = Math.Round(foreignAmount * exchangeRate, 4)
            };

            Assert.Equal(7275.00m, line.AmountLocal);
        }
    }
}
