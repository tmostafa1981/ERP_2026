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
    public class SalesmanController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code   = "SALM001";
        private const string SC_Name   = "Salesmen";
        private const string SC_Module = "MasterData";

        public SalesmanController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
        {
            _context     = context;
            _currentUser = currentUser;
            _audit       = audit;
        }

        [HttpGet]
        [HasPermission("MasterData.Salesmen.View")]
        public async Task<IActionResult> Index(string? searchString, int? branchId, int page = 1)
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

            int? effectiveBranchId = branchId;
            if (effectiveBranchId == null || effectiveBranchId == 0)
            {
                effectiveBranchId = _currentUser.HasAllBranches ? null : _currentUser.BranchId;
            }

            var query = _context.Salesmen
                .Include(s => s.Branch)
                .AsNoTracking();

            if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                if (!_currentUser.CanAccessBranch(effectiveBranchId.Value))
                    return Forbid();

                query = query.Where(s => s.BranchId == effectiveBranchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                query = query.Where(s => s.BranchId == _currentUser.BranchId);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(s =>
                    s.Code.Contains(searchString) ||
                    s.NameAr.Contains(searchString) ||
                    s.NameEn.Contains(searchString) ||
                    (s.Mobile != null && s.Mobile.Contains(searchString)));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(s => s.BranchId).ThenBy(s => s.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.SearchString = searchString;
            ViewBag.BranchId     = effectiveBranchId;
            ViewBag.CurrentPage  = page;
            ViewBag.TotalPages   = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount   = totalCount;

            return View(items);
        }

        [HttpGet]
        public async Task<IActionResult> GetByBranch(int branchId)
        {
            if (branchId <= 0 || !_currentUser.CanAccessBranch(branchId))
                return Json(new object[] { });

            var salesmen = await _context.Salesmen
                .Where(s => s.BranchId == branchId && s.IsActive)
                .OrderBy(s => s.Code)
                .Select(s => new { id = s.Id, name = s.LocalizedName, code = s.Code })
                .ToListAsync();

            return Json(salesmen);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Salesmen.Add")]
        public async Task<IActionResult> Create(Salesman model)
        {
            if (model.BranchId <= 0 || !_currentUser.CanAccessBranch(model.BranchId))
                ModelState.AddModelError("BranchId", "Invalid branch or access denied.");

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Salesman Code is required.");
            else if (await _context.Salesmen.AnyAsync(s => s.BranchId == model.BranchId && s.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "Salesman Code already exists in this branch.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (ModelState.IsValid)
            {
                model.Code      = model.Code.Trim();
                model.NameAr    = model.NameAr.Trim();
                model.NameEn    = model.NameEn.Trim();
                model.CreatedAt = DateTime.UtcNow;

                _context.Salesmen.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Salesman",
                    entityId:   model.Id.ToString(),
                    newValues: new { model.Id, model.BranchId, model.Code, model.NameAr, model.NameEn, model.Mobile, model.Phone, model.IsActive });

                TempData["SuccessMessage"] = "Salesman created successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Salesmen.Edit")]
        public async Task<IActionResult> Edit(Salesman model)
        {
            var entity = await _context.Salesmen.FindAsync(model.Id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId) || !_currentUser.CanAccessBranch(model.BranchId))
                return Forbid();

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Salesman Code is required.");
            else if (await _context.Salesmen.AnyAsync(s => s.BranchId == model.BranchId && s.Code == model.Code.Trim() && s.Id != model.Id))
                ModelState.AddModelError("Code", "Salesman Code already exists in this branch.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (ModelState.IsValid)
            {
                var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.NameAr, entity.NameEn, entity.Mobile, entity.Phone, entity.IsActive };

                entity.BranchId  = model.BranchId;
                entity.Code      = model.Code.Trim();
                entity.NameAr    = model.NameAr.Trim();
                entity.NameEn    = model.NameEn.Trim();
                entity.Mobile    = model.Mobile?.Trim();
                entity.Phone     = model.Phone?.Trim();
                entity.IsActive  = model.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Salesman",
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new { entity.Id, entity.BranchId, entity.Code, entity.NameAr, entity.NameEn, entity.Mobile, entity.Phone, entity.IsActive });

                TempData["SuccessMessage"] = "Salesman updated successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Salesmen.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Salesmen.FindAsync(id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            bool hasCustomers = await _context.Customers.AnyAsync(c => c.SalesmanId == id);
            if (hasCustomers)
            {
                TempData["ErrorMessage"] = "Cannot delete salesman because they are assigned to customers.";
                return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
            }

            var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.NameAr, entity.NameEn, entity.IsActive };

            _context.Salesmen.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Salesman",
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "Salesman deleted successfully.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Salesmen.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.Salesmen.FindAsync(id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Salesman",
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.BranchId, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"Salesman status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }
    }
}
