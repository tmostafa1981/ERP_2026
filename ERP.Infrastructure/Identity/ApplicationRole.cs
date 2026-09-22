using System;
using Microsoft.AspNetCore.Identity;

namespace ERP.Infrastructure.Identity
{
    public class ApplicationRole : IdentityRole
    {
        public ApplicationRole() : base() { }

        public ApplicationRole(string roleName) : base(roleName) { }

        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : (Name ?? string.Empty))
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : (Name ?? string.Empty));
    }
}
