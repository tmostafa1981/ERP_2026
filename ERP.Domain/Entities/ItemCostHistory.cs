using System;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// السجل التاريخي لحركات التكلفة والمتوسط المرجح المتحرك لكل حركة مخزنية.
    /// </summary>
    public class ItemCostHistory : IMustHaveBranch
    {
        public long Id { get; set; }

        public int BranchId { get; set; }
        public int WarehouseId { get; set; }
        public int ItemId { get; set; }

        public int DocumentHeaderId { get; set; }
        public int? DocumentLineId { get; set; }

        public CostMovementType MovementType { get; set; }
        public DateTime MovementDate { get; set; }

        public decimal Quantity { get; set; } = 0m;
        public decimal UnitCost { get; set; } = 0m;
        public decimal TotalCost { get; set; } = 0m;

        public decimal PreQuantity { get; set; } = 0m;
        public decimal PreTotalCost { get; set; } = 0m;
        public decimal PreAverageCost { get; set; } = 0m;

        public decimal PostQuantity { get; set; } = 0m;
        public decimal PostTotalCost { get; set; } = 0m;
        public decimal PostAverageCost { get; set; } = 0m;

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigations
        public Branch? Branch { get; set; }
        public Warehouse? Warehouse { get; set; }
        public Item? Item { get; set; }
        public DocumentHeader? DocumentHeader { get; set; }
        public DocumentLine? DocumentLine { get; set; }
    }
}
