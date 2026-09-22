using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Identity;
using ERP.Web.Authorization;
using ERP.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class UserController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _userContext;
        private readonly IAuditService _audit;

        // ─── Screen constants ─────────────────────────────────────────────────────
        private const string SC_Code   = "USR001";
        private const string SC_Name   = "Users";
        private const string SC_Module = "Administration";

        public UserController(
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager,
            ErpDbContext context,
            ICurrentUserContext userContext,
            IAuditService audit)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context     = context;
            _userContext = userContext;
            _audit       = audit;
        }

        // ─── GET: User List (Paginated, Searchable, Filterable) ───────────────────
        [HasPermission("Administration.Users.View")]
        public async Task<IActionResult> Index(string searchTerm = "", string statusFilter = "All", int pageNumber = 1, int pageSize = 10)
        {
            var query = _userManager.Users.AsQueryable();

            // Search filter
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(u =>
                    u.EmployeeCode.ToLower().Contains(term) ||
                    u.UserName!.ToLower().Contains(term) ||
                    (u.Email != null && u.Email.ToLower().Contains(term)) ||
                    (u.FullNameAr != null && u.FullNameAr.ToLower().Contains(term)) ||
                    (u.FullNameEn != null && u.FullNameEn.ToLower().Contains(term)) ||
                    (u.Mobile != null && u.Mobile.ToLower().Contains(term)));
            }

            // Status filter
            if (statusFilter == "Active") query = query.Where(u => u.IsActive);
            else if (statusFilter == "Inactive") query = query.Where(u => !u.IsActive);
            else if (statusFilter == "Locked") query = query.Where(u => u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow);

            var totalItems = await query.CountAsync();
            pageNumber = Math.Max(1, pageNumber);

            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userIds = users.Select(u => u.Id).ToList();
            var userRolesQuery = await (from ur in _context.UserRoles.AsNoTracking()
                                        join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.Id
                                        where userIds.Contains(ur.UserId)
                                        select new { ur.UserId, RoleName = r.Name })
                                        .ToListAsync();

            var userRolesMap = userRolesQuery
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName ?? string.Empty).ToList());

            var userItems = new List<UserItemViewModel>();
            foreach (var u in users)
            {
                var roles = userRolesMap.TryGetValue(u.Id, out var rList) ? rList : new List<string>();
                var isLocked = u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow;

                userItems.Add(new UserItemViewModel
                {
                    Id = u.Id,
                    EmployeeCode = u.EmployeeCode,
                    UserName = u.UserName ?? string.Empty,
                    FullNameAr = u.FullNameAr,
                    FullNameEn = u.FullNameEn,
                    Email = u.Email ?? string.Empty,
                    Mobile = u.Mobile,
                    IsLockedOut = isLocked,
                    HasAllBranches = u.HasAllBranches,
                    Roles = roles,
                    CreatedAt = u.CreatedAt
                });
            }

            ViewBag.AllRoles = await _roleManager.Roles.Where(r => r.IsActive).ToListAsync();

            var vm = new UserListViewModel
            {
                Users = userItems,
                SearchTerm = searchTerm,
                StatusFilter = statusFilter,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            return View(vm);
        }

        // ─── POST: Create User ───────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Administration.Users.Add")]
        public async Task<IActionResult> Create(UserCreateEditViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Validate EmployeeCode uniqueness
                var existingEmpCode = await _userManager.Users.FirstOrDefaultAsync(u => u.EmployeeCode == model.EmployeeCode);
                if (existingEmpCode != null)
                {
                    TempData["ErrorMessage"] = $"Employee Code '{model.EmployeeCode}' is already registered to another user.";
                    return RedirectToAction(nameof(Index));
                }

                var username = !string.IsNullOrWhiteSpace(model.UserName) ? model.UserName.Trim() : model.EmployeeCode.Trim();

                var user = new ApplicationUser
                {
                    UserName = username,
                    EmployeeCode = model.EmployeeCode.Trim(),
                    FullNameAr = model.FullNameAr?.Trim() ?? string.Empty,
                    FullNameEn = model.FullNameEn?.Trim() ?? string.Empty,
                    FullName = model.FullNameAr ?? model.FullNameEn ?? username,
                    Email = model.Email?.Trim(),
                    Mobile = model.Mobile?.Trim() ?? string.Empty,
                    PhoneNumber = model.Mobile?.Trim(),
                    IsActive = model.IsActive,
                    LockoutEnabled = model.LockoutEnabled,
                    PasswordResetRequired = model.PasswordResetRequired,
                    HasAllBranches = model.HasAllBranches,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _userContext.Username
                };

                var pwd = !string.IsNullOrWhiteSpace(model.Password) ? model.Password : "User@123";
                var result = await _userManager.CreateAsync(user, pwd);

                if (result.Succeeded)
                {
                    if (model.SelectedRoleIds != null && model.SelectedRoleIds.Any())
                    {
                        var roles = await _roleManager.Roles.Where(r => model.SelectedRoleIds.Contains(r.Id)).Select(r => r.Name!).ToListAsync();
                        await _userManager.AddToRolesAsync(user, roles);
                    }

                    // ── Audit: Create ─────────────────────────────────────────────
                    await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                        entityName: "User",
                        entityId:   user.Id,
                        newValues: new {
                            user.Id, user.EmployeeCode, user.UserName,
                            user.FullNameAr, user.FullNameEn,
                            user.Email, user.Mobile,
                            user.IsActive, user.HasAllBranches,
                            user.PasswordResetRequired
                        });

                    TempData["SuccessMessage"] = $"User '{user.EmployeeCode}' created successfully.";
                    return RedirectToAction(nameof(Index));
                }

                TempData["ErrorMessage"] = string.Join(" | ", result.Errors.Select(e => e.Description));
            }
            else
            {
                TempData["ErrorMessage"] = "Invalid data submitted.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ─── POST: Edit User ─────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Administration.Users.Edit")]
        public async Task<IActionResult> Edit(UserCreateEditViewModel model)
        {
            if (string.IsNullOrEmpty(model.Id)) return NotFound();

            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null) return NotFound();

            // Check EmployeeCode uniqueness if changed
            if (user.EmployeeCode != model.EmployeeCode)
            {
                var codeExists = await _userManager.Users.AnyAsync(u => u.EmployeeCode == model.EmployeeCode && u.Id != user.Id);
                if (codeExists)
                {
                    TempData["ErrorMessage"] = $"Employee Code '{model.EmployeeCode}' is already taken.";
                    return RedirectToAction(nameof(Index));
                }
            }

            // ── Capture OldValues BEFORE any changes ──────────────────────────────
            var oldValues = new {
                user.Id, user.EmployeeCode, user.UserName,
                user.FullNameAr, user.FullNameEn,
                user.Email, user.Mobile,
                user.IsActive, user.HasAllBranches,
                user.PasswordResetRequired
            };

            user.EmployeeCode         = model.EmployeeCode.Trim();
            user.FullNameAr           = model.FullNameAr?.Trim() ?? string.Empty;
            user.FullNameEn           = model.FullNameEn?.Trim() ?? string.Empty;
            user.FullName             = user.FullNameAr;
            user.Email                = model.Email?.Trim();
            user.Mobile               = model.Mobile?.Trim() ?? string.Empty;
            user.PhoneNumber          = model.Mobile?.Trim();
            user.IsActive             = model.IsActive;
            user.LockoutEnabled       = model.LockoutEnabled;
            user.PasswordResetRequired = model.PasswordResetRequired;
            user.HasAllBranches       = model.HasAllBranches;
            user.UpdatedAt            = DateTime.UtcNow;
            user.UpdatedBy            = _userContext.Username;

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                // Update roles
                var currentRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, currentRoles);

                if (model.SelectedRoleIds != null && model.SelectedRoleIds.Any())
                {
                    var newRoles = await _roleManager.Roles.Where(r => model.SelectedRoleIds.Contains(r.Id)).Select(r => r.Name!).ToListAsync();
                    await _userManager.AddToRolesAsync(user, newRoles);
                }

                // ── Audit: Update ─────────────────────────────────────────────────
                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "User",
                    entityId:   user.Id,
                    oldValues:  oldValues,
                    newValues: new {
                        user.Id, user.EmployeeCode, user.UserName,
                        user.FullNameAr, user.FullNameEn,
                        user.Email, user.Mobile,
                        user.IsActive, user.HasAllBranches,
                        user.PasswordResetRequired
                    });

                TempData["SuccessMessage"] = $"User '{user.EmployeeCode}' updated successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = string.Join(" | ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Index));
        }

        // ─── POST: Toggle Active / Inactive ─────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Administration.Users.Edit")]
        public async Task<IActionResult> ToggleActive(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            user.IsActive = !user.IsActive;
            user.UpdatedAt = DateTime.UtcNow;
            user.UpdatedBy = _userContext.Username;

            await _userManager.UpdateAsync(user);
            TempData["SuccessMessage"] = user.IsActive ? $"User '{user.EmployeeCode}' activated." : $"User '{user.EmployeeCode}' deactivated.";
            return RedirectToAction(nameof(Index));
        }

        // ─── POST: Toggle Lockout / Unlock ──────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Administration.Users.Edit")]
        public async Task<IActionResult> ToggleLockout(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            bool isCurrentlyLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;
            if (isCurrentlyLocked)
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                TempData["SuccessMessage"] = $"User '{user.EmployeeCode}' has been unlocked.";
            }
            else
            {
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                TempData["SuccessMessage"] = $"User '{user.EmployeeCode}' has been locked.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ─── POST: Reset Password ───────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Administration.Users.Edit")]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);

            if (result.Succeeded)
            {
                user.PasswordResetRequired = model.RequirePasswordResetOnNextLogin;
                await _userManager.UpdateAsync(user);
                TempData["SuccessMessage"] = $"Password for user '{user.EmployeeCode}' reset successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = string.Join(" | ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
