using System;
using System.Collections.Generic;

namespace ERP.Application.DTOs.Reports
{
    public class SubLedgerStatementDto
    {
        public int SubLedgerId { get; set; }
        public string SubLedgerType { get; set; } = string.Empty; // "Customer" or "Vendor"
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar") ? NameAr : NameEn;

        public string Phone { get; set; } = string.Empty;
        public string TaxNumber { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = string.Empty;
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
        public List<SubLedgerStatementLineDto> Lines { get; set; } = new List<SubLedgerStatementLineDto>();

        // Period Totals
        public decimal TotalPeriodDebitForeign { get; set; }
        public decimal TotalPeriodCreditForeign { get; set; }
        public decimal TotalPeriodDebitLocal { get; set; }
        public decimal TotalPeriodCreditLocal { get; set; }

        // Final Closing Balances
        public decimal ClosingBalanceForeign { get; set; }
        public decimal ClosingBalanceLocal { get; set; }
    }

    public class SubLedgerStatementLineDto
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
        public decimal BalanceForeign { get; set; }

        public decimal DebitLocal { get; set; }
        public decimal CreditLocal { get; set; }
        public decimal BalanceLocal { get; set; }
    }
}
