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
    public class CityController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code   = "CIT001";
        private const string SC_Name   = "Cities";
        private const string SC_Module = "MasterData";

        public CityController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
        {
            _context     = context;
            _currentUser = currentUser;
            _audit       = audit;
        }

        [HttpGet]
        [HasPermission("MasterData.Cities.View")]
        public async Task<IActionResult> Index(string? searchString, int? governorateId, int page = 1)
        {
            int pageSize = 15;
            var govList = await _context.Governorates.Where(g => g.IsActive).OrderBy(g => g.Code).ToListAsync();
            ViewBag.Governorates = govList;

            var query = _context.Cities
                .Include(c => c.Governorate)
                .AsNoTracking();

            if (governorateId.HasValue && governorateId.Value > 0)
            {
                query = query.Where(c => c.GovernorateId == governorateId.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(c =>
                    c.Code.Contains(searchString) ||
                    c.NameAr.Contains(searchString) ||
                    c.NameEn.Contains(searchString));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.GovernorateId).ThenBy(c => c.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.SearchString   = searchString;
            ViewBag.GovernorateId  = governorateId;
            ViewBag.CurrentPage    = page;
            ViewBag.TotalPages     = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount     = totalCount;

            return View(items);
        }

        [HttpGet]
        public async Task<IActionResult> GetByGovernorate(int governorateId)
        {
            if (governorateId <= 0)
                return Json(new object[] { });

            var cities = await _context.Cities
                .Where(c => c.GovernorateId == governorateId && c.IsActive)
                .OrderBy(c => c.Code)
                .Select(c => new { id = c.Id, name = c.LocalizedName, code = c.Code })
                .ToListAsync();

            return Json(cities);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Cities.Add")]
        public async Task<IActionResult> Create(City model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "City Code is required.");
            else if (await _context.Cities.AnyAsync(c => c.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "City Code already exists.");

            if (model.GovernorateId <= 0 || !await _context.Governorates.AnyAsync(g => g.Id == model.GovernorateId))
                ModelState.AddModelError("GovernorateId", "Please select a valid governorate.");

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

                _context.Cities.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "City",
                    entityId:   model.Id.ToString(),
                    newValues: new { model.Id, model.Code, model.NameAr, model.NameEn, model.GovernorateId, model.IsActive });

                TempData["SuccessMessage"] = "City created successfully.";
                return RedirectToAction(nameof(Index), new { governorateId = model.GovernorateId });
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { governorateId = model.GovernorateId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Cities.Edit")]
        public async Task<IActionResult> Edit(City model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "City Code is required.");
            else if (await _context.Cities.AnyAsync(c => c.Code == model.Code.Trim() && c.Id != model.Id))
                ModelState.AddModelError("Code", "City Code already exists.");

            if (model.GovernorateId <= 0 || !await _context.Governorates.AnyAsync(g => g.Id == model.GovernorateId))
                ModelState.AddModelError("GovernorateId", "Please select a valid governorate.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (ModelState.IsValid)
            {
                var entity = await _context.Cities.FindAsync(model.Id);
                if (entity == null) return NotFound();

                var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.GovernorateId, entity.IsActive };

                entity.Code          = model.Code.Trim();
                entity.NameAr        = model.NameAr.Trim();
                entity.NameEn        = model.NameEn.Trim();
                entity.GovernorateId = model.GovernorateId;
                entity.IsActive      = model.IsActive;
                entity.UpdatedAt     = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "City",
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.GovernorateId, entity.IsActive });

                TempData["SuccessMessage"] = "City updated successfully.";
                return RedirectToAction(nameof(Index), new { governorateId = model.GovernorateId });
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { governorateId = model.GovernorateId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Cities.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Cities.FindAsync(id);
            if (entity == null) return NotFound();

            var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.GovernorateId, entity.IsActive };

            _context.Cities.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "City",
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "City deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Cities.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.Cities.FindAsync(id);
            if (entity == null) return NotFound();

            var oldValues = new { entity.Id, entity.Code, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "City",
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"City status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
