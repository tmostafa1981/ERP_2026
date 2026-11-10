using System;
using System.Collections.Generic;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class ItemSerialNumber : IAuditableEntity, IMustHaveBranch, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public int ItemId { get; set; }
        public string SerialNumber { get; set; } = string.Empty;

        public int BranchId { get; set; }
        public int WarehouseId { get; set; }

        public ItemSerialStatus Status { get; set; } = ItemSerialStatus.Available;

        public decimal UnitCost { get; set; } = 0m;

        public DateTime InDate { get; set; } = DateTime.UtcNow;
        public DateTime? OutDate { get; set; }

        public string? SourceDocType { get; set; }
        public int? SourceDocId { get; set; }

        public string? OutDocType { get; set; }
        public int? OutDocId { get; set; }

        // Current Customer / Vendor Association
        public string? CurrentPartyType { get; set; } // "Customer", "Vendor"
        public int? CurrentPartyId { get; set; }
        public string? CurrentPartyName { get; set; }

        // Future-Proofing: Warranty Tracking
        public int? WarrantyMonths { get; set; }
        public DateTime? WarrantyStartDate { get; set; }
        public DateTime? WarrantyEndDate { get; set; }

        // Future-Proofing: Maintenance & Asset Tracking
        public string? AssetTag { get; set; }
        public bool IsAsset { get; set; } = false;
        public DateTime? LastServiceDate { get; set; }
        public int ServiceCount { get; set; } = 0;

        // Future-Proofing: Reservation Tracking
        public int? ReservedDocId { get; set; }
        public DateTime? ReservedUntil { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigations
        public Item? Item { get; set; }
        public Branch? Branch { get; set; }
        public Warehouse? Warehouse { get; set; }
        public ICollection<ItemSerialMovement> Movements { get; set; } = new List<ItemSerialMovement>();
    }
}
