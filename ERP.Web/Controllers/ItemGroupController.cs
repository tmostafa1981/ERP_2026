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
    public class ItemGroupController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code   = "ITMG001";
        private const string SC_Name   = "ItemGroups";
        private const string SC_Module = "MasterData";

        public ItemGroupController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
        {
            _context     = context;
            _currentUser = currentUser;
            _audit       = audit;
        }

        [HttpGet]
        [HasPermission("MasterData.ItemGroups.View")]
        public async Task<IActionResult> Index(string? searchString, int? parentId, int page = 1)
        {
            int pageSize = 15;
            var allGroups = await _context.ItemGroups.Where(ig => ig.IsActive).OrderBy(ig => ig.Code).ToListAsync();
            ViewBag.AllGroups = allGroups;

            var query = _context.ItemGroups
                .Include(ig => ig.ParentGroup)
                .Include(ig => ig.SubGroups)
                .Include(ig => ig.Items)
                .AsNoTracking();

            if (parentId.HasValue && parentId.Value > 0)
            {
                query = query.Where(ig => ig.ParentId == parentId.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(ig =>
                    ig.Code.Contains(searchString) ||
                    ig.NameAr.Contains(searchString) ||
                    ig.NameEn.Contains(searchString));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(ig => ig.ParentId).ThenBy(ig => ig.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.SearchString = searchString;
            ViewBag.ParentId     = parentId;
            ViewBag.CurrentPage  = page;
            ViewBag.TotalPages   = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount   = totalCount;

            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.ItemGroups.Add")]
        public async Task<IActionResult> Create(ItemGroup model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Item Group Code is required.");
            else if (await _context.ItemGroups.AnyAsync(ig => ig.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "Item Group Code already exists.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (model.ParentId.HasValue && model.ParentId.Value > 0 && !await _context.ItemGroups.AnyAsync(ig => ig.Id == model.ParentId.Value))
                ModelState.AddModelError("ParentId", "Selected parent group is invalid.");

            if (ModelState.IsValid)
            {
                model.Code      = model.Code.Trim();
                model.NameAr    = model.NameAr.Trim();
                model.NameEn    = model.NameEn.Trim();
                model.ParentId  = model.ParentId > 0 ? model.ParentId : null;
                model.CreatedAt = DateTime.UtcNow;

                _context.ItemGroups.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "ItemGroup",
                    entityId:   model.Id.ToString(),
                    newValues: new { model.Id, model.Code, model.NameAr, model.NameEn, model.ParentId, model.IsActive });

                TempData["SuccessMessage"] = "Item Group created successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.ItemGroups.Edit")]
        public async Task<IActionResult> Edit(ItemGroup model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Item Group Code is required.");
            else if (await _context.ItemGroups.AnyAsync(ig => ig.Code == model.Code.Trim() && ig.Id != model.Id))
                ModelState.AddModelError("Code", "Item Group Code already exists.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (model.ParentId.HasValue && model.ParentId.Value == model.Id)
                ModelState.AddModelError("ParentId", "A group cannot be its own parent.");

            if (ModelState.IsValid)
            {
                var entity = await _context.ItemGroups.FindAsync(model.Id);
                if (entity == null) return NotFound();

                var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.ParentId, entity.IsActive };

                entity.Code      = model.Code.Trim();
                entity.NameAr    = model.NameAr.Trim();
                entity.NameEn    = model.NameEn.Trim();
                entity.ParentId  = model.ParentId > 0 ? model.ParentId : null;
                entity.IsActive  = model.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "ItemGroup",
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.ParentId, entity.IsActive });

                TempData["SuccessMessage"] = "Item Group updated successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.ItemGroups.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.ItemGroups
                .Include(ig => ig.SubGroups)
                .Include(ig => ig.Items)
                .FirstOrDefaultAsync(ig => ig.Id == id);

            if (entity == null) return NotFound();

            if (entity.SubGroups.Any())
            {
                TempData["ErrorMessage"] = "Cannot delete item group because it contains subgroups.";
                return RedirectToAction(nameof(Index));
            }

            if (entity.Items.Any())
            {
                TempData["ErrorMessage"] = "Cannot delete item group because it has items assigned to it.";
                return RedirectToAction(nameof(Index));
            }

            var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.IsActive };

            _context.ItemGroups.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "ItemGroup",
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "Item Group deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.ItemGroups.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.ItemGroups.FindAsync(id);
            if (entity == null) return NotFound();

            var oldValues = new { entity.Id, entity.Code, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "ItemGroup",
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"Item Group status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
