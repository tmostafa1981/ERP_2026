using System;
using System.Collections.Generic;
using ERP.Domain.Enums;

namespace ERP.Application.DTOs.FixedAssets
{
    // ─── 1. ASSET CATEGORY DTOs ──────────────────────────────────────────────
    public class AssetCategoryDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string LocalizedName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int DefaultUsefulLifeMonths { get; set; }
        public DepreciationMethod DefaultDepreciationMethod { get; set; }
        public decimal DefaultResidualValueRate { get; set; }

        public int AssetCostAccountId { get; set; }
        public string AssetCostAccountCode { get; set; } = string.Empty;
        public string AssetCostAccountName { get; set; } = string.Empty;

        public int AccumulatedDepreciationAccountId { get; set; }
        public string AccumulatedDepreciationAccountCode { get; set; } = string.Empty;
        public string AccumulatedDepreciationAccountName { get; set; } = string.Empty;

        public int DepreciationExpenseAccountId { get; set; }
        public string DepreciationExpenseAccountCode { get; set; } = string.Empty;
        public string DepreciationExpenseAccountName { get; set; } = string.Empty;

        public int DisposalGainAccountId { get; set; }
        public string DisposalGainAccountCode { get; set; } = string.Empty;
        public string DisposalGainAccountName { get; set; } = string.Empty;

        public int DisposalLossAccountId { get; set; }
        public string DisposalLossAccountCode { get; set; } = string.Empty;
        public string DisposalLossAccountName { get; set; } = string.Empty;

        public int? CipAccountId { get; set; }
        public string? CipAccountCode { get; set; }
        public string? CipAccountName { get; set; }

