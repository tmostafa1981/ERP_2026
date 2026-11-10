using System;
using System.Collections.Generic;
using ERP.Domain.Enums;

namespace ERP.Application.DTOs
{
    public class TransactionTypeDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string LocalizedName { get; set; } = string.Empty;

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
        public string? DefaultWarehouseName { get; set; }
        public int? BranchId { get; set; }
        public string? BranchName { get; set; }

        public SerialTrackingPolicy SerialTrackingPolicy { get; set; }

        public int? DefaultDebitAccountId { get; set; }
        public string? DefaultDebitAccountName { get; set; }
        public int? DefaultCreditAccountId { get; set; }
        public string? DefaultCreditAccountName { get; set; }
        public int? DefaultTaxAccountId { get; set; }
        public string? DefaultTaxAccountName { get; set; }
        public int? DefaultDiscountAccountId { get; set; }
        public string? DefaultDiscountAccountName { get; set; }

        public int? DefaultCogsAccountId { get; set; }
        public string? DefaultCogsAccountName { get; set; }
        public int? DefaultInventoryAccountId { get; set; }
        public string? DefaultInventoryAccountName { get; set; }

        public int? DefaultCustomerId { get; set; }
        public string? DefaultCustomerName { get; set; }
        public int? DefaultVendorId { get; set; }
        public string? DefaultVendorName { get; set; }
        public int? DefaultCashBankAccountId { get; set; }
        public string? DefaultCashBankAccountName { get; set; }

        public bool IsActive { get; set; } = true;

        // User specific permission for this type
        public bool CanView { get; set; } = true;
        public bool CanCreate { get; set; } = true;
        public bool CanEdit { get; set; } = true;
        public bool CanDelete { get; set; } = true;
        public bool CanPost { get; set; } = true;
        public bool CanUnpost { get; set; } = true;
        public bool CanConvert { get; set; } = true;
    }

    public class UserTransactionTypePermissionMatrixDto
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public TransactionModule? Module { get; set; }
        public List<TransactionTypeDto> Permissions { get; set; } = new();
    }
}
