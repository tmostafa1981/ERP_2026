using System;
using System.Threading.Tasks;
using ERP.Application.DTOs.Revaluation;

namespace ERP.Application.Interfaces
{
    public interface IForeignCurrencyRevaluationService
    {
        Task<RevaluationPreviewResultDto> PreviewBalancesAsync(LoadBalancesFilterDto filter, string userId);
        Task<(bool Success, string Message, int? HeaderId, string? RevaluationNumber)> CreateRevaluationAsync(CreateRevaluationDto dto, string userId);
        Task<RevaluationDetailsDto?> GetRevaluationByIdAsync(int id);
        Task<PagedResult<RevaluationHeaderListDto>> GetRevaluationsAsync(RevaluationListFilterDto filter);
        Task<(bool Success, string Message, int? JournalEntryId)> ApproveAndPostAsync(int id, string userId);
        Task<(bool Success, string Message, int? ReversalJournalEntryId)> ReverseAsync(int id, DateTime? reversalDate, string userId);
        Task<(bool Success, string Message)> DeleteAsync(int id, string userId);
        Task<RevaluationSettingsDto> GetDefaultSettingsAsync(int? branchId = null);
    }
}
