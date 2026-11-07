using System;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class FiscalYear : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public string Year { get; set; } = string.Empty; // e.g., "2026"
        public string Name { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public string? NameEn { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsClosed { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public ICollection<FiscalPeriod> Periods { get; set; } = new List<FiscalPeriod>();

        public string LocalizedName => 
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar") 
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : Name)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : Name);
    }
}
