using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs.FixedAssets;

namespace ERP.Application.Interfaces
{
    public interface IFixedAssetReportService
    {
        Task<List<AssetRegisterReportItemDto>> GetAssetRegisterReportAsync(AssetReportFilterDto filter, string userId);
        Task<List<AssetDepreciationHistoryItemDto>> GetDepreciationHistoryReportAsync(AssetReportFilterDto filter, string userId);
        Task<List<AssetTransactionItemDto>> GetAssetMovementReportAsync(AssetReportFilterDto filter, string userId);
        Task<List<AssetRegisterReportItemDto>> GetDisposedAssetsReportAsync(AssetReportFilterDto filter, string userId);
        Task<List<AssetRegisterReportItemDto>> GetCipAssetsReportAsync(AssetReportFilterDto filter, string userId);
        Task<List<AssetRegisterReportItemDto>> GetUnapprovedOrPendingDepreciationReportAsync(AssetReportFilterDto filter, string userId);
    }
}
