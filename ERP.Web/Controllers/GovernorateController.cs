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
    public class GovernorateController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code   = "GOV001";
        private const string SC_Name   = "Governorates";
        private const string SC_Module = "MasterData";

        public GovernorateController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
        {
            _context     = context;
            _currentUser = currentUser;
            _audit       = audit;
        }

        [HttpGet]
        [HasPermission("MasterData.Governorates.View")]
        public async Task<IActionResult> Index(string? searchString, int page = 1)
        {
            int pageSize = 15;
            var query = _context.Governorates
                .Include(g => g.Cities)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(g =>
                    g.Code.Contains(searchString) ||
                    g.NameAr.Contains(searchString) ||
                    g.NameEn.Contains(searchString));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(g => g.Code)
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
        [HasPermission("MasterData.Governorates.Add")]
        public async Task<IActionResult> Create(Governorate model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Governorate Code is required.");
            else if (await _context.Governorates.AnyAsync(g => g.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "Governorate Code already exists.");

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

                _context.Governorates.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Governorate",
                    entityId:   model.Id.ToString(),
                    newValues: new { model.Id, model.Code, model.NameAr, model.NameEn, model.IsActive });

                TempData["SuccessMessage"] = "Governorate created successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Governorates.Edit")]
        public async Task<IActionResult> Edit(Governorate model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Governorate Code is required.");
            else if (await _context.Governorates.AnyAsync(g => g.Code == model.Code.Trim() && g.Id != model.Id))
                ModelState.AddModelError("Code", "Governorate Code already exists.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (ModelState.IsValid)
            {
                var entity = await _context.Governorates.FindAsync(model.Id);
                if (entity == null) return NotFound();

                var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.IsActive };

                entity.Code      = model.Code.Trim();
                entity.NameAr    = model.NameAr.Trim();
                entity.NameEn    = model.NameEn.Trim();
                entity.IsActive  = model.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Governorate",
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.IsActive });

                TempData["SuccessMessage"] = "Governorate updated successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Governorates.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Governorates
                .Include(g => g.Cities)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (entity == null) return NotFound();

            if (entity.Cities.Any())
            {
                TempData["ErrorMessage"] = "Cannot delete governorate because it contains associated cities.";
                return RedirectToAction(nameof(Index));
            }

            var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.IsActive };

            _context.Governorates.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Governorate",
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "Governorate deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Governorates.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.Governorates.FindAsync(id);
            if (entity == null) return NotFound();

            var oldValues = new { entity.Id, entity.Code, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Governorate",
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"Governorate status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
