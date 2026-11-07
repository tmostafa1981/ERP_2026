using System.Collections.Generic;

namespace ERP.Application.DTOs
{
    public class ItemAvailabilityDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;

        public int BranchId { get; set; }
        public string? BranchName { get; set; }

        public int? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }

        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal ConversionFactor { get; set; } = 1.0m;

        // Quantities in Selected Unit
        public decimal OnHandQuantity { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal AvailableQuantity { get; set; }

        // Quantities in Base Unit
        public decimal OnHandBaseQuantity { get; set; }
        public decimal ReservedBaseQuantity { get; set; }
        public decimal AvailableBaseQuantity { get; set; }

        // Current Operation Simulation
        public decimal RequestedQuantity { get; set; }
        public decimal RemainingAfterRequest { get; set; }

        public bool IsService { get; set; }
        public bool IsSerialTracked { get; set; }
        public int AvailableSerialCount { get; set; }
        public List<string> AvailableSerials { get; set; } = new();

        public bool AllowNegativeStock { get; set; }
        public bool IsAvailable { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class ItemAvailabilityRequestDto
    {
        public int ItemId { get; set; }
        public int BranchId { get; set; }
        public int? WarehouseId { get; set; }
        public int? UnitId { get; set; }
        public decimal RequestedQuantity { get; set; } = 1.0m;
        public int? ExcludeDocumentId { get; set; }
    }

    public class ItemAvailabilityValidationResultDto
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public decimal OnHandQuantity { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal AvailableQuantity { get; set; }
        public decimal RequestedQuantity { get; set; }
        public decimal ShortageQuantity { get; set; }
        public ItemAvailabilityDto? Details { get; set; }
    }
}
