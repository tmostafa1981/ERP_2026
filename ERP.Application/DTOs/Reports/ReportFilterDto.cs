using System;

namespace ERP.Application.DTOs.Reports
{
    public enum ReportDisplayMode
    {
        Both = 0,
        LocalOnly = 1,
        ForeignOnly = 2
    }

    public class ReportFilterDto
    {
        public int? FiscalYearId { get; set; }
        public int? BranchId { get; set; }
        public int? CurrencyId { get; set; }
        public int? AccountId { get; set; }
        public int? CustomerId { get; set; }
        public int? VendorId { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public ReportDisplayMode Mode { get; set; } = ReportDisplayMode.Both;
        public bool IncludeDrafts { get; set; } = true;
        public int? AccountLevel { get; set; }
        public bool OnlyWithTransactions { get; set; } = false;
    }
}
