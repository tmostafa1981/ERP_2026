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
    public class VendorCategoryController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code   = "VENC001";
        private const string SC_Name   = "VendorCategories";
        private const string SC_Module = "MasterData";

        public VendorCategoryController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
        {
            _context     = context;
            _currentUser = currentUser;
            _audit       = audit;
        }

        [HttpGet]
        [HasPermission("MasterData.VendorCategories.View")]
        public async Task<IActionResult> Index(string? searchString, int page = 1)
        {
            int pageSize = 15;
            var query = _context.VendorCategories
                .Include(vc => vc.Vendors)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(vc =>
                    vc.Code.Contains(searchString) ||
                    vc.NameAr.Contains(searchString) ||
                    vc.NameEn.Contains(searchString));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(vc => vc.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.SearchString = searchString;
            ViewBag.CurrentPage  = page;
            ViewBag.TotalPages   = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount   = totalCount;

            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.VendorCategories.Add")]
        public async Task<IActionResult> Create(VendorCategory model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Category Code is required.");
            else if (await _context.VendorCategories.AnyAsync(vc => vc.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "Category Code already exists.");

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

                _context.VendorCategories.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "VendorCategory",
                    entityId:   model.Id.ToString(),
                    newValues: new { model.Id, model.Code, model.NameAr, model.NameEn, model.ControlAccountId, model.IsActive });

                TempData["SuccessMessage"] = "Vendor Category created successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.VendorCategories.Edit")]
        public async Task<IActionResult> Edit(VendorCategory model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Category Code is required.");
            else if (await _context.VendorCategories.AnyAsync(vc => vc.Code == model.Code.Trim() && vc.Id != model.Id))
                ModelState.AddModelError("Code", "Category Code already exists.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (ModelState.IsValid)
            {
                var entity = await _context.VendorCategories.FindAsync(model.Id);
                if (entity == null) return NotFound();

                var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.ControlAccountId, entity.IsActive };

                entity.Code             = model.Code.Trim();
                entity.NameAr           = model.NameAr.Trim();
                entity.NameEn           = model.NameEn.Trim();
                entity.ControlAccountId = model.ControlAccountId;
                entity.IsActive         = model.IsActive;
                entity.UpdatedAt        = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "VendorCategory",
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.ControlAccountId, entity.IsActive });

                TempData["SuccessMessage"] = "Vendor Category updated successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.VendorCategories.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.VendorCategories
                .Include(vc => vc.Vendors)
                .FirstOrDefaultAsync(vc => vc.Id == id);

            if (entity == null) return NotFound();

            if (entity.Vendors.Any())
            {
                TempData["ErrorMessage"] = "Cannot delete category because it contains associated vendors.";
                return RedirectToAction(nameof(Index));
            }

            var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.IsActive };

            _context.VendorCategories.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "VendorCategory",
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "Vendor Category deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.VendorCategories.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.VendorCategories.FindAsync(id);
            if (entity == null) return NotFound();

            var oldValues = new { entity.Id, entity.Code, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "VendorCategory",
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"Vendor Category status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
