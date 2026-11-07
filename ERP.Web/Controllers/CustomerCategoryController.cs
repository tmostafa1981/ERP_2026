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
    public class CustomerCategoryController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;
        private readonly ICodeSequenceService _sequenceService;

        private const string SC_Code   = "CUSC001";
        private const string SC_Name   = "CustomerCategories";
        private const string SC_Module = "MasterData";
        private const string EntityName = "CustomerCategory";

        public CustomerCategoryController(
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
        [HasPermission("MasterData.CustomerCategories.View")]
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

            int currentBranchId = _currentUser.BranchId;
            int? effectiveBranchId = currentBranchId > 0 ? currentBranchId : branchId;

            var query = _context.CustomerCategories
                .Include(cc => cc.Branch)
                .Include(cc => cc.ControlAccount)
                .Include(cc => cc.Customers)
                .AsNoTracking();

            if (currentBranchId > 0)
            {
                query = query.Where(cc => cc.BranchId == currentBranchId);
            }
            else if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                if (!_currentUser.CanAccessBranch(effectiveBranchId.Value))
                    return Forbid();

                query = query.Where(cc => cc.BranchId == effectiveBranchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                query = query.Where(cc => cc.BranchId == _currentUser.BranchId);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(cc =>
                    cc.Code.Contains(searchString) ||
                    cc.NameAr.Contains(searchString) ||
                    cc.NameEn.Contains(searchString));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(cc => cc.BranchId).ThenBy(cc => cc.IsDefault ? 0 : 1).ThenBy(cc => cc.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            int? defaultBranchId = effectiveBranchId.HasValue && effectiveBranchId.Value > 0
                ? effectiveBranchId.Value
                : (allBranches.FirstOrDefault()?.Id ?? _currentUser.BranchId);

            var seqConfig = defaultBranchId.HasValue
                ? await _sequenceService.GetConfigAsync(EntityName, defaultBranchId.Value)
                : await _sequenceService.GetConfigAsync(EntityName);

            ViewBag.SearchString    = searchString;
            ViewBag.BranchId        = effectiveBranchId;
            ViewBag.CurrentPage     = page;
            ViewBag.TotalPages      = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount      = totalCount;
            ViewBag.NextCode        = seqConfig.PreviewCode;
            ViewBag.IsAutoCode      = seqConfig.IsAuto;
            ViewBag.IsManualAllowed = seqConfig.IsManualAllowed;
            ViewBag.DefaultBranchId = defaultBranchId;

            return View(items);
        }

        [HttpGet]
        public async Task<IActionResult> GetByBranch(int branchId)
        {
            if (branchId <= 0 || !_currentUser.CanAccessBranch(branchId))
                return Json(new object[] { });

            var categories = await _context.CustomerCategories
                .Include(c => c.ControlAccount)
                .Where(c => c.BranchId == branchId && c.IsActive)
                .OrderBy(c => c.IsDefault ? 0 : 1).ThenBy(c => c.Code)
                .Select(c => new
                {
                    id = c.Id,
                    code = c.Code,
                    name = c.NameAr,
                    isDefault = c.IsDefault,
                    controlAccountId = c.ControlAccountId,
                    controlAccountCode = c.ControlAccount != null ? c.ControlAccount.Code : null,
                    controlAccountName = c.ControlAccount != null ? c.ControlAccount.NameAr : null,
                    controlAccountDisplay = c.ControlAccount != null ? $"{c.ControlAccount.Code} - {c.ControlAccount.NameAr}" : null
                })
                .ToListAsync();

            return Json(categories);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.CustomerCategories.Add")]
        public async Task<IActionResult> Create(CustomerCategory model, bool? isManualCode = null)
        {
            if (model.BranchId <= 0 || !_currentUser.CanAccessBranch(model.BranchId))
                ModelState.AddModelError("BranchId", "يرجى تحديد فرع صحيح تملك صلاحية الوصول إليه.");

            // Auto-number logic
            bool shouldAutoCode = isManualCode.HasValue
                ? (!isManualCode.Value && await _sequenceService.IsAutoEnabledAsync(EntityName, model.BranchId))
                : (string.IsNullOrWhiteSpace(model.Code) || (await _sequenceService.IsAutoEnabledAsync(EntityName, model.BranchId) && !await _sequenceService.IsManualAllowedAsync(EntityName, model.BranchId)));

            if (model.BranchId > 0 && shouldAutoCode)
            {
                model.Code = await _sequenceService.GetNextCodeAsync(EntityName, model.BranchId);
            }

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود التصنيف مطلوب.");
            else if (await _context.CustomerCategories.AnyAsync(cc => cc.BranchId == model.BranchId && cc.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "كود التصنيف مسجل مسبقاً في هذا الفرع.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "الاسم العربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (ModelState.IsValid)
            {
                model.Code             = model.Code.Trim();
                model.NameAr           = model.NameAr?.Trim() ?? string.Empty;
                model.NameEn           = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();
                model.ControlAccountId = model.ControlAccountId > 0 ? model.ControlAccountId : null;
                model.CreatedAt        = DateTime.UtcNow;

                _context.CustomerCategories.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   model.Id.ToString(),
                    newValues: new { model.Id, model.BranchId, model.Code, model.NameAr, model.NameEn, model.ControlAccountId, model.IsDefault, model.IsActive });

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم حفظ تصنيف العملاء بنجاح.", Url.Action(nameof(Index), new { branchId = model.BranchId }));

                TempData["SuccessMessage"] = "Customer Category created successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure("لا يمكن حفظ تصنيف العملاء. يرجى مراجعة الأخطاء الموضحة وتصحيحها.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId > 0 ? (int?)model.BranchId : null });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.CustomerCategories.Edit")]
        public async Task<IActionResult> Edit(CustomerCategory model)
        {
            var entity = await _context.CustomerCategories.FindAsync(model.Id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId) || (model.BranchId > 0 && !_currentUser.CanAccessBranch(model.BranchId)))
                return Forbid();

            int targetBranchId = model.BranchId > 0 ? model.BranchId : entity.BranchId;

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود التصنيف مطلوب.");
            else if (await _context.CustomerCategories.AnyAsync(cc => cc.BranchId == targetBranchId && cc.Code == model.Code.Trim() && cc.Id != model.Id))
                ModelState.AddModelError("Code", "كود التصنيف مسجل مسبقاً في هذا الفرع.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "الاسم العربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (ModelState.IsValid)
            {
                var oldValues = new
                {
                    entity.Id,
                    entity.BranchId,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.ControlAccountId,
                    entity.IsDefault,
                    entity.IsActive,
                    OldCode = entity.Code
                };

                entity.Code             = model.Code.Trim();
                entity.NameAr           = model.NameAr?.Trim() ?? string.Empty;
                entity.NameEn           = string.IsNullOrWhiteSpace(model.NameEn) ? entity.NameAr : model.NameEn.Trim();
                entity.ControlAccountId = model.ControlAccountId > 0 ? model.ControlAccountId : null;
                entity.IsActive         = model.IsActive;
                entity.UpdatedAt        = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                var newValues = new
                {
                    entity.Id,
                    entity.BranchId,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.ControlAccountId,
                    entity.IsDefault,
                    entity.IsActive,
                    NewCode = entity.Code
                };

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues:  newValues);

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم تحديث تصنيف العملاء بنجاح.", Url.Action(nameof(Index), new { branchId = entity.BranchId }));

                TempData["SuccessMessage"] = "Customer Category updated successfully.";
                return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure("لا يمكن حفظ التعديلات. يرجى مراجعة الأخطاء الموضحة وتصحيحها.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.CustomerCategories.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.CustomerCategories
                .Include(cc => cc.Customers)
                .FirstOrDefaultAsync(cc => cc.Id == id);

            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            if (entity.IsDefault)
            {
                TempData["ErrorMessage"] = "Cannot delete the default category for this branch.";
                return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
            }

            if (entity.Customers.Any())
            {
                TempData["ErrorMessage"] = "Cannot delete customer category because it is assigned to existing customers.";
                return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
            }

            var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.NameAr, entity.NameEn, entity.IsDefault, entity.IsActive };

            _context.CustomerCategories.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: EntityName,
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "Customer Category deleted successfully.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.CustomerCategories.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.CustomerCategories.FindAsync(id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: EntityName,
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.BranchId, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"Customer Category status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }
    }
}
