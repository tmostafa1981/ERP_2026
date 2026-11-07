using System;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class AssetDepreciationHistory : IAuditableEntity
    {
        public int Id { get; set; }
        public int AssetDepreciationRunId { get; set; }
        public int AssetId { get; set; }

        public DateTime DepreciationDate { get; set; }
        public int FiscalYearId { get; set; }
        public int FiscalPeriodId { get; set; }

        public decimal AssetCostAtRun { get; set; }
        public decimal PriorAccumulatedDepreciation { get; set; }
        public decimal DepreciationAmount { get; set; }
        public decimal PostAccumulatedDepreciation { get; set; }
        public decimal BookValueBefore { get; set; }
        public decimal BookValueAfter { get; set; }

        public int BranchId { get; set; }
        public int? CostCenterId { get; set; }
        public int ExpenseAccountId { get; set; }
        public int AccumulatedAccountId { get; set; }

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigations
        public AssetDepreciationRun? AssetDepreciationRun { get; set; }
        public FixedAsset? Asset { get; set; }
        public Branch? Branch { get; set; }
        public CostCenter? CostCenter { get; set; }
        public GLAccount? ExpenseAccount { get; set; }
        public GLAccount? AccumulatedAccount { get; set; }
    }
}
