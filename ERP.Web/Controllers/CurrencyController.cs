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
    public class CurrencyController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code   = "CUR001";
        private const string SC_Name   = "Currencies";
        private const string SC_Module = "MasterData";

        public CurrencyController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
        {
            _context     = context;
            _currentUser = currentUser;
            _audit       = audit;
        }

        [HttpGet]
        [HasPermission("MasterData.Currencies.View")]
        public async Task<IActionResult> Index(string? searchString, int page = 1)
        {
            int pageSize = 15;
            var query = _context.Currencies.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(c =>
                    c.Code.Contains(searchString) ||
                    c.NameAr.Contains(searchString) ||
                    c.NameEn.Contains(searchString) ||
                    (c.Symbol != null && c.Symbol.Contains(searchString)));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.Code)
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
        [HasPermission("MasterData.Currencies.Add")]
        public async Task<IActionResult> Create(Currency model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Currency Code is required.");
            else if (await _context.Currencies.AnyAsync(c => c.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "Currency Code already exists.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (model.ExchangeRate <= 0)
                ModelState.AddModelError("ExchangeRate", "Exchange Rate must be greater than zero.");

            if (ModelState.IsValid)
            {
                model.Code      = model.Code.Trim().ToUpperInvariant();
                model.NameAr    = model.NameAr.Trim();
                model.NameEn    = model.NameEn.Trim();
                model.Symbol    = model.Symbol?.Trim();
                model.CreatedAt = DateTime.UtcNow;

                _context.Currencies.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Currency",
                    entityId:   model.Id.ToString(),
                    newValues: new { model.Id, model.Code, model.NameAr, model.NameEn, model.Symbol, model.ExchangeRate, model.IsActive });

                TempData["SuccessMessage"] = "Currency created successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Currencies.Edit")]
        public async Task<IActionResult> Edit(Currency model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Currency Code is required.");
            else if (await _context.Currencies.AnyAsync(c => c.Code == model.Code.Trim() && c.Id != model.Id))
                ModelState.AddModelError("Code", "Currency Code already exists.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "Arabic Name is required.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameEn", "English Name is required.");

            if (model.ExchangeRate <= 0)
                ModelState.AddModelError("ExchangeRate", "Exchange Rate must be greater than zero.");

            if (ModelState.IsValid)
            {
                var entity = await _context.Currencies.FindAsync(model.Id);
                if (entity == null) return NotFound();

                var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.Symbol, entity.ExchangeRate, entity.IsActive };

                entity.Code         = model.Code.Trim().ToUpperInvariant();
                entity.NameAr       = model.NameAr.Trim();
                entity.NameEn       = model.NameEn.Trim();
                entity.Symbol       = model.Symbol?.Trim();
                entity.ExchangeRate = model.ExchangeRate;
                entity.IsActive     = model.IsActive;
                entity.UpdatedAt    = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Currency",
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.Symbol, entity.ExchangeRate, entity.IsActive });

                TempData["SuccessMessage"] = "Currency updated successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Currencies.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Currencies.FindAsync(id);
            if (entity == null) return NotFound();

            bool isUsed = await _context.Customers.AnyAsync(c => c.CurrencyId == id) ||
                          await _context.Vendors.AnyAsync(v => v.CurrencyId == id);

            if (isUsed)
            {
                TempData["ErrorMessage"] = "Cannot delete currency because it is linked to customers or vendors.";
                return RedirectToAction(nameof(Index));
            }

            var oldValues = new { entity.Id, entity.Code, entity.NameAr, entity.NameEn, entity.ExchangeRate, entity.IsActive };

            _context.Currencies.Remove(entity);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Currency",
                entityId:   id.ToString(),
                oldValues:  oldValues);

            TempData["SuccessMessage"] = "Currency deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Currencies.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.Currencies.FindAsync(id);
            if (entity == null) return NotFound();

            var oldValues = new { entity.Id, entity.Code, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Currency",
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"Currency status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