        public int? BranchId { get; set; }
        public string? BranchName { get; set; }
        public bool IsActive { get; set; }
        public int AssetsCount { get; set; }
    }

    public class CreateAssetCategoryDto
    {
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int DefaultUsefulLifeMonths { get; set; } = 60;
        public DepreciationMethod DefaultDepreciationMethod { get; set; } = DepreciationMethod.StraightLine;
        public decimal DefaultResidualValueRate { get; set; } = 0m;

        public int AssetCostAccountId { get; set; }
        public int AccumulatedDepreciationAccountId { get; set; }
        public int DepreciationExpenseAccountId { get; set; }
        public int DisposalGainAccountId { get; set; }
        public int DisposalLossAccountId { get; set; }
        public int? CipAccountId { get; set; }

        public int? BranchId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // ─── 2. FIXED ASSET REGISTER DTOs ────────────────────────────────────────
    public class FixedAssetListDto
    {
        public int Id { get; set; }
        public string AssetCode { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string LocalizedName { get; set; } = string.Empty;
        public int AssetCategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string? CostCenterName { get; set; }
        public string? Department { get; set; }
        public string? CustodianEmployeeName { get; set; }
        public AssetStatus Status { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public DateTime PurchaseDate { get; set; }
        public DateTime? DepreciationStartDate { get; set; }
        public decimal CurrentCostLocal { get; set; }
        public decimal CurrentAccumulatedDepreciation { get; set; }
        public decimal CurrentBookValue { get; set; }
        public DepreciationMethod DepreciationMethod { get; set; }
        public string DepreciationMethodName { get; set; } = string.Empty;
        public bool IsFullyDepreciated { get; set; }
    }

    public class FixedAssetDetailsDto
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public string AssetCode { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string LocalizedName => !string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn;
        public string? Description { get; set; }
        public string? SerialNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ImagePath { get; set; }

        public int AssetCategoryId { get; set; }
        public string CategoryCode { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;

        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public int? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public string? Department { get; set; }
        public int? CostCenterId { get; set; }
        public string? CostCenterName { get; set; }
        public string? CustodianEmployeeName { get; set; }

        public AssetStatus Status { get; set; }
        public string StatusName { get; set; } = string.Empty;

        public DateTime PurchaseDate { get; set; }
        public DateTime? PlacedInServiceDate { get; set; }
        public int? VendorId { get; set; }
        public string? VendorName { get; set; }
        public int? PurchaseDocumentHeaderId { get; set; }
        public string? PurchaseDocumentNumber { get; set; }
        public string? PurchaseInvoiceReference { get; set; }

        public int CurrencyId { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public string CurrencyName { get; set; } = string.Empty;
        public decimal PurchasePriceForeign { get; set; }
        public decimal ExchangeRate { get; set; }
        public decimal InitialCostLocal { get; set; }
        public decimal AdditionalExpensesLocal { get; set; }
        public decimal CurrentCostLocal { get; set; }
        public decimal ResidualValue { get; set; }

        public int UsefulLifeMonths { get; set; }
        public DateTime? DepreciationStartDate { get; set; }
        public DepreciationMethod DepreciationMethod { get; set; }
        public string DepreciationMethodName { get; set; } = string.Empty;
        public decimal DepreciationRate { get; set; }
        public decimal TotalExpectedUnits { get; set; }
        public decimal TotalProducedUnits { get; set; }
        public FirstMonthPolicy FirstMonthPolicy { get; set; }
        public string FirstMonthPolicyName { get; set; } = string.Empty;

        // Effective Accounts
        public int AssetCostAccountId { get; set; }
        public string AssetCostAccountName { get; set; } = string.Empty;
        public int AccumulatedDepreciationAccountId { get; set; }
        public string AccumulatedDepreciationAccountName { get; set; } = string.Empty;
        public int DepreciationExpenseAccountId { get; set; }
        public string DepreciationExpenseAccountName { get; set; } = string.Empty;
        public int DisposalGainAccountId { get; set; }
        public string DisposalGainAccountName { get; set; } = string.Empty;
        public int DisposalLossAccountId { get; set; }
        public string DisposalLossAccountName { get; set; } = string.Empty;

        public bool IsOpeningAsset { get; set; }
        public decimal OpeningCarryingValue { get; set; }
        public decimal OpeningAccumulatedDepreciation { get; set; }
        public decimal CurrentAccumulatedDepreciation { get; set; }
        public decimal CurrentBookValue { get; set; }
        public DateTime? LastDepreciationDate { get; set; }
        public bool IsFullyDepreciated { get; set; }

        public DateTime? DisposalDate { get; set; }
        public decimal? DisposalProceeds { get; set; }
        public decimal? DisposalExpenses { get; set; }
        public string? DisposalReason { get; set; }
        public int? DisposalJournalEntryId { get; set; }
        public string? DisposalJournalEntryNumber { get; set; }

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedByUserName { get; set; }

        public List<AssetDepreciationHistoryItemDto> DepreciationHistories { get; set; } = new();
        public List<AssetTransactionItemDto> Transactions { get; set; } = new();
    }

    public class CreateFixedAssetDto
    {
        public string? AssetCode { get; set; } // If null, auto-generated
        public string NameAr { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public int AssetCategoryId { get; set; }
        public string? Description { get; set; }
        public string? SerialNumber { get; set; }
        public string? Barcode { get; set; }

        public int BranchId { get; set; }
        public int? WarehouseId { get; set; }
        public string? Department { get; set; }
        public int? CostCenterId { get; set; }
        public string? CustodianEmployeeName { get; set; }

        public AssetStatus Status { get; set; } = AssetStatus.InService;

        public DateTime PurchaseDate { get; set; } = DateTime.Today;
        public DateTime? PlacedInServiceDate { get; set; }
        public int? VendorId { get; set; }
        public int? PurchaseDocumentHeaderId { get; set; }
        public string? PurchaseInvoiceReference { get; set; }

        public int CurrencyId { get; set; } = 1;
        public decimal PurchasePriceForeign { get; set; }
        public decimal ExchangeRate { get; set; } = 1.000000m;
        public decimal InitialCostLocal { get; set; }
        public decimal AdditionalExpensesLocal { get; set; }
        public decimal ResidualValue { get; set; } = 0m;

        public int UsefulLifeMonths { get; set; } = 60;
        public DateTime? DepreciationStartDate { get; set; }
        public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;
        public decimal DepreciationRate { get; set; } = 0m;
        public decimal TotalExpectedUnits { get; set; } = 0m;
        public FirstMonthPolicy FirstMonthPolicy { get; set; } = FirstMonthPolicy.FullMonth;

        // Custom account overrides (null to inherit from category)
        public int? AssetCostAccountId { get; set; }
        public int? AccumulatedDepreciationAccountId { get; set; }
        public int? DepreciationExpenseAccountId { get; set; }
        public int? DisposalGainAccountId { get; set; }
        public int? DisposalLossAccountId { get; set; }

        // Opening balance fields
        public bool IsOpeningAsset { get; set; } = false;
        public decimal OpeningAccumulatedDepreciation { get; set; } = 0m;
        public decimal OpeningCarryingValue { get; set; } = 0m;

        public string? Notes { get; set; }
    }

    public class UpdateFixedAssetDto : CreateFixedAssetDto
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }

    public class FixedAssetFilterDto
    {
        public int? CompanyId { get; set; }
        public int? BranchId { get; set; }
        public int? AssetCategoryId { get; set; }
        public AssetStatus? Status { get; set; }
        public int? CostCenterId { get; set; }
        public string? SearchTerm { get; set; }
        public DateTime? FromPurchaseDate { get; set; }
        public DateTime? ToPurchaseDate { get; set; }
        public bool? FullyDepreciated { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    // ─── 3. DEPRECIATION RUN & ENGINE DTOs ────────────────────────────────────
    public class RunDepreciationFilterDto
    {
        public DateTime RunDate { get; set; } = DateTime.Today;
        public int CompanyId { get; set; } = 1;
        public int? BranchId { get; set; }
        public int? AssetCategoryId { get; set; }
        public int? SpecificAssetId { get; set; }
    }

    public class AssetDepreciationPreviewItemDto
    {
        public int AssetId { get; set; }
        public string AssetCode { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public int? CostCenterId { get; set; }
        public string? CostCenterName { get; set; }

        public decimal AssetCost { get; set; }
        public decimal ResidualValue { get; set; }
        public decimal DepreciableBasis => Math.Max(0m, AssetCost - ResidualValue);
        public int UsefulLifeMonths { get; set; }
        public DepreciationMethod DepreciationMethod { get; set; }
        public string DepreciationMethodName { get; set; } = string.Empty;

        public decimal PriorAccumulatedDepreciation { get; set; }
        public decimal CurrentPeriodDepreciation { get; set; }
        public decimal PostAccumulatedDepreciation { get; set; }
        public decimal BookValueBefore { get; set; }
        public decimal BookValueAfter { get; set; }

        public int ExpenseAccountId { get; set; }
        public string ExpenseAccountCode { get; set; } = string.Empty;
        public string ExpenseAccountName { get; set; } = string.Empty;

        public int AccumulatedAccountId { get; set; }
        public string AccumulatedAccountCode { get; set; } = string.Empty;
        public string AccumulatedAccountName { get; set; } = string.Empty;

        public bool IsEligible { get; set; } = true;
        public string? IneligibilityReason { get; set; }
    }

    public class DepreciationCalculationResultDto
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public DateTime RunDate { get; set; }
        public int FiscalYearId { get; set; }
        public string FiscalYearName { get; set; } = string.Empty;
        public int FiscalPeriodId { get; set; }
        public string FiscalPeriodName { get; set; } = string.Empty;

        public int? BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public int? AssetCategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;

        public int TotalAssetsCount { get; set; }
        public decimal TotalDepreciationAmount { get; set; }
        public List<AssetDepreciationPreviewItemDto> Lines { get; set; } = new();
    }

    public class ExecuteDepreciationRunDto
    {
        public DateTime RunDate { get; set; }
        public int CompanyId { get; set; } = 1;
        public int? BranchId { get; set; }
        public int? AssetCategoryId { get; set; }
        public int? SpecificAssetId { get; set; }
        public string? Notes { get; set; }
        public bool AutoPost { get; set; } = true;
    }

    public class DepreciationRunListDto
    {
        public int Id { get; set; }
        public string RunNumber { get; set; } = string.Empty;
        public DateTime RunDate { get; set; }
        public int FiscalYearId { get; set; }
        public string FiscalYearName { get; set; } = string.Empty;
        public string FiscalPeriodName { get; set; } = string.Empty;
        public int? BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public int AssetsCount { get; set; }
        public decimal TotalDepreciationAmount { get; set; }
        public DepreciationRunStatus Status { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public int? JournalEntryId { get; set; }
        public string? JournalEntryNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedByUserName { get; set; }
    }

    public class DepreciationRunDetailsDto : DepreciationRunListDto
    {
        public string? Notes { get; set; }
        public int? ReversalJournalEntryId { get; set; }
        public string? ReversalJournalEntryNumber { get; set; }
        public DateTime? ReversedAt { get; set; }
        public string? ReversedByUserName { get; set; }
        public List<AssetDepreciationHistoryItemDto> Lines { get; set; } = new();
    }

    public class AssetDepreciationHistoryItemDto
    {
        public int Id { get; set; }
        public int AssetDepreciationRunId { get; set; }
        public string RunNumber { get; set; } = string.Empty;
        public int AssetId { get; set; }
        public string AssetCode { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public DateTime DepreciationDate { get; set; }
        public string FiscalPeriodName { get; set; } = string.Empty;
        public decimal AssetCostAtRun { get; set; }
        public decimal PriorAccumulatedDepreciation { get; set; }
        public decimal DepreciationAmount { get; set; }
        public decimal PostAccumulatedDepreciation { get; set; }
        public decimal BookValueBefore { get; set; }
        public decimal BookValueAfter { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string? CostCenterName { get; set; }
        public string ExpenseAccountCode { get; set; } = string.Empty;
        public string ExpenseAccountName { get; set; } = string.Empty;
        public string AccumulatedAccountCode { get; set; } = string.Empty;
        public string AccumulatedAccountName { get; set; } = string.Empty;
        public int? JournalEntryId { get; set; }
        public string? JournalEntryNumber { get; set; }
    }

    // ─── 4. ASSET TRANSACTIONS DTOs (Transfer, Additional Cost, Disposal, CIP) ───
    public class AssetTransferDto
    {
        public int AssetId { get; set; }
        public DateTime TransferDate { get; set; } = DateTime.Today;
        public int ToBranchId { get; set; }
        public int? ToWarehouseId { get; set; }
        public int? ToCostCenterId { get; set; }
        public string? ToDepartment { get; set; }
        public string? ToCustodian { get; set; }
        public string? Reason { get; set; }
    }

    public class AssetAdditionalCostDto
    {
        public int AssetId { get; set; }
        public DateTime TransactionDate { get; set; } = DateTime.Today;
        public decimal Amount { get; set; }
        public bool IsCapitalized { get; set; } = true; // Capitalize to asset cost vs immediate expense
        public int? ExpenseAccountId { get; set; } // If immediate expense
        public int? CashBankAccountId { get; set; } // Credit account (Bank/Cash or AP)
        public int? VendorId { get; set; }
        public string? Reason { get; set; }
        public bool CreateJournalEntry { get; set; } = true;
    }

    public class AssetDisposalDto
    {
        public int AssetId { get; set; }
        public DateTime DisposalDate { get; set; } = DateTime.Today;
        public decimal ProceedsAmount { get; set; } = 0m; // ثمن البيع إن وجد
        public decimal DisposalExpenses { get; set; } = 0m; // مصاريف البيع
        public int? ProceedsCashBankAccountId { get; set; } // حساب التحصيل (الصندوق / البنك / العميل)
        public int? CustomerId { get; set; }
        public string? Reason { get; set; }
        public bool AutoPostJournalEntry { get; set; } = true;
    }

    public class AssetCapitalizeCipDto
    {
        public int AssetId { get; set; }
        public DateTime CapitalizationDate { get; set; } = DateTime.Today;
        public DateTime DepreciationStartDate { get; set; }
        public int UsefulLifeMonths { get; set; } = 60;
        public decimal ResidualValue { get; set; } = 0m;
        public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;
        public decimal DepreciationRate { get; set; } = 0m;
        public string? Notes { get; set; }
        public bool CreateReclassificationEntry { get; set; } = true;
    }

    public class AssetTransactionItemDto
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public AssetTransactionType TransactionType { get; set; }
        public string TransactionTypeName { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
        public string? FromBranchName { get; set; }
        public string? ToBranchName { get; set; }
        public string? FromCostCenterName { get; set; }
        public string? ToCostCenterName { get; set; }
        public string? FromCustodian { get; set; }
        public string? ToCustodian { get; set; }
        public decimal Amount { get; set; }
        public bool IsCapitalized { get; set; }
        public int? JournalEntryId { get; set; }
        public string? JournalEntryNumber { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedByUserName { get; set; }
    }

    // ─── 5. REPORTS DTOs ─────────────────────────────────────────────────────
    public class AssetReportFilterDto
    {
        public int? CompanyId { get; set; }
        public int? BranchId { get; set; }
        public int? AssetCategoryId { get; set; }
        public int? CostCenterId { get; set; }
        public AssetStatus? Status { get; set; }
        public DateTime? AsOfDate { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int? SpecificAssetId { get; set; }
        public string? SearchTerm { get; set; }
    }

    public class AssetRegisterReportItemDto
    {
        public string AssetCode { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string? CostCenterName { get; set; }
        public string? Custodian { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public DateTime PurchaseDate { get; set; }
        public DateTime? InServiceDate { get; set; }
        public decimal InitialCost { get; set; }
        public decimal AdditionalCost { get; set; }
        public decimal CurrentCost { get; set; }
        public decimal ResidualValue { get; set; }
        public decimal AccumulatedDepreciation { get; set; }
        public decimal BookValue { get; set; }
        public string DepreciationMethod { get; set; } = string.Empty;
        public int UsefulLifeMonths { get; set; }
    }
}
