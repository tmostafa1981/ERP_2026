using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class CompanyController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly IAuditService _audit;

        // ─── Screen constants ─────────────────────────────────────────────────────
        private const string SC_Code   = "COM001";
        private const string SC_Name   = "Company";
        private const string SC_Module = "Organization";

        public CompanyController(ErpDbContext context, IAuditService audit)
        {
            _context = context;
            _audit   = audit;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var company = await _context.Companies.FirstOrDefaultAsync();
            if (company == null)
            {
                company = new Company
                {
                    Code    = "COMP01",
                    Name    = "ABC Global Corporation",
                    NameAr  = "شركة أيه بي سي العالمية",
                    NameEn  = "ABC Global Corporation",
                    IsActive = true
                };
                _context.Companies.Add(company);
                await _context.SaveChangesAsync();
            }

            return View(company);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Company model)
        {
            if (string.IsNullOrWhiteSpace(model.NameAr) && string.IsNullOrWhiteSpace(model.NameEn))
            {
                ModelState.AddModelError("", "At least one name (Arabic or English) is required.");
            }

            if (ModelState.IsValid)
            {
                var company = await _context.Companies.FindAsync(model.Id);
                if (company == null) return NotFound();

                // ── Capture OldValues BEFORE any changes ──────────────────────────
                var oldValues = new {
                    company.Id, company.Code, company.Name,
                    company.NameAr, company.NameEn,
                    company.AddressAr, company.AddressEn,
                    company.Phone, company.Email, company.TaxNumber, company.IsActive
                };

                company.Code      = model.Code;
                company.Name      = !string.IsNullOrWhiteSpace(model.NameAr) ? model.NameAr : model.NameEn ?? "Company";
                company.NameAr    = model.NameAr;
                company.NameEn    = model.NameEn;
                company.AddressAr = model.AddressAr;
                company.AddressEn = model.AddressEn;
                company.Phone     = model.Phone;
                company.Email     = model.Email;
                company.TaxNumber = model.TaxNumber;
                company.IsActive  = model.IsActive;
                company.UpdatedAt = DateTime.UtcNow;

                _context.Update(company);
                await _context.SaveChangesAsync();

                // ── Audit: Update ─────────────────────────────────────────────────
                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Company",
                    entityId:   company.Id.ToString(),
                    oldValues:  oldValues,
                    newValues: new {
                        company.Id, company.Code, company.Name,
                        company.NameAr, company.NameEn,
                        company.AddressAr, company.AddressEn,
                        company.Phone, company.Email, company.TaxNumber, company.IsActive
                    });

                TempData["SuccessMessage"] = "Company details updated successfully.";
                return RedirectToAction(nameof(Index));
            }

            return View("Index", model);
        }
    }
}
