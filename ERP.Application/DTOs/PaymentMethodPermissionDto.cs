using System.Collections.Generic;
using ERP.Domain.Enums;

namespace ERP.Application.DTOs
{
    public class PaymentMethodPermissionDto
    {
        public VoucherPaymentMethod PaymentMethod { get; set; }
        public string PaymentMethodName => PaymentMethod.ToString();
        public bool CanView { get; set; } = true;
        public bool CanCreate { get; set; } = true;
        public bool CanEdit { get; set; } = true;
        public bool CanDelete { get; set; } = true;
        public bool CanPost { get; set; } = true;
        public bool CanUnpost { get; set; } = true;
    }

    public class UserPaymentMethodMatrixDto
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public VoucherType? VoucherType { get; set; }
        public List<PaymentMethodPermissionDto> Permissions { get; set; } = new();
    }
}
