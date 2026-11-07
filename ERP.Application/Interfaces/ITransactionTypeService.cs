using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;

namespace ERP.Application.Interfaces
{
    public interface ITransactionTypeService
    {
        Task<List<TransactionTypeDto>> GetAllowedTypesAsync(string userId, int branchId, TransactionModule? module = null);
        Task<TransactionTypeDto?> GetByIdAsync(int id, string userId);
        Task<TransactionTypeDto?> GetByCodeAsync(string code, string userId);
        Task<bool> HasPermissionAsync(string userId, int transactionTypeId, string action);

        // Management API
        Task<List<TransactionTypeDto>> GetAllConfiguredTypesAsync(TransactionModule? module = null);
        Task<UserTransactionTypePermissionMatrixDto> GetUserPermissionMatrixAsync(string userId, TransactionModule? module = null);
        Task SaveUserPermissionsAsync(string userId, List<TransactionTypeDto> permissions);
        Task<TransactionType> CreateTransactionTypeAsync(TransactionType entity);
        Task<TransactionType> UpdateTransactionTypeAsync(TransactionType entity);
    }
}
