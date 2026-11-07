using System;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// جدول ربط أنواع الحركات والمستندات ببنود المصاريف والخصومات المسموح بها (Transaction Type Allowed Adjustments).
    /// </summary>
    public class TransactionTypeAdjustment : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public int TransactionTypeId { get; set; }
        public int DocumentAdjustmentId { get; set; }

        public bool IsDefault { get; set; } = false; // ينزل تلقائياً عند فتح شاشة الفاتورة
        public bool IsMandatory { get; set; } = false; // إجباري على المستخدم
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigations
        public TransactionType? TransactionType { get; set; }
        public DocumentAdjustment? DocumentAdjustment { get; set; }
    }
}
