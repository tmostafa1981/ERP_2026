using System;
using System.Collections.Generic;

namespace ERP.Application.DTOs.Reports
{
    public class TrialBalanceDto
    {
        public string BranchName { get; set; } = string.Empty;
        public string FiscalYearName { get; set; } = string.Empty;
        public string? CurrencyCode { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public ReportDisplayMode Mode { get; set; } = ReportDisplayMode.LocalOnly;

        public List<TrialBalanceItemDto> Items { get; set; } = new List<TrialBalanceItemDto>();

        // Local Currency Totals
        public decimal TotalOpeningDebitLocal { get; set; }
        public decimal TotalOpeningCreditLocal { get; set; }
        public decimal TotalPeriodDebitLocal { get; set; }
        public decimal TotalPeriodCreditLocal { get; set; }
        public decimal TotalClosingDebitLocal { get; set; }
        public decimal TotalClosingCreditLocal { get; set; }

        // Foreign Currency Totals (when filtered by currency or foreign mode)
        public decimal TotalOpeningDebitForeign { get; set; }
        public decimal TotalOpeningCreditForeign { get; set; }
        public decimal TotalPeriodDebitForeign { get; set; }
        public decimal TotalPeriodCreditForeign { get; set; }
        public decimal TotalClosingDebitForeign { get; set; }
        public decimal TotalClosingCreditForeign { get; set; }

        public bool IsBalanced
        {
            get
            {
                if (Mode == ReportDisplayMode.ForeignOnly)
                {
                    return Math.Abs(TotalPeriodDebitForeign - TotalPeriodCreditForeign) < 0.0001m &&
                           Math.Abs(TotalClosingDebitForeign - TotalClosingCreditForeign) < 0.0001m;
                }

                return Math.Abs(TotalOpeningDebitLocal - TotalOpeningCreditLocal) < 0.0001m &&
                       Math.Abs(TotalPeriodDebitLocal - TotalPeriodCreditLocal) < 0.0001m &&
                       Math.Abs(TotalClosingDebitLocal - TotalClosingCreditLocal) < 0.0001m;
            }
        }
    }

    public class TrialBalanceItemDto
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
        public string AccountNature { get; set; } = string.Empty;

        // Foreign Currency
        public string? CurrencyCode { get; set; }
        public decimal OpeningDebitForeign { get; set; }
        public decimal OpeningCreditForeign { get; set; }
        public decimal PeriodDebitForeign { get; set; }
        public decimal PeriodCreditForeign { get; set; }
        public decimal ClosingDebitForeign { get; set; }
        public decimal ClosingCreditForeign { get; set; }
        public decimal NetClosingForeign { get; set; }

        // Local Currency
        public decimal OpeningDebitLocal { get; set; }
        public decimal OpeningCreditLocal { get; set; }
        public decimal PeriodDebitLocal { get; set; }
        public decimal PeriodCreditLocal { get; set; }
        public decimal ClosingDebitLocal { get; set; }
        public decimal ClosingCreditLocal { get; set; }
        public decimal NetClosingLocal { get; set; }
    }
}
