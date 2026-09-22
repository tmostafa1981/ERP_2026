using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class VendorController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code   = "VEN001";
        private const string SC_Name   = "Vendors";
        private const string SC_Module = "MasterData";

        public VendorController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
        {
            _context     = context;
            _currentUser = currentUser;
            _audit       = audit;
        }

        [HttpGet]
        [HasPermission("MasterData.Vendors.View")]
        public async Task<IActionResult> Index(string? searchString, int? branchId, int? categoryId, int page = 1)
        {
            int pageSize = 15;

            var branchesQuery = _currentUser.HasAllBranches
                ? _context.Branches.Where(b => b.IsActive)
                : _context.Branches.Where(b => b.IsActive && b.CompanyId == _currentUser.CompanyId);

            var allBranches = await branchesQuery.OrderBy(b => b.Code).ToListAsync();
            if (!_currentUser.HasAllBranches)
            {
                allBranches = allBranches.Where(b => _currentUser.CanAccessBranch(b.Id)).ToList();
            }
            ViewBag.Branches = allBranches;

            ViewBag.Categories   = await _context.VendorCategories.Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();
            ViewBag.Governorates = await _context.Governorates.Where(g => g.IsActive).OrderBy(g => g.Code).ToListAsync();
            ViewBag.Currencies   = await _context.Currencies.Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();

            int? effectiveBranchId = branchId;
            if (effectiveBranchId == null || effectiveBranchId == 0)
            {
                effectiveBranchId = _currentUser.HasAllBranches ? null : _currentUser.BranchId;
            }

            var query = _context.Vendors
                .Include(v => v.Branch)
                .Include(v => v.VendorCategory)
                .Include(v => v.Governorate)
                .Include(v => v.City)
                .Include(v => v.Currency)
                .AsNoTracking();

            if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                if (!_currentUser.CanAccessBranch(effectiveBranchId.Value))
                    return Forbid();

                query = query.Where(v => v.BranchId == effectiveBranchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                query = query.Where(v => v.BranchId == _currentUser.BranchId);
            }

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                query = query.Where(v => v.VendorCategoryId == categoryId.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(v =>
                    v.Code.Contains(searchString) ||
                    v.NameAr.Contains(searchString) ||
                    v.NameEn.Contains(searchString) ||
                    (v.TaxNumber != null && v.TaxNumber.Contains(searchString)) ||
                    (v.Mobile != null && v.Mobile.Contains(searchString)));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(v => v.BranchId).ThenBy(v => v.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.SearchString = searchString;
            ViewBag.BranchId     = effectiveBranchId;
            ViewBag.CategoryId   = categoryId;
            ViewBag.CurrentPage  = page;
            ViewBag.TotalPages   = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount   = totalCount;

            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Vendors.Add")]
        public async Task<IActionResult> Create(Vendor model)
        {
            if (model.BranchId <= 0 || !_currentUser.CanAccessBranch(model.BranchId))
                ModelState.AddModelError("BranchId", "Invalid branch or access denied.");

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Vendor Code is required.");
            else if (await _context.Vendors.AnyAsync(v => v.BranchId == model.BranchId && v.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "Vendor Code already exists in this branch.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (model.VendorCategoryId <= 0 || !await _context.VendorCategories.AnyAsync(vc => vc.Id == model.VendorCategoryId))
                ModelState.AddModelError("VendorCategoryId", "Please select a valid vendor category.");

            if (model.CityId.HasValue && model.CityId.Value > 0)
            {
                var city = await _context.Cities.FindAsync(model.CityId.Value);
                if (city == null || (model.GovernorateId.HasValue && city.GovernorateId != model.GovernorateId.Value))
                    ModelState.AddModelError("CityId", "City does not belong to the selected governorate.");
            }

            if (ModelState.IsValid)
            {
                model.Code               = model.Code.Trim();
                model.NameAr             = model.NameAr.Trim();
                model.NameEn             = model.NameEn.Trim();
                model.Address            = model.Address?.Trim();
                model.TaxNumber          = model.TaxNumber?.Trim();
                model.CommercialRegister = model.CommercialRegister?.Trim();
                model.TaxPayerType       = model.TaxPayerType?.Trim();
                model.Phone              = model.Phone?.Trim();
                model.Mobile             = model.Mobile?.Trim();
                model.Email              = model.Email?.Trim();
                model.CreatedAt          = DateTime.UtcNow;

                _context.Vendors.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Vendor",
                    entityId:   model.Id.ToString(),
                    newValues: new { model.Id, model.BranchId, model.Code, model.NameAr, model.NameEn, model.VendorCategoryId, model.IsActive });

                TempData["SuccessMessage"] = "Vendor created successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Vendors.Edit")]
        public async Task<IActionResult> Edit(Vendor model)
        {
            var entity = await _context.Vendors.FindAsync(model.Id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId) || !_currentUser.CanAccessBranch(model.BranchId))
                return Forbid();

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Vendor Code is required.");
            else if (await _context.Vendors.AnyAsync(v => v.BranchId == model.BranchId && v.Code == model.Code.Trim() && v.Id != model.Id))
                ModelState.AddModelError("Code", "Vendor Code already exists in this branch.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (model.VendorCategoryId <= 0 || !await _context.VendorCategories.AnyAsync(vc => vc.Id == model.VendorCategoryId))
                ModelState.AddModelError("VendorCategoryId", "Please select a valid vendor category.");

            if (model.CityId.HasValue && model.CityId.Value > 0)
            {
                var city = await _context.Cities.FindAsync(model.CityId.Value);
                if (city == null || (model.GovernorateId.HasValue && city.GovernorateId != model.GovernorateId.Value))
                    ModelState.AddModelError("CityId", "City does not belong to the selected governorate.");
            }

            if (ModelState.IsValid)
            {
                var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.NameAr, entity.NameEn, entity.VendorCategoryId, entity.IsActive };

                entity.BranchId            = model.BranchId;
                entity.Code                = model.Code.Trim();
                entity.NameAr              = model.NameAr.Trim();
                entity.NameEn              = model.NameEn.Trim();
                entity.VendorCategoryId    = model.VendorCategoryId;
                entity.Address             = model.Address?.Trim();
                entity.GovernorateId       = model.GovernorateId;
                entity.CityId              = model.CityId;
                entity.TaxNumber           = model.TaxNumber?.Trim();
                entity.CommercialRegister  = model.CommercialRegister?.Trim();
                entity.TaxPayerType        = model.TaxPayerType?.Trim();
                entity.CurrencyId          = model.CurrencyId;
                entity.Phone               = model.Phone?.Trim();
                entity.Mobile              = model.Mobile?.Trim();
                entity.Email               = model.Email?.Trim();
                entity.IsActive            = model.IsActive;
                entity.UpdatedAt           = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Vendor",
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new { entity.Id, entity.BranchId, entity.Code, entity.NameAr, entity.NameEn, entity.VendorCategoryId, entity.IsActive });

                TempData["SuccessMessage"] = "Vendor updated successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Vendors.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Vendors.FindAsync(id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.NameAr, entity.NameEn, entity.IsActive };

            _context.Vendors.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Vendor",
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "Vendor deleted successfully.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Vendors.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.Vendors.FindAsync(id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Vendor",
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.BranchId, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"Vendor status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }
    }
}
