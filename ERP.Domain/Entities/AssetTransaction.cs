using System;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class AssetTransaction : IAuditableEntity
    {
        public int Id { get; set; }
        public int AssetId { get; set; }

        public AssetTransactionType TransactionType { get; set; }
        public DateTime TransactionDate { get; set; } = DateTime.Today;

        // Locations & Branches
        public int? FromBranchId { get; set; }
        public int? ToBranchId { get; set; }
        public int? FromWarehouseId { get; set; }
        public int? ToWarehouseId { get; set; }

        // Departments & Custodians
        public string? FromDepartment { get; set; }
        public string? ToDepartment { get; set; }
        public string? FromCustodian { get; set; }
        public string? ToCustodian { get; set; }

        // Cost Centers
        public int? FromCostCenterId { get; set; }
        public int? ToCostCenterId { get; set; }

        // Amounts for addition / disposal
        public decimal Amount { get; set; } = 0m;
        public bool IsCapitalized { get; set; } = true; // For additions: capitalized to asset cost vs immediate expense
        public int? ExpenseAccountId { get; set; }

        // Associated Journal Entry (e.g. for Addition, Disposal)
        public int? JournalEntryId { get; set; }

        public string? Reason { get; set; }
        public string? Notes { get; set; }
        public string? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigations
        public FixedAsset? Asset { get; set; }
        public Branch? FromBranch { get; set; }
        public Branch? ToBranch { get; set; }
        public Warehouse? FromWarehouse { get; set; }
        public Warehouse? ToWarehouse { get; set; }
        public CostCenter? FromCostCenter { get; set; }
        public CostCenter? ToCostCenter { get; set; }
        public JournalEntryHeader? JournalEntry { get; set; }
    }
}
