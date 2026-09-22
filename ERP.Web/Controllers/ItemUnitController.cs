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
    public class ItemUnitController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code   = "ITMU001";
        private const string SC_Name   = "ItemUnits";
        private const string SC_Module = "MasterData";

        public ItemUnitController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
        {
            _context     = context;
            _currentUser = currentUser;
            _audit       = audit;
        }

        [HttpGet]
        [HasPermission("MasterData.ItemUnits.View")]
        public async Task<IActionResult> Index(int? itemId, string? searchString, int page = 1)
        {
            int pageSize = 15;

            var items = await _context.Items
                .Where(i => i.IsActive)
                .OrderBy(i => i.Code)
                .AsNoTracking()
                .ToListAsync();
            ViewBag.Items = items;

            var units = await _context.Units
                .Where(u => u.IsActive)
                .OrderBy(u => u.Code)
                .AsNoTracking()
                .ToListAsync();
            ViewBag.Units = units;

            if (itemId.HasValue && itemId.Value > 0)
            {
                var currentItem = items.FirstOrDefault(i => i.Id == itemId.Value);
                ViewBag.CurrentItem = currentItem;
            }

            var query = _context.ItemUnits
                .Include(iu => iu.Item)
                .Include(iu => iu.Unit)
                .AsNoTracking();

            if (itemId.HasValue && itemId.Value > 0)
            {
                query = query.Where(iu => iu.ItemId == itemId.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(iu =>
                    (iu.Item != null && (iu.Item.Code.Contains(searchString) || iu.Item.NameAr.Contains(searchString) || iu.Item.NameEn.Contains(searchString))) ||
                    (iu.Unit != null && (iu.Unit.Code.Contains(searchString) || iu.Unit.NameAr.Contains(searchString) || iu.Unit.NameEn.Contains(searchString))));
            }

            int totalCount = await query.CountAsync();
            var itemUnits = await query
                .OrderBy(iu => iu.ItemId).ThenBy(iu => iu.ConversionFactor)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.ItemId       = itemId;
            ViewBag.SearchString = searchString;
            ViewBag.CurrentPage  = page;
            ViewBag.TotalPages   = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount   = totalCount;

            return View(itemUnits);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.ItemUnits.Add")]
        public async Task<IActionResult> Create(ItemUnit model)
        {
            if (model.ItemId <= 0 || !await _context.Items.AnyAsync(i => i.Id == model.ItemId))
                ModelState.AddModelError("ItemId", "Please select a valid item.");

            if (model.UnitId <= 0 || !await _context.Units.AnyAsync(u => u.Id == model.UnitId))
                ModelState.AddModelError("UnitId", "Please select a valid unit.");

            if (model.ItemId > 0 && model.UnitId > 0 && await _context.ItemUnits.AnyAsync(iu => iu.ItemId == model.ItemId && iu.UnitId == model.UnitId))
                ModelState.AddModelError("UnitId", "This unit is already assigned to the selected item.");

            if (model.ConversionFactor <= 0)
                ModelState.AddModelError("ConversionFactor", "Conversion factor must be greater than zero.");

            if (model.SalesPrice < 0)
                ModelState.AddModelError("SalesPrice", "Sales price cannot be negative.");

            if (model.PurchasePrice < 0)
                ModelState.AddModelError("PurchasePrice", "Purchase price cannot be negative.");

            if (ModelState.IsValid)
            {
                // If setting as default sales, clear other default sales for this item
                if (model.IsDefaultSales)
                {
                    var existingSalesDefaults = await _context.ItemUnits
                        .Where(iu => iu.ItemId == model.ItemId && iu.IsDefaultSales)
                        .ToListAsync();
                    foreach (var ed in existingSalesDefaults)
                    {
                        ed.IsDefaultSales = false;
                    }
                }

                // If setting as default purchase, clear other default purchases for this item
                if (model.IsDefaultPurchase)
                {
                    var existingPurchaseDefaults = await _context.ItemUnits
                        .Where(iu => iu.ItemId == model.ItemId && iu.IsDefaultPurchase)
                        .ToListAsync();
                    foreach (var ed in existingPurchaseDefaults)
                    {
                        ed.IsDefaultPurchase = false;
                    }
                }

                model.CreatedAt = DateTime.UtcNow;
                _context.ItemUnits.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "ItemUnit",
                    entityId:   model.Id.ToString(),
                    newValues: new
                    {
                        model.Id,
                        model.ItemId,
                        model.UnitId,
                        model.ConversionFactor,
                        model.SalesPrice,
                        model.PurchasePrice,
                        model.IsDefaultSales,
                        model.IsDefaultPurchase,
                        model.IsActive
                    });

                TempData["SuccessMessage"] = "Item unit added successfully.";
                return RedirectToAction(nameof(Index), new { itemId = model.ItemId });
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { itemId = model.ItemId > 0 ? (int?)model.ItemId : null });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.ItemUnits.Edit")]
        public async Task<IActionResult> Edit(ItemUnit model)
        {
            var entity = await _context.ItemUnits.FindAsync(model.Id);
            if (entity == null) return NotFound();

            if (model.UnitId <= 0 || !await _context.Units.AnyAsync(u => u.Id == model.UnitId))
                ModelState.AddModelError("UnitId", "Please select a valid unit.");

            if (await _context.ItemUnits.AnyAsync(iu => iu.ItemId == entity.ItemId && iu.UnitId == model.UnitId && iu.Id != model.Id))
                ModelState.AddModelError("UnitId", "This unit is already assigned to this item.");

            if (model.ConversionFactor <= 0)
                ModelState.AddModelError("ConversionFactor", "Conversion factor must be greater than zero.");

            if (model.SalesPrice < 0)
                ModelState.AddModelError("SalesPrice", "Sales price cannot be negative.");

            if (model.PurchasePrice < 0)
                ModelState.AddModelError("PurchasePrice", "Purchase price cannot be negative.");

            if (ModelState.IsValid)
            {
                var oldValues = new
                {
                    entity.Id,
                    entity.ItemId,
                    entity.UnitId,
                    entity.ConversionFactor,
                    entity.SalesPrice,
                    entity.PurchasePrice,
                    entity.IsDefaultSales,
                    entity.IsDefaultPurchase,
                    entity.IsActive
                };

                // Handle default sales exclusivity
                if (model.IsDefaultSales && !entity.IsDefaultSales)
                {
                    var existingSalesDefaults = await _context.ItemUnits
                        .Where(iu => iu.ItemId == entity.ItemId && iu.Id != entity.Id && iu.IsDefaultSales)
                        .ToListAsync();
                    foreach (var ed in existingSalesDefaults)
                    {
                        ed.IsDefaultSales = false;
                    }
                }

                // Handle default purchase exclusivity
                if (model.IsDefaultPurchase && !entity.IsDefaultPurchase)
                {
                    var existingPurchaseDefaults = await _context.ItemUnits
                        .Where(iu => iu.ItemId == entity.ItemId && iu.Id != entity.Id && iu.IsDefaultPurchase)
                        .ToListAsync();
                    foreach (var ed in existingPurchaseDefaults)
                    {
                        ed.IsDefaultPurchase = false;
                    }
                }

                entity.UnitId            = model.UnitId;
                entity.ConversionFactor  = model.ConversionFactor;
                entity.SalesPrice        = model.SalesPrice;
                entity.PurchasePrice     = model.PurchasePrice;
                entity.IsDefaultSales    = model.IsDefaultSales;
                entity.IsDefaultPurchase = model.IsDefaultPurchase;
                entity.IsActive          = model.IsActive;
                entity.UpdatedAt         = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "ItemUnit",
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new
                    {
                        entity.Id,
                        entity.ItemId,
                        entity.UnitId,
                        entity.ConversionFactor,
                        entity.SalesPrice,
                        entity.PurchasePrice,
                        entity.IsDefaultSales,
                        entity.IsDefaultPurchase,
                        entity.IsActive
                    });

                TempData["SuccessMessage"] = "Item unit updated successfully.";
                return RedirectToAction(nameof(Index), new { itemId = entity.ItemId });
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { itemId = entity.ItemId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.ItemUnits.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.ItemUnits.FindAsync(id);
            if (entity == null) return NotFound();

            int itemId = entity.ItemId;
            var oldValues = new { entity.Id, entity.ItemId, entity.UnitId, entity.ConversionFactor, entity.IsActive };

            _context.ItemUnits.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "ItemUnit",
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "Item unit deleted successfully.";
            return RedirectToAction(nameof(Index), new { itemId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.ItemUnits.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.ItemUnits.FindAsync(id);
            if (entity == null) return NotFound();

            var oldValues = new { entity.Id, entity.ItemId, entity.UnitId, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "ItemUnit",
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.ItemId, entity.UnitId, entity.IsActive });

            TempData["SuccessMessage"] = $"Item unit status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index), new { itemId = entity.ItemId });
        }
    }
}
