using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs.FixedAssets;

namespace ERP.Application.Interfaces
{
    public interface IFixedAssetService
    {
        Task<List<FixedAssetListDto>> GetAssetsAsync(FixedAssetFilterDto filter, string userId);
        Task<FixedAssetDetailsDto?> GetAssetByIdAsync(int id, string userId);
        Task<(bool Success, string Message, int? AssetId, string? AssetCode)> CreateAssetAsync(CreateFixedAssetDto dto, string userId);
        Task<(bool Success, string Message)> UpdateAssetAsync(UpdateFixedAssetDto dto, string userId);
        Task<(bool Success, string Message)> DeleteAssetAsync(int id, string userId);

        Task<(bool Success, string Message)> TransferAssetAsync(AssetTransferDto dto, string userId);
        Task<(bool Success, string Message, int? JournalEntryId)> AddAdditionalCostAsync(AssetAdditionalCostDto dto, string userId);
        Task<(bool Success, string Message, int? JournalEntryId)> DisposeAssetAsync(AssetDisposalDto dto, string userId);
        Task<(bool Success, string Message, int? JournalEntryId)> CapitalizeCipAssetAsync(AssetCapitalizeCipDto dto, string userId);
        Task<(bool Success, string Message, int? AssetId)> ConvertPurchaseInvoiceToAssetAsync(int documentHeaderId, int assetCategoryId, int usefulLifeMonths, string userId);

        Task<string> GenerateNextAssetCodeAsync(int branchId, int fiscalYearId, string fyYear);
    }
}
