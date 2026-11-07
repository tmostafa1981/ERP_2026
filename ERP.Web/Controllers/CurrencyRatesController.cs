using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class CurrencyRatesController : Controller
    {
        private readonly ICurrencyRateService _rateService;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public CurrencyRatesController(
            ICurrencyRateService rateService,
            ErpDbContext context,
            ICurrentUserContext currentUser)
        {
            _rateService = rateService;
            _context = context;
            _currentUser = currentUser;
        }

        [HttpGet]
        [HasPermission("MasterData.CurrencyRates.View,MasterData.Currencies.View")]
        public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate, int? currencyId)
        {
            var baseCurr = await _rateService.GetBaseCurrencyAsync();
            ViewBag.BaseCurrency = baseCurr;

            ViewBag.Currencies = await _context.Currencies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.IsActive && !c.IsBaseCurrency && c.Code != "EGP")
                .OrderBy(c => c.Code)
                .ToListAsync();

            ViewBag.AllCurrencies = await _context.Currencies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Code)
                .ToListAsync();

            var defaultToDate = toDate ?? DateTime.Today;
            var defaultFromDate = fromDate ?? defaultToDate.AddMonths(-1);

            ViewBag.FromDate = defaultFromDate;
            ViewBag.ToDate = defaultToDate;
            ViewBag.SelectedCurrencyId = currencyId;

            var rates = await _rateService.GetDailyRatesAsync(defaultFromDate, defaultToDate, currencyId);
            return View(rates);
        }

        [HttpGet]
        [AllowAnonymous] // or Authorize, used by AJAX across documents
        public async Task<IActionResult> GetRate(int currencyId, DateTime documentDate)
        {
            if (currencyId <= 0)
                return Json(new { success = false, message = "معرف العملة غير صالح." });

            var result = await _rateService.ResolveExchangeRateAsync(currencyId, documentDate);
            return Json(new
            {
                success = result.Success,
                rate = result.Rate,
                rateDate = result.RateDate?.ToString("yyyy-MM-dd"),
                currencyId = result.CurrencyId,
                currencyCode = result.CurrencyCode,
                currencyName = result.CurrencyName,
                isBaseCurrency = result.IsBaseCurrency,
                message = result.Message
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetPartyCurrency(string partyType, int partyId)
        {
            if (partyId <= 0)
                return Json(new { success = false, message = "معرف الطرف غير صالح." });

            var result = await _rateService.GetPartyCurrencyAsync(partyType, partyId);
            if (result == null)
                return Json(new { success = false, message = "الطرف غير موجود." });

            return Json(new
            {
                success = true,
                partyId = result.PartyId,
                partyType = result.PartyType,
                currencyId = result.CurrencyId,
                currencyCode = result.CurrencyCode,
                currencyName = result.CurrencyName,
                isRestricted = result.IsRestricted
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetAccountCurrency(int accountId)
        {
            if (accountId <= 0)
                return Json(new { success = false, message = "معرف الحساب غير صالح." });

            var result = await _rateService.GetAccountCurrencyAsync(accountId);
            if (result == null)
                return Json(new { success = false, message = "الحساب غير موجود." });

            return Json(new
            {
                success = true,
                accountId = result.AccountId,
                currencyId = result.CurrencyId,
                currencyCode = result.CurrencyCode,
                currencyName = result.CurrencyName,
                isRestricted = result.IsRestricted
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.CurrencyRates.Add,MasterData.CurrencyRates.Edit,MasterData.Currencies.Edit")]
        public async Task<IActionResult> Save([FromForm] CreateOrUpdateDailyRateDto dto)
        {
            if (!ModelState.IsValid)
            {
                var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return Json(new { success = false, message = errors });
            }

            var userId = _currentUser.UserId ?? "System";
            var (success, message, data) = await _rateService.SaveDailyRateAsync(dto, userId);

            return Json(new { success, message, data });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.CurrencyRates.Delete,MasterData.Currencies.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            if (id <= 0)
                return Json(new { success = false, message = "معرف السجل غير صالح." });

            var userId = _currentUser.UserId ?? "System";
            var (success, message) = await _rateService.DeleteDailyRateAsync(id, userId);

            return Json(new { success, message });
        }
    }
}
