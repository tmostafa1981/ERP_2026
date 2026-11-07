using System;
using System.Collections.Generic;

namespace ERP.Application.DTOs
{
    public class ConvertDocumentRequestDto
    {
        public int TargetTransactionTypeId { get; set; }
        public DateTime DocumentDate { get; set; } = DateTime.Today;
        public List<int> SourceDocumentIds { get; set; } = new();
        public List<ConvertDocumentLineSelectionDto> SelectedLines { get; set; } = new();
        public string? Notes { get; set; }
    }

    public class ConvertDocumentLineSelectionDto
    {
        public int SourceDocumentLineId { get; set; }
        public decimal QuantityToConvert { get; set; }
        public List<string> SerialNumbers { get; set; } = new();
    }

    public class AvailableForConversionDocumentDto
    {
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; }
        public string TransactionTypeName { get; set; } = string.Empty;
        public string? PartyName { get; set; }
        public decimal TotalAmountNet { get; set; }
        public List<AvailableForConversionLineDto> AvailableLines { get; set; } = new();
    }

    public class AvailableForConversionLineDto
    {
        public int LineId { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal OriginalQuantity { get; set; }
        public decimal ConvertedQuantity { get; set; }
        public decimal RemainingQuantity { get; set; }
        public decimal UnitPrice { get; set; }
        public int? WarehouseId { get; set; }
    }
}
