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
    public class FixedAssetsController : Controller
    {
        private readonly IFixedAssetService _assetService;
        private readonly IFixedAssetCategoryService _categoryService;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public FixedAssetsController(
            IFixedAssetService assetService,
            IFixedAssetCategoryService categoryService,
            ErpDbContext context,
            ICurrentUserContext currentUser)
        {
            _assetService = assetService;
            _categoryService = categoryService;
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<IActionResult> Index(FixedAssetFilterDto filter)
        {
            filter.BranchId ??= _currentUser.HasAllBranches ? null : _currentUser.BranchId;
            var assets = await _assetService.GetAssetsAsync(filter, _currentUser.UserId ?? "SYSTEM");

            // Stats
            var allQuery = _context.FixedAssets.IgnoreQueryFilters().AsNoTracking();
            if (filter.BranchId.HasValue && filter.BranchId > 0)
                allQuery = allQuery.Where(a => a.BranchId == filter.BranchId.Value);

            ViewBag.TotalAssetsCount = await allQuery.CountAsync();
            ViewBag.InServiceCount = await allQuery.CountAsync(a => a.Status == AssetStatus.InService);
            ViewBag.CipCount = await allQuery.CountAsync(a => a.Status == AssetStatus.UnderConstruction);
            ViewBag.DisposedCount = await allQuery.CountAsync(a => a.Status == AssetStatus.Disposed);
            ViewBag.TotalCost = await allQuery.Where(a => a.Status != AssetStatus.Disposed).SumAsync(a => (decimal?)a.CurrentCostLocal) ?? 0m;
            ViewBag.TotalAccDep = await allQuery.Where(a => a.Status != AssetStatus.Disposed).SumAsync(a => (decimal?)a.CurrentAccumulatedDepreciation) ?? 0m;
            ViewBag.TotalBookValue = await allQuery.Where(a => a.Status != AssetStatus.Disposed).SumAsync(a => (decimal?)a.CurrentBookValue) ?? 0m;

            await PopulateFilterDropDownsAsync();
            ViewBag.Filter = filter;
            return View(assets);
        }

        public async Task<IActionResult> Details(int id)
        {
            var asset = await _assetService.GetAssetByIdAsync(id, _currentUser.UserId ?? "SYSTEM");
            if (asset == null) return NotFound();

            await PopulateModalDropDownsAsync(asset.BranchId);
            return View(asset);
        }

        public async Task<IActionResult> Create()
        {
            await PopulateFormDropDownsAsync();
            var model = new CreateFixedAssetDto
            {
                BranchId = _currentUser.BranchId > 0 ? _currentUser.BranchId : 1,
                PurchaseDate = DateTime.Today,
                PlacedInServiceDate = DateTime.Today,
                DepreciationStartDate = DateTime.Today,
                Status = AssetStatus.InService,
                DepreciationMethod = DepreciationMethod.StraightLine,
                FirstMonthPolicy = FirstMonthPolicy.FullMonth,
                UsefulLifeMonths = 60
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateFixedAssetDto dto)
        {
            if (!ModelState.IsValid)
            {
                await PopulateFormDropDownsAsync();
                return View(dto);
            }

            var res = await _assetService.CreateAssetAsync(dto, _currentUser.UserId ?? "SYSTEM");
            if (!res.Success)
            {
                ModelState.AddModelError("", res.Message);
                await PopulateFormDropDownsAsync();
                return View(dto);
            }

            TempData["SuccessMessage"] = res.Message;
            return RedirectToAction(nameof(Details), new { id = res.AssetId });
        }

        public async Task<IActionResult> Edit(int id)
        {
            var asset = await _assetService.GetAssetByIdAsync(id, _currentUser.UserId ?? "SYSTEM");
            if (asset == null) return NotFound();

            await PopulateFormDropDownsAsync();

            var model = new UpdateFixedAssetDto
            {
                Id = asset.Id,
                RowVersion = asset.RowVersion,
                AssetCode = asset.AssetCode,
                NameAr = asset.NameAr,
                NameEn = asset.NameEn,
                AssetCategoryId = asset.AssetCategoryId,
                Description = asset.Description,
                SerialNumber = asset.SerialNumber,
                Barcode = asset.Barcode,
                BranchId = asset.BranchId,
                WarehouseId = asset.WarehouseId,
                Department = asset.Department,
                CostCenterId = asset.CostCenterId,
                CustodianEmployeeName = asset.CustodianEmployeeName,
                Status = asset.Status,
                PurchaseDate = asset.PurchaseDate,
                PlacedInServiceDate = asset.PlacedInServiceDate,
                VendorId = asset.VendorId,
                PurchaseInvoiceReference = asset.PurchaseInvoiceReference,
                CurrencyId = asset.CurrencyId,
                PurchasePriceForeign = asset.PurchasePriceForeign,
                ExchangeRate = asset.ExchangeRate,
                InitialCostLocal = asset.InitialCostLocal,
                AdditionalExpensesLocal = asset.AdditionalExpensesLocal,
                ResidualValue = asset.ResidualValue,
                UsefulLifeMonths = asset.UsefulLifeMonths,
                DepreciationStartDate = asset.DepreciationStartDate,
                DepreciationMethod = asset.DepreciationMethod,
                DepreciationRate = asset.DepreciationRate,
                TotalExpectedUnits = asset.TotalExpectedUnits,
                FirstMonthPolicy = asset.FirstMonthPolicy,
                AssetCostAccountId = asset.AssetCostAccountId,
                AccumulatedDepreciationAccountId = asset.AccumulatedDepreciationAccountId,
                DepreciationExpenseAccountId = asset.DepreciationExpenseAccountId,
                DisposalGainAccountId = asset.DisposalGainAccountId,
                DisposalLossAccountId = asset.DisposalLossAccountId,
                IsOpeningAsset = asset.IsOpeningAsset,
                OpeningAccumulatedDepreciation = asset.OpeningAccumulatedDepreciation,
                OpeningCarryingValue = asset.OpeningCarryingValue,
                Notes = asset.Notes
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UpdateFixedAssetDto dto)
        {
            if (!ModelState.IsValid)
            {
                await PopulateFormDropDownsAsync();
                return View(dto);
            }

            var res = await _assetService.UpdateAssetAsync(dto, _currentUser.UserId ?? "SYSTEM");
            if (!res.Success)
            {
                ModelState.AddModelError("", res.Message);
                await PopulateFormDropDownsAsync();
                return View(dto);
            }

            TempData["SuccessMessage"] = res.Message;
            return RedirectToAction(nameof(Details), new { id = dto.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Transfer(AssetTransferDto dto)
        {
            var res = await _assetService.TransferAssetAsync(dto, _currentUser.UserId ?? "SYSTEM");
            if (!res.Success) TempData["ErrorMessage"] = res.Message;
            else TempData["SuccessMessage"] = res.Message;

            return RedirectToAction(nameof(Details), new { id = dto.AssetId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdditionalCost(AssetAdditionalCostDto dto)
        {
            var res = await _assetService.AddAdditionalCostAsync(dto, _currentUser.UserId ?? "SYSTEM");
            if (!res.Success) TempData["ErrorMessage"] = res.Message;
            else TempData["SuccessMessage"] = res.Message;

            return RedirectToAction(nameof(Details), new { id = dto.AssetId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisposeAsset(AssetDisposalDto dto)
        {
            var res = await _assetService.DisposeAssetAsync(dto, _currentUser.UserId ?? "SYSTEM");
            if (!res.Success) TempData["ErrorMessage"] = res.Message;
            else TempData["SuccessMessage"] = res.Message;

            return RedirectToAction(nameof(Details), new { id = dto.AssetId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CapitalizeCip(AssetCapitalizeCipDto dto)
        {
            var res = await _assetService.CapitalizeCipAssetAsync(dto, _currentUser.UserId ?? "SYSTEM");
            if (!res.Success) TempData["ErrorMessage"] = res.Message;
            else TempData["SuccessMessage"] = res.Message;

            return RedirectToAction(nameof(Details), new { id = dto.AssetId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var res = await _assetService.DeleteAssetAsync(id, _currentUser.UserId ?? "SYSTEM");
            if (!res.Success) TempData["ErrorMessage"] = res.Message;
            else TempData["SuccessMessage"] = res.Message;

            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateFilterDropDownsAsync()
        {
            var branches = await _context.Branches.IgnoreQueryFilters().AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Code).ToListAsync();
            ViewBag.Branches = new SelectList(branches, "Id", "LocalizedName");

            var categories = await _context.AssetCategories.IgnoreQueryFilters().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "LocalizedName");

            var costCenters = await _context.CostCenters.IgnoreQueryFilters().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();
            ViewBag.CostCenters = new SelectList(costCenters, "Id", "LocalizedName");
        }

        private async Task PopulateFormDropDownsAsync()
        {
            await PopulateFilterDropDownsAsync();

            var vendors = await _context.Vendors.IgnoreQueryFilters().AsNoTracking().Where(v => v.IsActive).OrderBy(v => v.NameAr).ToListAsync();
            ViewBag.Vendors = new SelectList(vendors, "Id", "LocalizedName");

            var warehouses = await _context.Warehouses.IgnoreQueryFilters().AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Code).ToListAsync();
            ViewBag.Warehouses = new SelectList(warehouses, "Id", "LocalizedName");

            var currencies = await _context.Currencies.IgnoreQueryFilters().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();
            ViewBag.Currencies = new SelectList(currencies, "Id", "Code");

            var accounts = await _context.GLAccounts.IgnoreQueryFilters().AsNoTracking().Where(a => a.IsActive && !a.IsGroup).OrderBy(a => a.Code)
                .Select(a => new { a.Id, Display = $"{a.Code} - {a.NameAr}" }).ToListAsync();
            ViewBag.Accounts = new SelectList(accounts, "Id", "Display");
        }

        private async Task PopulateModalDropDownsAsync(int currentBranchId)
        {
            var branches = await _context.Branches.IgnoreQueryFilters().AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Code).ToListAsync();
            ViewBag.Branches = new SelectList(branches, "Id", "LocalizedName", currentBranchId);

            var costCenters = await _context.CostCenters.IgnoreQueryFilters().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();
            ViewBag.CostCenters = new SelectList(costCenters, "Id", "LocalizedName");

            var warehouses = await _context.Warehouses.IgnoreQueryFilters().AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Code).ToListAsync();
            ViewBag.Warehouses = new SelectList(warehouses, "Id", "LocalizedName");

            var cashAccounts = await _context.GLAccounts.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.IsActive && !a.IsGroup && (a.Code.StartsWith("1101") || a.Code.StartsWith("1102")))
                .OrderBy(a => a.Code)
                .Select(a => new { a.Id, Display = $"{a.Code} - {a.NameAr}" }).ToListAsync();
            ViewBag.CashAccounts = new SelectList(cashAccounts, "Id", "Display");
        }
    }
}
