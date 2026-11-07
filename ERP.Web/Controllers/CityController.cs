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
    public class CityController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;
        private readonly ICodeSequenceService _sequenceService;

        private const string SC_Code   = "CIT001";
        private const string SC_Name   = "Cities";
        private const string SC_Module = "MasterData";
        private const string EntityName = "City";

        public CityController(
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

            var seqConfig = await _sequenceService.GetConfigAsync(EntityName);

            ViewBag.SearchString    = searchString;
            ViewBag.GovernorateId   = governorateId;
            ViewBag.CurrentPage     = page;
            ViewBag.TotalPages      = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount      = totalCount;
            ViewBag.NextCode        = seqConfig.PreviewCode;
            ViewBag.IsAutoCode      = seqConfig.IsAuto;
            ViewBag.IsManualAllowed = seqConfig.IsManualAllowed;

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
        public async Task<IActionResult> Create(City model, bool? isManualCode = null)
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
                ModelState.AddModelError("Code", "كود المدينة مطلوب.");
            else if (await _context.Cities.AnyAsync(c => c.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "كود المدينة مسجل مسبقاً.");

            if (model.GovernorateId <= 0 || !await _context.Governorates.AnyAsync(g => g.Id == model.GovernorateId))
                ModelState.AddModelError("GovernorateId", "يرجى اختيار المحافظة التابعة لها المدينة.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "الاسم العربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (ModelState.IsValid)
            {
                model.Code      = model.Code.Trim();
                model.NameAr    = model.NameAr?.Trim() ?? string.Empty;
                model.NameEn    = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();
                model.CreatedAt = DateTime.UtcNow;

                _context.Cities.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   model.Id.ToString(),
                    newValues: new { model.Id, model.Code, model.NameAr, model.NameEn, model.GovernorateId, model.IsActive });

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم حفظ المدينة بنجاح.", Url.Action(nameof(Index), new { governorateId = model.GovernorateId }));

                TempData["SuccessMessage"] = "City created successfully.";
                return RedirectToAction(nameof(Index), new { governorateId = model.GovernorateId });
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure("لا يمكن حفظ المدينة. يرجى مراجعة الأخطاء الموضحة وتصحيحها.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { governorateId = model.GovernorateId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Cities.Edit")]
        public async Task<IActionResult> Edit(City model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود المدينة مطلوب.");
            else if (await _context.Cities.AnyAsync(c => c.Code == model.Code.Trim() && c.Id != model.Id))
                ModelState.AddModelError("Code", "كود المدينة مسجل مسبقاً لصالح مدينة أخرى.");

            if (model.GovernorateId <= 0 || !await _context.Governorates.AnyAsync(g => g.Id == model.GovernorateId))
                ModelState.AddModelError("GovernorateId", "يرجى اختيار المحافظة التابعة لها المدينة.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "الاسم العربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (ModelState.IsValid)
            {
                var entity = await _context.Cities.FindAsync(model.Id);
                if (entity == null) return NotFound();

                var oldValues = new
                {
                    entity.Id,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.GovernorateId,
                    entity.IsActive,
                    OldCode = entity.Code
                };

                entity.Code          = model.Code.Trim();
                entity.NameAr        = model.NameAr?.Trim() ?? string.Empty;
                entity.NameEn        = string.IsNullOrWhiteSpace(model.NameEn) ? (model.NameAr?.Trim() ?? string.Empty) : model.NameEn.Trim();
                entity.GovernorateId = model.GovernorateId;
                entity.IsActive      = model.IsActive;
                entity.UpdatedAt     = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                var newValues = new
                {
                    entity.Id,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.GovernorateId,
                    entity.IsActive,
                    NewCode = entity.Code
                };

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues:  newValues);

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم تحديث بيانات المدينة بنجاح.", Url.Action(nameof(Index), new { governorateId = entity.GovernorateId }));

                TempData["SuccessMessage"] = "City updated successfully.";
                return RedirectToAction(nameof(Index), new { governorateId = entity.GovernorateId });
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure("لا يمكن حفظ التعديلات. يرجى مراجعة الأخطاء الموضحة وتصحيحها.");

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

            // Delete Protection: Check for associated customers or vendors
            bool hasCustomers = await _context.Customers.AnyAsync(c => c.CityId == id);
            bool hasVendors = await _context.Vendors.AnyAsync(v => v.CityId == id);

            if (hasCustomers || hasVendors)
            {
                TempData["ErrorMessage"] = "لا يمكن حذف المدينة لوجود عملاء أو موردين مرتبطين بها.";
                return RedirectToAction(nameof(Index), new { governorateId = entity.GovernorateId });
            }

            var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.GovernorateId, entity.IsActive };

            try
            {
                _context.Cities.Remove(entity);
                await _context.SaveChangesAsync();

                await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   id.ToString(),
                    oldValues:  oldValues);

                TempData["SuccessMessage"] = "تم حذف المدينة بنجاح.";
            }
            catch (DbUpdateException)
            {
                TempData["ErrorMessage"] = "لا يمكن حذف المدينة لوجود سجلات مرتبطة بها.";
            }

            return RedirectToAction(nameof(Index), new { governorateId = entity.GovernorateId });
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
                entityName: EntityName,
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"City status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index), new { governorateId = entity.GovernorateId });
        }
    }
}
