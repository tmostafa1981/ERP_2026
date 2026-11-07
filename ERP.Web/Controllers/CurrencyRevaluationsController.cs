using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ERP.Application.DTOs.Revaluation;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class CurrencyRevaluationsController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly IForeignCurrencyRevaluationService _revaluationService;
        private readonly ICurrentUserContext _currentUser;
        private readonly ICurrencyRateService _currencyRateService;

        public CurrencyRevaluationsController(
            ErpDbContext context,
            IForeignCurrencyRevaluationService revaluationService,
            ICurrentUserContext currentUser,
            ICurrencyRateService currencyRateService)
        {
            _context = context;
            _revaluationService = revaluationService;
            _currentUser = currentUser;
            _currencyRateService = currencyRateService;
        }

        // ─── GET: Index ──────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.CurrencyRevaluation.View")]
        public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate, int? branchId, RevaluationStatus? status, int page = 1)
        {
            var filter = new RevaluationListFilterDto
            {
                FromDate = fromDate,
                ToDate = toDate,
                BranchId = branchId,
                Status = status,
                PageNumber = page,
                PageSize = 20
            };

            var result = await _revaluationService.GetRevaluationsAsync(filter);

            await PopulateFilterLookupsAsync(branchId);
            ViewBag.CurrentFilter = filter;

            return View(result);
        }

        // ─── GET: Create ─────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.CurrencyRevaluation.Add")]
        public async Task<IActionResult> Create()
        {
            await PopulateFormLookupsAsync();
            var settings = await _revaluationService.GetDefaultSettingsAsync(_currentUser.BranchId);

            ViewBag.DefaultGainAccountId = settings.DefaultGainAccountId;
            ViewBag.DefaultGainAccountName = settings.DefaultGainAccountName;
            ViewBag.DefaultLossAccountId = settings.DefaultLossAccountId;
            ViewBag.DefaultLossAccountName = settings.DefaultLossAccountName;

            return View();
        }

        // ─── POST: PreviewBalances (AJAX - تحميل الأرصدة) ────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.CurrencyRevaluation.Add")]
        public async Task<IActionResult> PreviewBalances([FromBody] LoadBalancesFilterDto filter)
        {
            if (filter == null)
            {
                return Json(new { success = false, message = "معاملات البحث غير صحيحة." });
            }

            if (filter.CompanyId <= 0)
            {
                filter.CompanyId = _currentUser.CompanyId > 0 ? _currentUser.CompanyId : 1;
            }

            if (!_currentUser.HasAllBranches && (!filter.BranchId.HasValue || filter.BranchId.Value <= 0))
            {
                filter.BranchId = _currentUser.BranchId;
            }

            try
            {
                var preview = await _revaluationService.PreviewBalancesAsync(filter, _currentUser.UserId);
                return Json(preview);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "حدث خطأ أثناء تحميل الأرصدة: " + ex.Message });
            }
        }

        // ─── POST: Create (حفظ واعتماد عملية التقييم) ────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.CurrencyRevaluation.Add")]
        public async Task<IActionResult> Create([FromBody] CreateRevaluationDto dto)
        {
            if (dto == null)
            {
                return Json(new { success = false, message = "بيانات العملية غير صحيحة." });
            }

            if (dto.CompanyId <= 0)
            {
                dto.CompanyId = _currentUser.CompanyId > 0 ? _currentUser.CompanyId : 1;
            }

            if (!_currentUser.HasAllBranches && (!dto.BranchId.HasValue || dto.BranchId.Value <= 0))
            {
                dto.BranchId = _currentUser.BranchId;
            }

            var (success, message, headerId, revNumber) = await _revaluationService.CreateRevaluationAsync(dto, _currentUser.UserId);
            return Json(new
            {
                success = success,
                message = message,
                headerId = headerId,
                revaluationNumber = revNumber
            });
        }

        // ─── GET: Details ────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.CurrencyRevaluation.View")]
        public async Task<IActionResult> Details(int id)
        {
            var details = await _revaluationService.GetRevaluationByIdAsync(id);
            if (details == null)
            {
                TempData["ErrorMessage"] = "عملية إعادة التقييم غير موجودة.";
                return RedirectToAction(nameof(Index));
            }

            return View(details);
        }

        // ─── POST: Approve (اعتماد وترحيل القيد) ─────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.CurrencyRevaluation.Approve")]
        public async Task<IActionResult> Approve(int id)
        {
            var (success, message, journalEntryId) = await _revaluationService.ApproveAndPostAsync(id, _currentUser.UserId);
            return Json(new { success, message, journalEntryId });
        }

        // ─── POST: Reverse (عكس القيد المحاسبي) ──────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.CurrencyRevaluation.Reverse")]
        public async Task<IActionResult> Reverse(int id, DateTime? reversalDate)
        {
            var (success, message, reversalJournalEntryId) = await _revaluationService.ReverseAsync(id, reversalDate, _currentUser.UserId);
            return Json(new { success, message, reversalJournalEntryId });
        }

        // ─── POST: Delete ────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.CurrencyRevaluation.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var (success, message) = await _revaluationService.DeleteAsync(id, _currentUser.UserId);
            return Json(new { success, message });
        }

        // ─── GET: Print ──────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.CurrencyRevaluation.Print")]
        public async Task<IActionResult> Print(int id)
        {
            var details = await _revaluationService.GetRevaluationByIdAsync(id);
            if (details == null)
            {
                TempData["ErrorMessage"] = "عملية إعادة التقييم غير موجودة.";
                return RedirectToAction(nameof(Index));
            }

            return View(details);
        }

        // ─── GET: ExportExcel (تصدير CSV / Excel) ────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.CurrencyRevaluation.Export")]
        public async Task<IActionResult> ExportExcel(int id)
        {
            var details = await _revaluationService.GetRevaluationByIdAsync(id);
            if (details == null)
            {
                return NotFound("عملية إعادة التقييم غير موجودة.");
            }

            var sb = new StringBuilder();
            sb.AppendLine("م,نوع الحساب,كود الحساب/الطرف,الاسم,الفرع,العملة,الرصيد الأجنبي,القيمة الدفترية المحلية,سعر الصرف المسجل,سعر التقييم,القيمة بعد التقييم,فرق التقييم,نوع الفرق");

            foreach (var l in details.Lines)
            {
                string diffTypeName = l.DifferenceType == RevaluationDifferenceType.Gain ? "ربح"
                    : (l.DifferenceType == RevaluationDifferenceType.Loss ? "خسارة" : "لا يوجد");

                sb.AppendLine($"\"{l.LineNumber}\",\"{l.SourceTypeName}\",\"{l.SourceCode}\",\"{l.SourceName}\",\"{l.BranchName}\",\"{l.CurrencyCode}\",\"{l.ForeignBalance:F4}\",\"{l.CarryingLocalAmount:F4}\",\"{l.CurrentRecordedRate:F6}\",\"{l.ExchangeRate:F6}\",\"{l.RevaluedLocalAmount:F4}\",\"{l.DifferenceAmount:F4}\",\"{diffTypeName}\"");
            }

            byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            string fileName = $"ForeignCurrencyRevaluation_{details.RevaluationNumber}_{details.RevaluationDate:yyyyMMdd}.csv";

            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // ─── Helper: Populate Lookups ────────────────────────────────────────
        private async Task PopulateFilterLookupsAsync(int? selectedBranchId)
        {
            var branchesQuery = _context.Branches.Where(b => b.IsActive);
            if (!_currentUser.HasAllBranches)
            {
                var allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUser.UserId)
                    .Select(ub => ub.BranchId)
                    .ToListAsync();

                if (!allowedBranchIds.Any() && _currentUser.BranchId > 0)
                    allowedBranchIds = new List<int> { _currentUser.BranchId };

                branchesQuery = branchesQuery.Where(b => allowedBranchIds.Contains(b.Id));
            }

            ViewBag.Branches = await branchesQuery.OrderBy(b => b.NameAr).AsNoTracking().ToListAsync();
            ViewBag.Currencies = await _context.Currencies.Where(c => c.IsActive && !c.IsBaseCurrency).OrderBy(c => c.Code).AsNoTracking().ToListAsync();
        }

        private async Task PopulateFormLookupsAsync()
        {
            var branchesQuery = _context.Branches.Where(b => b.IsActive);
            if (!_currentUser.HasAllBranches)
            {
                var allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUser.UserId)
                    .Select(ub => ub.BranchId)
                    .ToListAsync();

                if (!allowedBranchIds.Any() && _currentUser.BranchId > 0)
                    allowedBranchIds = new List<int> { _currentUser.BranchId };

                branchesQuery = branchesQuery.Where(b => allowedBranchIds.Contains(b.Id));
            }

            ViewBag.Branches = await branchesQuery.OrderBy(b => b.NameAr).AsNoTracking().ToListAsync();
            ViewBag.Currencies = await _context.Currencies.Where(c => c.IsActive && !c.IsBaseCurrency).OrderBy(c => c.Code).AsNoTracking().ToListAsync();

            // Companies
            ViewBag.Companies = await _context.Companies.Where(c => c.IsActive).OrderBy(c => c.NameAr).AsNoTracking().ToListAsync();

            // Gain Accounts (Revenues & Credits)
            ViewBag.GainAccounts = await _context.GLAccounts
                .Where(a => a.IsActive && !a.IsGroup && (a.AccountCategory == AccountCategory.Revenues || a.AccountNature == AccountNature.Credit))
                .OrderBy(a => a.Code)
                .Select(a => new { a.Id, a.Code, Name = a.LocalizedName })
                .AsNoTracking()
                .ToListAsync();

            // Loss Accounts (Expenses & Debits)
            ViewBag.LossAccounts = await _context.GLAccounts
                .Where(a => a.IsActive && !a.IsGroup && (a.AccountCategory == AccountCategory.Expenses || a.AccountNature == AccountNature.Debit))
                .OrderBy(a => a.Code)
                .Select(a => new { a.Id, a.Code, Name = a.LocalizedName })
                .AsNoTracking()
                .ToListAsync();
        }
    }
}
