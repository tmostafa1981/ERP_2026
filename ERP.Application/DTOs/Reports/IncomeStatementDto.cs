using System;
using System.Collections.Generic;

namespace ERP.Application.DTOs.Reports
{
    public class IncomeStatementDto
    {
        public string BranchName { get; set; } = string.Empty;
        public string FiscalYearName { get; set; } = string.Empty;
        public string? CurrencyCode { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public ReportDisplayMode Mode { get; set; } = ReportDisplayMode.LocalOnly;

        // --- Major Income Statement Sections ---
        // 1. Revenues (الإيرادات)
        public decimal TotalOperatingRevenues { get; set; }
        public decimal TotalOtherRevenues { get; set; }
        public decimal TotalRevenues => TotalOperatingRevenues + TotalOtherRevenues;

        // 2. Cost of Sales / Cost of Goods Sold (تكلفة المبيعات)
        public decimal TotalCostOfSales { get; set; }

        // 3. Gross Profit / Loss (مجمل الربح / الخسارة)
        public decimal GrossProfit => TotalRevenues - TotalCostOfSales;
        public decimal GrossProfitMargin => TotalRevenues > 0 ? (GrossProfit / TotalRevenues) * 100m : 0m;

        // 4. Operating Expenses (المصروفات التشغيلية)
        public decimal TotalGeneralAndAdminExpenses { get; set; }
        public decimal TotalSellingAndMarketingExpenses { get; set; }
        public decimal TotalOtherOperatingExpenses { get; set; }
        public decimal TotalOperatingExpenses => TotalGeneralAndAdminExpenses + TotalSellingAndMarketingExpenses + TotalOtherOperatingExpenses;

        // 5. Operating Income / EBIT (صافي الربح التشغيلي)
        public decimal OperatingIncome => GrossProfit - TotalOperatingExpenses;
        public decimal OperatingMargin => TotalRevenues > 0 ? (OperatingIncome / TotalRevenues) * 100m : 0m;

        // 6. Other Income / Expenses (إيرادات ومصروفات غير تشغيلية)
        public decimal TotalFinancingExpenses { get; set; }
        public decimal TotalOtherExpenses { get; set; }

        // 7. Net Profit / Loss (صافي الربح / الخسارة للفترة)
        public decimal NetIncome => OperatingIncome - TotalFinancingExpenses - TotalOtherExpenses;
        public decimal NetProfitMargin => TotalRevenues > 0 ? (NetIncome / TotalRevenues) * 100m : 0m;

        // Detail items grouped for display
        public List<FinancialStatementItemDto> RevenueItems { get; set; } = new();
        public List<FinancialStatementItemDto> CostOfSalesItems { get; set; } = new();
        public List<FinancialStatementItemDto> OperatingExpenseItems { get; set; } = new();
        public List<FinancialStatementItemDto> OtherExpenseItems { get; set; } = new();
    }

    public class FinancialStatementItemDto
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountNameAr { get; set; } = string.Empty;
        public string AccountNameEn { get; set; } = string.Empty;
        public string LocalizedAccountName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar") ? AccountNameAr : AccountNameEn;

        public int Level { get; set; }
        public bool IsGroup { get; set; }
        public string? ParentCode { get; set; }
        public string Section { get; set; } = string.Empty;

        public decimal AmountLocal { get; set; }
        public decimal AmountForeign { get; set; }
        public decimal PercentageOfRevenue { get; set; }
    }
}
