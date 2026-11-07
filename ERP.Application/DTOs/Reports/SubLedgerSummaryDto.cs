using System;
using System.Collections.Generic;

namespace ERP.Application.DTOs.Reports
{
    public class SubLedgerSummaryFilterDto : ReportFilterDto
    {
        /// <summary>
        /// نوع الأطراف المستعلم عنها: "All" (الكل)، "Customer" (العملاء فقط)، "Vendor" (الموردين فقط)
        /// </summary>
        public string PartyType { get; set; } = "All";

        /// <summary>
        /// قصر النتائج على الأطراف التي لها أرصدة غير صفرية فقط
        /// </summary>
        public bool OnlyWithBalances { get; set; } = false;

        /// <summary>
        /// بحث حر في كود الطرف أو اسمه
        /// </summary>
        public string? SearchTerm { get; set; }
    }

    public class SubLedgerSummaryDto
    {
        public string BranchName { get; set; } = string.Empty;
        public string FiscalYearName { get; set; } = string.Empty;
        public string? CurrencyCode { get; set; }
        public string PartyType { get; set; } = "All";
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public ReportDisplayMode Mode { get; set; } = ReportDisplayMode.Both;

        public List<SubLedgerSummaryItemDto> Items { get; set; } = new List<SubLedgerSummaryItemDto>();

        // ─── كروت المؤشرات الإحصائية العامة (KPIs) ──────────────────────────────────
        public int TotalCustomersCount { get; set; }
        public int TotalVendorsCount { get; set; }
        public decimal TotalCustomerReceivablesLocal { get; set; } // إجمالي مستحقات على العملاء (مديونيات العملاء لنا)
        public decimal TotalVendorPayablesLocal { get; set; }     // إجمالي مستحقات للموردين (التزاماتنا للموردين)
        public decimal NetOverallBalanceLocal { get; set; }       // صافي الموقف المالي العام

        // ─── إجماليات العملة المحلية (Local Currency EGP) ──────────────────────────
        public decimal TotalOpeningDebitLocal { get; set; }
        public decimal TotalOpeningCreditLocal { get; set; }
        public decimal TotalOpeningBalanceLocal => TotalOpeningDebitLocal - TotalOpeningCreditLocal;

        public decimal TotalPeriodDebitLocal { get; set; }
        public decimal TotalPeriodCreditLocal { get; set; }
        public decimal TotalPeriodNetLocal => TotalPeriodDebitLocal - TotalPeriodCreditLocal;

        public decimal TotalClosingDebitLocal { get; set; }
        public decimal TotalClosingCreditLocal { get; set; }
        public decimal TotalClosingBalanceLocal => TotalClosingDebitLocal - TotalClosingCreditLocal;

        // ─── إجماليات العملة الأجنبية (عند التصفية بعملة محددة) ────────────────────
        public decimal TotalOpeningDebitForeign { get; set; }
        public decimal TotalOpeningCreditForeign { get; set; }
        public decimal TotalOpeningBalanceForeign => TotalOpeningDebitForeign - TotalOpeningCreditForeign;

        public decimal TotalPeriodDebitForeign { get; set; }
        public decimal TotalPeriodCreditForeign { get; set; }
        public decimal TotalPeriodNetForeign => TotalPeriodDebitForeign - TotalPeriodCreditForeign;

        public decimal TotalClosingDebitForeign { get; set; }
        public decimal TotalClosingCreditForeign { get; set; }
        public decimal TotalClosingBalanceForeign => TotalClosingDebitForeign - TotalClosingCreditForeign;
    }

    public class SubLedgerSummaryItemDto
    {
        public int PartyId { get; set; }
        public string PartyType { get; set; } = string.Empty; // "Customer" or "Vendor"
        public string PartyTypeText => PartyType == "Customer" ? "عميل" : "مورد";
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar") ? NameAr : (string.IsNullOrWhiteSpace(NameEn) ? NameAr : NameEn);

        public string Phone { get; set; } = string.Empty;
        public string TaxNumber { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public int? BranchId { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public int? CurrencyId { get; set; }
        public int? FinancialAccountId { get; set; }
        public string ControlAccountCode { get; set; } = string.Empty;
        public string ControlAccountName { get; set; } = string.Empty;

        // ─── أرصدة أول المدة (Opening Balances) ────────────────────────────────────
        public decimal OpeningDebitLocal { get; set; }
        public decimal OpeningCreditLocal { get; set; }
        public decimal OpeningBalanceLocal => OpeningDebitLocal - OpeningCreditLocal;

        public decimal OpeningDebitForeign { get; set; }
        public decimal OpeningCreditForeign { get; set; }
        public decimal OpeningBalanceForeign => OpeningDebitForeign - OpeningCreditForeign;

        // ─── حركات الفترة (Period Movements) ──────────────────────────────────────
        public decimal PeriodDebitLocal { get; set; }
        public decimal PeriodCreditLocal { get; set; }
        public decimal PeriodNetLocal => PeriodDebitLocal - PeriodCreditLocal;

        public decimal PeriodDebitForeign { get; set; }
        public decimal PeriodCreditForeign { get; set; }
        public decimal PeriodNetForeign => PeriodDebitForeign - PeriodCreditForeign;

        // ─── الأرصدة الختامية الحالية (Closing Balances) ───────────────────────────
        public decimal ClosingDebitLocal => OpeningDebitLocal + PeriodDebitLocal;
        public decimal ClosingCreditLocal => OpeningCreditLocal + PeriodCreditLocal;
        public decimal ClosingBalanceLocal => ClosingDebitLocal - ClosingCreditLocal;

        public decimal ClosingDebitForeign => OpeningDebitForeign + PeriodDebitForeign;
        public decimal ClosingCreditForeign => OpeningCreditForeign + PeriodCreditForeign;
        public decimal ClosingBalanceForeign => ClosingDebitForeign - ClosingCreditForeign;

        // ─── حالة وطبيعة الرصيد ──────────────────────────────────────────────────
        public string BalanceStatusText
        {
            get
            {
                if (Math.Abs(ClosingBalanceLocal) < 0.0001m) return "متزن (0.00)";
                return ClosingBalanceLocal > 0 ? "مدين (لنا)" : "دائن (علينا)";
            }
        }
    }
}
