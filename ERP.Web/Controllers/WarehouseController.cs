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
    public class WarehouseController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code   = "WH001";
        private const string SC_Name   = "Warehouses";
        private const string SC_Module = "MasterData";

        public WarehouseController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
        {
            _context     = context;
            _currentUser = currentUser;
            _audit       = audit;
        }

        [HttpGet]
        [HasPermission("MasterData.Warehouses.View")]
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

            var query = _context.Warehouses
                .Include(w => w.Branch)
                .AsNoTracking();

            if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                if (!_currentUser.CanAccessBranch(effectiveBranchId.Value))
                    return Forbid();

                query = query.Where(w => w.BranchId == effectiveBranchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                query = query.Where(w => w.BranchId == _currentUser.BranchId);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(w =>
                    w.Code.Contains(searchString) ||
                    w.NameAr.Contains(searchString) ||
                    w.NameEn.Contains(searchString));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(w => w.BranchId).ThenBy(w => w.Code)
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Warehouses.Add")]
        public async Task<IActionResult> Create(Warehouse model)
        {
            if (model.BranchId <= 0)
                ModelState.AddModelError("BranchId", "Please select a valid Branch.");
            else if (!_currentUser.CanAccessBranch(model.BranchId))
                ModelState.AddModelError("BranchId", "You do not have access to this branch.");

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Warehouse Code is required.");
            else if (await _context.Warehouses.AnyAsync(w => w.BranchId == model.BranchId && w.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "Warehouse Code already exists for this branch.");

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

                _context.Warehouses.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Warehouse",
                    entityId:   model.Id.ToString(),
                    newValues: new
                    {
                        model.Id,
                        model.BranchId,
                        model.Code,
                        model.NameAr,
                        model.NameEn,
                        model.InventoryAccountId,
                        model.IsActive
                    });

                TempData["SuccessMessage"] = "Warehouse created successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId > 0 ? (int?)model.BranchId : null });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Warehouses.Edit")]
        public async Task<IActionResult> Edit(Warehouse model)
        {
            var entity = await _context.Warehouses.FindAsync(model.Id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            if (model.BranchId <= 0)
                ModelState.AddModelError("BranchId", "Please select a valid Branch.");
            else if (!_currentUser.CanAccessBranch(model.BranchId))
                ModelState.AddModelError("BranchId", "You do not have access to this branch.");

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Warehouse Code is required.");
            else if (await _context.Warehouses.AnyAsync(w => w.BranchId == model.BranchId && w.Code == model.Code.Trim() && w.Id != model.Id))
                ModelState.AddModelError("Code", "Warehouse Code already exists for this branch.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (ModelState.IsValid)
            {
                var oldValues = new
                {
                    entity.Id,
                    entity.BranchId,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.InventoryAccountId,
                    entity.IsActive
                };

                entity.BranchId           = model.BranchId;
                entity.Code               = model.Code.Trim();
                entity.NameAr             = model.NameAr.Trim();
                entity.NameEn             = model.NameEn.Trim();
                entity.InventoryAccountId = model.InventoryAccountId;
                entity.IsActive           = model.IsActive;
                entity.UpdatedAt          = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Warehouse",
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new
                    {
                        entity.Id,
                        entity.BranchId,
                        entity.Code,
                        entity.NameAr,
                        entity.NameEn,
                        entity.InventoryAccountId,
                        entity.IsActive
                    });

                TempData["SuccessMessage"] = "Warehouse updated successfully.";
                return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Warehouses.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Warehouses.FindAsync(id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            var oldValues = new
            {
                entity.Id,
                entity.BranchId,
                entity.Code,
                entity.NameAr,
                entity.NameEn,
                entity.IsActive
            };

            _context.Warehouses.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Warehouse",
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "Warehouse deleted successfully.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Warehouses.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.Warehouses.FindAsync(id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Warehouse",
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.BranchId, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"Warehouse status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        [HttpGet]
        [HasPermission("MasterData.Warehouses.View")]
        public async Task<IActionResult> GetByBranch(int branchId)
        {
            if (!_currentUser.CanAccessBranch(branchId))
                return Forbid();

            var warehouses = await _context.Warehouses
                .Where(w => w.BranchId == branchId && w.IsActive)
                .OrderBy(w => w.Code)
                .Select(w => new
                {
                    w.Id,
                    w.Code,
                    Name = w.LocalizedName
                })
                .ToListAsync();

            return Json(warehouses);
        }
    }
}
