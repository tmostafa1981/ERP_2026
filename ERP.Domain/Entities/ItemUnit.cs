using System;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class ItemUnit : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public int ItemId { get; set; }
        public int UnitId { get; set; }
        public decimal ConversionFactor { get; set; } = 1.0m;
        public decimal SalesPrice { get; set; } = 0m;
        public decimal PurchasePrice { get; set; } = 0m;
        public bool IsDefaultSales { get; set; } = false;
        public bool IsDefaultPurchase { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Item? Item { get; set; }
        public Unit? Unit { get; set; }
    }
}
