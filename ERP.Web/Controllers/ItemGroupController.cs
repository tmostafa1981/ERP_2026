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
    public class ItemGroupController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;
        private readonly ICodeSequenceService _sequenceService;

        private const string SC_Code   = "ITMG001";
        private const string SC_Name   = "ItemGroups";
        private const string SC_Module = "MasterData";
        private const string EntityName = "ItemGroup";

        public ItemGroupController(
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

            var seqConfig = await _sequenceService.GetConfigAsync(EntityName);

            ViewBag.SearchString    = searchString;
            ViewBag.ParentId        = parentId;
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
        [HasPermission("MasterData.ItemGroups.Add")]
        public async Task<IActionResult> Create(ItemGroup model, bool? isManualCode = null)
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
                ModelState.AddModelError("Code", "كود مجموعة الأصناف مطلوب.");
            else if (await _context.ItemGroups.AnyAsync(ig => ig.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "كود مجموعة الأصناف موجود بالفعل.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "اسم مجموعة الأصناف بالعربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (model.ParentId.HasValue && model.ParentId.Value > 0 && !await _context.ItemGroups.AnyAsync(ig => ig.Id == model.ParentId.Value))
                ModelState.AddModelError("ParentId", "المجموعة الرئيسية المحددة غير صالحة.");

            if (ModelState.IsValid)
            {
                model.Code      = model.Code.Trim();
                model.NameAr    = model.NameAr?.Trim() ?? string.Empty;
                model.NameEn    = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();
                model.ParentId  = model.ParentId > 0 ? model.ParentId : null;
                model.CreatedAt = DateTime.UtcNow;

                _context.ItemGroups.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   model.Id.ToString(),
                    newValues: new { model.Id, model.Code, model.NameAr, model.NameEn, model.ParentId, model.IsActive });

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم إضافة مجموعة الأصناف بنجاح.");

                TempData["SuccessMessage"] = "تم إضافة مجموعة الأصناف بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure(ModelState, "يرجى تصحيح أخطاء الإدخال الموضحة أدناه.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.ItemGroups.Edit")]
        public async Task<IActionResult> Edit(ItemGroup model)
        {
            var entity = await _context.ItemGroups.FindAsync(model.Id);
            if (entity == null)
            {
                if (Request.IsAjaxRequest())
                    return this.JsonValidationFailure(new[] { "مجموعة الأصناف غير موجودة أو تم حذفها." });
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود مجموعة الأصناف مطلوب.");
            else if (await _context.ItemGroups.AnyAsync(ig => ig.Code == model.Code.Trim() && ig.Id != model.Id))
                ModelState.AddModelError("Code", "كود مجموعة الأصناف موجود بالفعل.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "اسم مجموعة الأصناف بالعربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (model.ParentId.HasValue && model.ParentId.Value > 0)
            {
                if (model.ParentId.Value == model.Id)
                {
                    ModelState.AddModelError("ParentId", "لا يمكن للمجموعة أن تكون مجموعة رئيسية لنفسها.");
                }
                else if (!await _context.ItemGroups.AnyAsync(ig => ig.Id == model.ParentId.Value))
                {
                    ModelState.AddModelError("ParentId", "المجموعة الرئيسية المحددة غير صالحة.");
                }
                else if (await IsDescendantGroupAsync(model.ParentId.Value, model.Id))
                {
                    ModelState.AddModelError("ParentId", "لا يمكن تعيين مجموعة فرعية كمجموعة رئيسية (منع تكوين حلقة دائرية).");
                }
            }

            if (ModelState.IsValid)
            {
                var oldValues = new
                {
                    entity.Id,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.ParentId,
                    entity.IsActive,
                    OldCode = entity.Code
                };

                entity.Code      = model.Code.Trim();
                entity.NameAr    = model.NameAr?.Trim() ?? string.Empty;
                entity.NameEn    = string.IsNullOrWhiteSpace(model.NameEn) ? entity.NameAr : model.NameEn.Trim();
                entity.ParentId  = model.ParentId > 0 ? model.ParentId : null;
                entity.IsActive  = model.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                var newValues = new
                {
                    entity.Id,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.ParentId,
                    entity.IsActive,
                    NewCode = entity.Code
                };

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues:  newValues);

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم تحديث مجموعة الأصناف بنجاح.");

                TempData["SuccessMessage"] = "تم تحديث مجموعة الأصناف بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure(ModelState, "يرجى تصحيح أخطاء الإدخال الموضحة أدناه.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        private async Task<bool> IsDescendantGroupAsync(int potentialChildId, int ancestorId)
        {
            var currentId = (int?)potentialChildId;
            var visited = new HashSet<int>();

            while (currentId.HasValue && currentId.Value > 0)
            {
                if (currentId.Value == ancestorId)
                    return true;

                if (!visited.Add(currentId.Value))
                    break;

                var parentId = await _context.ItemGroups
                    .AsNoTracking()
                    .Where(g => g.Id == currentId.Value)
                    .Select(g => g.ParentId)
                    .FirstOrDefaultAsync();

                currentId = parentId;
            }

            return false;
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
                TempData["ErrorMessage"] = "لا يمكن حذف مجموعة الأصناف لوجود مجموعات فرعية تابعة لها.";
                return RedirectToAction(nameof(Index));
            }

            if (entity.Items.Any())
            {
                TempData["ErrorMessage"] = "لا يمكن حذف مجموعة الأصناف لوجود أصناف مرتبطة بها.";
                return RedirectToAction(nameof(Index));
            }

            var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.ParentId, entity.IsActive };

            try
            {
                _context.ItemGroups.Remove(entity);
                await _context.SaveChangesAsync();

                await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   id.ToString(),
                    oldValues:  oldValues);

                TempData["SuccessMessage"] = "تم حذف مجموعة الأصناف بنجاح.";
            }
            catch (DbUpdateException)
            {
                TempData["ErrorMessage"] = "لا يمكن حذف مجموعة الأصناف لوجود سجلات مرتبطة بها.";
            }

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
                entityName: EntityName,
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"Item Group status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
