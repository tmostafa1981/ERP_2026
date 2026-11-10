using System;
using System.Collections.Generic;
using ERP.Domain.Enums;

namespace ERP.Application.DTOs.InventoryCosting
{
    public class ItemWarehouseCostDto
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string? BranchName { get; set; }
        public int WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public int ItemId { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public decimal CurrentQuantity { get; set; }
        public decimal TotalCost { get; set; }
        public decimal MovingAverageCost { get; set; }
        public DateTime LastUpdatedDate { get; set; }
        public int? LastDocumentId { get; set; }
    }

    public class ItemCostHistoryDto
    {
        public long Id { get; set; }
        public int BranchId { get; set; }
        public string? BranchName { get; set; }
        public int WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public int ItemId { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public int DocumentHeaderId { get; set; }
        public string? DocumentNumber { get; set; }
        public CostMovementType MovementType { get; set; }
        public string MovementTypeName { get; set; } = string.Empty;
        public DateTime MovementDate { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal TotalCost { get; set; }
        public decimal PreQuantity { get; set; }
        public decimal PreTotalCost { get; set; }
        public decimal PreAverageCost { get; set; }
        public decimal PostQuantity { get; set; }
        public decimal PostTotalCost { get; set; }
        public decimal PostAverageCost { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CostAdjustmentRecordDto
    {
        public long Id { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public int BranchId { get; set; }
        public string? BranchName { get; set; }
        public int WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public int ItemId { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public int SourceDocumentHeaderId { get; set; }
        public string? SourceDocumentNumber { get; set; }
        public int? AffectedDocumentHeaderId { get; set; }
        public string? AffectedDocumentNumber { get; set; }
        public CostMovementType AffectedMovementType { get; set; }
        public DateTime MovementDate { get; set; }
        public decimal OriginalUnitCost { get; set; }
        public decimal RecalculatedUnitCost { get; set; }
        public decimal OriginalTotalCost { get; set; }
        public decimal RecalculatedTotalCost { get; set; }
        public decimal CostDifference { get; set; }
        public int? AdjustmentJournalEntryId { get; set; }
        public string? AdjustmentJournalEntryNumber { get; set; }
        public DateTime EffectiveDate { get; set; }
        public bool IsClosedPeriod { get; set; }
        public string? Reason { get; set; }
        public string? CreatedByUserName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CostRecalculationSummaryDto
    {
        public string BatchNumber { get; set; } = string.Empty;
        public int SourcePurchaseInvoiceId { get; set; }
        public string SourcePurchaseInvoiceNumber { get; set; } = string.Empty;
        public int AffectedItemsCount { get; set; }
        public int AffectedSalesInvoicesCount { get; set; }
        public int AffectedTransfersCount { get; set; }
        public decimal TotalInventoryValueDifference { get; set; }
        public decimal TotalCogsDifference { get; set; }
        public int? OpenPeriodJournalEntryId { get; set; }
        public string? OpenPeriodJournalEntryNumber { get; set; }
        public int? ClosedPeriodAdjustmentJournalEntryId { get; set; }
        public string? ClosedPeriodAdjustmentJournalEntryNumber { get; set; }
        public List<CostAdjustmentRecordDto> Records { get; set; } = new();
    }
}
