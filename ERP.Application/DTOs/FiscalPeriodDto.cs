using System;

namespace ERP.Application.DTOs
{
    public class FiscalPeriodDto
    {
        public int Id { get; set; }
        public int FiscalYearId { get; set; }
        public string FiscalYearName { get; set; } = string.Empty;
        public int PeriodNumber { get; set; }
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string LocalizedName { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsClosed { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? ClosedByUserId { get; set; }
    }
}
