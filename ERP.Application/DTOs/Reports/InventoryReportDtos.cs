using System;
using System.Collections.Generic;

namespace ERP.Application.DTOs.Reports
{
    public class InventoryReportFilterDto
    {
        public int? BranchId { get; set; }
        public int? WarehouseId { get; set; }
        public int? ItemId { get; set; }
        public int? ItemGroupId { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int StagnantDays { get; set; } = 90;
        public string? SerialNumber { get; set; }
    }

    public class StockBalanceReportItemDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public decimal OnHandQuantity { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal AvailableQuantity => OnHandQuantity - ReservedQuantity;
        public decimal PurchasePrice { get; set; }
        public decimal EstimatedValue => OnHandQuantity * PurchasePrice;
    }

    public class StockBalanceReportDto
    {
        public InventoryReportFilterDto Filter { get; set; } = new();
        public List<StockBalanceReportItemDto> Items { get; set; } = new();
        public decimal TotalOnHandQuantity { get; set; }
        public decimal TotalAvailableQuantity { get; set; }
        public decimal TotalEstimatedValue { get; set; }
    }

    public class StockCardMovementDto
    {
        public DateTime Date { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public string TransactionTypeName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public string PartyName { get; set; } = string.Empty;
        public decimal InQuantity { get; set; }
        public decimal OutQuantity { get; set; }
        public decimal BalanceAfter { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public class ItemStockCardReportDto
    {
        public InventoryReportFilterDto Filter { get; set; } = new();
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public decimal OpeningBalance { get; set; }
        public decimal TotalIn { get; set; }
        public decimal TotalOut { get; set; }
        public decimal CurrentBalance => OpeningBalance + TotalIn - TotalOut;
        public List<StockCardMovementDto> Movements { get; set; } = new();
    }

    public class InwardOutwardReportItemDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public decimal OpeningBalance { get; set; }
        public decimal TotalInward { get; set; }
        public decimal TotalOutward { get; set; }
        public decimal NetMovement => TotalInward - TotalOutward;
        public decimal EndingBalance => OpeningBalance + TotalInward - TotalOutward;
    }

    public class InwardOutwardReportDto
    {
        public InventoryReportFilterDto Filter { get; set; } = new();
        public List<InwardOutwardReportItemDto> Items { get; set; } = new();
        public decimal GrandTotalInward { get; set; }
        public decimal GrandTotalOutward { get; set; }
    }

    public class WarehouseTransferItemDto
    {
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; }
        public string FromWarehouseName { get; set; } = string.Empty;
        public string ToWarehouseName { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class WarehouseTransfersReportDto
    {
        public InventoryReportFilterDto Filter { get; set; } = new();
        public List<WarehouseTransferItemDto> Items { get; set; } = new();
        public decimal TotalTransferredQuantity { get; set; }
    }

    public class ReturnsAndAdjustmentsItemDto
    {
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; }
        public string MovementType { get; set; } = string.Empty; // تسوية موجبة، تسوية سالبة، مرتجع بيع، مرتجع شراء
        public string WarehouseName { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalValue { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public class ReturnsAndAdjustmentsReportDto
    {
        public InventoryReportFilterDto Filter { get; set; } = new();
        public List<ReturnsAndAdjustmentsItemDto> Items { get; set; } = new();
        public decimal TotalQuantity { get; set; }
        public decimal TotalValue { get; set; }
    }

    public class ReservedAndAvailableItemDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public decimal OnHandQuantity { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal AvailableQuantity => OnHandQuantity - ReservedQuantity;
        public List<string> ReservationOrders { get; set; } = new();
    }

    public class ReservedAndAvailableReportDto
    {
        public InventoryReportFilterDto Filter { get; set; } = new();
        public List<ReservedAndAvailableItemDto> Items { get; set; } = new();
    }

    public class ReorderLevelAlertItemDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public decimal CurrentStock { get; set; }
        public decimal ReorderPoint { get; set; }
        public decimal Deficit => Math.Max(0, ReorderPoint - CurrentStock);
        public string Severity => CurrentStock <= 0 ? "نفد تماماً (Critical)" : "أقل من حد الطلب (Warning)";
    }

    public class ReorderLevelAlertReportDto
    {
        public InventoryReportFilterDto Filter { get; set; } = new();
        public List<ReorderLevelAlertItemDto> Items { get; set; } = new();
    }

    public class DeadStockItemDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public decimal CurrentStock { get; set; }
        public DateTime? LastMovementDate { get; set; }
        public int DaysWithoutMovement { get; set; }
        public decimal EstimatedValue { get; set; }
    }

    public class DeadStockReportDto
    {
        public InventoryReportFilterDto Filter { get; set; } = new();
        public List<DeadStockItemDto> Items { get; set; } = new();
        public decimal TotalDeadStockQuantity { get; set; }
        public decimal TotalTiedUpCapital { get; set; }
    }

    public class SerialNumberHistoryItemDto
    {
        public int SerialId { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string CurrentStatus { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public string CurrentPartyName { get; set; } = string.Empty;
        public DateTime InDate { get; set; }
        public DateTime? OutDate { get; set; }
        public List<SerialMovementLogDto> Movements { get; set; } = new();
    }

    public class SerialMovementLogDto
    {
        public DateTime Date { get; set; }
        public string MovementType { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string DocumentNumber { get; set; } = string.Empty;
        public string FromLocation { get; set; } = string.Empty;
        public string ToLocation { get; set; } = string.Empty;
        public string StatusAfter { get; set; } = string.Empty;
        public string PartyName { get; set; } = string.Empty;
    }

    public class SerialNumbersReportDto
    {
        public InventoryReportFilterDto Filter { get; set; } = new();
        public List<SerialNumberHistoryItemDto> Items { get; set; } = new();
    }

    public class StockDiscrepancyItemDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public decimal CalculatedStockFromTransactions { get; set; }
        public decimal RecordedStockOrPhysical { get; set; }
        public decimal Discrepancy => CalculatedStockFromTransactions - RecordedStockOrPhysical;
        public string Status => Discrepancy == 0 ? "متطابق تماماً" : (Discrepancy > 0 ? "زيادة محسوبة" : "عجز محسوب");
    }

    public class StockDiscrepancyReportDto
    {
        public InventoryReportFilterDto Filter { get; set; } = new();
        public List<StockDiscrepancyItemDto> Items { get; set; } = new();
    }
}
