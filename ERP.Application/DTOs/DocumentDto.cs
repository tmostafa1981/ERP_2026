using System;
using System.Collections.Generic;
using ERP.Domain.Enums;

namespace ERP.Application.DTOs
{
    public class DocumentHeaderDto
    {
        public int Id { get; set; }
        public byte[]? RowVersion { get; set; }
        public int BranchId { get; set; }
        public string? BranchName { get; set; }
        public int FiscalYearId { get; set; }
        public int TransactionTypeId { get; set; }
        public string TransactionTypeCode { get; set; } = string.Empty;
        public string TransactionTypeName { get; set; } = string.Empty;
        public TransactionModule Module { get; set; }
        public DocumentBaseNature BaseNature { get; set; }

        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; } = DateTime.Today;
        public DateTime? DueDate { get; set; }

        public DocumentPartyType PartyType { get; set; }
        public int? PartyId { get; set; }
        public string? PartyCode { get; set; }
        public string? PartyName { get; set; }

        public VoucherPaymentMethod PaymentMethod { get; set; } = VoucherPaymentMethod.Cash;
        public int? CashBankAccountId { get; set; }
        public string? CashBankAccountName { get; set; }

        public int? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public int CurrencyId { get; set; } = 1;
        public string? CurrencyCode { get; set; }
        public decimal ExchangeRate { get; set; } = 1.000000m;

        public decimal TotalAmountBeforeTax { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalAdditions { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmountNet { get; set; }

        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
        public DocumentConversionStatus ConversionStatus { get; set; } = DocumentConversionStatus.Unconverted;

        public InventoryEffect InventoryEffect { get; set; }
        public AccountingEffect AccountingEffect { get; set; }

        public int? JournalEntryHeaderId { get; set; }
        public string? JournalEntryNumber { get; set; }

        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }
        public bool? AllowNegativeStock { get; set; }

        public List<DocumentLineDto> Lines { get; set; } = new();
        public List<DocumentAdjustmentLineDto> Adjustments { get; set; } = new();
        public List<DocumentJournalEntryLineDto> JournalEntryLines { get; set; } = new();
    }

    public class DocumentJournalEntryLineDto
    {
        public int LineNumber { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal DebitLocal { get; set; }
        public decimal CreditLocal { get; set; }
        public decimal DebitForeign { get; set; }
        public decimal CreditForeign { get; set; }
        public string? CurrencyCode { get; set; }
        public string? CostCenterName { get; set; }
        public string? Description { get; set; }
    }

    public class DocumentLineDto
    {
        public int Id { get; set; }
        public int DocumentHeaderId { get; set; }
        public int LineNumber { get; set; }

        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public ItemTrackingType TrackingType { get; set; }

        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal ConversionRate { get; set; } = 1.0m;

        public decimal Quantity { get; set; }
        public decimal ConvertedQuantity { get; set; }
        public decimal RemainingQuantity { get; set; }

        public decimal UnitPrice { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxRate { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal LineTotal { get; set; }

        public int? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public int? CostCenterId { get; set; }
        public string? CostCenterName { get; set; }
        public string? Notes { get; set; }

        public List<string> SerialNumbers { get; set; } = new();
        public List<string> LockedSerialNumbers { get; set; } = new();
    }
}
