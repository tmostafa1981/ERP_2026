using System;
using System.Collections.Generic;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class AssetDepreciationRun : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public string RunNumber { get; set; } = string.Empty;
        public DateTime RunDate { get; set; } = DateTime.Today;

        public int CompanyId { get; set; }
        public int? BranchId { get; set; } // null = All authorized branches
        public int FiscalYearId { get; set; }
        public int FiscalPeriodId { get; set; }

        public int? AssetCategoryId { get; set; } // null = All categories
        public decimal TotalDepreciationAmount { get; set; }
        public int AssetsCount { get; set; }

        public DepreciationRunStatus Status { get; set; } = DepreciationRunStatus.Draft;

        // Journal Entry References
        public int? JournalEntryId { get; set; }
        public int? ReversalJournalEntryId { get; set; }
        public DateTime? ReversedAt { get; set; }
        public string? ReversedByUserId { get; set; }

        public string? Notes { get; set; }
        public string? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public DateTime? ApprovedAt { get; set; }
        public string? ApprovedByUserId { get; set; }

        // Navigations
        public Company? Company { get; set; }
        public Branch? Branch { get; set; }
        public FiscalYear? FiscalYear { get; set; }
        public FiscalPeriod? FiscalPeriod { get; set; }
        public AssetCategory? AssetCategory { get; set; }
        public JournalEntryHeader? JournalEntry { get; set; }
        public JournalEntryHeader? ReversalJournalEntry { get; set; }

        public ICollection<AssetDepreciationHistory> Histories { get; set; } = new List<AssetDepreciationHistory>();
    }
}
