using ERP.Web.Models;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Identity;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Application.Interfaces;
using ERP.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ERP.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ErpDbContext _context;
        private readonly Microsoft.Extensions.Localization.IStringLocalizer<ERP.Web.Resources.SharedResource> _localizer;
        private readonly IAuditService _audit;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ErpDbContext context,
            Microsoft.Extensions.Localization.IStringLocalizer<ERP.Web.Resources.SharedResource> localizer,
            IAuditService audit)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
            _localizer = localizer;
            _audit = audit;
        }

        // ─── GET: Login ───────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Login()
        {
            var model = new LoginViewModel();
            var fiscalYears = await _context.FiscalYears
                .Where(f => !f.IsClosed && f.IsActive)
                .OrderByDescending(f => f.Year)
                .ToListAsync();
            model.FiscalYears = fiscalYears.Select(f =>
                new SelectListItem { Value = f.Id.ToString(), Text = f.LocalizedName });
            return View(model);
        }

        // ─── AJAX: GetCompanies (by EmployeeCode) ─────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetCompanies(string employeeCode)
        {
            if (string.IsNullOrWhiteSpace(employeeCode))
                return Json(new { success = false, message = "Employee code is required." });

            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.EmployeeCode == employeeCode || u.UserName == employeeCode);

            if (user == null)
                return Json(new { success = false, message = "Employee not found." });

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "System Administrator");
            List<Company> companies;

            if (isAdmin || user.HasAllBranches)
            {
                companies = await _context.Companies
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.Code)
                    .ToListAsync();
            }
            else
            {
                companies = await _context.UserBranches
                    .Include(ub => ub.Branch)
                    .ThenInclude(b => b!.Company)
                    .Where(ub => ub.UserId == user.Id && ub.Branch!.IsActive && ub.Branch.Company!.IsActive)
                    .Select(ub => ub.Branch!.Company!)
                    .Distinct()
                    .OrderBy(c => c.Code)
                    .ToListAsync();
            }

            var companyItems = companies.Select(c => new { id = c.Id, name = c.LocalizedName, code = c.Code });
            return Json(new { success = true, companies = companyItems });
        }

        // ─── AJAX: GetBranches (by EmployeeCode + CompanyId) ──────────────────────
        [HttpGet]
        public async Task<IActionResult> GetBranches(string employeeCode, int companyId)
        {
            if (string.IsNullOrWhiteSpace(employeeCode) || companyId <= 0)
                return Json(new { success = false });

            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.EmployeeCode == employeeCode || u.UserName == employeeCode);

            if (user == null)
                return Json(new { success = false, message = "Employee not found." });

            // Verify user has access to this company via allowed branches (server-side check)
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "System Administrator");
            bool hasCompanyAccess = isAdmin || user.HasAllBranches ||
                await _context.UserBranches.AnyAsync(ub => ub.UserId == user.Id && ub.Branch!.CompanyId == companyId && ub.Branch.IsActive);

            if (!hasCompanyAccess)
                return Json(new { success = false, message = "Access denied to this company." });

            List<Branch> branches;

            if (isAdmin || user.HasAllBranches)
            {
                branches = await _context.Branches
                    .Where(b => b.IsActive && b.CompanyId == companyId)
                    .OrderBy(b => b.Code)
                    .ToListAsync();
            }
            else
            {
                branches = await _context.UserBranches
                    .Include(ub => ub.Branch)
                    .Where(ub => ub.UserId == user.Id &&
                                 ub.Branch!.IsActive &&
                                 ub.Branch.CompanyId == companyId)
                    .Select(ub => ub.Branch!)
                    .OrderBy(b => b.Code)
                    .ToListAsync();
            }

            var branchItems = branches.Select(b => new { id = b.Id, name = b.LocalizedName, code = b.Code });
            return Json(new { success = true, branches = branchItems });
        }

        // ─── POST: Login ──────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            // Reload fiscal years for re-render on failure
            async Task ReloadFiscalYears()
            {
                var fy = await _context.FiscalYears
                    .Where(f => !f.IsClosed && f.IsActive)
                    .OrderByDescending(f => f.Year)
                    .ToListAsync();
                model.FiscalYears = fy.Select(f =>
                    new SelectListItem { Value = f.Id.ToString(), Text = f.LocalizedName });
            }

            if (model.FiscalYearId <= 0)
                ModelState.AddModelError("FiscalYearId", _localizer["SelectFiscalYear"]);

            if (string.IsNullOrWhiteSpace(model.EmployeeCode))
                ModelState.AddModelError("EmployeeCode", _localizer["RequiredField"]);

            if (model.CompanyId <= 0)
                ModelState.AddModelError("CompanyId", _localizer["SelectCompany"]);

            if (model.BranchId <= 0)
                ModelState.AddModelError("BranchId", _localizer["SelectBranch"]);

            if (string.IsNullOrWhiteSpace(model.Password))
                ModelState.AddModelError("Password", _localizer["RequiredField"]);

            if (!ModelState.IsValid)
            {
                await ReloadFiscalYears();
                return View(model);
            }

            // 1. Find user by EmployeeCode
            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.EmployeeCode == model.EmployeeCode ||
                                          u.UserName == model.EmployeeCode);

            if (user == null)
            {
                ModelState.AddModelError("", _localizer["InvalidLogin"]);
                await ReloadFiscalYears();
                return View(model);
            }

            if (!user.IsActive)
            {
                ModelState.AddModelError("", _localizer["Inactive"]);
                await ReloadFiscalYears();
                return View(model);
            }

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "System Administrator");

            // 2. Validate Company access (server-side via allowed branches)
            bool hasCompanyAccess = isAdmin || user.HasAllBranches ||
                await _context.UserBranches.AnyAsync(ub => ub.UserId == user.Id && ub.Branch!.CompanyId == model.CompanyId && ub.Branch.IsActive);

            if (!hasCompanyAccess)
            {
                ModelState.AddModelError("", _localizer["AccessDeniedMessage"]);
                await ReloadFiscalYears();
                return View(model);
            }

            // 3. Validate Branch access (server-side) – branch must belong to the selected company & be assigned to user
            var branchExists = await _context.Branches
                .AnyAsync(b => b.Id == model.BranchId && b.CompanyId == model.CompanyId && b.IsActive);

            if (!branchExists)
            {
                ModelState.AddModelError("", _localizer["NoBranchAccess"]);
                await ReloadFiscalYears();
                return View(model);
            }

            bool hasBranchAccess = isAdmin || user.HasAllBranches ||
                await _context.UserBranches.AnyAsync(ub =>
                    ub.UserId == user.Id &&
                    ub.BranchId == model.BranchId &&
                    ub.Branch!.IsActive &&
                    ub.Branch.CompanyId == model.CompanyId);

            if (!hasBranchAccess)
            {
                ModelState.AddModelError("", _localizer["NoBranchAccess"]);
                await ReloadFiscalYears();
                return View(model);
            }

            // 4. Validate FiscalYear
            var fiscalYear = await _context.FiscalYears.FindAsync(model.FiscalYearId);
            if (fiscalYear == null || !fiscalYear.IsActive)
            {
                ModelState.AddModelError("FiscalYearId", _localizer["SelectFiscalYear"]);
                await ReloadFiscalYears();
                return View(model);
            }

            // 5. Check password safely (guaranteed non-null)
            var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password ?? string.Empty, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                await _audit.LogFailedAsync("ACC001", "Login", "Account", "User", user.Id, AuditAction.Login,
                    result.IsLockedOut ? "User is locked out" : "Invalid credentials");

                if (result.IsLockedOut)
                    ModelState.AddModelError("", _localizer["Locked"]);
                else
                    ModelState.AddModelError("", _localizer["InvalidLogin"]);
                await ReloadFiscalYears();
                return View(model);
            }

            // 6. Build Claims & Load Granted Permissions
            var company = await _context.Companies.FindAsync(model.CompanyId);
            var branch = await _context.Branches.FindAsync(model.BranchId);

            var allowedCompanyIds = (isAdmin || user.HasAllBranches)
                ? await _context.Companies.Where(c => c.IsActive).Select(c => c.Id.ToString()).ToListAsync()
                : await _context.UserBranches.Where(ub => ub.UserId == user.Id && ub.Branch!.IsActive).Select(ub => ub.Branch!.CompanyId.ToString()).Distinct().ToListAsync();

            var allowedBranchIds = (isAdmin || user.HasAllBranches)
                ? await _context.Branches.Where(b => b.IsActive && b.CompanyId == model.CompanyId).Select(b => b.Id.ToString()).ToListAsync()
                : await _context.UserBranches.Where(ub => ub.UserId == user.Id && ub.Branch!.CompanyId == model.CompanyId && ub.Branch.IsActive).Select(ub => ub.BranchId.ToString()).ToListAsync();

            var claims = new List<Claim>
            {
                new Claim("EmployeeCode", user.EmployeeCode ?? user.UserName ?? ""),
                new Claim("CompanyId", company!.Id.ToString()),
                new Claim("CompanyName", company.LocalizedName),
                new Claim("BranchId", branch!.Id.ToString()),
                new Claim("BranchName", branch.LocalizedName),
                new Claim("FiscalYearId", fiscalYear.Id.ToString()),
                new Claim("FiscalYearName", fiscalYear.LocalizedName),
                new Claim("HasAllBranches", (isAdmin || user.HasAllBranches).ToString()),
            };

            foreach (var cid in allowedCompanyIds)
                claims.Add(new Claim("AllowedCompanyId", cid));

            foreach (var bid in allowedBranchIds)
                claims.Add(new Claim("AllowedBranchId", bid));

            // Load role-permission claims
            var userRoleNames = await _userManager.GetRolesAsync(user);
            var userRoleIds = await _context.Roles.Where(r => userRoleNames.Contains(r.Name!)).Select(r => r.Id).ToListAsync();
            var grantedPermissionCodes = await _context.RolePermissions
                .Where(rp => userRoleIds.Contains(rp.RoleId) && rp.IsGranted)
                .Select(rp => rp.Permission!.Code)
                .Distinct()
                .ToListAsync();

            foreach (var permCode in grantedPermissionCodes)
            {
                claims.Add(new Claim("Permission", permCode));
            }

            await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, claims);

            await _audit.LogAsync(new AuditLogEntry
            {
                ScreenCode = "ACC001",
                ScreenName = "Login",
                ModuleName = "Account",
                EntityName = "User",
                EntityId = user.Id,
                Action = AuditAction.Login,
                Description = $"User {user.EmployeeCode} ({user.UserName}) logged in successfully to Branch {branch?.Code}",
                Success = true,
                OverrideUserId = user.Id,
                OverrideUserName = user.UserName,
                OverrideEmployeeCode = user.EmployeeCode
            });

            return RedirectToAction("Index", "Home");
        }

        // ─── GET: Access Denied ──────────────────────────────────────────────────
        [HttpGet]
        public IActionResult AccessDenied(string? permission)
        {
            ViewData["Permission"] = permission;
            return View();
        }

        // ─── POST: SwitchCompany ──────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SwitchCompany(int companyId, string? returnUrl)
        {
            if (!User.Identity?.IsAuthenticated ?? true) return RedirectToAction("Login");

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "System Administrator");
            bool hasAccess = isAdmin || user.HasAllBranches ||
                await _context.UserBranches.AnyAsync(ub => ub.UserId == user.Id && ub.Branch!.CompanyId == companyId && ub.Branch.IsActive);

            if (!hasAccess)
            {
                TempData["ErrorMessage"] = "Access Denied: You are not authorized for this company.";
                return LocalRedirect(string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "~/" : returnUrl);
            }

            var company = await _context.Companies.FindAsync(companyId);
            if (company == null || !company.IsActive)
            {
                TempData["ErrorMessage"] = "Invalid or inactive company.";
                return LocalRedirect(string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "~/" : returnUrl);
            }

            // After switching company, reset to the first allowed branch within that company
            var fyId = User.FindFirst("FiscalYearId")?.Value ?? "0";
            var fyName = User.FindFirst("FiscalYearName")?.Value ?? "N/A";

            List<Branch> allowedBranches;
            if (isAdmin || user.HasAllBranches)
            {
                allowedBranches = await _context.Branches
                    .Where(b => b.IsActive && b.CompanyId == companyId)
                    .OrderBy(b => b.Code)
                    .ToListAsync();
            }
            else
            {
                allowedBranches = await _context.UserBranches
                    .Include(ub => ub.Branch)
                    .Where(ub => ub.UserId == user.Id && ub.Branch!.IsActive && ub.Branch.CompanyId == companyId)
                    .Select(ub => ub.Branch!)
                    .OrderBy(b => b.Code)
                    .ToListAsync();
            }

            var firstBranch = allowedBranches.FirstOrDefault();

            var allowedCompanyIds = (isAdmin || user.HasAllBranches)
                ? await _context.Companies.Where(c => c.IsActive).Select(c => c.Id.ToString()).ToListAsync()
                : await _context.UserBranches.Where(ub => ub.UserId == user.Id && ub.Branch!.IsActive).Select(ub => ub.Branch!.CompanyId.ToString()).Distinct().ToListAsync();

            var allowedBranchIds = allowedBranches.Select(b => b.Id.ToString()).ToList();

            var newClaims = new List<Claim>
            {
                new Claim("EmployeeCode", user.EmployeeCode ?? user.UserName ?? ""),
                new Claim("CompanyId", company.Id.ToString()),
                new Claim("CompanyName", company.LocalizedName),
                new Claim("BranchId", firstBranch?.Id.ToString() ?? "0"),
                new Claim("BranchName", firstBranch?.LocalizedName ?? "N/A"),
                new Claim("FiscalYearId", fyId),
                new Claim("FiscalYearName", fyName),
                new Claim("HasAllBranches", (isAdmin || user.HasAllBranches).ToString()),
            };

            foreach (var cid in allowedCompanyIds)
                newClaims.Add(new Claim("AllowedCompanyId", cid));
            foreach (var bid in allowedBranchIds)
                newClaims.Add(new Claim("AllowedBranchId", bid));

            await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, newClaims);
            TempData["SuccessMessage"] = $"Switched to company: {company.LocalizedName}";
            return LocalRedirect(string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "~/" : returnUrl);
        }

        // ─── POST: SwitchBranch ───────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SwitchBranch(int branchId, string? returnUrl)
        {
            if (!User.Identity?.IsAuthenticated ?? true) return RedirectToAction("Login");

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "System Administrator");
            var currentCompanyId = int.TryParse(User.FindFirst("CompanyId")?.Value, out var cid) ? cid : 0;

            // Branch must belong to the current company
            var branch = await _context.Branches
                .FirstOrDefaultAsync(b => b.Id == branchId && b.CompanyId == currentCompanyId && b.IsActive);

            if (branch == null)
            {
                TempData["ErrorMessage"] = "Access Denied: Branch not found or does not belong to your current company.";
                return LocalRedirect(string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "~/" : returnUrl);
            }

            bool hasAccess = isAdmin || user.HasAllBranches ||
                await _context.UserBranches.AnyAsync(ub => ub.UserId == user.Id && ub.BranchId == branchId);

            if (!hasAccess)
            {
                TempData["ErrorMessage"] = "Access Denied: You are not authorized for this branch.";
                return LocalRedirect(string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "~/" : returnUrl);
            }

            var companyName = User.FindFirst("CompanyName")?.Value ?? "";
            var fyId = User.FindFirst("FiscalYearId")?.Value ?? "0";
            var fyName = User.FindFirst("FiscalYearName")?.Value ?? "N/A";

            var allowedCompanyIds = (isAdmin || user.HasAllBranches)
                ? await _context.Companies.Where(c => c.IsActive).Select(c => c.Id.ToString()).ToListAsync()
                : await _context.UserBranches.Where(ub => ub.UserId == user.Id && ub.Branch!.IsActive).Select(ub => ub.Branch!.CompanyId.ToString()).Distinct().ToListAsync();

            var allowedBranchIds = (isAdmin || user.HasAllBranches)
                ? await _context.Branches.Where(b => b.IsActive && b.CompanyId == currentCompanyId).Select(b => b.Id.ToString()).ToListAsync()
                : await _context.UserBranches.Where(ub => ub.UserId == user.Id && ub.Branch!.CompanyId == currentCompanyId).Select(ub => ub.BranchId.ToString()).ToListAsync();

            var newClaims = new List<Claim>
            {
                new Claim("EmployeeCode", user.EmployeeCode ?? user.UserName ?? ""),
                new Claim("CompanyId", currentCompanyId.ToString()),
                new Claim("CompanyName", companyName),
                new Claim("BranchId", branch.Id.ToString()),
                new Claim("BranchName", branch.LocalizedName),
                new Claim("FiscalYearId", fyId),
                new Claim("FiscalYearName", fyName),
                new Claim("HasAllBranches", (isAdmin || user.HasAllBranches).ToString()),
            };

            foreach (var c2 in allowedCompanyIds)
                newClaims.Add(new Claim("AllowedCompanyId", c2));
            foreach (var bid in allowedBranchIds)
                newClaims.Add(new Claim("AllowedBranchId", bid));

            await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, newClaims);
            TempData["SuccessMessage"] = $"Branch switched to: {branch.LocalizedName}";
            return LocalRedirect(string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "~/" : returnUrl);
        }

        // ─── POST: SwitchFiscalYear ───────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SwitchFiscalYear(int fiscalYearId, string? returnUrl)
        {
            if (!User.Identity?.IsAuthenticated ?? true) return RedirectToAction("Login");

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            var fy = await _context.FiscalYears.FindAsync(fiscalYearId);
            if (fy == null || !fy.IsActive)
            {
                TempData["ErrorMessage"] = "Invalid or inactive fiscal year.";
                return LocalRedirect(string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "~/" : returnUrl);
            }

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "System Administrator");
            var currentCompanyId = int.TryParse(User.FindFirst("CompanyId")?.Value, out var cid) ? cid : 0;

            var allowedCompanyIds = (isAdmin || user.HasAllBranches)
                ? await _context.Companies.Where(c => c.IsActive).Select(c => c.Id.ToString()).ToListAsync()
                : await _context.UserBranches.Where(ub => ub.UserId == user.Id && ub.Branch!.IsActive).Select(ub => ub.Branch!.CompanyId.ToString()).Distinct().ToListAsync();

            var allowedBranchIds = (isAdmin || user.HasAllBranches)
                ? await _context.Branches.Where(b => b.IsActive && b.CompanyId == currentCompanyId).Select(b => b.Id.ToString()).ToListAsync()
                : await _context.UserBranches.Where(ub => ub.UserId == user.Id && ub.Branch!.CompanyId == currentCompanyId).Select(ub => ub.BranchId.ToString()).ToListAsync();

            var newClaims = new List<Claim>
            {
                new Claim("EmployeeCode", user.EmployeeCode ?? user.UserName ?? ""),
                new Claim("CompanyId", User.FindFirst("CompanyId")?.Value ?? "0"),
                new Claim("CompanyName", User.FindFirst("CompanyName")?.Value ?? ""),
                new Claim("BranchId", User.FindFirst("BranchId")?.Value ?? "0"),
                new Claim("BranchName", User.FindFirst("BranchName")?.Value ?? "N/A"),
                new Claim("FiscalYearId", fy.Id.ToString()),
                new Claim("FiscalYearName", fy.LocalizedName),
                new Claim("HasAllBranches", (isAdmin || user.HasAllBranches).ToString()),
            };

            foreach (var c2 in allowedCompanyIds)
                newClaims.Add(new Claim("AllowedCompanyId", c2));
            foreach (var bid in allowedBranchIds)
                newClaims.Add(new Claim("AllowedBranchId", bid));

            await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, newClaims);
            TempData["SuccessMessage"] = $"Fiscal Year switched to: {fy.LocalizedName}";
            return LocalRedirect(string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "~/" : returnUrl);
        }

        // ─── GET / POST: SetLanguage ──────────────────────────────────────────────
        [HttpGet]
        [HttpPost]
        [AllowAnonymous]
        public IActionResult SetLanguage(string culture, string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(culture)) culture = "ar-EG";

            Response.Cookies.Append(
                Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
                Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(
                    new Microsoft.AspNetCore.Localization.RequestCulture(culture)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Path = "/"
                });

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }
            return RedirectToAction("Index", "Home");
        }

        // ─── POST: Logout ─────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var currentEmpCode = User.FindFirst("EmployeeCode")?.Value;
            var currentUserName = User.Identity?.Name;

            await _audit.LogAsync(new AuditLogEntry
            {
                ScreenCode = "ACC001",
                ScreenName = "Logout",
                ModuleName = "Account",
                EntityName = "User",
                EntityId = currentUserId,
                Action = AuditAction.Logout,
                Description = $"User {currentEmpCode} ({currentUserName}) logged out",
                Success = true,
                OverrideUserId = currentUserId,
                OverrideUserName = currentUserName,
                OverrideEmployeeCode = currentEmpCode
            });

            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }
    }
}
