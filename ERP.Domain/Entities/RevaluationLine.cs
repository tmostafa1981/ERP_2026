using System;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// سطر تفاصيل إعادة تقييم رصيد عملة أجنبية لحساب أو عميل أو مورد.
    /// Foreign Currency Revaluation Line Detail.
    /// </summary>
    public class RevaluationLine : IAuditableEntity
    {
        public int Id { get; set; }
        public int RevaluationHeaderId { get; set; }
        public int LineNumber { get; set; }

        /// <summary>
        /// نوع المصدر: عميل، مورد، حساب عام.
        /// </summary>
        public RevaluationSourceType SourceType { get; set; }

        /// <summary>
        /// معرف المصدر (CustomerId / VendorId / GLAccountId).
        /// </summary>
        public int SourceId { get; set; }

        public string SourceCode { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;

        /// <summary>
        /// الحساب المحاسبي المتأثر في شجرة الحسابات (الحساب الرقابي أو حساب الحركة).
        /// </summary>
        public int AccountId { get; set; }

        public int? CustomerId { get; set; }
        public int? VendorId { get; set; }

        /// <summary>
        /// الفرع التابع له الرصيد.
        /// </summary>
        public int BranchId { get; set; }

        /// <summary>
        /// العملة الأجنبية للرصيد.
        /// </summary>
        public int CurrencyId { get; set; }

        /// <summary>
        /// الرصيد المفتوح بالعملة الأجنبية حتى تاريخ التقييم.
        /// </summary>
        public decimal ForeignBalance { get; set; }

        /// <summary>
        /// القيمة الدفترية الحالية بالعملة المحلية (EGP) حتى تاريخ التقييم.
        /// </summary>
        public decimal CarryingLocalAmount { get; set; }

        /// <summary>
        /// سعر صرف العملة الأجنبية بتاريخ التقييم (المجمد والمستخدم في الحساب).
        /// </summary>
        public decimal ExchangeRate { get; set; }

        /// <summary>
        /// القيمة الجديدة المعاد تقييمها بالعملة المحلية (ForeignBalance * ExchangeRate).
        /// </summary>
        public decimal RevaluedLocalAmount { get; set; }

        /// <summary>
        /// مبلغ فرق العملة (|RevaluedLocalAmount - CarryingLocalAmount|).
        /// </summary>
        public decimal DifferenceAmount { get; set; }

        /// <summary>
        /// نوع الفرق: ربح، خسارة، بدون فرق.
        /// </summary>
        public RevaluationDifferenceType DifferenceType { get; set; } = RevaluationDifferenceType.None;

        /// <summary>
        /// طبيعة الرصيد المحاسبية (مدين أو دائن).
        /// </summary>
        public AccountNature BalanceNature { get; set; } = AccountNature.Debit;

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation Properties
        public RevaluationHeader? RevaluationHeader { get; set; }
        public GLAccount? Account { get; set; }
        public Customer? Customer { get; set; }
        public Vendor? Vendor { get; set; }
        public Branch? Branch { get; set; }
        public Currency? Currency { get; set; }
    }
}
