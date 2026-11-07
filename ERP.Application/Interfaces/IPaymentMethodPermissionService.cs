using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Domain.Enums;

namespace ERP.Application.Interfaces
{
    public interface IPaymentMethodPermissionService
    {
        Task<Dictionary<VoucherPaymentMethod, PaymentMethodPermissionDto>> GetPermissionsAsync(string userId, VoucherType? voucherType = null);
        Task<UserPaymentMethodMatrixDto> GetUserMatrixAsync(string userId, VoucherType? voucherType = null);
        Task<bool> HasPermissionAsync(string userId, VoucherType voucherType, VoucherPaymentMethod method, string action);
        Task<(bool Allowed, string? ErrorMessage)> ValidateVoucherLinesPermissionAsync(string userId, VoucherType voucherType, IEnumerable<VoucherPaymentMethod> methods, string action);
        Task SavePermissionsAsync(string userId, VoucherType? voucherType, List<PaymentMethodPermissionDto> permissions);
        void InvalidateCache(string userId);
    }
}
