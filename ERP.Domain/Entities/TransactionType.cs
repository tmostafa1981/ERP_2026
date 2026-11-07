using System;
using System.Collections.Generic;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class TransactionType : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;

        public TransactionModule Module { get; set; }
        public DocumentBaseNature BaseNature { get; set; }

        public InventoryEffect InventoryEffect { get; set; }
        public AccountingEffect AccountingEffect { get; set; }

        public bool IsCashAllowed { get; set; } = true;
        public bool IsCreditAllowed { get; set; } = true;
        public bool AutoPost { get; set; } = false;
        public bool ShowLineNotes { get; set; } = true;

        public string CodeSequencePrefix { get; set; } = "DOC-";

        public int? DefaultWarehouseId { get; set; }
        public int? BranchId { get; set; }

        public SerialTrackingPolicy SerialTrackingPolicy { get; set; } = SerialTrackingPolicy.AsPerItemDefinition;

        public int? DefaultDebitAccountId { get; set; }
        public int? DefaultCreditAccountId { get; set; }
        public int? DefaultTaxAccountId { get; set; }
        public int? DefaultDiscountAccountId { get; set; }

        public int? DefaultCustomerId { get; set; }
        public int? DefaultVendorId { get; set; }
        public int? DefaultCashBankAccountId { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigations
        public Warehouse? DefaultWarehouse { get; set; }
        public Branch? Branch { get; set; }
        public Customer? DefaultCustomer { get; set; }
        public Vendor? DefaultVendor { get; set; }
        public GLAccount? DefaultCashBankAccount { get; set; }
        public GLAccount? DefaultDebitAccount { get; set; }
        public GLAccount? DefaultCreditAccount { get; set; }
        public GLAccount? DefaultTaxAccount { get; set; }
        public GLAccount? DefaultDiscountAccount { get; set; }

        public ICollection<UserTransactionTypePermission> UserPermissions { get; set; } = new List<UserTransactionTypePermission>();
        public ICollection<TransactionTypeConversion> SourceConversions { get; set; } = new List<TransactionTypeConversion>();
        public ICollection<TransactionTypeConversion> TargetConversions { get; set; } = new List<TransactionTypeConversion>();
        public ICollection<TransactionTypeAdjustment> AllowedAdjustments { get; set; } = new List<TransactionTypeAdjustment>();

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar")
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : NameAr);
    }
}
