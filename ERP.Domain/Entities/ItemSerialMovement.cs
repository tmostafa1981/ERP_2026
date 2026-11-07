using System;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class ItemSerialMovement
    {
        public long Id { get; set; }

        public int ItemSerialNumberId { get; set; }
        public ItemSerialNumber? ItemSerialNumber { get; set; }

        public int ItemId { get; set; }
        public Item? Item { get; set; }

        public string SerialNumber { get; set; } = string.Empty;

        public SerialMovementType MovementType { get; set; }

        public int? FromBranchId { get; set; }
        public Branch? FromBranch { get; set; }

        public int? FromWarehouseId { get; set; }
        public Warehouse? FromWarehouse { get; set; }

        public int? ToBranchId { get; set; }
        public Branch? ToBranch { get; set; }

        public int? ToWarehouseId { get; set; }
        public Warehouse? ToWarehouse { get; set; }

        public ItemSerialStatus PreviousStatus { get; set; }
        public ItemSerialStatus NewStatus { get; set; }

        public string? DocumentType { get; set; }
        public int? DocumentHeaderId { get; set; }
        public string? DocumentNumber { get; set; }

        public int? PartyId { get; set; }
        public string? PartyName { get; set; }

        public string? Notes { get; set; }
        public string? UserId { get; set; }
        public DateTime MovementDate { get; set; } = DateTime.UtcNow;
    }
}
