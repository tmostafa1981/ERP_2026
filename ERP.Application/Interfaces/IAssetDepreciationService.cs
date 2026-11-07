using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs.FixedAssets;
using ERP.Domain.Entities;

namespace ERP.Application.Interfaces
{
    public interface IAssetDepreciationService
    {
        Task<DepreciationCalculationResultDto> PreviewDepreciationRunAsync(RunDepreciationFilterDto filter, string userId);
        Task<(bool Success, string Message, int? RunId, string? RunNumber, int? JournalEntryId)> ExecuteDepreciationRunAsync(ExecuteDepreciationRunDto dto, string userId);
        Task<(bool Success, string Message, int? JournalEntryId)> ApproveDepreciationRunAsync(int runId, string userId);
        Task<(bool Success, string Message, int? ReversalJournalEntryId)> ReverseDepreciationRunAsync(int runId, DateTime reversalDate, string userId);
        Task<List<DepreciationRunListDto>> GetDepreciationRunsAsync(int companyId, int? branchId, int? fiscalYearId, string userId);
        Task<DepreciationRunDetailsDto?> GetDepreciationRunByIdAsync(int id, string userId);

        /// <summary>
        /// Pure depreciation calculation engine method for a single asset and period
        /// </summary>
        decimal CalculatePeriodDepreciation(FixedAsset asset, DateTime runDate, DateTime periodStartDate, DateTime periodEndDate);
    }
}
