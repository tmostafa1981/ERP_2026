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
        [HasPermission("Organization.Branch.View")]
        public async Task<IActionResult> Index(string? searchString, int? companyIdFilter, int? pageSize = null, int page = 1)
        {
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
            var orderedQuery = query.OrderBy(b => b.CompanyId).ThenBy(b => b.Code);

            List<Branch> branches;
            if (pageSize.HasValue && pageSize.Value > 0)
            {
                branches = await orderedQuery
                    .Skip((page - 1) * pageSize.Value)
                    .Take(pageSize.Value)
                    .ToListAsync();
                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize.Value);
            }
            else
            {
                branches = await orderedQuery.ToListAsync();
                ViewBag.CurrentPage = 1;
                ViewBag.TotalPages = 1;
            }

            ViewBag.SearchString    = searchString;
            ViewBag.CompanyIdFilter = effectiveCompanyId;
            ViewBag.TotalCount      = totalCount;

            return View(branches);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Organization.Branch.Add")]
        public async Task<IActionResult> Create(Branch model)
        {
            if (string.IsNullOrWhiteSpace(model.BranchCode) && !string.IsNullOrWhiteSpace(model.Code))
                model.BranchCode = model.Code.Trim();
            if (string.IsNullOrWhiteSpace(model.Code) && !string.IsNullOrWhiteSpace(model.BranchCode))
                model.Code = model.BranchCode.Trim();

            if (string.IsNullOrWhiteSpace(model.BranchCode))
                ModelState.AddModelError("BranchCode", "كود الفرع مطلوب.");
            else if (await _context.Branches.AnyAsync(b => b.BranchCode == model.BranchCode.Trim() || b.Code == model.BranchCode.Trim()))
                ModelState.AddModelError("BranchCode", "كود الفرع موجود بالفعل.");

            if (string.IsNullOrWhiteSpace(model.NameAr) && string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("", "يجب إدخال اسم الفرع (بالعربي أو بالإنجليزي على الأقل).");

            if (model.CompanyId <= 0)
                ModelState.AddModelError("CompanyId", "يرجى اختيار الشركة التابع لها الفرع.");

            if (model.CompanyId > 0 && !_currentUser.CanAccessCompany(model.CompanyId))
                ModelState.AddModelError("CompanyId", "ليس لديك صلاحية الوصول إلى هذه الشركة.");

            if (ModelState.IsValid)
            {
                model.BranchCode = model.BranchCode.Trim();
                model.Code       = model.BranchCode;
                model.Name       = !string.IsNullOrWhiteSpace(model.NameAr) ? model.NameAr.Trim() : model.NameEn!.Trim();
                model.CreatedAt  = DateTime.UtcNow;

                _context.Branches.Add(model);
                await _context.SaveChangesAsync();

                // 1. Create Default Customer Categories
                bool isSpareParts = (!string.IsNullOrWhiteSpace(model.NameAr) ? model.NameAr : model.Name).Contains("قطع غيار");
                _context.CustomerCategories.AddRange(
                    new CustomerCategory
                    {
                        BranchId = model.Id,
                        Code = "EGP-MAC",
                        NameAr = "عملاء مصري ماكينات",
                        NameEn = "Egyptian Customers - Machines",
                        ControlAccountId = 620,
                        IsDefault = !isSpareParts,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new CustomerCategory
                    {
                        BranchId = model.Id,
                        Code = "EGP-SPR",
                        NameAr = "عملاء مصري قطع غيار",
                        NameEn = "Egyptian Customers - Spare Parts",
                        ControlAccountId = 622,
                        IsDefault = isSpareParts,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new CustomerCategory
                    {
                        BranchId = model.Id,
                        Code = "USD-MAC",
                        NameAr = "عملاء دولار ماكينات",
                        NameEn = "Dollar Customers - Machines",
                        ControlAccountId = 624,
                        IsDefault = false,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new CustomerCategory
                    {
                        BranchId = model.Id,
                        Code = "USD-SPR",
                        NameAr = "عملاء دولار قطع غيار",
                        NameEn = "Dollar Customers - Spare Parts",
                        ControlAccountId = 626,
                        IsDefault = false,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    }
                );

                // 2. Create Default Vendor Category
                var defaultVendCat = new VendorCategory
                {
                    BranchId  = model.Id,
                    Code      = model.BranchCode,
                    NameAr    = !string.IsNullOrWhiteSpace(model.NameAr) ? model.NameAr.Trim() : model.Name,
                    NameEn    = !string.IsNullOrWhiteSpace(model.NameEn) ? model.NameEn.Trim() : model.Name,
                    IsDefault = true,
                    IsActive  = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.VendorCategories.Add(defaultVendCat);

                // 3. Pre-initialize Code Sequences for Branch
                var custSeq = new CodeSequence
                {
                    EntityName      = "Customer",
                    BranchId        = model.Id,
                    Prefix          = $"{model.BranchCode}-C",
                    LastNumber      = 0,
                    PaddingLength   = 6,
                    IsAuto          = true,
                    IsManualAllowed = true,
                    CreatedAt       = DateTime.UtcNow
                };
                _context.CodeSequences.Add(custSeq);

                var vendSeq = new CodeSequence
                {
                    EntityName      = "Vendor",
                    BranchId        = model.Id,
                    Prefix          = $"{model.BranchCode}-V",
                    LastNumber      = 0,
                    PaddingLength   = 6,
                    IsAuto          = true,
                    IsManualAllowed = true,
                    CreatedAt       = DateTime.UtcNow
                };
                _context.CodeSequences.Add(vendSeq);

                var whSeq = new CodeSequence
                {
                    EntityName      = "Warehouse",
                    BranchId        = model.Id,
                    Prefix          = "WH",
                    LastNumber      = 1,
                    PaddingLength   = 6,
                    IsAuto          = true,
                    IsManualAllowed = true,
                    CreatedAt       = DateTime.UtcNow
                };
                _context.CodeSequences.Add(whSeq);

                var slmSeq = new CodeSequence
                {
                    EntityName      = "Salesman",
                    BranchId        = model.Id,
                    Prefix          = "SLM",
                    LastNumber      = 0,
                    PaddingLength   = 6,
                    IsAuto          = true,
                    IsManualAllowed = true,
                    CreatedAt       = DateTime.UtcNow
                };
                _context.CodeSequences.Add(slmSeq);

                // 4. Create Default Warehouse for Branch
                var whCode = (!string.IsNullOrWhiteSpace(model.BranchCode) && model.BranchCode.StartsWith("B", StringComparison.OrdinalIgnoreCase))
                    ? "WH" + model.BranchCode.Substring(1)
                    : "WH" + model.BranchCode;

                var defaultWarehouse = new Warehouse
                {
                    BranchId  = model.Id,
                    Code      = whCode,
                    NameAr    = !string.IsNullOrWhiteSpace(model.NameAr) ? model.NameAr.Trim() : model.Name,
                    NameEn    = !string.IsNullOrWhiteSpace(model.NameEn) ? model.NameEn.Trim() : model.Name,
                    IsActive  = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Warehouses.Add(defaultWarehouse);

                await _context.SaveChangesAsync();

                // ── Audit: Create ─────────────────────────────────────────────────
                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Branch",
                    entityId:   model.Id.ToString(),
                    newValues: new {
                        model.Id, model.Code, model.BranchCode, model.CompanyId,
                        model.NameAr, model.NameEn, model.Name,
                        model.AddressAr, model.AddressEn,
                        model.Phone, model.Email, model.IsActive
                    });

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم إضافة الفرع بنجاح.");

                TempData["SuccessMessage"] = "تم إضافة الفرع بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure(ModelState, "يرجى تصحيح أخطاء الإدخال الموضحة أدناه.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Organization.Branch.Edit")]
        public async Task<IActionResult> Edit(Branch model)
        {
            if (string.IsNullOrWhiteSpace(model.BranchCode) && !string.IsNullOrWhiteSpace(model.Code))
                model.BranchCode = model.Code.Trim();
            if (string.IsNullOrWhiteSpace(model.Code) && !string.IsNullOrWhiteSpace(model.BranchCode))
                model.Code = model.BranchCode.Trim();

            if (string.IsNullOrWhiteSpace(model.BranchCode))
                ModelState.AddModelError("BranchCode", "كود الفرع مطلوب.");
            else if (await _context.Branches.AnyAsync(b => (b.BranchCode == model.BranchCode.Trim() || b.Code == model.BranchCode.Trim()) && b.Id != model.Id))
                ModelState.AddModelError("BranchCode", "كود الفرع موجود بالفعل.");

            if (ModelState.IsValid)
            {
                var branch = await _context.Branches.FindAsync(model.Id);
                if (branch == null)
                {
                    if (Request.IsAjaxRequest())
                        return this.JsonValidationFailure(new[] { "الفرع غير موجود أو تم حذفه." });
                    return NotFound();
                }

                if (!_currentUser.CanAccessCompany(branch.CompanyId))
                {
                    if (Request.IsAjaxRequest())
                        return this.JsonValidationFailure(new[] { "ليس لديك صلاحية لتعديل هذا الفرع." });
                    return Forbid();
                }

                // ── Capture OldValues BEFORE any changes ──────────────────────────
                var oldValues = new {
                    branch.Id, branch.Code, branch.BranchCode, OldBranchCode = branch.BranchCode,
                    branch.CompanyId,
                    branch.NameAr, branch.NameEn, branch.Name,
                    branch.AddressAr, branch.AddressEn,
                    branch.Phone, branch.Email, branch.IsActive
                };

                branch.BranchCode = model.BranchCode.Trim();
                branch.Code       = model.BranchCode.Trim();
                if (model.CompanyId > 0 && _currentUser.CanAccessCompany(model.CompanyId))
                    branch.CompanyId = model.CompanyId;
                branch.NameAr    = model.NameAr;
                branch.NameEn    = model.NameEn;
                branch.Name      = !string.IsNullOrWhiteSpace(model.NameAr) ? model.NameAr : model.NameEn!;
                branch.AddressAr = model.AddressAr;
                branch.AddressEn = model.AddressEn;
                branch.Phone     = model.Phone;
                branch.Email     = model.Email;
                branch.UpdatedAt = DateTime.UtcNow;

                if (model.RowVersion != null && model.RowVersion.Length > 0)
                {
                    _context.Entry(branch).Property(e => e.RowVersion).OriginalValue = model.RowVersion;
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (Request.IsAjaxRequest())
                        return this.JsonFailure("تم تعديل السجل بواسطة مستخدم آخر أثناء عملك عليه. يرجى إعادة تحميل البيانات قبل الحفظ.");

                    TempData["ErrorMessage"] = "تم تعديل السجل بواسطة مستخدم آخر أثناء عملك عليه. يرجى إعادة تحميل البيانات قبل الحفظ.";
                    return RedirectToAction(nameof(Index));
                }

                // ── Audit: Update (auto-skipped if nothing changed) ───────────────
                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Branch",
                    entityId:   branch.Id.ToString(),
                    oldValues:  oldValues,
                    newValues:  new {
                        branch.Id, branch.Code, branch.BranchCode, NewBranchCode = branch.BranchCode,
                        branch.CompanyId,
                        branch.NameAr, branch.NameEn, branch.Name,
                        branch.AddressAr, branch.AddressEn,
                        branch.Phone, branch.Email, branch.IsActive
                    });

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم تحديث الفرع بنجاح.");

                TempData["SuccessMessage"] = "تم تحديث الفرع بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure(ModelState, "يرجى تصحيح أخطاء الإدخال الموضحة أدناه.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Organization.Branch.Edit")]
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
