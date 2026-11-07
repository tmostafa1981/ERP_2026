using System;
using System.Collections.Generic;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class DocumentHeader : IAuditableEntity, IMustHaveBranch, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public int BranchId { get; set; }
        public int FiscalYearId { get; set; }
        public int TransactionTypeId { get; set; }

        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; } = DateTime.Today;
        public DateTime? DueDate { get; set; }

        public DocumentPartyType PartyType { get; set; } = DocumentPartyType.None;
        public int? PartyId { get; set; } // CustomerId, VendorId, or TargetBranchId

        public VoucherPaymentMethod PaymentMethod { get; set; } = VoucherPaymentMethod.Cash;
        public int? CashBankAccountId { get; set; }

        public int? WarehouseId { get; set; }
        public int CurrencyId { get; set; } = 1;
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

        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }

        public string? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigations
        public Branch? Branch { get; set; }
        public FiscalYear? FiscalYear { get; set; }
        public TransactionType? TransactionType { get; set; }
        public Warehouse? Warehouse { get; set; }
        public Currency? Currency { get; set; }
        public GLAccount? CashBankAccount { get; set; }
        public JournalEntryHeader? JournalEntryHeader { get; set; }

        public ICollection<DocumentLine> Lines { get; set; } = new List<DocumentLine>();
        public ICollection<DocumentAdjustmentLine> Adjustments { get; set; } = new List<DocumentAdjustmentLine>();
        public ICollection<DocumentConversionLink> SourceLinks { get; set; } = new List<DocumentConversionLink>();
        public ICollection<DocumentConversionLink> TargetLinks { get; set; } = new List<DocumentConversionLink>();
    }
}
