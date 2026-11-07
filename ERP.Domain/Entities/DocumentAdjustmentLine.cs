using System;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// سطور المصاريف الإضافية والخصومات المسجلة على الفاتورة/المستند (Document Adjustment Lines).
    /// </summary>
    public class DocumentAdjustmentLine : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public int DocumentHeaderId { get; set; }
        public int DocumentAdjustmentId { get; set; }

        public int LineNumber { get; set; } = 1;

        public AdjustmentType AdjustmentType { get; set; } = AdjustmentType.Discount;
        public AdjustmentCalculationMethod CalculationMethod { get; set; } = AdjustmentCalculationMethod.Percentage;

        public decimal? Percentage { get; set; } // نسبة الخصم أو الإضافة % إن وُجدت
        public decimal Amount { get; set; } = 0m; // المبلغ بعملة المستند
        public decimal AmountLocal { get; set; } = 0m; // المبلغ بالعملة المحلية

        public int GLAccountId { get; set; } // الحساب المحاسبي الذي يتم التوجيه إليه في القيد

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigations
        public DocumentHeader? DocumentHeader { get; set; }
        public DocumentAdjustment? DocumentAdjustment { get; set; }
        public GLAccount? GLAccount { get; set; }
    }
}
