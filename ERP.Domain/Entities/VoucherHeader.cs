using System;
using System.Collections.Generic;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class VoucherHeader : IAuditableEntity, IMustHaveBranch, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        // Branch Security & Fiscal Year Validation
        public int BranchId { get; set; }
        public int FiscalYearId { get; set; }

        public VoucherType VoucherType { get; set; } = VoucherType.Receipt;
        public string VoucherNumber { get; set; } = string.Empty;
        public DateTime VoucherDate { get; set; } = DateTime.UtcNow.Date;

        // طرف النقدية / البنك (حساب الحركة المالي)
        public int CashBankAccountId { get; set; }
        public VoucherPaymentMethod PaymentMethod { get; set; } = VoucherPaymentMethod.Cash;

        // تفاصيل الشيك أو التحويل البنكي
        public string? ChequeNumber { get; set; }
        public DateTime? ChequeDate { get; set; }
        public DateTime? ChequeDueDate { get; set; }
        public string? BankName { get; set; }

        // العملة وسعر الصرف
        public int CurrencyId { get; set; }
        public decimal ExchangeRate { get; set; } = 1.000000m;
        public decimal TotalAmountForeign { get; set; }
        public decimal TotalAmountLocal { get; set; }

        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }
        public VoucherStatus Status { get; set; } = VoucherStatus.Draft;

        // الربط بقيد اليومية العام المولد آلياً
        public int? JournalEntryHeaderId { get; set; }
        public DateTime? PostedAt { get; set; }
        public string? PostedByUserId { get; set; }

        // التتبع والتدقيق
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByUserId { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // العلاقات الملاحية
        public Branch? Branch { get; set; }
        public FiscalYear? FiscalYear { get; set; }
        public GLAccount? CashBankAccount { get; set; }
        public Currency? Currency { get; set; }
        public JournalEntryHeader? JournalEntryHeader { get; set; }
        public ICollection<VoucherLine> Lines { get; set; } = new List<VoucherLine>();
    }
}
