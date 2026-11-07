using System;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// دليل البنوك التجارية (Commercial Banks Master)
    /// يُستخدم في محرك البحث التفاعلي واختيار البنك المسحوب عليه الشيك في السندات
    /// </summary>
    public class Bank : IAuditableEntity
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? SwiftCode { get; set; }
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar") ? NameAr : NameEn;
    }
}
