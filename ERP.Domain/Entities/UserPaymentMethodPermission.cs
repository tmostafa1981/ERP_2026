using System;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class UserPaymentMethodPermission : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Nullable: null means applicable to both Receipt and Payment vouchers.
        /// 1: Receipt Voucher only, 2: Payment Voucher only.
        /// </summary>
        public VoucherType? VoucherType { get; set; }

        public VoucherPaymentMethod PaymentMethod { get; set; }

        public bool CanView { get; set; } = true;
        public bool CanCreate { get; set; } = true;
        public bool CanEdit { get; set; } = true;
        public bool CanDelete { get; set; } = true;
        public bool CanPost { get; set; } = true;
        public bool CanUnpost { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
