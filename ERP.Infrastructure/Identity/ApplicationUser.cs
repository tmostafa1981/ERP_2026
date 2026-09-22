using Microsoft.AspNetCore.Identity;

namespace ERP.Infrastructure.Identity
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
        public string FullNameAr { get; set; } = string.Empty;
        public string FullNameEn { get; set; } = string.Empty;
        public string Mobile { get; set; } = string.Empty;

        /// <summary>Employee code used as the login identifier on the login screen.</summary>
        public string EmployeeCode { get; set; } = string.Empty;

        /// <summary>If true, user has access to ALL branches without individual UserBranch records.</summary>
        public bool HasAllBranches { get; set; } = false;

        public bool IsActive { get; set; } = true;
        public bool PasswordResetRequired { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }

        public string LocalizedFullName =>
            System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
                ? (!string.IsNullOrWhiteSpace(FullNameAr) ? FullNameAr : (!string.IsNullOrWhiteSpace(FullName) ? FullName : FullNameEn))
                : (!string.IsNullOrWhiteSpace(FullNameEn) ? FullNameEn : (!string.IsNullOrWhiteSpace(FullName) ? FullName : FullNameAr));
    }
}
