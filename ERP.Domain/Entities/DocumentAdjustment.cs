using System;
using System.Collections.Generic;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// جدول البنود الرئيسية للمصاريف الإضافية والخصومات القابلة للتهيئة (Master Document Adjustments).
    /// </summary>
    public class DocumentAdjustment : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;

        public AdjustmentType AdjustmentType { get; set; } = AdjustmentType.Discount;
        public AdjustmentCalculationMethod CalculationMethod { get; set; } = AdjustmentCalculationMethod.Percentage;

        public decimal? DefaultValue { get; set; } // نسبة أو مبلغ افتراضي

        public int GLAccountId { get; set; }

        public bool IsTaxable { get; set; } = false; // خاضع لضريبة القيمة المضافة أو يؤثر في وعاء الضريبة
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigations
        public GLAccount? GLAccount { get; set; }
        public ICollection<TransactionTypeAdjustment> AllowedTransactionTypes { get; set; } = new List<TransactionTypeAdjustment>();
        public ICollection<DocumentAdjustmentLine> DocumentLines { get; set; } = new List<DocumentAdjustmentLine>();

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar")
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : NameAr);
    }
}
