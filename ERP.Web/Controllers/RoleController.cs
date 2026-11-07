using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
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
    public class RoleController : Controller
    {
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _userContext;
        private readonly IAuditService _audit;

        // ─── Screen constants ─────────────────────────────────────────────────────
        private const string SC_Code   = "ROL001";
        private const string SC_Name   = "Roles";
        private const string SC_Module = "Administration";

        public RoleController(
            RoleManager<ApplicationRole> roleManager,
            UserManager<ApplicationUser> userManager,
            ErpDbContext context,
            ICurrentUserContext userContext,
            IAuditService audit)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _context     = context;
            _userContext = userContext;
            _audit       = audit;
        }

        // ─── GET: Role List ───────────────────────────────────────────────────────
        [HasPermission("Administration.Roles.View")]
        public async Task<IActionResult> Index()
        {
            var roles = await _roleManager.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
            var roleIds = roles.Select(r => r.Id).ToList();

            var userRoleCounts = await _context.UserRoles
                .AsNoTracking()
                .Where(ur => roleIds.Contains(ur.RoleId))
                .GroupBy(ur => ur.RoleId)
                .Select(g => new { RoleId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RoleId, x => x.Count);

            var permCounts = await _context.RolePermissions
                .AsNoTracking()
                .Where(rp => roleIds.Contains(rp.RoleId) && rp.IsGranted)
                .GroupBy(rp => rp.RoleId)
                .Select(g => new { RoleId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RoleId, x => x.Count);

            var roleItems = roles.Select(r => new RoleItemViewModel
            {
                Id = r.Id,
                Name = r.Name ?? string.Empty,
                NameAr = r.NameAr,
                NameEn = r.NameEn,
                Description = r.Description,
                IsActive = r.IsActive,
                UserCount = userRoleCounts.TryGetValue(r.Id, out var uc) ? uc : 0,
                GrantedPermissionCount = permCounts.TryGetValue(r.Id, out var pc) ? pc : 0,
                CreatedAt = r.CreatedAt
            }).ToList();

            return View(roleItems);
        }

        // ─── POST: Create Role ───────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Administration.Roles.Add")]
        public async Task<IActionResult> Create(RoleCreateEditViewModel model)
        {
            if (ModelState.IsValid)
            {
                var roleExists = await _roleManager.RoleExistsAsync(model.Name.Trim());
                if (roleExists)
                {
                    TempData["ErrorMessage"] = $"Role name '{model.Name}' already exists.";
                    return RedirectToAction(nameof(Index));
                }

                var role = new ApplicationRole(model.Name.Trim())
                {
                    NameAr = model.NameAr?.Trim() ?? model.Name.Trim(),
                    NameEn = model.NameEn?.Trim() ?? model.Name.Trim(),
                    Description = model.Description?.Trim() ?? string.Empty,
                    IsActive = model.IsActive,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _userContext.Username
                };

                var result = await _roleManager.CreateAsync(role);
                if (result.Succeeded)
                {
                    // ── Audit: Create ─────────────────────────────────────────────
                    await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                        entityName: "Role",
                        entityId:   role.Id,
                        newValues: new {
                            role.Id, role.Name, role.NameAr, role.NameEn,
                            role.Description, role.IsActive
                        });

                    TempData["SuccessMessage"] = $"Role '{role.Name}' created successfully.";
                    return RedirectToAction(nameof(Index));
                }

                TempData["ErrorMessage"] = string.Join(" | ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Index));
        }

        // ─── POST: Edit Role ─────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Administration.Roles.Edit")]
        public async Task<IActionResult> Edit(RoleCreateEditViewModel model)
        {
            if (string.IsNullOrEmpty(model.Id)) return NotFound();

            var role = await _roleManager.FindByIdAsync(model.Id);
            if (role == null) return NotFound();

            if (role.Name != model.Name)
            {
                var exists = await _roleManager.RoleExistsAsync(model.Name);
                if (exists)
                {
                    TempData["ErrorMessage"] = $"Role name '{model.Name}' is already taken.";
                    return RedirectToAction(nameof(Index));
                }
            }

            // ── Capture OldValues BEFORE any changes ──────────────────────────────
            var oldValues = new {
                role.Id, role.Name, role.NameAr, role.NameEn,
                role.Description, role.IsActive
            };

            role.Name        = model.Name.Trim();
            role.NameAr      = model.NameAr?.Trim() ?? model.Name;
            role.NameEn      = model.NameEn?.Trim() ?? model.Name;
            role.Description = model.Description?.Trim() ?? string.Empty;
            role.IsActive    = model.IsActive;
            role.UpdatedAt   = DateTime.UtcNow;
            role.UpdatedBy   = _userContext.Username;

            var result = await _roleManager.UpdateAsync(role);
            if (result.Succeeded)
            {
                // ── Audit: Update ─────────────────────────────────────────────────
                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Role",
                    entityId:   role.Id,
                    oldValues:  oldValues,
                    newValues: new {
                        role.Id, role.Name, role.NameAr, role.NameEn,
                        role.Description, role.IsActive
                    });

                TempData["SuccessMessage"] = $"Role '{role.Name}' updated successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = string.Join(" | ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Index));
        }

        // ─── POST: Toggle Active ─────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Administration.Roles.Edit")]
        public async Task<IActionResult> ToggleActive(string roleId)
        {
            var role = await _roleManager.FindByIdAsync(roleId);
            if (role == null) return NotFound();

            role.IsActive = !role.IsActive;
            role.UpdatedAt = DateTime.UtcNow;
            role.UpdatedBy = _userContext.Username;

            await _roleManager.UpdateAsync(role);
            TempData["SuccessMessage"] = role.IsActive ? $"Role '{role.Name}' activated." : $"Role '{role.Name}' deactivated.";
            return RedirectToAction(nameof(Index));
        }

        // ─── GET: Permissions Matrix ─────────────────────────────────────────────
        [HasPermission("Administration.Permissions.View")]
        public async Task<IActionResult> Permissions(string roleId)
        {
            if (string.IsNullOrEmpty(roleId)) return NotFound();

            var role = await _roleManager.FindByIdAsync(roleId);
            if (role == null) return NotFound();

            var allPermissions = await _context.Permissions
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.Module)
                .ThenBy(p => p.Screen)
                .ToListAsync();

            var grantedPermissionIds = await _context.RolePermissions
                .Where(rp => rp.RoleId == roleId && rp.IsGranted)
                .Select(rp => rp.PermissionId)
                .ToListAsync();

            // Group by Module -> Screen
            var modules = allPermissions
                .GroupBy(p => p.Module)
                .Select(modGrp => new PermissionModuleGroupViewModel
                {
                    ModuleName = modGrp.Key,
                    Screens = modGrp
                        .GroupBy(s => s.Screen)
                        .Select(scrGrp => new PermissionScreenGroupViewModel
                        {
                            ScreenName = scrGrp.Key,
                            ScreenTitleAr = scrGrp.FirstOrDefault(a => !string.IsNullOrEmpty(a.Description))?.Description,
                            ScreenTitleEn = scrGrp.FirstOrDefault(a => !string.IsNullOrEmpty(a.Description))?.Description,
                            Actions = scrGrp.Select(a => new PermissionActionItemViewModel
                            {
                                PermissionId = a.Id,
                                Code = a.Code,
                                Action = a.Action,
                                NameAr = a.NameAr,
                                NameEn = a.NameEn,
                                IsGranted = grantedPermissionIds.Contains(a.Id)
                            }).ToList()
                        }).ToList()
                }).ToList();

            var vm = new RolePermissionsMatrixViewModel
            {
                RoleId = role.Id,
                RoleName = role.LocalizedName,
                RoleDescription = role.Description,
                Modules = modules,
                SelectedPermissionIds = grantedPermissionIds
            };

            return View(vm);
        }

        // ─── POST: Save Permissions Matrix ───────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Administration.Permissions.Edit")]
        public async Task<IActionResult> SavePermissions(string roleId, List<int> selectedPermissionIds)
        {
            if (string.IsNullOrEmpty(roleId)) return NotFound();

            var role = await _roleManager.FindByIdAsync(roleId);
            if (role == null) return NotFound();

            // Remove current role permissions
            var existingPermissions = await _context.RolePermissions
                .Where(rp => rp.RoleId == roleId)
                .ToListAsync();

            _context.RolePermissions.RemoveRange(existingPermissions);

            // Add newly selected permissions
            if (selectedPermissionIds != null && selectedPermissionIds.Any())
            {
                foreach (var permId in selectedPermissionIds.Distinct())
                {
                    _context.RolePermissions.Add(new RolePermission
                    {
                        RoleId = roleId,
                        PermissionId = permId,
                        IsGranted = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Permissions updated for role '{role.Name}' successfully.";
            return RedirectToAction(nameof(Permissions), new { roleId });
        }
    }
}
