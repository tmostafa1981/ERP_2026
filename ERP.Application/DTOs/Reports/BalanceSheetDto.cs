using System;
using System.Collections.Generic;

namespace ERP.Application.DTOs.Reports
{
    public class BalanceSheetDto
    {
        public string BranchName { get; set; } = string.Empty;
        public string FiscalYearName { get; set; } = string.Empty;
        public string? CurrencyCode { get; set; }
        public DateTime? AsOfDate { get; set; }
        public ReportDisplayMode Mode { get; set; } = ReportDisplayMode.LocalOnly;

        // --- 1. ASSETS (الأصول) ---
        // Current Assets (الأصول المتداولة)
        public decimal TotalCashAndBanks { get; set; }
        public decimal TotalAccountsReceivable { get; set; }
        public decimal TotalInventory { get; set; }
        public decimal TotalOtherCurrentAssets { get; set; }
        public decimal TotalCurrentAssets => TotalCashAndBanks + TotalAccountsReceivable + TotalInventory + TotalOtherCurrentAssets;

        // Non-Current Assets (الأصول غير المتداولة / الثابتة)
        public decimal TotalFixedAssets { get; set; }
        public decimal TotalAccumulatedDepreciation { get; set; }
        public decimal NetFixedAssets => TotalFixedAssets - TotalAccumulatedDepreciation;
        public decimal TotalOtherNonCurrentAssets { get; set; }
        public decimal TotalNonCurrentAssets => NetFixedAssets + TotalOtherNonCurrentAssets;

        // TOTAL ASSETS (إجمالي الأصول)
        public decimal TotalAssets => TotalCurrentAssets + TotalNonCurrentAssets;

        // --- 2. LIABILITIES (الخصوم والالتزامات) ---
        // Current Liabilities (الالتزامات المتداولة)
        public decimal TotalAccountsPayable { get; set; }
        public decimal TotalAccruedAndOtherCurrentLiabilities { get; set; }
        public decimal TotalCurrentLiabilities => TotalAccountsPayable + TotalAccruedAndOtherCurrentLiabilities;

        // Long-Term Liabilities (الالتزامات غير المتداولة / طويلة الأجل)
        public decimal TotalLongTermLiabilities { get; set; }

        // TOTAL LIABILITIES (إجمالي الالتزامات)
        public decimal TotalLiabilities => TotalCurrentLiabilities + TotalLongTermLiabilities;

        // --- 3. EQUITY (حقوق الملكية) ---
        public decimal TotalCapital { get; set; }
        public decimal TotalReserves { get; set; }
        public decimal TotalRetainedEarnings { get; set; }
        
        /// <summary>
        /// صافي نتيجة النشاط للفترة (ربح أو خسارة) المحولة من قائمة الدخل
        /// </summary>
        public decimal CurrentPeriodNetIncome { get; set; }

        // TOTAL EQUITY (إجمالي حقوق الملكية)
        public decimal TotalEquity => TotalCapital + TotalReserves + TotalRetainedEarnings + CurrentPeriodNetIncome;

        // --- TOTAL LIABILITIES & EQUITY (إجمالي الخصوم وحقوق الملكية) ---
        public decimal TotalLiabilitiesAndEquity => TotalLiabilities + TotalEquity;

        // Balance Verification: Total Assets == Total Liabilities + Total Equity
        public decimal BalanceDifference => TotalAssets - TotalLiabilitiesAndEquity;
        public bool IsBalanced => Math.Abs(BalanceDifference) < 0.0001m;

        // Detailed breakdowns for display
        public List<FinancialStatementItemDto> CurrentAssetItems { get; set; } = new();
        public List<FinancialStatementItemDto> NonCurrentAssetItems { get; set; } = new();
        public List<FinancialStatementItemDto> CurrentLiabilityItems { get; set; } = new();
        public List<FinancialStatementItemDto> NonCurrentLiabilityItems { get; set; } = new();
        public List<FinancialStatementItemDto> EquityItems { get; set; } = new();
    }
}
