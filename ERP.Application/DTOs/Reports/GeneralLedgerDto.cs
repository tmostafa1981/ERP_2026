using System;
using System.Collections.Generic;

namespace ERP.Application.DTOs.Reports
{
    public class GeneralLedgerDto
    {
        public string BranchName { get; set; } = string.Empty;
        public string FiscalYearName { get; set; } = string.Empty;
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public ReportDisplayMode Mode { get; set; } = ReportDisplayMode.Both;

        public List<GeneralLedgerAccountGroupDto> Accounts { get; set; } = new List<GeneralLedgerAccountGroupDto>();

        public decimal GrandTotalDebitForeign { get; set; }
        public decimal GrandTotalCreditForeign { get; set; }
        public decimal GrandTotalDebitLocal { get; set; }
        public decimal GrandTotalCreditLocal { get; set; }
    }

    public class GeneralLedgerAccountGroupDto
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountNameAr { get; set; } = string.Empty;
        public string AccountNameEn { get; set; } = string.Empty;
        public string LocalizedAccountName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar") ? AccountNameAr : AccountNameEn;

        public string CurrencyCode { get; set; } = string.Empty;

        // Opening Balances
        public decimal OpeningBalanceForeign { get; set; }
        public decimal OpeningBalanceLocal { get; set; }

        public List<AccountStatementLineDto> Lines { get; set; } = new List<AccountStatementLineDto>();

        // Account Totals
        public decimal TotalDebitForeign { get; set; }
        public decimal TotalCreditForeign { get; set; }
        public decimal ClosingBalanceForeign { get; set; }

        public decimal TotalDebitLocal { get; set; }
        public decimal TotalCreditLocal { get; set; }
        public decimal ClosingBalanceLocal { get; set; }
    }
}
