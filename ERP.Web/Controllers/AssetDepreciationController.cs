using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs.FixedAssets;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class AssetDepreciationController : Controller
    {
        private readonly IAssetDepreciationService _depreciationService;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public AssetDepreciationController(
            IAssetDepreciationService depreciationService,
            ErpDbContext context,
            ICurrentUserContext currentUser)
        {
            _depreciationService = depreciationService;
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<IActionResult> Index(int? branchId, int? fiscalYearId)
        {
            int companyId = _currentUser.CompanyId > 0 ? _currentUser.CompanyId : 1;
            branchId ??= _currentUser.HasAllBranches ? null : _currentUser.BranchId;

            var runs = await _depreciationService.GetDepreciationRunsAsync(companyId, branchId, fiscalYearId, _currentUser.UserId ?? "SYSTEM");

            await PopulateFilterDropDownsAsync();
            ViewBag.SelectedBranchId = branchId;
            ViewBag.SelectedFiscalYearId = fiscalYearId;

            return View(runs);
        }

        public async Task<IActionResult> Create()
        {
            await PopulateFormDropDownsAsync();
            var model = new ExecuteDepreciationRunDto
            {
                RunDate = DateTime.Today,
                BranchId = _currentUser.HasAllBranches ? null : _currentUser.BranchId,
                CompanyId = _currentUser.CompanyId > 0 ? _currentUser.CompanyId : 1,
                AutoPost = true
            };
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Preview([FromBody] RunDepreciationFilterDto filter)
        {
            if (filter == null) return Json(new { success = false, message = "بيانات غير صالحة." });

            filter.CompanyId = _currentUser.CompanyId > 0 ? _currentUser.CompanyId : 1;
            var result = await _depreciationService.PreviewDepreciationRunAsync(filter, _currentUser.UserId ?? "SYSTEM");
            return Json(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ExecuteDepreciationRunDto dto)
        {
            dto.CompanyId = _currentUser.CompanyId > 0 ? _currentUser.CompanyId : 1;
            var res = await _depreciationService.ExecuteDepreciationRunAsync(dto, _currentUser.UserId ?? "SYSTEM");

            if (!res.Success)
            {
                TempData["ErrorMessage"] = res.Message;
                await PopulateFormDropDownsAsync();
                return View(dto);
            }

            TempData["SuccessMessage"] = res.Message;
            return RedirectToAction(nameof(Details), new { id = res.RunId });
        }

        public async Task<IActionResult> Details(int id)
        {
            var run = await _depreciationService.GetDepreciationRunByIdAsync(id, _currentUser.UserId ?? "SYSTEM");
            if (run == null) return NotFound();

            return View(run);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            var res = await _depreciationService.ApproveDepreciationRunAsync(id, _currentUser.UserId ?? "SYSTEM");
            if (!res.Success) TempData["ErrorMessage"] = res.Message;
            else TempData["SuccessMessage"] = res.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reverse(int id, DateTime? reversalDate)
        {
            var date = reversalDate ?? DateTime.Today;
            var res = await _depreciationService.ReverseDepreciationRunAsync(id, date, _currentUser.UserId ?? "SYSTEM");
            if (!res.Success) TempData["ErrorMessage"] = res.Message;
            else TempData["SuccessMessage"] = res.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        public async Task<IActionResult> Print(int id)
        {
            var run = await _depreciationService.GetDepreciationRunByIdAsync(id, _currentUser.UserId ?? "SYSTEM");
            if (run == null) return NotFound();

            var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == (_currentUser.CompanyId > 0 ? _currentUser.CompanyId : 1));
            ViewBag.Company = company;

            return View(run);
        }

        private async Task PopulateFilterDropDownsAsync()
        {
            var branches = await _context.Branches.IgnoreQueryFilters().AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Code).ToListAsync();
            ViewBag.Branches = new SelectList(branches, "Id", "LocalizedName");

            var fiscalYears = await _context.FiscalYears.IgnoreQueryFilters().AsNoTracking().OrderByDescending(y => y.Year).ToListAsync();
            ViewBag.FiscalYears = new SelectList(fiscalYears, "Id", "LocalizedName");
        }

        private async Task PopulateFormDropDownsAsync()
        {
            await PopulateFilterDropDownsAsync();

            var categories = await _context.AssetCategories.IgnoreQueryFilters().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "LocalizedName");

            var assets = await _context.FixedAssets.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.Status == AssetStatus.InService && !a.IsFullyDepreciated)
                .OrderBy(a => a.AssetCode)
                .Select(a => new { a.Id, Display = $"{a.AssetCode} - {a.NameAr}" })
                .ToListAsync();
            ViewBag.Assets = new SelectList(assets, "Id", "Display");
        }
    }
}
