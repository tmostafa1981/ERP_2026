using System;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// سجل تدقيق ومراجعة عمليات إعادة احتساب التكلفة الناتجة عن تعديل المشتريات أو التسويات المحاسبية.
    /// </summary>
    public class CostAdjustmentRecord : IMustHaveBranch
    {
        public long Id { get; set; }

        public string BatchNumber { get; set; } = string.Empty;
        public int BranchId { get; set; }
        public int WarehouseId { get; set; }
        public int ItemId { get; set; }

        public int SourceDocumentHeaderId { get; set; }
        public int? AffectedDocumentHeaderId { get; set; }
        public int? AffectedDocumentLineId { get; set; }

        public CostMovementType AffectedMovementType { get; set; }
        public DateTime MovementDate { get; set; }

        public decimal OriginalUnitCost { get; set; } = 0m;
        public decimal RecalculatedUnitCost { get; set; } = 0m;
        public decimal OriginalTotalCost { get; set; } = 0m;
        public decimal RecalculatedTotalCost { get; set; } = 0m;
        public decimal CostDifference { get; set; } = 0m;

        public int? AdjustmentJournalEntryId { get; set; }
        public DateTime EffectiveDate { get; set; }
        public bool IsClosedPeriod { get; set; } = false;

        public string? Reason { get; set; }
        public string? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigations
        public Branch? Branch { get; set; }
        public Warehouse? Warehouse { get; set; }
        public Item? Item { get; set; }
        public DocumentHeader? SourceDocumentHeader { get; set; }
        public DocumentHeader? AffectedDocumentHeader { get; set; }
        public DocumentLine? AffectedDocumentLine { get; set; }
        public JournalEntryHeader? AdjustmentJournalEntry { get; set; }
    }
}
