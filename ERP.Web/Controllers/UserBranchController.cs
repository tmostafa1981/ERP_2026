using ERP.Domain.Entities;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Identity;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class UserBranchController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code = "UBR001";
        private const string SC_Name = "UserBranchAssignment";
        private const string SC_Module = "Administration";

        public UserBranchController(
            ErpDbContext context,
            UserManager<ApplicationUser> userManager,
            ICurrentUserContext currentUser,
            IAuditService audit)
        {
            _context = context;
            _userManager = userManager;
            _currentUser = currentUser;
            _audit = audit;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? userId, int? companyIdFilter)
        {
            var users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
            ViewBag.Users = users;

            if (string.IsNullOrEmpty(userId) && users.Any())
                userId = users.First().Id;

            var selectedUser = users.FirstOrDefault(u => u.Id == userId);

            // Companies available
            var companies = _currentUser.HasAllBranches
                ? await _context.Companies.Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync()
                : await _context.Companies.Where(c => c.IsActive && c.Id == _currentUser.CompanyId).OrderBy(c => c.Code).ToListAsync();

            ViewBag.Companies = companies;

            // Resolve effective company filter
            int effectiveCompanyId = companyIdFilter ?? 0;
            ViewBag.CompanyIdFilter = effectiveCompanyId;

            var branches = await _context.Branches
                .Include(b => b.Company)
                .Where(b => b.IsActive && (effectiveCompanyId == 0 || b.CompanyId == effectiveCompanyId))
                .OrderBy(b => b.Company != null ? b.Company.Code : string.Empty).ThenBy(b => b.Code)
                .ToListAsync();

            List<int> assignedBranchIds = new();
            if (selectedUser != null)
            {
                assignedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == selectedUser.Id)
                    .Select(ub => ub.BranchId)
                    .ToListAsync();
            }

            ViewBag.SelectedUser = selectedUser;
            ViewBag.AssignedBranchIds = assignedBranchIds;
            ViewBag.AllBranches = branches;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAssignment(
            string userId, bool hasAllBranches, List<int> selectedBranchIds, int? companyIdFilter)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            var oldHasAllBranches = user.HasAllBranches;
            var oldBranchIds = await _context.UserBranches
                .Where(ub => ub.UserId == userId)
                .Select(ub => ub.BranchId)
                .ToListAsync();

            user.HasAllBranches = hasAllBranches;
            await _userManager.UpdateAsync(user);

            // Clear existing assignments for the filtered company only (if a company filter is set)
            if (companyIdFilter.HasValue && companyIdFilter > 0)
            {
                var existingForCompany = await _context.UserBranches
                    .Where(ub => ub.UserId == userId && ub.Branch.CompanyId == companyIdFilter.Value)
                    .ToListAsync();
                _context.UserBranches.RemoveRange(existingForCompany);
            }
            else
            {
                // Clear all if no company filter
                var existing = await _context.UserBranches.Where(ub => ub.UserId == userId).ToListAsync();
                _context.UserBranches.RemoveRange(existing);
            }

            if (!hasAllBranches && selectedBranchIds != null && selectedBranchIds.Any())
            {
                foreach (var branchId in selectedBranchIds)
                {
                    _context.UserBranches.Add(new UserBranch { UserId = userId, BranchId = branchId });
                }
            }

            await _context.SaveChangesAsync();

            // Audit User Branch Assignment
            await _audit.LogUpdateAsync(
                SC_Code, SC_Name, SC_Module,
                entityName: "UserBranch",
                entityId: userId,
                oldValues: new { HasAllBranches = oldHasAllBranches, AssignedBranchIds = oldBranchIds },
                newValues: new { HasAllBranches = hasAllBranches, AssignedBranchIds = selectedBranchIds ?? new List<int>() },
                description: $"Updated branch assignments for user {user.EmployeeCode} ({user.UserName})");

            TempData["SuccessMessage"] = "User branch assignments saved successfully.";
            return RedirectToAction(nameof(Index), new { userId, companyIdFilter });
        }
    }
}
