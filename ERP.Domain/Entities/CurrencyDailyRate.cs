using System;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// سجل أسعار صرف العملات اليومية مقابل العملة الأساسية للنظام.
    /// Daily exchange rates for currencies relative to system base currency.
    /// </summary>
    public class CurrencyDailyRate : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public int CurrencyId { get; set; }

        /// <summary>
        /// تاريخ سعر الصرف (بدون وقت).
        /// </summary>
        public DateTime RateDate { get; set; }

        /// <summary>
        /// سعر الصرف مقابل العملة الأساسية للنظام (مثل الجنيه المصري EGP).
        /// </summary>
        public decimal ExchangeRate { get; set; } = 1.000000m;

        public string? Notes { get; set; }

        public string? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? UpdatedByUserId { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public Currency? Currency { get; set; }
    }
}
