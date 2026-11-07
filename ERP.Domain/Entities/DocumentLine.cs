using System;
using System.Collections.Generic;

namespace ERP.Domain.Entities
{
    public class DocumentLine
    {
        public int Id { get; set; }
        public int DocumentHeaderId { get; set; }
        public int LineNumber { get; set; }

        public int ItemId { get; set; }
        public int UnitId { get; set; }
        public decimal ConversionRate { get; set; } = 1.0000m;

        public decimal Quantity { get; set; }
        public decimal ConvertedQuantity { get; set; } = 0m;
        public decimal RemainingQuantity => Math.Max(0m, Quantity - ConvertedQuantity);

        public decimal UnitPrice { get; set; }
        public decimal DiscountAmount { get; set; } = 0m;
        public decimal TaxRate { get; set; } = 0m;
        public decimal TaxAmount { get; set; } = 0m;
        public decimal LineTotal { get; set; }

        public int? WarehouseId { get; set; }
        public int? CostCenterId { get; set; }
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigations
        public DocumentHeader? DocumentHeader { get; set; }
        public Item? Item { get; set; }
        public Unit? Unit { get; set; }
        public Warehouse? Warehouse { get; set; }
        public CostCenter? CostCenter { get; set; }

        public ICollection<DocumentLineSerial> Serials { get; set; } = new List<DocumentLineSerial>();
        public ICollection<DocumentLineLink> SourceLineLinks { get; set; } = new List<DocumentLineLink>();
        public ICollection<DocumentLineLink> TargetLineLinks { get; set; } = new List<DocumentLineLink>();
    }
}
