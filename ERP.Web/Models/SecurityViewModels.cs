using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.Web.Models
{
    // ─── User Management ViewModels ───────────────────────────────────────────
    public class UserListViewModel
    {
        public List<UserItemViewModel> Users { get; set; } = new List<UserItemViewModel>();
        public string SearchTerm { get; set; } = string.Empty;
        public string StatusFilter { get; set; } = string.Empty; // "All", "Active", "Inactive", "Locked"
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalItems { get; set; } = 0;
        public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);
        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;
    }

    public class UserItemViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string EmployeeCode { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FullNameAr { get; set; } = string.Empty;
        public string FullNameEn { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Mobile { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsLockedOut { get; set; }
        public bool HasAllBranches { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
        public DateTime CreatedAt { get; set; }

        public string LocalizedFullName =>
            System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
                ? (!string.IsNullOrWhiteSpace(FullNameAr) ? FullNameAr : (!string.IsNullOrWhiteSpace(FullNameEn) ? FullNameEn : UserName))
                : (!string.IsNullOrWhiteSpace(FullNameEn) ? FullNameEn : (!string.IsNullOrWhiteSpace(FullNameAr) ? FullNameAr : UserName));
    }

    public class UserCreateEditViewModel
    {
        public string? Id { get; set; }

        [Required(ErrorMessage = "EmployeeCodeRequired")]
        [Display(Name = "EmployeeCode")]
        public string EmployeeCode { get; set; } = string.Empty;

        [Display(Name = "Username")]
        public string? UserName { get; set; }

        [Required(ErrorMessage = "FullNameArRequired")]
        [Display(Name = "FullNameAr")]
        public string FullNameAr { get; set; } = string.Empty;

        [Required(ErrorMessage = "FullNameEnRequired")]
        [Display(Name = "FullNameEn")]
        public string FullNameEn { get; set; } = string.Empty;

        [EmailAddress]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [Display(Name = "Mobile")]
        public string? Mobile { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string? Password { get; set; }

        [Display(Name = "IsActive")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "LockoutEnabled")]
        public bool LockoutEnabled { get; set; } = true;

        [Display(Name = "PasswordResetRequired")]
        public bool PasswordResetRequired { get; set; } = false;

        [Display(Name = "HasAllBranches")]
        public bool HasAllBranches { get; set; } = false;

        public List<string> SelectedRoleIds { get; set; } = new List<string>();
    }

    public class ResetPasswordViewModel
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        public string EmployeeCode { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        public bool RequirePasswordResetOnNextLogin { get; set; } = true;
    }

    // ─── Role Management ViewModels ───────────────────────────────────────────
    public class RoleItemViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int UserCount { get; set; }
        public int GrantedPermissionCount { get; set; }
        public DateTime CreatedAt { get; set; }

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : (Name ?? string.Empty))
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : (Name ?? string.Empty));
    }

    public class RoleCreateEditViewModel
    {
        public string? Id { get; set; }

        [Required(ErrorMessage = "RoleNameRequired")]
        [Display(Name = "RoleName")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "NameAr")]
        public string NameAr { get; set; } = string.Empty;

        [Display(Name = "NameEn")]
        public string NameEn { get; set; } = string.Empty;

        [Display(Name = "Description")]
        public string Description { get; set; } = string.Empty;

        [Display(Name = "IsActive")]
        public bool IsActive { get; set; } = true;
    }

    // ─── Permission Matrix ViewModels ────────────────────────────────────────
    public class RolePermissionsMatrixViewModel
    {
        public string RoleId { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string RoleDescription { get; set; } = string.Empty;

        public List<PermissionModuleGroupViewModel> Modules { get; set; } = new List<PermissionModuleGroupViewModel>();
        public List<int> SelectedPermissionIds { get; set; } = new List<int>();
    }

    public class PermissionModuleGroupViewModel
    {
        public string ModuleName { get; set; } = string.Empty;
        public List<PermissionScreenGroupViewModel> Screens { get; set; } = new List<PermissionScreenGroupViewModel>();
    }

    public class PermissionScreenGroupViewModel
    {
        public string ScreenName { get; set; } = string.Empty;
        public List<PermissionActionItemViewModel> Actions { get; set; } = new List<PermissionActionItemViewModel>();
    }

    public class PermissionActionItemViewModel
    {
        public int PermissionId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public bool IsGranted { get; set; }
    }
}
