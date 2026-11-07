using System;
using System.Collections.Generic;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// رأس عملية إعادة تقييم أرصدة العملات الأجنبية.
    /// Foreign Currency Revaluation Header.
    /// </summary>
    public class RevaluationHeader : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// رقم عملية إعادة التقييم المولد آلياً.
        /// </summary>
        public string RevaluationNumber { get; set; } = string.Empty;

        /// <summary>
        /// تاريخ التقييم المحدد من قبل المستخدم.
        /// </summary>
        public DateTime RevaluationDate { get; set; } = DateTime.Today;

        public int CompanyId { get; set; }

        /// <summary>
        /// الفرع المحدد، أو null إذا كان التقييم شاملاً لكافة الفروع المصرح بها.
        /// </summary>
        public int? BranchId { get; set; }

        public int FiscalYearId { get; set; }

        /// <summary>
        /// نطاق التقييم: عملاء، موردين، حسابات عامة، أو الكل.
        /// </summary>
        public RevaluationScope Scope { get; set; } = RevaluationScope.All;

        /// <summary>
        /// العملة المحددة للتقييم، أو null لكافة العملات الأجنبية المفتوحة.
        /// </summary>
        public int? CurrencyId { get; set; }

        /// <summary>
        /// حساب أرباح فروق العملة غير المحققة (إيرادات).
        /// </summary>
        public int UnrealizedGainAccountId { get; set; }

        /// <summary>
        /// حساب خسائر فروق العملة غير المحققة (مصروفات).
        /// </summary>
        public int UnrealizedLossAccountId { get; set; }

        /// <summary>
        /// إجمالي أرباح فروق العملة الناتجة.
        /// </summary>
        public decimal TotalGain { get; set; }

        /// <summary>
        /// إجمالي خسائر فروق العملة الناتجة.
        /// </summary>
        public decimal TotalLoss { get; set; }

        /// <summary>
        /// صافي فرق إعادة التقييم (أرباح - خسائر).
        /// </summary>
        public decimal NetDifference { get; set; }

        /// <summary>
        /// حالة العملية: مسودة، معتمدة، معكوسة، ملغاة.
        /// </summary>
        public RevaluationStatus Status { get; set; } = RevaluationStatus.Draft;

        /// <summary>
        /// قيد اليومية العام الأساسي المولد آلياً عند الاعتماد.
        /// </summary>
        public int? JournalEntryId { get; set; }

        /// <summary>
        /// القيد العكسي المولد آلياً في بداية الفترة التالية.
        /// </summary>
        public int? ReversalJournalEntryId { get; set; }

        /// <summary>
        /// تاريخ القيد العكسي.
        /// </summary>
        public DateTime? ReversalDate { get; set; }

        public string? Notes { get; set; }

        public string? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? ApprovedByUserId { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? ReversedByUserId { get; set; }
        public DateTime? ReversedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Navigation Properties
        public Company? Company { get; set; }
        public Branch? Branch { get; set; }
        public FiscalYear? FiscalYear { get; set; }
        public Currency? Currency { get; set; }
        public GLAccount? UnrealizedGainAccount { get; set; }
        public GLAccount? UnrealizedLossAccount { get; set; }
        public JournalEntryHeader? JournalEntry { get; set; }
        public JournalEntryHeader? ReversalJournalEntry { get; set; }

        public ICollection<RevaluationLine> Lines { get; set; } = new List<RevaluationLine>();
    }
}
