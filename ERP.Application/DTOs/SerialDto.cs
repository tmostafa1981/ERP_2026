using System;
using System.Collections.Generic;
using ERP.Domain.Enums;

namespace ERP.Application.DTOs
{
    public class SerialLookupDto
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public ItemSerialStatus Status { get; set; }
        public string StatusNameAr { get; set; } = string.Empty;
        public DateTime InDate { get; set; }
        public string? Notes { get; set; }
    }

    public class SerialValidationRequestDto
    {
        public int ItemId { get; set; }
        public int BranchId { get; set; }
        public int WarehouseId { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
    }

    public class SerialValidationResultDto
    {
        public bool IsValid { get; set; }
        public string? Message { get; set; }
        public int? SerialId { get; set; }
    }

    public class SerialMovementDto
    {
        public long Id { get; set; }
        public int ItemSerialNumberId { get; set; }
        public SerialMovementType MovementType { get; set; }
        public string MovementTypeNameAr { get; set; } = string.Empty;
        public string MovementTypeNameEn { get; set; } = string.Empty;

        public int? FromBranchId { get; set; }
        public string? FromBranchName { get; set; }
        public int? FromWarehouseId { get; set; }
        public string? FromWarehouseName { get; set; }

        public int? ToBranchId { get; set; }
        public string? ToBranchName { get; set; }
        public int? ToWarehouseId { get; set; }
        public string? ToWarehouseName { get; set; }

        public ItemSerialStatus PreviousStatus { get; set; }
        public ItemSerialStatus NewStatus { get; set; }
        public string NewStatusNameAr { get; set; } = string.Empty;

        public string? DocumentType { get; set; }
        public int? DocumentHeaderId { get; set; }
        public string? DocumentNumber { get; set; }

        public int? PartyId { get; set; }
        public string? PartyName { get; set; }

        public string? Notes { get; set; }
        public string? UserId { get; set; }
        public DateTime MovementDate { get; set; }
    }

    public class SerialInquiryDto
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemNameAr { get; set; } = string.Empty;
        public string ItemNameEn { get; set; } = string.Empty;

        public string SerialNumber { get; set; } = string.Empty;

        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;

        public ItemSerialStatus Status { get; set; }
        public string StatusNameAr { get; set; } = string.Empty;
        public string StatusBadgeClass { get; set; } = "badge-secondary";

        public DateTime InDate { get; set; }
        public DateTime? OutDate { get; set; }

        public string? SourceDocType { get; set; }
        public int? SourceDocId { get; set; }
        public string? SourceDocNumber { get; set; }

        public string? OutDocType { get; set; }
        public int? OutDocId { get; set; }
        public string? OutDocNumber { get; set; }

        // Party
        public string? CurrentPartyType { get; set; }
        public int? CurrentPartyId { get; set; }
        public string? CurrentPartyName { get; set; }

        public string? VendorName { get; set; }
        public string? CustomerName { get; set; }

        // Warranty & Asset
        public int? WarrantyMonths { get; set; }
        public DateTime? WarrantyStartDate { get; set; }
        public DateTime? WarrantyEndDate { get; set; }
        public bool IsWarrantyValid => WarrantyEndDate.HasValue && WarrantyEndDate.Value >= DateTime.UtcNow;

        public string? AssetTag { get; set; }
        public bool IsAsset { get; set; }
        public DateTime? LastServiceDate { get; set; }
        public int ServiceCount { get; set; }

        public string? Notes { get; set; }

        public List<SerialMovementDto> Movements { get; set; } = new List<SerialMovementDto>();
    }

    public class SerialInquiryFilterDto
    {
        public string? SerialNumber { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public int? BranchId { get; set; }
        public int? WarehouseId { get; set; }
        public ItemSerialStatus? Status { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public enum SerialPasteValidationStatus
    {
        Valid = 1,
        DuplicateInPaste = 2,
        AlreadyExists = 3,
        Invalid = 4
    }

    public class SerialBatchValidationRequestDto
    {
        public int ItemId { get; set; }
        public int WarehouseId { get; set; }
        public int BranchId { get; set; }
        public List<string> RawSerials { get; set; } = new List<string>();
        public List<string> ExistingLineSerials { get; set; } = new List<string>();
        public bool IsSalesReturn { get; set; }
        public int? ExcludeDocumentId { get; set; }
    }

    public class SerialBatchValidationItemDto
    {
        public string SerialNumber { get; set; } = string.Empty;
        public SerialPasteValidationStatus Status { get; set; }
        public string StatusKey { get; set; } = string.Empty;
        public string StatusTextAr { get; set; } = string.Empty;
        public string StatusTextEn { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public bool IsValid { get; set; }
    }

    public class SerialBatchValidationResponseDto
    {
        public int TotalCount { get; set; }
        public int ValidCount { get; set; }
        public int DuplicateCount { get; set; }
        public int ExistingCount { get; set; }
        public int InvalidCount { get; set; }
        public List<SerialBatchValidationItemDto> Results { get; set; } = new List<SerialBatchValidationItemDto>();
    }

    public class AvailableSerialsQueryDto
    {
        public int ItemId { get; set; }
        public int WarehouseId { get; set; }
        public int BranchId { get; set; }
        public string? Search { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int? ExcludeDocumentId { get; set; }
    }

    public class PagedSerialLookupResultDto
    {
        public List<SerialLookupDto> Items { get; set; } = new List<SerialLookupDto>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
    }
}
