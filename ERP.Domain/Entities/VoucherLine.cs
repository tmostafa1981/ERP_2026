using System;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class VoucherLine : IAuditableEntity
    {
        public int Id { get; set; }
        public int VoucherHeaderId { get; set; }
        public int LineNumber { get; set; }

        public VoucherLineType LineType { get; set; } = VoucherLineType.Customer;
        public int? SubLedgerId { get; set; } // معرف العميل أو المورد أو البنك
        public int AccountId { get; set; }     // حساب المراقبة أو الحساب المالي المباشر
        public int? CostCenterId { get; set; }

        public decimal AmountForeign { get; set; }
        public decimal AmountLocal { get; set; }

        // ─── بيانات طريقة الدفع والشيكات على مستوى السطر (Line-Level Payment & Cheque) ───
        public VoucherPaymentMethod PaymentMethod { get; set; } = VoucherPaymentMethod.Cash;
        public string? ChequeNumber { get; set; }
        public DateTime? ChequeDate { get; set; }
        public DateTime? DueDate { get; set; }
        public int? BankId { get; set; }
        public string? BankName { get; set; }
        public string? DrawerName { get; set; }
        public ChequeStatus ChequeStatus { get; set; } = ChequeStatus.None;

        public string? Description { get; set; }
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // العلاقات الملاحية
        public VoucherHeader? VoucherHeader { get; set; }
        public GLAccount? Account { get; set; }
        public CostCenter? CostCenter { get; set; }
        public Bank? Bank { get; set; }
    }
}
