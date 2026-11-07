using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Domain.Entities;

namespace ERP.Application.Interfaces
{
    public interface ICurrencyRateService
    {
        /// <summary>
        /// جلب أحدث سعر صرف مسجل للعملة بشرط ألا يتجاوز تاريخ المستند (RateDate <= documentDate).
        /// العملة الأساسية سعرها دائمًا 1.0.
        /// </summary>
        Task<ResolveExchangeRateResultDto> ResolveExchangeRateAsync(int currencyId, DateTime documentDate);

        /// <summary>
        /// جلب سعر الصرف كرقم مباشر أو null في حالة عدم وجود سعر صالح قبل أو مطابق لتاريخ المستند.
        /// </summary>
        Task<decimal?> GetExchangeRateAsync(int currencyId, DateTime documentDate);

        /// <summary>
        /// استعلام أسعار الصرف اليومية مع التصفية بالتواريخ أو العملة.
        /// </summary>
        Task<List<CurrencyDailyRateDto>> GetDailyRatesAsync(DateTime? fromDate = null, DateTime? toDate = null, int? currencyId = null);

        /// <summary>
        /// جلب سجل سعر صرف يومي محدد.
        /// </summary>
        Task<CurrencyDailyRateDto?> GetDailyRateByIdAsync(int id);

        /// <summary>
        /// إضافة أو تعديل سعر صرف يومي مع التحقق من عدم التكرار وتسجيل التغييرات في Audit Log.
        /// </summary>
        Task<(bool Success, string Message, CurrencyDailyRateDto? Data)> SaveDailyRateAsync(CreateOrUpdateDailyRateDto dto, string userId);

        /// <summary>
        /// حذف سعر صرف يومي مع التحقق والتسجيل في Audit Log.
        /// </summary>
        Task<(bool Success, string Message)> DeleteDailyRateAsync(int id, string userId);

        /// <summary>
        /// استرجاع معلومات عملة العميل أو المورد المعتمدة وما إذا كانت مقيدة.
        /// </summary>
        Task<PartyCurrencyInfoDto?> GetPartyCurrencyAsync(string partyType, int partyId);

        /// <summary>
        /// استرجاع معلومات عملة الحساب المالي المقيدة من دليل الحسابات.
        /// </summary>
        Task<AccountCurrencyInfoDto?> GetAccountCurrencyAsync(int accountId);

        /// <summary>
        /// استرجاع العملة الأساسية للنظام.
        /// </summary>
        Task<Currency> GetBaseCurrencyAsync();

        /// <summary>
        /// التحقق الشامل عند حفظ المستند المالي أو المحاسبي من صحة العملة وسعر الصرف طبقا لقواعد العميل والمورد وتاريخ المستند.
        /// </summary>
        Task<(bool IsValid, string? ErrorMessage, decimal ResolvedRate)> ValidateAndResolveDocumentCurrencyAsync(
            string? partyType,
            int? partyId,
            int? accountId,
            int documentCurrencyId,
            DateTime documentDate,
            decimal documentExchangeRate);
    }
}
