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
using ERP.Web.Extensions;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class ItemController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;
        private readonly ICodeSequenceService _sequenceService;

        private const string SC_Code   = "ITEM001";
        private const string SC_Name   = "Items";
        private const string SC_Module = "MasterData";
        private const string EntityName = "Item";

        public ItemController(
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
        [HasPermission("MasterData.Items.View")]
        public async Task<IActionResult> Index(string? searchString, int? itemGroupId, bool? isService, int page = 1)
        {
            int pageSize = 15;

            var groups = await _context.ItemGroups
                .Where(g => g.IsActive)
                .OrderBy(g => g.Code)
                .AsNoTracking()
                .ToListAsync();
            ViewBag.ItemGroups = groups;

            var units = await _context.Units
                .Where(u => u.IsActive)
                .OrderBy(u => u.Code)
                .AsNoTracking()
                .ToListAsync();
            ViewBag.Units = units;

            var query = _context.Items
                .Include(i => i.ItemGroup)
                .Include(i => i.ItemUnits)
                    .ThenInclude(iu => iu.Unit)
                .AsNoTracking();

            if (itemGroupId.HasValue && itemGroupId.Value > 0)
            {
                query = query.Where(i => i.ItemGroupId == itemGroupId.Value);
            }

            if (isService.HasValue)
            {
                query = query.Where(i => i.IsService == isService.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(i =>
                    i.Code.Contains(searchString) ||
                    (i.Barcode != null && i.Barcode.Contains(searchString)) ||
                    (i.TaxCode != null && i.TaxCode.Contains(searchString)) ||
                    i.NameAr.Contains(searchString) ||
                    i.NameEn.Contains(searchString));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(i => i.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var seqConfig = await _sequenceService.GetConfigAsync(EntityName);

            ViewBag.SearchString    = searchString;
            ViewBag.ItemGroupId     = itemGroupId;
            ViewBag.IsService       = isService;
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
        [HasPermission("MasterData.Items.Add")]
        public async Task<IActionResult> Create(Item model, int? baseUnitId, decimal? salesPrice, decimal? purchasePrice, bool? isManualCode = null)
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
                ModelState.AddModelError("Code", "كود الصنف مطلوب.");
            else if (await _context.Items.AnyAsync(i => i.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "كود الصنف مسجل مسبقاً.");

            if (!string.IsNullOrWhiteSpace(model.Barcode))
            {
                var trimmedBarcode = model.Barcode.Trim();
                if (await _context.Items.AnyAsync(i => i.Barcode == trimmedBarcode))
                    ModelState.AddModelError("Barcode", "الباركود المدخل مستخدم بالفعل لصنف آخر.");
            }

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "الاسم العربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (model.ItemGroupId <= 0 || !await _context.ItemGroups.AnyAsync(g => g.Id == model.ItemGroupId))
                ModelState.AddModelError("ItemGroupId", "يرجى اختيار مجموعة الصنف.");

            if (!baseUnitId.HasValue || baseUnitId.Value <= 0 || !await _context.Units.AnyAsync(u => u.Id == baseUnitId.Value))
                ModelState.AddModelError("baseUnitId", "يرجى اختيار وحدة الصنف.");

            if (ModelState.IsValid)
            {
                model.Code            = model.Code.Trim();
                model.Barcode         = string.IsNullOrWhiteSpace(model.Barcode) ? null : model.Barcode.Trim();
                model.TaxCode         = string.IsNullOrWhiteSpace(model.TaxCode) ? null : model.TaxCode.Trim();
                model.NameAr          = model.NameAr?.Trim() ?? string.Empty;
                model.NameEn          = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();
                model.StorageLocation = string.IsNullOrWhiteSpace(model.StorageLocation) ? null : model.StorageLocation.Trim();
                model.CreatedAt       = DateTime.UtcNow;

                _context.Items.Add(model);
                await _context.SaveChangesAsync();

                // Add base item unit (prices default to 0 if not provided)
                var itemUnit = new ItemUnit
                {
                    ItemId            = model.Id,
                    UnitId            = baseUnitId!.Value,
                    ConversionFactor  = 1.0m,
                    SalesPrice        = salesPrice ?? 0m,
                    PurchasePrice     = purchasePrice ?? 0m,
                    IsDefaultSales    = true,
                    IsDefaultPurchase = true,
                    IsActive          = true,
                    CreatedAt         = DateTime.UtcNow
                };
                _context.ItemUnits.Add(itemUnit);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   model.Id.ToString(),
                    newValues: new
                    {
                        model.Id,
                        model.Code,
                        model.Barcode,
                        model.TaxCode,
                        model.NameAr,
                        model.NameEn,
                        model.ItemGroupId,
                        model.IsService,
                        model.StorageLocation,
                        model.IsActive,
                        BaseUnitId = baseUnitId.Value,
                        SalesPrice = salesPrice ?? 0m,
                        PurchasePrice = purchasePrice ?? 0m
                    });

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم حفظ الصنف بنجاح.", Url.Action(nameof(Index)));

                TempData["SuccessMessage"] = "Item created successfully.";
                return RedirectToAction(nameof(Index));
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure("لا يمكن حفظ الصنف. يرجى مراجعة الأخطاء الموضحة وتصحيحها.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Items.Edit")]
        public async Task<IActionResult> Edit(Item model, int? baseUnitId = null, decimal? salesPrice = null, decimal? purchasePrice = null)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود الصنف مطلوب.");
            else if (await _context.Items.AnyAsync(i => i.Code == model.Code.Trim() && i.Id != model.Id))
                ModelState.AddModelError("Code", "كود الصنف مسجل مسبقاً لصنف آخر.");

            if (!string.IsNullOrWhiteSpace(model.Barcode))
            {
                var trimmedBarcode = model.Barcode.Trim();
                if (await _context.Items.AnyAsync(i => i.Barcode == trimmedBarcode && i.Id != model.Id))
                    ModelState.AddModelError("Barcode", "الباركود المدخل مستخدم بالفعل لصنف آخر.");
            }

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "الاسم العربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (model.ItemGroupId <= 0 || !await _context.ItemGroups.AnyAsync(g => g.Id == model.ItemGroupId))
                ModelState.AddModelError("ItemGroupId", "يرجى اختيار مجموعة الصنف.");

            if (!baseUnitId.HasValue || baseUnitId.Value <= 0 || !await _context.Units.AnyAsync(u => u.Id == baseUnitId.Value))
                ModelState.AddModelError("baseUnitId", "يرجى اختيار وحدة الصنف.");

            if (ModelState.IsValid)
            {
                var entity = await _context.Items
                    .Include(i => i.ItemUnits)
                    .FirstOrDefaultAsync(i => i.Id == model.Id);

                if (entity == null) return NotFound();

                var oldValues = new
                {
                    entity.Id,
                    entity.Code,
                    entity.Barcode,
                    entity.TaxCode,
                    entity.NameAr,
                    entity.NameEn,
                    entity.ItemGroupId,
                    entity.IsService,
                    entity.StorageLocation,
                    entity.IsActive,
                    OldCode = entity.Code
                };

                entity.Code            = model.Code.Trim();
                entity.Barcode         = string.IsNullOrWhiteSpace(model.Barcode) ? null : model.Barcode.Trim();
                entity.TaxCode         = string.IsNullOrWhiteSpace(model.TaxCode) ? null : model.TaxCode.Trim();
                entity.NameAr          = model.NameAr?.Trim() ?? string.Empty;
                entity.NameEn          = string.IsNullOrWhiteSpace(model.NameEn) ? entity.NameAr : model.NameEn.Trim();
                entity.ItemGroupId     = model.ItemGroupId;
                entity.IsService       = model.IsService;
                entity.TrackingType    = model.TrackingType;
                entity.StorageLocation = string.IsNullOrWhiteSpace(model.StorageLocation) ? null : model.StorageLocation.Trim();
                entity.IsActive        = model.IsActive;
                entity.UpdatedAt       = DateTime.UtcNow;

                // Handle base unit update or creation
                var baseUnit = entity.ItemUnits?.FirstOrDefault(u => u.ConversionFactor == 1.0m)
                               ?? entity.ItemUnits?.FirstOrDefault();

                if (baseUnit != null)
                {
                    baseUnit.UnitId        = baseUnitId!.Value;
                    baseUnit.SalesPrice    = salesPrice ?? 0m;
                    baseUnit.PurchasePrice = purchasePrice ?? 0m;
                    baseUnit.UpdatedAt     = DateTime.UtcNow;
                }
                else
                {
                    var newBaseUnit = new ItemUnit
                    {
                        ItemId            = entity.Id,
                        UnitId            = baseUnitId!.Value,
                        ConversionFactor  = 1.0m,
                        SalesPrice        = salesPrice ?? 0m,
                        PurchasePrice     = purchasePrice ?? 0m,
                        IsDefaultSales    = true,
                        IsDefaultPurchase = true,
                        IsActive          = true,
                        CreatedAt         = DateTime.UtcNow
                    };
                    _context.ItemUnits.Add(newBaseUnit);
                }

                if (model.RowVersion != null && model.RowVersion.Length > 0)
                {
                    _context.Entry(entity).Property(e => e.RowVersion).OriginalValue = model.RowVersion;
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    var conflictMsg = "تم تعديل السجل بواسطة مستخدم آخر أثناء عملك عليه. يرجى إعادة تحميل البيانات قبل الحفظ.";
                    if (Request.IsAjaxRequest())
                        return this.JsonFailure(conflictMsg);

                    TempData["ErrorMessage"] = conflictMsg;
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    var msg = ex.InnerException?.Message ?? ex.Message;
                    if (Request.IsAjaxRequest())
                        return this.JsonFailure("حدث خطأ أثناء حفظ تعديلات الصنف: " + msg);

                    TempData["ErrorMessage"] = "حدث خطأ أثناء حفظ تعديلات الصنف: " + msg;
                    return RedirectToAction(nameof(Index));
                }

                var newValues = new
                {
                    entity.Id,
                    entity.Code,
                    entity.Barcode,
                    entity.TaxCode,
                    entity.NameAr,
                    entity.NameEn,
                    entity.ItemGroupId,
                    entity.IsService,
                    entity.StorageLocation,
                    entity.IsActive,
                    BaseUnitId = baseUnitId!.Value,
                    SalesPrice = salesPrice ?? 0m,
                    PurchasePrice = purchasePrice ?? 0m,
                    NewCode = entity.Code
                };

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues:  newValues);

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم تحديث الصنف بنجاح.", Url.Action(nameof(Index)));

                TempData["SuccessMessage"] = "Item updated successfully.";
                return RedirectToAction(nameof(Index));
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure("لا يمكن حفظ التعديلات. يرجى مراجعة الأخطاء الموضحة وتصحيحها.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Items.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Items
                .Include(i => i.ItemUnits)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (entity == null) return NotFound();

            var oldValues = new
            {
                entity.Id,
                entity.Code,
                entity.Barcode,
                entity.TaxCode,
                entity.NameAr,
                entity.NameEn,
                entity.ItemGroupId,
                entity.IsService,
                entity.StorageLocation,
                entity.IsActive
            };

            _context.Items.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: EntityName,
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "Item deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Items.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.Items.FindAsync(id);
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

            TempData["SuccessMessage"] = $"Item status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [HasPermission("MasterData.Items.View")]
        public async Task<IActionResult> GetItemUnits(int id)
        {
            var units = await _context.ItemUnits
                .Where(iu => iu.ItemId == id)
                .Include(iu => iu.Unit)
                .Select(iu => new
                {
                    iu.Id,
                    iu.UnitId,
                    UnitName = iu.Unit != null ? iu.Unit.LocalizedName : "",
                    UnitCode = iu.Unit != null ? iu.Unit.Code : "",
                    iu.ConversionFactor,
                    iu.SalesPrice,
                    iu.PurchasePrice,
                    iu.IsDefaultSales,
                    iu.IsDefaultPurchase,
                    iu.IsActive
                })
                .ToListAsync();

            return Json(units);
        }
    }
}
