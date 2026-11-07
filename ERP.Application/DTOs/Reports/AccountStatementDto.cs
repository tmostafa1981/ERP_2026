using System;
using System.Collections.Generic;

namespace ERP.Application.DTOs.Reports
{
    public class AccountStatementDto
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountNameAr { get; set; } = string.Empty;
        public string AccountNameEn { get; set; } = string.Empty;
        public string LocalizedAccountName => 
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar") ? AccountNameAr : AccountNameEn;
        
        public string CurrencyCode { get; set; } = string.Empty;
        public string CurrencyName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string FiscalYearName { get; set; } = string.Empty;
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public ReportDisplayMode Mode { get; set; } = ReportDisplayMode.Both;

        // Opening Balances
        public decimal OpeningDebitForeign { get; set; }
        public decimal OpeningCreditForeign { get; set; }
        public decimal OpeningBalanceForeign { get; set; }

        public decimal OpeningDebitLocal { get; set; }
        public decimal OpeningCreditLocal { get; set; }
        public decimal OpeningBalanceLocal { get; set; }

        // Statement Lines
        public List<AccountStatementLineDto> Lines { get; set; } = new List<AccountStatementLineDto>();

        // Period Totals
        public decimal TotalPeriodDebitForeign { get; set; }
        public decimal TotalPeriodCreditForeign { get; set; }
        public decimal TotalPeriodDebitLocal { get; set; }
        public decimal TotalPeriodCreditLocal { get; set; }

        // Final Closing Balances
        public decimal ClosingBalanceForeign { get; set; }
        public decimal ClosingBalanceLocal { get; set; }
    }

    public class AccountStatementLineDto
    {
        public int Id { get; set; }
        public int JournalEntryHeaderId { get; set; }
        public string EntryNumber { get; set; } = string.Empty;
        public DateTime EntryDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? ReferenceNumber { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public decimal ExchangeRate { get; set; } = 1.0m;

        public decimal DebitForeign { get; set; }
        public decimal CreditForeign { get; set; }
        public decimal RunningBalanceForeign { get; set; }

        public decimal DebitLocal { get; set; }
        public decimal CreditLocal { get; set; }
        public decimal RunningBalanceLocal { get; set; }

        public string SubLedgerName { get; set; } = string.Empty;
    }
}
