using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IDocumentAdjustmentService
    {
        Task<List<DocumentAdjustmentDto>> GetAllAdjustmentsAsync(bool activeOnly = false);
        Task<DocumentAdjustmentDto?> GetAdjustmentByIdAsync(int id);
        Task<DocumentAdjustmentDto> CreateAdjustmentAsync(CreateDocumentAdjustmentDto dto);
        Task<DocumentAdjustmentDto> UpdateAdjustmentAsync(CreateDocumentAdjustmentDto dto);
        Task<bool> DeleteAdjustmentAsync(int id);

        Task<List<DocumentAdjustmentDto>> GetAllowedAdjustmentsByTransactionTypeAsync(int transactionTypeId);
        Task<TransactionTypeAdjustmentConfigDto> GetTransactionTypeAdjustmentConfigAsync(int transactionTypeId);
        Task SaveTransactionTypeAdjustmentConfigAsync(TransactionTypeAdjustmentConfigDto config);
    }
}
