using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERP.Tests.Unit
{
    public class AccountingValidationTests
    {
        private ErpDbContext CreateDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new ErpDbContext(options);
        }


        [Fact]
        public async Task ValidateAsync_ThrowsWhenDebitLocalDoesNotEqualCreditLocal()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(dbName);

            // Open fiscal year
            db.FiscalYears.Add(new FiscalYear { Id = 1, Year = "2026", Name = "FY 2026", IsClosed = false, IsActive = true });
            // Add GLAccounts
            db.GLAccounts.AddRange(
                new GLAccount { Id = 1, Code = "101", NameAr = "نقدية", NameEn = "Cash", IsGroup = false, IsActive = true, AccountUsage = AccountUsage.All },
                new GLAccount { Id = 2, Code = "201", NameAr = "موردين", NameEn = "AP", IsGroup = false, IsActive = true, AccountUsage = AccountUsage.All }
            );
            await db.SaveChangesAsync();

            var service = new JournalEntryValidationService(db);

            var header = new JournalEntryHeader
            {
                FiscalYearId = 1,
                SourceDocumentType = SourceDocumentType.ManualJV,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine { AccountId = 1, DebitLocal = 1000m, CreditLocal = 0m, CurrencyId = 1, ExchangeRate = 1m },
                    new JournalEntryLine { AccountId = 2, DebitLocal = 0m, CreditLocal = 900m, CurrencyId = 1, ExchangeRate = 1m } // Unbalanced!
                }
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateAsync(header));
            Assert.Contains("غير متوازن محلياً", ex.Message);
        }

        [Fact]
        public async Task ValidateAsync_ThrowsWhenFiscalYearIsClosed()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(dbName);

            db.FiscalYears.Add(new FiscalYear { Id = 1, Year = "2025", Name = "FY 2025", IsClosed = true, IsActive = true });
            await db.SaveChangesAsync();

            var service = new JournalEntryValidationService(db);

            var header = new JournalEntryHeader
            {
                FiscalYearId = 1,
                SourceDocumentType = SourceDocumentType.ManualJV,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine { AccountId = 1, DebitLocal = 100m, CreditLocal = 0m },
                    new JournalEntryLine { AccountId = 2, DebitLocal = 0m, CreditLocal = 100m }
                }
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateAsync(header));
            Assert.Contains("سنة مالية مغلقة", ex.Message);
        }

        [Fact]
        public async Task ValidateAsync_ThrowsWhenPostingToGroupAccount()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(dbName);

            db.FiscalYears.Add(new FiscalYear { Id = 1, Year = "2026", Name = "FY 2026", IsClosed = false, IsActive = true });
            db.GLAccounts.AddRange(
                new GLAccount { Id = 10, Code = "11", NameAr = "الأصول المتداولة", NameEn = "Current Assets", IsGroup = true, IsActive = true },
                new GLAccount { Id = 2, Code = "201", NameAr = "موردين", NameEn = "AP", IsGroup = false, IsActive = true }
            );
            await db.SaveChangesAsync();

            var service = new JournalEntryValidationService(db);

            var header = new JournalEntryHeader
            {
                FiscalYearId = 1,
                SourceDocumentType = SourceDocumentType.ManualJV,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine { AccountId = 10, DebitLocal = 1000m, CreditLocal = 0m, CurrencyId = 1, ExchangeRate = 1m },
                    new JournalEntryLine { AccountId = 2, DebitLocal = 0m, CreditLocal = 1000m, CurrencyId = 1, ExchangeRate = 1m }
                }
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateAsync(header));
            Assert.Contains("حساب رئيسي تجميعي ولا يقبل حركات مباشرة", ex.Message);
        }

        [Fact]
        public async Task ValidateAsync_ThrowsWhenCurrencyDoesNotMatchAccountCurrency()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(dbName);

            db.FiscalYears.Add(new FiscalYear { Id = 1, Year = "2026", Name = "FY 2026", IsClosed = false, IsActive = true });
            db.GLAccounts.AddRange(
                new GLAccount { Id = 1, Code = "101", NameAr = "بنك دولار", NameEn = "USD Bank", CurrencyId = 2, IsGroup = false, IsActive = true, AccountUsage = AccountUsage.All },
                new GLAccount { Id = 2, Code = "201", NameAr = "إيرادات", NameEn = "Revenue", IsGroup = false, IsActive = true, AccountUsage = AccountUsage.All }
            );
            await db.SaveChangesAsync();

            var service = new JournalEntryValidationService(db);

            var header = new JournalEntryHeader
            {
                FiscalYearId = 1,
                SourceDocumentType = SourceDocumentType.ManualJV,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine { AccountId = 1, CurrencyId = 1 /* EGP instead of USD (2) */, DebitLocal = 1000m, CreditLocal = 0m },
                    new JournalEntryLine { AccountId = 2, CurrencyId = 1, DebitLocal = 0m, CreditLocal = 1000m }
                }
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateAsync(header));
            Assert.Contains("عملة الحركة لا تطابق", ex.Message);
        }

        [Fact]
        public async Task ValidateAsync_ThrowsWhenManualEditAttemptedOnAutoGeneratedEntry()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(dbName);

            var service = new JournalEntryValidationService(db);

            var header = new JournalEntryHeader
            {
                Id = 10, // existing persisted entry
                SourceDocumentType = SourceDocumentType.SalesInvoice, // Auto-generated
                FiscalYearId = 1
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateAsync(header, isManualAction: true));
            Assert.Contains("لا يمكن تعديل هذا القيد يدوياً", ex.Message);
        }

        [Fact]
        public async Task ValidateAsync_ThrowsWhenUserLacksCanPostPermission()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(dbName);

            db.FiscalYears.Add(new FiscalYear { Id = 1, Year = "2026", Name = "FY 2026", IsClosed = false, IsActive = true });
            db.GLAccounts.AddRange(
                new GLAccount { Id = 1, Code = "101", NameAr = "خزينة سرية", NameEn = "Secret Safe", IsGroup = false, IsActive = true, AccountUsage = AccountUsage.All },
                new GLAccount { Id = 2, Code = "201", NameAr = "مصروفات", NameEn = "Expenses", IsGroup = false, IsActive = true, AccountUsage = AccountUsage.All }
            );
            db.AccountPermissions.Add(new AccountPermission
            {
                AccountId = 1,
                UserId = "user-restricted",
                CanView = true,
                CanPost = false // Blocked from posting
            });
            await db.SaveChangesAsync();

            var service = new JournalEntryValidationService(db);

            var header = new JournalEntryHeader
            {
                FiscalYearId = 1,
                SourceDocumentType = SourceDocumentType.ManualJV,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine { AccountId = 1, DebitLocal = 500m, CreditLocal = 0m, CurrencyId = 1, ExchangeRate = 1m },
                    new JournalEntryLine { AccountId = 2, DebitLocal = 0m, CreditLocal = 500m, CurrencyId = 1, ExchangeRate = 1m }
                }
            };

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ValidateAsync(header, currentUserId: "user-restricted"));
            Assert.Contains("صلاحية الترحيل والتوجيه", ex.Message);
        }

        [Fact]
        public async Task ValidateAsync_SucceedsWhenAllRulesAreSatisfied()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(dbName);

            db.FiscalYears.Add(new FiscalYear { Id = 1, Year = "2026", Name = "FY 2026", IsClosed = false, IsActive = true });
            db.GLAccounts.AddRange(
                new GLAccount { Id = 1, Code = "101", NameAr = "نقدية بالصندوق", NameEn = "Cash", IsGroup = false, IsActive = true, AccountUsage = AccountUsage.All },
                new GLAccount { Id = 2, Code = "401", NameAr = "إيرادات مبيعات", NameEn = "Sales Revenue", IsGroup = false, IsActive = true, AccountUsage = AccountUsage.All }
            );
            await db.SaveChangesAsync();

            var service = new JournalEntryValidationService(db);

            var header = new JournalEntryHeader
            {
                FiscalYearId = 1,
                SourceDocumentType = SourceDocumentType.ManualJV,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine { AccountId = 1, DebitLocal = 2500m, CreditLocal = 0m, CurrencyId = 1, ExchangeRate = 1m },
                    new JournalEntryLine { AccountId = 2, DebitLocal = 0m, CreditLocal = 2500m, CurrencyId = 1, ExchangeRate = 1m }
                }
            };

            await service.ValidateAsync(header); // Must not throw
        }

        [Fact]
        public async Task ValidateAsync_ThrowsWhenAccountBelongsToDifferentBranch()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(dbName);

            db.FiscalYears.Add(new FiscalYear { Id = 1, Year = "2026", Name = "FY 2026", IsClosed = false, IsActive = true });
            db.GLAccounts.AddRange(
                new GLAccount { Id = 1, Code = "101", NameAr = "صندوق فرع الإسكندرية", NameEn = "Alex Cash", IsGroup = false, IsActive = true, BranchId = 2, AccountUsage = AccountUsage.All },
                new GLAccount { Id = 2, Code = "401", NameAr = "إيرادات", NameEn = "Revenue", IsGroup = false, IsActive = true, BranchId = null, AccountUsage = AccountUsage.All }
            );
            await db.SaveChangesAsync();

            var service = new JournalEntryValidationService(db);

            // Entry belongs to Cairo branch (BranchId = 1)
            var header = new JournalEntryHeader
            {
                BranchId = 1,
                FiscalYearId = 1,
                SourceDocumentType = SourceDocumentType.ManualJV,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine { AccountId = 1, DebitLocal = 1000m, CreditLocal = 0m, CurrencyId = 1, ExchangeRate = 1m }, // Branch 2 account in Branch 1 entry!
                    new JournalEntryLine { AccountId = 2, DebitLocal = 0m, CreditLocal = 1000m, CurrencyId = 1, ExchangeRate = 1m }
                }
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateAsync(header));
            Assert.Contains("مخصص لفرع آخر", ex.Message);
        }

        [Fact]
        public async Task ValidateAsync_SucceedsWhenAccountMatchesBranchOrIsShared()
        {
            var dbName = Guid.NewGuid().ToString();
            using var db = CreateDbContext(dbName);

            db.FiscalYears.Add(new FiscalYear { Id = 1, Year = "2026", Name = "FY 2026", IsClosed = false, IsActive = true });
            db.GLAccounts.AddRange(
                new GLAccount { Id = 1, Code = "10101", NameAr = "صندوق فرع القاهرة", NameEn = "Cairo Cash", IsGroup = false, IsActive = true, BranchId = 1, AccountUsage = AccountUsage.All },
                new GLAccount { Id = 2, Code = "40101", NameAr = "إيرادات عامة", NameEn = "General Revenue", IsGroup = false, IsActive = true, BranchId = null, AccountUsage = AccountUsage.All }
            );
            await db.SaveChangesAsync();

            var service = new JournalEntryValidationService(db);

            var header = new JournalEntryHeader
            {
                BranchId = 1,
                FiscalYearId = 1,
                SourceDocumentType = SourceDocumentType.ManualJV,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine { AccountId = 1, DebitLocal = 500m, CreditLocal = 0m, CurrencyId = 1, ExchangeRate = 1m },
                    new JournalEntryLine { AccountId = 2, DebitLocal = 0m, CreditLocal = 500m, CurrencyId = 1, ExchangeRate = 1m }
                }
            };

            await service.ValidateAsync(header); // Must succeed without error
        }
    }
}
