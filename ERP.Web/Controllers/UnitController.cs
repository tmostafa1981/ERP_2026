using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using ERP.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class UnitController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;
        private readonly ICodeSequenceService _sequenceService;

        private const string SC_Code   = "UNIT001";
        private const string SC_Name   = "Units";
        private const string SC_Module = "MasterData";
        private const string EntityName = "Unit";

        public UnitController(
            ErpDbContext context,
            ICurrentUserContext currentUser,
            IAuditService audit,
            ICodeSequenceService sequenceService)
        {
            _context         = context;
            _currentUser     = currentUser;
            _audit           = audit;
            _sequenceService = sequenceService;
        }

        [HttpGet]
        [HasPermission("MasterData.Units.View")]
        public async Task<IActionResult> Index(string? searchString, int page = 1)
        {
            int pageSize = 15;
            var query = _context.Units
                .Include(u => u.ItemUnits)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(u =>
                    u.Code.Contains(searchString) ||
                    u.NameAr.Contains(searchString) ||
                    u.NameEn.Contains(searchString));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(u => u.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var seqConfig = await _sequenceService.GetConfigAsync(EntityName);

            ViewBag.SearchString    = searchString;
            ViewBag.CurrentPage     = page;
            ViewBag.TotalPages      = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount      = totalCount;
            ViewBag.NextCode        = seqConfig.PreviewCode;
            ViewBag.IsAutoCode      = seqConfig.IsAuto;
            ViewBag.IsManualAllowed = seqConfig.IsManualAllowed;

            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Units.Add")]
        public async Task<IActionResult> Create(Unit model, bool? isManualCode = null)
        {
            // Auto-number logic
            bool shouldAutoCode = isManualCode.HasValue
                ? (!isManualCode.Value && await _sequenceService.IsAutoEnabledAsync(EntityName))
                : (string.IsNullOrWhiteSpace(model.Code) || (await _sequenceService.IsAutoEnabledAsync(EntityName) && !await _sequenceService.IsManualAllowedAsync(EntityName)));

            if (shouldAutoCode)
            {
                model.Code = await _sequenceService.GetNextCodeAsync(EntityName);
            }

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود الوحدة مطلوب.");
            else if (await _context.Units.AnyAsync(u => u.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "كود الوحدة موجود بالفعل.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "اسم الوحدة بالعربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (ModelState.IsValid)
            {
                model.Code      = model.Code.Trim();
                model.NameAr    = model.NameAr?.Trim() ?? string.Empty;
                model.NameEn    = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();
                model.CreatedAt = DateTime.UtcNow;

                _context.Units.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   model.Id.ToString(),
                    newValues: new { model.Id, model.Code, model.NameAr, model.NameEn, model.IsActive });

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم إضافة الوحدة بنجاح.");

                TempData["SuccessMessage"] = "تم إضافة الوحدة بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure(ModelState, "يرجى تصحيح أخطاء الإدخال الموضحة أدناه.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Units.Edit")]
        public async Task<IActionResult> Edit(Unit model)
        {
            var entity = await _context.Units.FindAsync(model.Id);
            if (entity == null)
            {
                if (Request.IsAjaxRequest())
                    return this.JsonValidationFailure(new[] { "الوحدة غير موجودة أو تم حذفها." });
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود الوحدة مطلوب.");
            else if (await _context.Units.AnyAsync(u => u.Code == model.Code.Trim() && u.Id != model.Id))
                ModelState.AddModelError("Code", "كود الوحدة موجود بالفعل.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "اسم الوحدة بالعربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (ModelState.IsValid)
            {
                var oldValues = new
                {
                    entity.Id,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.IsActive,
                    OldCode = entity.Code
                };

                entity.Code      = model.Code.Trim();
                entity.NameAr    = model.NameAr?.Trim() ?? string.Empty;
                entity.NameEn    = string.IsNullOrWhiteSpace(model.NameEn) ? entity.NameAr : model.NameEn.Trim();
                entity.IsActive  = model.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                var newValues = new
                {
                    entity.Id,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.IsActive,
                    NewCode = entity.Code
                };

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues:  newValues);

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم تحديث الوحدة بنجاح.");

                TempData["SuccessMessage"] = "تم تحديث الوحدة بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure(ModelState, "يرجى تصحيح أخطاء الإدخال الموضحة أدناه.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Units.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Units
                .Include(u => u.ItemUnits)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (entity == null) return NotFound();

            if (entity.ItemUnits.Any())
            {
                TempData["ErrorMessage"] = "Cannot delete unit because it is used by items.";
                return RedirectToAction(nameof(Index));
            }

            var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.IsActive };

            _context.Units.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: EntityName,
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "Unit deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Units.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.Units.FindAsync(id);
            if (entity == null) return NotFound();

            var oldValues = new { entity.Id, entity.Code, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: EntityName,
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"Unit status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
