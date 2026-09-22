using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class FiscalYearController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly IAuditService _audit;

        // ─── Screen constants ─────────────────────────────────────────────────────
        private const string SC_Code   = "FYR001";
        private const string SC_Name   = "FiscalYears";
        private const string SC_Module = "Organization";

        public FiscalYearController(ErpDbContext context, IAuditService audit)
        {
            _context = context;
            _audit   = audit;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var years = await _context.FiscalYears.OrderByDescending(f => f.Year).ToListAsync();
            return View(years);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FiscalYear model)
        {
            if (string.IsNullOrWhiteSpace(model.Year))
                ModelState.AddModelError("Year", "Year is required.");
            else if (await _context.FiscalYears.AnyAsync(f => f.Year == model.Year))
                ModelState.AddModelError("Year", "Fiscal Year already exists.");

            if (model.StartDate >= model.EndDate)
                ModelState.AddModelError("EndDate", "Start Date must be earlier than End Date.");

            if (ModelState.IsValid)
            {
                model.Name      = !string.IsNullOrWhiteSpace(model.NameAr) ? model.NameAr : (model.NameEn ?? model.Year);
                model.CreatedAt = DateTime.UtcNow;
                model.IsActive  = true;
                model.IsClosed  = false;

                _context.FiscalYears.Add(model);
                await _context.SaveChangesAsync();

                // ── Audit: Create ─────────────────────────────────────────────────
                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "FiscalYear",
                    entityId:   model.Id.ToString(),
                    newValues: new {
                        model.Id, model.Year, model.NameAr, model.NameEn,
                        model.StartDate, model.EndDate,
                        model.IsActive, model.IsClosed
                    });

                TempData["SuccessMessage"] = "Fiscal Year created successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(FiscalYear model)
        {
            if (model.StartDate >= model.EndDate)
                ModelState.AddModelError("EndDate", "Start Date must be earlier than End Date.");

            if (ModelState.IsValid)
            {
                var fy = await _context.FiscalYears.FindAsync(model.Id);
                if (fy == null) return NotFound();

                // ── Capture OldValues BEFORE any changes ──────────────────────────
                var oldValues = new {
                    fy.Id, fy.Year, fy.NameAr, fy.NameEn,
                    fy.StartDate, fy.EndDate, fy.IsActive, fy.IsClosed
                };

                fy.Year      = model.Year;
                fy.NameAr    = model.NameAr;
                fy.NameEn    = model.NameEn;
                fy.Name      = !string.IsNullOrWhiteSpace(model.NameAr) ? model.NameAr : (model.NameEn ?? model.Year);
                fy.StartDate = model.StartDate;
                fy.EndDate   = model.EndDate;
                fy.IsActive  = model.IsActive;
                fy.UpdatedAt = DateTime.UtcNow;

                _context.Update(fy);
                await _context.SaveChangesAsync();

                // ── Audit: Update ─────────────────────────────────────────────────
                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "FiscalYear",
                    entityId:   fy.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new {
                        fy.Id, fy.Year, fy.NameAr, fy.NameEn,
                        fy.StartDate, fy.EndDate, fy.IsActive, fy.IsClosed
                    });

                TempData["SuccessMessage"] = "Fiscal Year updated successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleClose(int id)
        {
            var fy = await _context.FiscalYears.FindAsync(id);
            if (fy == null) return NotFound();

            var oldIsClosed = fy.IsClosed;
            fy.IsClosed  = !fy.IsClosed;
            fy.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // ── Audit: Update (close/open toggle) ─────────────────────────────────
            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "FiscalYear",
                entityId:   fy.Id.ToString(),
                oldValues: new { fy.Id, fy.Year, IsClosed = oldIsClosed },
                newValues: new { fy.Id, fy.Year, fy.IsClosed },
                description: $"FiscalYear {fy.Year} toggled IsClosed: {oldIsClosed} → {fy.IsClosed}");

            TempData["SuccessMessage"] = $"Fiscal Year status changed to {(fy.IsClosed ? "Closed" : "Open")}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
