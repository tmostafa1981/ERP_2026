using System;
using System.Threading.Tasks;
using ERP.Application.DTOs.FixedAssets;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class AssetCategoriesController : Controller
    {
        private readonly IFixedAssetCategoryService _categoryService;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public AssetCategoriesController(
            IFixedAssetCategoryService categoryService,
            ErpDbContext context,
            ICurrentUserContext currentUser)
        {
            _categoryService = categoryService;
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<IActionResult> Index()
        {
            var categories = await _categoryService.GetAllAsync(_currentUser.HasAllBranches ? null : _currentUser.BranchId);
            return View(categories);
        }

        public async Task<IActionResult> Create()
        {
            await PopulateDropDownsAsync();
            var model = new CreateAssetCategoryDto
            {
                DefaultUsefulLifeMonths = 60,
                DefaultDepreciationMethod = Domain.Enums.DepreciationMethod.StraightLine,
                BranchId = _currentUser.HasAllBranches ? null : _currentUser.BranchId
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateAssetCategoryDto dto)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDropDownsAsync();
                return View(dto);
            }

            var res = await _categoryService.CreateAsync(dto, _currentUser.UserId ?? "SYSTEM");
            if (!res.Success)
            {
                ModelState.AddModelError("", res.Message);
                await PopulateDropDownsAsync();
                return View(dto);
            }

            TempData["SuccessMessage"] = res.Message;
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var cat = await _categoryService.GetByIdAsync(id);
            if (cat == null) return NotFound();

            await PopulateDropDownsAsync();

            var model = new CreateAssetCategoryDto
            {
                Code = cat.Code,
                NameAr = cat.NameAr,
                NameEn = cat.NameEn,
                Description = cat.Description,
                DefaultUsefulLifeMonths = cat.DefaultUsefulLifeMonths,
                DefaultDepreciationMethod = cat.DefaultDepreciationMethod,
                DefaultResidualValueRate = cat.DefaultResidualValueRate,
                AssetCostAccountId = cat.AssetCostAccountId,
                AccumulatedDepreciationAccountId = cat.AccumulatedDepreciationAccountId,
                DepreciationExpenseAccountId = cat.DepreciationExpenseAccountId,
                DisposalGainAccountId = cat.DisposalGainAccountId,
                DisposalLossAccountId = cat.DisposalLossAccountId,
                CipAccountId = cat.CipAccountId,
                BranchId = cat.BranchId,
                IsActive = cat.IsActive
            };

            ViewBag.CategoryId = id;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CreateAssetCategoryDto dto)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.CategoryId = id;
                await PopulateDropDownsAsync();
                return View(dto);
            }

            var res = await _categoryService.UpdateAsync(id, dto, _currentUser.UserId ?? "SYSTEM");
            if (!res.Success)
            {
                ViewBag.CategoryId = id;
                ModelState.AddModelError("", res.Message);
                await PopulateDropDownsAsync();
                return View(dto);
            }

            TempData["SuccessMessage"] = res.Message;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var res = await _categoryService.DeleteAsync(id, _currentUser.UserId ?? "SYSTEM");
            if (!res.Success)
            {
                TempData["ErrorMessage"] = res.Message;
            }
            else
            {
                TempData["SuccessMessage"] = res.Message;
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateDropDownsAsync()
        {
            var accounts = await _context.GLAccounts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(a => a.IsActive && !a.IsGroup)
                .OrderBy(a => a.Code)
                .Select(a => new { a.Id, Display = $"{a.Code} - {a.NameAr}" })
                .ToListAsync();

            ViewBag.Accounts = new SelectList(accounts, "Id", "Display");

            var branches = await _context.Branches
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(b => b.IsActive)
                .OrderBy(b => b.Code)
                .Select(b => new { b.Id, Display = $"{b.Code} - {b.NameAr}" })
                .ToListAsync();

            ViewBag.Branches = new SelectList(branches, "Id", "Display");
        }
    }
}
