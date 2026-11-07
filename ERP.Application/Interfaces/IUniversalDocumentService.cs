using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;

namespace ERP.Application.Interfaces
{
    public interface IUniversalDocumentService
    {
        Task<(bool Success, string Message, int? DocumentId, string? DocumentNumber)> CreateDocumentAsync(DocumentHeaderDto dto, string userId, bool postImmediately = false);
        Task<(bool Success, string Message)> UpdateDocumentAsync(DocumentHeaderDto dto, string userId);
        Task<(bool Success, string Message)> DeleteDocumentAsync(int documentId, string userId);
        Task<(bool Success, string Message, int? JournalEntryId)> PostDocumentAsync(int documentId, string userId);
        Task<(bool Success, string Message)> UnpostDocumentAsync(int documentId, string userId);

        Task<DocumentHeaderDto?> GetDocumentByIdAsync(int documentId);
        Task<List<DocumentHeaderDto>> GetDocumentsListAsync(int branchId, TransactionModule? module = null, int? transactionTypeId = null, DocumentStatus? status = null);
        Task<string> GenerateNextDocumentNumberAsync(int transactionTypeId, int branchId, int fiscalYearId);
    }
}
