using System;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// رصيد وتكلفة الصنف اللحظية بالمتوسط المرجح المتحرك على مستوى (الصنف + المخزن + الفرع).
    /// </summary>
    public class ItemWarehouseCost : IAuditableEntity, IMustHaveBranch, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public int BranchId { get; set; }
        public int WarehouseId { get; set; }
        public int ItemId { get; set; }

        public decimal CurrentQuantity { get; set; } = 0m;
        public decimal TotalCost { get; set; } = 0m;
        public decimal MovingAverageCost { get; set; } = 0m;

        public DateTime LastUpdatedDate { get; set; } = DateTime.UtcNow;
        public int? LastDocumentId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigations
        public Branch? Branch { get; set; }
        public Warehouse? Warehouse { get; set; }
        public Item? Item { get; set; }
        public DocumentHeader? LastDocument { get; set; }
    }
}
