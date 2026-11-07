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
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class CurrencyRateService : ICurrencyRateService
    {
        private readonly ErpDbContext _context;
        private readonly IAuditService _auditService;
        private readonly ICurrentUserContext _currentUser;

        public CurrencyRateService(
            ErpDbContext context,
            IAuditService auditService,
            ICurrentUserContext currentUser)
        {
            _context = context;
            _auditService = auditService;
            _currentUser = currentUser;
        }

        public async Task<Currency> GetBaseCurrencyAsync()
        {
            var baseCurr = await _context.Currencies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.IsBaseCurrency || c.Code == "EGP");

            if (baseCurr == null)
            {
                baseCurr = await _context.Currencies
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.ExchangeRate == 1.0m)
                    ?? new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", ExchangeRate = 1.0m, IsBaseCurrency = true };
            }

            return baseCurr;
        }

        public async Task<ResolveExchangeRateResultDto> ResolveExchangeRateAsync(int currencyId, DateTime documentDate)
        {
            var docDate = documentDate.Date;

            var curr = await _context.Currencies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == currencyId);

            if (curr == null)
            {
                return new ResolveExchangeRateResultDto
                {
                    Success = false,
                    CurrencyId = currencyId,
                    Message = "العملة المحددة غير موجودة في النظام."
                };
            }

            // العملة الأساسية سعرها دائمًا 1 مقابل نفسها
            if (curr.IsBaseCurrency || string.Equals(curr.Code, "EGP", StringComparison.OrdinalIgnoreCase))
            {
                return new ResolveExchangeRateResultDto
                {
                    Success = true,
                    Rate = 1.000000m,
                    RateDate = docDate,
                    CurrencyId = curr.Id,
                    CurrencyCode = curr.Code,
                    CurrencyName = curr.LocalizedName,
                    IsBaseCurrency = true,
                    Message = string.Empty
                };
            }

            // البحث عن أحدث سعر صرف مسجل للعملة بشرط أن يكون تاريخ سعر الصرف <= تاريخ المستند
            var latestRateRecord = await _context.CurrencyDailyRates
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.CurrencyId == currencyId && r.RateDate <= docDate)
                .OrderByDescending(r => r.RateDate)
                .ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync();

            if (latestRateRecord != null && latestRateRecord.ExchangeRate > 0)
            {
                return new ResolveExchangeRateResultDto
                {
                    Success = true,
                    Rate = latestRateRecord.ExchangeRate,
                    RateDate = latestRateRecord.RateDate,
                    CurrencyId = curr.Id,
                    CurrencyCode = curr.Code,
                    CurrencyName = curr.LocalizedName,
                    IsBaseCurrency = false,
                    Message = string.Empty
                };
            }

            // عدم استخدام أي سعر مستقبلي أو عشوائي؛ وإرجاع رسالة واضحة للمستخدم
            return new ResolveExchangeRateResultDto
            {
                Success = false,
                Rate = 0m,
                RateDate = null,
                CurrencyId = curr.Id,
                CurrencyCode = curr.Code,
                CurrencyName = curr.LocalizedName,
                IsBaseCurrency = false,
                Message = $"لا يوجد سعر صرف مسجل لعملة ({curr.LocalizedName} - {curr.Code}) حتى تاريخ المستند ({docDate:yyyy-MM-dd}). يرجى تسجيل سعر الصرف بسجل أسعار الصرف أولاً."
            };
        }

        public async Task<decimal?> GetExchangeRateAsync(int currencyId, DateTime documentDate)
        {
            var result = await ResolveExchangeRateAsync(currencyId, documentDate);
            return result.Success ? result.Rate : null;
        }

        public async Task<List<CurrencyDailyRateDto>> GetDailyRatesAsync(DateTime? fromDate = null, DateTime? toDate = null, int? currencyId = null)
        {
            var query = _context.CurrencyDailyRates
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(r => r.Currency)
                .AsQueryable();

            if (currencyId.HasValue && currencyId.Value > 0)
            {
                query = query.Where(r => r.CurrencyId == currencyId.Value);
            }

            if (fromDate.HasValue)
            {
                var fDate = fromDate.Value.Date;
                query = query.Where(r => r.RateDate >= fDate);
            }

            if (toDate.HasValue)
            {
                var tDate = toDate.Value.Date;
                query = query.Where(r => r.RateDate <= tDate);
            }

            var rates = await query
                .OrderByDescending(r => r.RateDate)
                .ThenBy(r => r.CurrencyId)
                .ToListAsync();

            return rates.Select(r => new CurrencyDailyRateDto
            {
                Id = r.Id,
                CurrencyId = r.CurrencyId,
                CurrencyCode = r.Currency?.Code ?? string.Empty,
                CurrencyName = r.Currency?.LocalizedName ?? string.Empty,
                CurrencySymbol = r.Currency?.Symbol,
                IsBaseCurrency = r.Currency?.IsBaseCurrency ?? false,
                RateDate = r.RateDate,
                ExchangeRate = r.ExchangeRate,
                Notes = r.Notes,
                CreatedByUserId = r.CreatedByUserId,
                CreatedAt = r.CreatedAt,
                UpdatedByUserId = r.UpdatedByUserId,
                UpdatedAt = r.UpdatedAt
            }).ToList();
        }

        public async Task<CurrencyDailyRateDto?> GetDailyRateByIdAsync(int id)
        {
            var r = await _context.CurrencyDailyRates
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.Currency)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (r == null) return null;

            return new CurrencyDailyRateDto
            {
                Id = r.Id,
                CurrencyId = r.CurrencyId,
                CurrencyCode = r.Currency?.Code ?? string.Empty,
                CurrencyName = r.Currency?.LocalizedName ?? string.Empty,
                CurrencySymbol = r.Currency?.Symbol,
                IsBaseCurrency = r.Currency?.IsBaseCurrency ?? false,
                RateDate = r.RateDate,
                ExchangeRate = r.ExchangeRate,
                Notes = r.Notes,
                CreatedByUserId = r.CreatedByUserId,
                CreatedAt = r.CreatedAt,
                UpdatedByUserId = r.UpdatedByUserId,
                UpdatedAt = r.UpdatedAt
            };
        }

        public async Task<(bool Success, string Message, CurrencyDailyRateDto? Data)> SaveDailyRateAsync(CreateOrUpdateDailyRateDto dto, string userId)
        {
            if (dto == null)
                return (false, "البيانات المدخلة غير صحيحة.", null);

            var curr = await _context.Currencies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == dto.CurrencyId);

            if (curr == null)
                return (false, "العملة المحددة غير موجودة في النظام.", null);

            if (curr.IsBaseCurrency || string.Equals(curr.Code, "EGP", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "العملة الأساسية للنظام يكون سعرها دائماً 1.0 مقابل نفسها ولا يتطلب تسجيل سعر صرف لها.", null);
            }

            if (dto.ExchangeRate <= 0)
                return (false, "سعر الصرف يجب أن يكون أكبر من الصفر.", null);

            var targetDate = dto.RateDate.Date;

            // التحقق من عدم تكرار نفس العملة في نفس التاريخ
            bool exists = await _context.CurrencyDailyRates
                .IgnoreQueryFilters()
                .AnyAsync(r => r.CurrencyId == dto.CurrencyId && r.RateDate == targetDate && (!dto.Id.HasValue || r.Id != dto.Id.Value));

            if (exists)
            {
                return (false, $"يوجد بالفعل سعر صرف مسجل لعملة ({curr.Code}) في تاريخ ({targetDate:yyyy-MM-dd}). يمكنك تعديل السعر المسجل.", null);
            }

            CurrencyDailyRate entity;
            bool isNew = !dto.Id.HasValue || dto.Id.Value <= 0;

            if (isNew)
            {
                entity = new CurrencyDailyRate
                {
                    CurrencyId = dto.CurrencyId,
                    RateDate = targetDate,
                    ExchangeRate = dto.ExchangeRate,
                    Notes = dto.Notes?.Trim(),
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.CurrencyDailyRates.Add(entity);
            }
            else
            {
                var existing = await _context.CurrencyDailyRates
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.Id == dto.Id.Value);

                if (existing == null)
                    return (false, "سجل سعر الصرف المطلوب تعديله غير موجود.", null);

                var oldValues = new
                {
                    existing.Id,
                    existing.CurrencyId,
                    existing.RateDate,
                    existing.ExchangeRate,
                    existing.Notes
                };

                existing.CurrencyId = dto.CurrencyId;
                existing.RateDate = targetDate;
                existing.ExchangeRate = dto.ExchangeRate;
                existing.Notes = dto.Notes?.Trim();
                existing.UpdatedByUserId = userId;
                existing.UpdatedAt = DateTime.UtcNow;

                entity = existing;

                await _auditService.LogAsync(new AuditLogEntry
                {
                    ScreenCode = "MD_CURR_RATES",
                    ScreenName = "Daily Currency Rates",
                    ModuleName = "MasterData",
                    EntityName = "CurrencyDailyRate",
                    EntityId = existing.Id.ToString(),
                    Action = AuditAction.Update,
                    Description = $"تعديل سعر الصرف اليومي لعملة ({curr.Code}) في تاريخ ({targetDate:yyyy-MM-dd}) من {oldValues.ExchangeRate} إلى {dto.ExchangeRate}.",
                    OldValues = oldValues,
                    NewValues = new
                    {
                        existing.Id,
                        existing.CurrencyId,
                        existing.RateDate,
                        existing.ExchangeRate,
                        existing.Notes
                    },
                    Success = true
                });
            }

            await _context.SaveChangesAsync();

            // تحديث سعر الصرف الافتراضي في جدول العملات إذا كان هذا التاريخ هو الأحدث
            var maxDate = await _context.CurrencyDailyRates
                .IgnoreQueryFilters()
                .Where(r => r.CurrencyId == dto.CurrencyId)
                .MaxAsync(r => (DateTime?)r.RateDate);

            if (maxDate == targetDate)
            {
                curr.ExchangeRate = dto.ExchangeRate;
                curr.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            if (isNew)
            {
                await _auditService.LogAsync(new AuditLogEntry
                {
                    ScreenCode = "MD_CURR_RATES",
                    ScreenName = "Daily Currency Rates",
                    ModuleName = "MasterData",
                    EntityName = "CurrencyDailyRate",
                    EntityId = entity.Id.ToString(),
                    Action = AuditAction.Create,
                    Description = $"تسجيل سعر صرف يومي جديد لعملة ({curr.Code}) في تاريخ ({targetDate:yyyy-MM-dd}): {dto.ExchangeRate}.",
                    NewValues = new
                    {
                        entity.Id,
                        entity.CurrencyId,
                        entity.RateDate,
                        entity.ExchangeRate,
                        entity.Notes
                    },
                    Success = true
                });
            }

            var dtoResult = new CurrencyDailyRateDto
            {
                Id = entity.Id,
                CurrencyId = entity.CurrencyId,
                CurrencyCode = curr.Code,
                CurrencyName = curr.LocalizedName,
                CurrencySymbol = curr.Symbol,
                IsBaseCurrency = curr.IsBaseCurrency,
                RateDate = entity.RateDate,
                ExchangeRate = entity.ExchangeRate,
                Notes = entity.Notes,
                CreatedByUserId = entity.CreatedByUserId,
                CreatedAt = entity.CreatedAt,
                UpdatedByUserId = entity.UpdatedByUserId,
                UpdatedAt = entity.UpdatedAt
            };

            return (true, "تم حفظ سعر الصرف اليومي بنجاح.", dtoResult);
        }

        public async Task<(bool Success, string Message)> DeleteDailyRateAsync(int id, string userId)
        {
            var rate = await _context.CurrencyDailyRates
                .IgnoreQueryFilters()
                .Include(r => r.Currency)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rate == null)
                return (false, "سجل سعر الصرف غير موجود.");

            var oldValues = new
            {
                rate.Id,
                rate.CurrencyId,
                CurrencyCode = rate.Currency?.Code,
                rate.RateDate,
                rate.ExchangeRate,
                rate.Notes
            };

            _context.CurrencyDailyRates.Remove(rate);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(new AuditLogEntry
            {
                ScreenCode = "MD_CURR_RATES",
                ScreenName = "Daily Currency Rates",
                ModuleName = "MasterData",
                EntityName = "CurrencyDailyRate",
                EntityId = id.ToString(),
                Action = AuditAction.Delete,
                Description = $"حذف سعر الصرف اليومي لعملة ({rate.Currency?.Code}) لتاريخ ({rate.RateDate:yyyy-MM-dd}).",
                OldValues = oldValues,
                Success = true
            });

            return (true, "تم حذف سجل سعر الصرف بنجاح.");
        }

        public async Task<PartyCurrencyInfoDto?> GetPartyCurrencyAsync(string partyType, int partyId)
        {
            if (string.Equals(partyType, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                var cust = await _context.Customers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(c => c.Currency)
                    .FirstOrDefaultAsync(c => c.Id == partyId);

                if (cust == null) return null;

                return new PartyCurrencyInfoDto
                {
                    PartyId = cust.Id,
                    PartyType = "Customer",
                    PartyCode = cust.Code,
                    PartyName = cust.LocalizedName,
                    CurrencyId = cust.CurrencyId,
                    CurrencyCode = cust.Currency?.Code,
                    CurrencyName = cust.Currency?.LocalizedName,
                    IsRestricted = cust.CurrencyId.HasValue
                };
            }
            else if (string.Equals(partyType, "Vendor", StringComparison.OrdinalIgnoreCase))
            {
                var vend = await _context.Vendors
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(v => v.Currency)
                    .FirstOrDefaultAsync(v => v.Id == partyId);

                if (vend == null) return null;

                return new PartyCurrencyInfoDto
                {
                    PartyId = vend.Id,
                    PartyType = "Vendor",
                    PartyCode = vend.Code,
                    PartyName = vend.LocalizedName,
                    CurrencyId = vend.CurrencyId,
                    CurrencyCode = vend.Currency?.Code,
                    CurrencyName = vend.Currency?.LocalizedName,
                    IsRestricted = vend.CurrencyId.HasValue
                };
            }

            return null;
        }

        public async Task<AccountCurrencyInfoDto?> GetAccountCurrencyAsync(int accountId)
        {
            var acc = await _context.GLAccounts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(a => a.Currency)
                .FirstOrDefaultAsync(a => a.Id == accountId);

            if (acc == null) return null;

            return new AccountCurrencyInfoDto
            {
                AccountId = acc.Id,
                AccountCode = acc.Code,
                AccountName = acc.LocalizedName,
                CurrencyId = acc.CurrencyId,
                CurrencyCode = acc.Currency?.Code,
                CurrencyName = acc.Currency?.LocalizedName,
                IsRestricted = acc.CurrencyId.HasValue
            };
        }

        public async Task<(bool IsValid, string? ErrorMessage, decimal ResolvedRate)> ValidateAndResolveDocumentCurrencyAsync(
            string? partyType,
            int? partyId,
            int? accountId,
            int documentCurrencyId,
            DateTime documentDate,
            decimal documentExchangeRate)
        {
            // 1. التحقق من تطابق عملة العميل أو المورد
            if (!string.IsNullOrWhiteSpace(partyType) && partyId.HasValue && partyId.Value > 0)
            {
                var partyInfo = await GetPartyCurrencyAsync(partyType, partyId.Value);
                if (partyInfo != null && partyInfo.CurrencyId.HasValue)
                {
                    if (documentCurrencyId != partyInfo.CurrencyId.Value)
                    {
                        string partyLabel = partyType.Equals("Customer", StringComparison.OrdinalIgnoreCase) ? "العميل" : "المورد";
                        return (false, $"عملة المستند غير مطابقة لعملة {partyLabel} المعتمدة ({partyInfo.CurrencyCode} - {partyInfo.CurrencyName}). لا يُسمح بإجراء حركة بعملة مختلفة.", 0);
                    }
                }
            }

            // 2. التحقق من تطابق عملة الحساب المالي إذا كان مقيداً
            if (accountId.HasValue && accountId.Value > 0)
            {
                var accInfo = await GetAccountCurrencyAsync(accountId.Value);
                if (accInfo != null && accInfo.IsRestricted && accInfo.CurrencyId.HasValue)
                {
                    if (documentCurrencyId != accInfo.CurrencyId.Value)
                    {
                        return (false, $"الحساب المالي ({accInfo.AccountCode} - {accInfo.AccountName}) مقيد بالعملة ({accInfo.CurrencyCode}) ولا يمكن إجراء حركات عليه بعملة أخرى.", 0);
                    }
                }
            }

            // 3. جلب والتحقق من سعر الصرف المناسب لتاريخ المستند
            var rateRes = await ResolveExchangeRateAsync(documentCurrencyId, documentDate);
            if (!rateRes.Success)
            {
                return (false, rateRes.Message, 0);
            }

            return (true, null, rateRes.Rate);
        }
    }
}
