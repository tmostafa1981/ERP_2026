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
    public class BranchController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        // ─── Screen constants ─────────────────────────────────────────────────────
        private const string SC_Code   = "BRN001";
        private const string SC_Name   = "Branches";
        private const string SC_Module = "Organization";

        public BranchController(ErpDbContext context, ICurrentUserContext currentUser, IAuditService audit)
        {
            _context     = context;
            _currentUser = currentUser;
            _audit       = audit;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? searchString, int? companyIdFilter, int page = 1)
        {
            int pageSize = 15;

            var companiesQuery = _currentUser.HasAllBranches
                ? _context.Companies.Where(c => c.IsActive)
                : _context.Companies.Where(c => c.IsActive && c.Id == _currentUser.CompanyId);

            var allCompanies = await companiesQuery.OrderBy(c => c.Code).ToListAsync();
            ViewBag.Companies = allCompanies;

            int? effectiveCompanyId = companyIdFilter;
            if (effectiveCompanyId == null || effectiveCompanyId == 0)
            {
                effectiveCompanyId = _currentUser.HasAllBranches ? null : _currentUser.CompanyId;
            }

            var query = _context.Branches.Include(b => b.Company).AsNoTracking();

            if (effectiveCompanyId.HasValue && effectiveCompanyId > 0)
                query = query.Where(b => b.CompanyId == effectiveCompanyId.Value);
            else if (!_currentUser.HasAllBranches)
                query = query.Where(b => b.CompanyId == _currentUser.CompanyId);

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(b =>
                    b.Code.Contains(searchString) ||
                    b.Name.Contains(searchString) ||
                    (b.NameAr != null && b.NameAr.Contains(searchString)) ||
                    (b.NameEn != null && b.NameEn.Contains(searchString)));
            }

            int totalCount = await query.CountAsync();
            var branches = await query
                .OrderBy(b => b.CompanyId).ThenBy(b => b.Code)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            ViewBag.SearchString    = searchString;
            ViewBag.CompanyIdFilter = effectiveCompanyId;
            ViewBag.CurrentPage     = page;
            ViewBag.TotalPages      = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount      = totalCount;

            return View(branches);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Branch model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Branch Code is required.");
            else if (await _context.Branches.AnyAsync(b => b.Code == model.Code))
                ModelState.AddModelError("Code", "Branch Code already exists.");

            if (string.IsNullOrWhiteSpace(model.NameAr) && string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("", "At least one name (Arabic or English) is required.");

            if (model.CompanyId <= 0)
                ModelState.AddModelError("CompanyId", "Please select a company.");

            if (model.CompanyId > 0 && !_currentUser.CanAccessCompany(model.CompanyId))
                ModelState.AddModelError("CompanyId", "Access denied to the selected company.");

            if (ModelState.IsValid)
            {
                model.Name      = !string.IsNullOrWhiteSpace(model.NameAr) ? model.NameAr : model.NameEn!;
                model.CreatedAt = DateTime.UtcNow;
                _context.Branches.Add(model);
                await _context.SaveChangesAsync();

                // ── Audit: Create ─────────────────────────────────────────────────
                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Branch",
                    entityId:   model.Id.ToString(),
                    newValues: new {
                        model.Id, model.Code, model.CompanyId,
                        model.NameAr, model.NameEn, model.Name,
                        model.AddressAr, model.AddressEn,
                        model.Phone, model.Email, model.IsActive
                    });

                TempData["SuccessMessage"] = "Branch added successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Branch model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "Branch Code is required.");
            else if (await _context.Branches.AnyAsync(b => b.Code == model.Code && b.Id != model.Id))
                ModelState.AddModelError("Code", "Branch Code already exists.");

            if (ModelState.IsValid)
            {
                var branch = await _context.Branches.FindAsync(model.Id);
                if (branch == null) return NotFound();

                if (!_currentUser.CanAccessCompany(branch.CompanyId))
                    return Forbid();

                // ── Capture OldValues BEFORE any changes ──────────────────────────
                var oldValues = new {
                    branch.Id, branch.Code, branch.CompanyId,
                    branch.NameAr, branch.NameEn, branch.Name,
                    branch.AddressAr, branch.AddressEn,
                    branch.Phone, branch.Email, branch.IsActive
                };

                branch.Code = model.Code;
                if (model.CompanyId > 0 && _currentUser.CanAccessCompany(model.CompanyId))
                    branch.CompanyId = model.CompanyId;
                branch.NameAr    = model.NameAr;
                branch.NameEn    = model.NameEn;
                branch.Name      = !string.IsNullOrWhiteSpace(model.NameAr) ? model.NameAr : model.NameEn!;
                branch.AddressAr = model.AddressAr;
                branch.AddressEn = model.AddressEn;
                branch.Phone     = model.Phone;
                branch.Email     = model.Email;
                branch.IsActive  = model.IsActive;
                branch.UpdatedAt = DateTime.UtcNow;

                _context.Update(branch);
                await _context.SaveChangesAsync();

                // ── Audit: Update (auto-skipped if nothing changed) ───────────────
                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Branch",
                    entityId:   branch.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new {
                        branch.Id, branch.Code, branch.CompanyId,
                        branch.NameAr, branch.NameEn, branch.Name,
                        branch.AddressAr, branch.AddressEn,
                        branch.Phone, branch.Email, branch.IsActive
                    });

                TempData["SuccessMessage"] = "Branch updated successfully.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var branch = await _context.Branches.FindAsync(id);
            if (branch == null) return NotFound();

            if (!_currentUser.CanAccessCompany(branch.CompanyId))
                return Forbid();

            var oldIsActive  = branch.IsActive;
            branch.IsActive  = !branch.IsActive;
            branch.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // ── Audit: Update (status toggle) ─────────────────────────────────────
            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "Branch",
                entityId:   branch.Id.ToString(),
                oldValues: new { branch.Id, branch.Code, IsActive = oldIsActive },
                newValues: new { branch.Id, branch.Code, branch.IsActive },
                description: $"Branch {branch.Code} toggled IsActive: {oldIsActive} → {branch.IsActive}");

            TempData["SuccessMessage"] = $"Branch status updated to {(branch.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
