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
    public class CustomerController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code   = "CUS001";
        private const string SC_Name   = "Customers";
        private const string SC_Module = "MasterData";

        public CustomerController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
        {
            _context     = context;
            _currentUser = currentUser;
            _audit       = audit;
        }

        [HttpGet]
        [HasPermission("MasterData.Customers.View")]
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

            ViewBag.Categories   = await _context.CustomerCategories.Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();
            ViewBag.Governorates = await _context.Governorates.Where(g => g.IsActive).OrderBy(g => g.Code).ToListAsync();
            ViewBag.Currencies   = await _context.Currencies.Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();

            int? effectiveBranchId = branchId;
            if (effectiveBranchId == null || effectiveBranchId == 0)
            {
                effectiveBranchId = _currentUser.HasAllBranches ? null : _currentUser.BranchId;
            }

            var query = _context.Customers
                .Include(c => c.Branch)
                .Include(c => c.CustomerCategory)
                .Include(c => c.Governorate)
                .Include(c => c.City)
                .Include(c => c.Salesman)
                .Include(c => c.Currency)
                .AsNoTracking();

            if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                if (!_currentUser.CanAccessBranch(effectiveBranchId.Value))
                    return Forbid();

                query = query.Where(c => c.BranchId == effectiveBranchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                query = query.Where(c => c.BranchId == _currentUser.BranchId);
            }

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                query = query.Where(c => c.CustomerCategoryId == categoryId.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(c =>
                    c.Code.Contains(searchString) ||
                    c.NameAr.Contains(searchString) ||
                    c.NameEn.Contains(searchString) ||
                    (c.TaxNumber != null && c.TaxNumber.Contains(searchString)) ||
                    (c.Mobile != null && c.Mobile.Contains(searchString)));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.BranchId).ThenBy(c => c.Code)
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
        [HasPermission("MasterData.Customers.Add")]
        public async Task<IActionResult> Create(Customer model)
        {
            // 1. Branch Authorization check
            if (model.BranchId <= 0 || !_currentUser.CanAccessBranch(model.BranchId))
                ModelState.AddModelError("BranchId", "Invalid branch or access denied.");

            // 2. Code uniqueness in the same branch
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Customer Code is required.");
            else if (await _context.Customers.AnyAsync(c => c.BranchId == model.BranchId && c.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "Customer Code already exists in this branch.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (model.CustomerCategoryId <= 0 || !await _context.CustomerCategories.AnyAsync(cc => cc.Id == model.CustomerCategoryId))
                ModelState.AddModelError("CustomerCategoryId", "Please select a valid customer category.");

            // 3. Salesman branch consistency: Salesman.BranchId == Customer.BranchId
            if (model.SalesmanId.HasValue && model.SalesmanId.Value > 0)
            {
                var salesman = await _context.Salesmen.FindAsync(model.SalesmanId.Value);
                if (salesman == null || salesman.BranchId != model.BranchId)
                    ModelState.AddModelError("SalesmanId", "Salesman must belong to the same branch as the customer.");
            }

            // 4. City governorate consistency: City.GovernorateId == Customer.GovernorateId
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

                _context.Customers.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Customer",
                    entityId:   model.Id.ToString(),
                    newValues: new { model.Id, model.BranchId, model.Code, model.NameAr, model.NameEn, model.CustomerCategoryId, model.IsActive });

                TempData["SuccessMessage"] = "Customer created successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Customers.Edit")]
        public async Task<IActionResult> Edit(Customer model)
        {
            var entity = await _context.Customers.FindAsync(model.Id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId) || !_currentUser.CanAccessBranch(model.BranchId))
                return Forbid();

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Customer Code is required.");
            else if (await _context.Customers.AnyAsync(c => c.BranchId == model.BranchId && c.Code == model.Code.Trim() && c.Id != model.Id))
                ModelState.AddModelError("Code", "Customer Code already exists in this branch.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (model.CustomerCategoryId <= 0 || !await _context.CustomerCategories.AnyAsync(cc => cc.Id == model.CustomerCategoryId))
                ModelState.AddModelError("CustomerCategoryId", "Please select a valid customer category.");

            if (model.SalesmanId.HasValue && model.SalesmanId.Value > 0)
            {
                var salesman = await _context.Salesmen.FindAsync(model.SalesmanId.Value);
                if (salesman == null || salesman.BranchId != model.BranchId)
                    ModelState.AddModelError("SalesmanId", "Salesman must belong to the same branch as the customer.");
            }

            if (model.CityId.HasValue && model.CityId.Value > 0)
            {
                var city = await _context.Cities.FindAsync(model.CityId.Value);
                if (city == null || (model.GovernorateId.HasValue && city.GovernorateId != model.GovernorateId.Value))
                    ModelState.AddModelError("CityId", "City does not belong to the selected governorate.");
            }

            if (ModelState.IsValid)
            {
                var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.NameAr, entity.NameEn, entity.CustomerCategoryId, entity.IsActive };

                entity.BranchId            = model.BranchId;
                entity.Code                = model.Code.Trim();
                entity.NameAr              = model.NameAr.Trim();
                entity.NameEn              = model.NameEn.Trim();
                entity.CustomerCategoryId  = model.CustomerCategoryId;
                entity.Address             = model.Address?.Trim();
                entity.GovernorateId       = model.GovernorateId;
                entity.CityId              = model.CityId;
                entity.TaxNumber           = model.TaxNumber?.Trim();
                entity.CommercialRegister  = model.CommercialRegister?.Trim();
                entity.TaxPayerType        = model.TaxPayerType?.Trim();
                entity.SalesmanId          = model.SalesmanId;
                entity.CurrencyId          = model.CurrencyId;
                entity.Phone               = model.Phone?.Trim();
                entity.Mobile              = model.Mobile?.Trim();
                entity.Email               = model.Email?.Trim();
                entity.IsActive            = model.IsActive;
                entity.UpdatedAt           = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Customer",
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new { entity.Id, entity.BranchId, entity.Code, entity.NameAr, entity.NameEn, entity.CustomerCategoryId, entity.IsActive });

                TempData["SuccessMessage"] = "Customer updated successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Customers.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Customers.FindAsync(id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.NameAr, entity.NameEn, entity.IsActive };

            _context.Customers.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Customer",
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "Customer deleted successfully.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Customers.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.Customers.FindAsync(id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Customer",
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.BranchId, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"Customer status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }
    }
}
