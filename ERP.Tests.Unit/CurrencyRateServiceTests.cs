using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Application.Models;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ERP.Tests.Unit
{
    public class CurrencyRateServiceTests : IDisposable
    {
        private readonly ErpDbContext _db;
        private readonly Mock<ICurrentUserContext> _userContextMock;
        private readonly Mock<IAuditService> _auditMock;
        private readonly ICurrencyRateService _service;

        private readonly Currency _egp;
        private readonly Currency _usd;
        private readonly Currency _eur;

        public CurrencyRateServiceTests()
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ErpDbContext(options);
            _userContextMock = new Mock<ICurrentUserContext>();
            _auditMock = new Mock<IAuditService>();

            _userContextMock.Setup(u => u.UserId).Returns("test-user-id");
            _userContextMock.Setup(u => u.Username).Returns("testadmin");

            // Currencies
            _egp = new Currency
            {
                Id = 1,
                Code = "EGP",
                NameAr = "جنيه مصري",
                NameEn = "Egyptian Pound",
                Symbol = "ج.م",
                ExchangeRate = 1.0m,
                IsBaseCurrency = true,
                IsActive = true
            };

            _usd = new Currency
            {
                Id = 2,
                Code = "USD",
                NameAr = "دولار أمريكي",
                NameEn = "US Dollar",
                Symbol = "$",
                ExchangeRate = 50.0m,
                IsBaseCurrency = false,
                IsActive = true
            };

            _eur = new Currency
            {
                Id = 3,
                Code = "EUR",
                NameAr = "يورو",
                NameEn = "Euro",
                Symbol = "€",
                ExchangeRate = 54.0m,
                IsBaseCurrency = false,
                IsActive = true
            };

            _db.Currencies.AddRange(_egp, _usd, _eur);
            _db.SaveChanges();

            _service = new CurrencyRateService(_db, _auditMock.Object, _userContextMock.Object);
        }

        public void Dispose()
        {
            _db.Database.EnsureDeleted();
            _db.Dispose();
        }

        [Fact]
        public async Task ResolveExchangeRate_BaseCurrency_AlwaysReturnsOne()
        {
            // Even if document date is in the far past or far future, base currency is always 1.0
            var result = await _service.ResolveExchangeRateAsync(_egp.Id, new DateTime(2026, 5, 10));

            Assert.True(result.Success);
            Assert.True(result.IsBaseCurrency);
            Assert.Equal(1.000000m, result.Rate);
        }

        [Fact]
        public async Task ResolveExchangeRate_UserExample_PicksLatestRateBeforeOrEqualToDocDate()
        {
            // Case 1 from specification:
            // Registered dates: 09-11 (rate 48.0) and 12-11 (rate 52.0)
            // Document date: 10-11 -> should pick 09-11 rate (48.0)
            await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _usd.Id,
                RateDate = new DateTime(2026, 11, 9),
                ExchangeRate = 48.0m,
                Notes = "سعر 9 نوفمبر"
            }, "test-user-id");

            await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _usd.Id,
                RateDate = new DateTime(2026, 11, 12),
                ExchangeRate = 52.0m,
                Notes = "سعر 12 نوفمبر"
            }, "test-user-id");

            var docDate = new DateTime(2026, 11, 10);
            var result = await _service.ResolveExchangeRateAsync(_usd.Id, docDate);

            Assert.True(result.Success);
            Assert.Equal(48.0m, result.Rate);
            Assert.Equal(new DateTime(2026, 11, 9), result.RateDate);
        }

        [Fact]
        public async Task ResolveExchangeRate_SameDateMatch_PicksExactDate()
        {
            // Case 2 from specification:
            // Registered dates: 09-11 (48.0) and 10-11 (49.5)
            // Document date: 10-11 -> should pick 10-11 rate (49.5)
            await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _usd.Id,
                RateDate = new DateTime(2026, 11, 9),
                ExchangeRate = 48.0m
            }, "test-user-id");

            await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _usd.Id,
                RateDate = new DateTime(2026, 11, 10),
                ExchangeRate = 49.5m
            }, "test-user-id");

            var docDate = new DateTime(2026, 11, 10);
            var result = await _service.ResolveExchangeRateAsync(_usd.Id, docDate);

            Assert.True(result.Success);
            Assert.Equal(49.5m, result.Rate);
            Assert.Equal(new DateTime(2026, 11, 10), result.RateDate);
        }

        [Fact]
        public async Task ResolveExchangeRate_FutureRateOnly_ReturnsFailureAndBlocks()
        {
            // Case 3 from specification:
            // Registered dates: 12-11 only (52.0)
            // Document date: 10-11 -> No valid rate exists (future rate must never be used)
            await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _usd.Id,
                RateDate = new DateTime(2026, 11, 12),
                ExchangeRate = 52.0m
            }, "test-user-id");

            var docDate = new DateTime(2026, 11, 10);
            var result = await _service.ResolveExchangeRateAsync(_usd.Id, docDate);

            Assert.False(result.Success);
            Assert.Equal(0, result.Rate);
            Assert.Contains("لا يوجد سعر صرف مسجل", result.Message);
        }

        [Fact]
        public async Task ResolveExchangeRate_MultipleRates_PicksMostRecentEffectiveRate()
        {
            // Case 4 from specification:
            // Registered dates: 09-11 (48.0), 10-11 (49.5), 12-11 (52.0)
            // Document date: 12-11 -> should pick 12-11 rate (52.0)
            await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _usd.Id,
                RateDate = new DateTime(2026, 11, 9),
                ExchangeRate = 48.0m
            }, "test-user-id");

            await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _usd.Id,
                RateDate = new DateTime(2026, 11, 10),
                ExchangeRate = 49.5m
            }, "test-user-id");

            await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _usd.Id,
                RateDate = new DateTime(2026, 11, 12),
                ExchangeRate = 52.0m
            }, "test-user-id");

            var docDate = new DateTime(2026, 11, 12);
            var result = await _service.ResolveExchangeRateAsync(_usd.Id, docDate);

            Assert.True(result.Success);
            Assert.Equal(52.0m, result.Rate);
            Assert.Equal(new DateTime(2026, 11, 12), result.RateDate);
        }

        [Fact]
        public async Task SaveDailyRate_BaseCurrency_RejectsDailyRateRegistration()
        {
            // Rule: Base currency rate is always 1.0 against itself and does not require daily rates
            var (success, msg, data) = await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _egp.Id,
                RateDate = new DateTime(2026, 11, 1),
                ExchangeRate = 1.0m
            }, "test-user-id");

            Assert.False(success);
            Assert.Contains("العملة الأساسية للنظام", msg);
            Assert.Null(data);
        }

        [Fact]
        public async Task SaveDailyRate_SameDateWithoutId_PreventsDuplicate_AndWithId_UpdatesExisting()
        {
            // Rule: No duplicate currency on same date
            var date = new DateTime(2026, 11, 5);

            var (firstSuccess, _, first) = await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _usd.Id,
                RateDate = date,
                ExchangeRate = 49.0m,
                Notes = "السعر الأول"
            }, "test-user-id");

            Assert.True(firstSuccess);
            Assert.NotNull(first);

            // 1. Attempting to add same date without specifying Id should be blocked
            var (duplicateAttemptSuccess, dupMsg, _) = await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _usd.Id,
                RateDate = date,
                ExchangeRate = 51.0m,
                Notes = "محاولة تكرار"
            }, "test-user-id");

            Assert.False(duplicateAttemptSuccess);
            Assert.Contains("يوجد بالفعل سعر صرف مسجل", dupMsg);

            // 2. Updating with existing Id succeeds and modifies rate
            var (updateSuccess, _, updated) = await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                Id = first.Id,
                CurrencyId = _usd.Id,
                RateDate = date,
                ExchangeRate = 49.8m,
                Notes = "تعديل السعر"
            }, "test-user-id");

            var ratesCount = await _db.CurrencyDailyRates
                .Where(r => r.CurrencyId == _usd.Id && r.RateDate == date)
                .CountAsync();

            Assert.True(updateSuccess);
            Assert.Equal(1, ratesCount);
            Assert.Equal(first.Id, updated!.Id);
            Assert.Equal(49.8m, updated.ExchangeRate);
            Assert.Equal("تعديل السعر", updated.Notes);
        }

        [Fact]
        public async Task SaveDailyRate_LogsAudit()
        {
            await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _usd.Id,
                RateDate = new DateTime(2026, 11, 1),
                ExchangeRate = 50.25m,
                Notes = "Audit test"
            }, "test-user-id");

            _auditMock.Verify(a => a.LogAsync(
                It.Is<AuditLogEntry>(e => e.EntityName == nameof(CurrencyDailyRate) && e.ScreenCode == "MD_CURR_RATES")
            ), Times.AtLeastOnce);
        }

        [Fact]
        public async Task ValidateAndResolveDocumentCurrency_CustomerCurrencyMismatch_ReturnsInvalid()
        {
            var customer = new Customer
            {
                Id = 10,
                Code = "CUST-01",
                NameAr = "عميل دولار",
                NameEn = "Dollar Customer",
                CurrencyId = _usd.Id,
                BranchId = 1,
                IsActive = true
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();

            // Attempt to create document with EGP currency for USD customer
            var (isValid, errorMessage, _) = await _service.ValidateAndResolveDocumentCurrencyAsync(
                partyType: "Customer",
                partyId: customer.Id,
                accountId: null,
                documentCurrencyId: _egp.Id,
                documentDate: DateTime.Today,
                documentExchangeRate: 1.0m
            );

            Assert.False(isValid);
            Assert.NotNull(errorMessage);
            Assert.Contains("غير مطابقة لعملة العميل", errorMessage);
            Assert.Contains("USD", errorMessage);
        }

        [Fact]
        public async Task ValidateAndResolveDocumentCurrency_VendorCurrencyMismatch_ReturnsInvalid()
        {
            var vendor = new Vendor
            {
                Id = 20,
                Code = "VEND-01",
                NameAr = "مورد يورو",
                NameEn = "Euro Vendor",
                CurrencyId = _eur.Id,
                BranchId = 1,
                IsActive = true
            };
            _db.Vendors.Add(vendor);
            await _db.SaveChangesAsync();

            // Attempt to create document with USD currency for EUR vendor
            var (isValid, errorMessage, _) = await _service.ValidateAndResolveDocumentCurrencyAsync(
                partyType: "Vendor",
                partyId: vendor.Id,
                accountId: null,
                documentCurrencyId: _usd.Id,
                documentDate: DateTime.Today,
                documentExchangeRate: 50.0m
            );

            Assert.False(isValid);
            Assert.NotNull(errorMessage);
            Assert.Contains("غير مطابقة لعملة المورد", errorMessage);
            Assert.Contains("EUR", errorMessage);
        }

        [Fact]
        public async Task ValidateAndResolveDocumentCurrency_RestrictedAccountMismatch_ReturnsInvalid()
        {
            var restrictedAccount = new GLAccount
            {
                Id = 30,
                Code = "120101",
                NameAr = "حساب بنك يورو",
                NameEn = "Euro Bank Account",
                CurrencyId = _eur.Id, // Restricted to EUR
                AccountCategory = AccountCategory.Assets,
                IsGroup = false,
                IsActive = true
            };
            _db.GLAccounts.Add(restrictedAccount);
            await _db.SaveChangesAsync();

            // Attempt to post USD line to EUR-only account
            var (isValid, errorMessage, _) = await _service.ValidateAndResolveDocumentCurrencyAsync(
                partyType: null,
                partyId: null,
                accountId: restrictedAccount.Id,
                documentCurrencyId: _usd.Id,
                documentDate: DateTime.Today,
                documentExchangeRate: 50.0m
            );

            Assert.False(isValid);
            Assert.NotNull(errorMessage);
            Assert.Contains("مقيد بالعملة", errorMessage);
            Assert.Contains("EUR", errorMessage);
        }

        [Fact]
        public async Task ValidateAndResolveDocumentCurrency_MatchingCurrencyAndRate_ReturnsResolvedRate()
        {
            var customer = new Customer
            {
                Id = 40,
                Code = "CUST-02",
                NameAr = "عميل دولار 2",
                NameEn = "Dollar Customer 2",
                CurrencyId = _usd.Id,
                BranchId = 1,
                IsActive = true
            };
            _db.Customers.Add(customer);

            var rateDate = new DateTime(2026, 11, 1);
            await _service.SaveDailyRateAsync(new CreateOrUpdateDailyRateDto
            {
                CurrencyId = _usd.Id,
                RateDate = rateDate,
                ExchangeRate = 50.45m
            }, "test-user-id");

            var docDate = new DateTime(2026, 11, 2);
            var (isValid, errorMessage, rate) = await _service.ValidateAndResolveDocumentCurrencyAsync(
                partyType: "Customer",
                partyId: customer.Id,
                accountId: null,
                documentCurrencyId: _usd.Id,
                documentDate: docDate,
                documentExchangeRate: 50.45m
            );

            Assert.True(isValid);
            Assert.Null(errorMessage);
            Assert.Equal(50.45m, rate);
        }

        [Fact]
        public async Task ValidateAndResolveDocumentCurrency_NoRateExists_ReturnsInvalid()
        {
            var customer = new Customer
            {
                Id = 50,
                Code = "CUST-03",
                NameAr = "عميل يورو 3",
                NameEn = "Euro Customer 3",
                CurrencyId = _eur.Id,
                BranchId = 1,
                IsActive = true
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();

            // No EUR rates seeded for doc date
            var docDate = new DateTime(2026, 11, 10);
            var (isValid, errorMessage, _) = await _service.ValidateAndResolveDocumentCurrencyAsync(
                partyType: "Customer",
                partyId: customer.Id,
                accountId: null,
                documentCurrencyId: _eur.Id,
                documentDate: docDate,
                documentExchangeRate: 54.0m
            );

            Assert.False(isValid);
            Assert.NotNull(errorMessage);
            Assert.Contains("لا يوجد سعر صرف مسجل", errorMessage);
        }
    }
}
