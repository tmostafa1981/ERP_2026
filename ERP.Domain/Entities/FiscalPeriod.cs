using System;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class FiscalPeriod : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public int FiscalYearId { get; set; }
        public FiscalYear? FiscalYear { get; set; }

        public int PeriodNumber { get; set; } // 1..12
        public string NameAr { get; set; } = string.Empty; // e.g., "يناير 2026"
        public string NameEn { get; set; } = string.Empty; // e.g., "January 2026"

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsClosed { get; set; } = false;
        public DateTime? ClosedAt { get; set; }
        public string? ClosedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar")
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : NameAr);
    }
}
