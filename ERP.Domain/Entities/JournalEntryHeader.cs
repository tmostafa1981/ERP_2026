using System;
using System.Collections.Generic;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class JournalEntryHeader : IAuditableEntity, IMustHaveBranch, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public int BranchId { get; set; }
        public int FiscalYearId { get; set; }
        public string EntryNumber { get; set; } = string.Empty;
        public DateTime EntryDate { get; set; } = DateTime.UtcNow.Date;
        public string Description { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public JournalEntryStatus Status { get; set; } = JournalEntryStatus.Draft;
        public string? ReferenceNumber { get; set; }

        public SourceDocumentType SourceDocumentType { get; set; } = SourceDocumentType.ManualJV;
        public int? SourceDocumentId { get; set; }

        public decimal TotalDebitLocal { get; set; }
        public decimal TotalCreditLocal { get; set; }

        public DateTime? PostedAt { get; set; }
        public string? PostedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Branch? Branch { get; set; }
        public FiscalYear? FiscalYear { get; set; }
        public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
    }
}
