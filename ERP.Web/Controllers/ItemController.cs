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
    public class ItemController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code   = "ITEM001";
        private const string SC_Name   = "Items";
        private const string SC_Module = "MasterData";

        public ItemController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
        {
            _context     = context;
            _currentUser = currentUser;
            _audit       = audit;
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

            ViewBag.SearchString = searchString;
            ViewBag.ItemGroupId  = itemGroupId;
            ViewBag.IsService    = isService;
            ViewBag.CurrentPage  = page;
            ViewBag.TotalPages   = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount   = totalCount;

            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Items.Add")]
        public async Task<IActionResult> Create(Item model, int? baseUnitId, decimal? salesPrice, decimal? purchasePrice)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Item Code is required.");
            else if (await _context.Items.AnyAsync(i => i.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "Item Code already exists.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (model.ItemGroupId <= 0 || !await _context.ItemGroups.AnyAsync(g => g.Id == model.ItemGroupId))
                ModelState.AddModelError("ItemGroupId", "Please select a valid item group.");

            if (baseUnitId.HasValue && baseUnitId.Value > 0 && !await _context.Units.AnyAsync(u => u.Id == baseUnitId.Value))
                ModelState.AddModelError("baseUnitId", "Please select a valid base unit.");

            if (ModelState.IsValid)
            {
                model.Code            = model.Code.Trim();
                model.Barcode         = string.IsNullOrWhiteSpace(model.Barcode) ? null : model.Barcode.Trim();
                model.TaxCode         = string.IsNullOrWhiteSpace(model.TaxCode) ? null : model.TaxCode.Trim();
                model.NameAr          = model.NameAr.Trim();
                model.NameEn          = model.NameEn.Trim();
                model.StorageLocation = string.IsNullOrWhiteSpace(model.StorageLocation) ? null : model.StorageLocation.Trim();
                model.CreatedAt       = DateTime.UtcNow;

                _context.Items.Add(model);
                await _context.SaveChangesAsync();

                // If base unit is selected, add as default base item unit
                if (baseUnitId.HasValue && baseUnitId.Value > 0)
                {
                    var itemUnit = new ItemUnit
                    {
                        ItemId            = model.Id,
                        UnitId            = baseUnitId.Value,
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
                }

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Item",
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
                        BaseUnitId = baseUnitId
                    });

                TempData["SuccessMessage"] = "Item created successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Items.Edit")]
        public async Task<IActionResult> Edit(Item model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Item Code is required.");
            else if (await _context.Items.AnyAsync(i => i.Code == model.Code.Trim() && i.Id != model.Id))
                ModelState.AddModelError("Code", "Item Code already exists.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (model.ItemGroupId <= 0 || !await _context.ItemGroups.AnyAsync(g => g.Id == model.ItemGroupId))
                ModelState.AddModelError("ItemGroupId", "Please select a valid item group.");

            if (ModelState.IsValid)
            {
                var entity = await _context.Items.FindAsync(model.Id);
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

                entity.Code            = model.Code.Trim();
                entity.Barcode         = string.IsNullOrWhiteSpace(model.Barcode) ? null : model.Barcode.Trim();
                entity.TaxCode         = string.IsNullOrWhiteSpace(model.TaxCode) ? null : model.TaxCode.Trim();
                entity.NameAr          = model.NameAr.Trim();
                entity.NameEn          = model.NameEn.Trim();
                entity.ItemGroupId     = model.ItemGroupId;
                entity.IsService       = model.IsService;
                entity.StorageLocation = string.IsNullOrWhiteSpace(model.StorageLocation) ? null : model.StorageLocation.Trim();
                entity.IsActive        = model.IsActive;
                entity.UpdatedAt       = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Item",
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new
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
                    });

                TempData["SuccessMessage"] = "Item updated successfully.";
                return RedirectToAction(nameof(Index));
            }

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

            var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.IsActive };

            _context.Items.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Item",
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
                entityName: "Item",
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
