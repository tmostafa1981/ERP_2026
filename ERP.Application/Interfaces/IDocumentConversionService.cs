using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IDocumentConversionService
    {
        Task<List<AvailableForConversionDocumentDto>> GetAvailableSourceDocumentsAsync(int targetTransactionTypeId, int branchId, int? partyId = null);
        Task<(bool Success, string Message, int? NewDocumentId, string? DocumentNumber)> ConvertDocumentsAsync(ConvertDocumentRequestDto request, string userId, int branchId);
        Task<bool> CanConvertAsync(int sourceTransactionTypeId, int targetTransactionTypeId);
    }
}
