using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs.FixedAssets;

namespace ERP.Application.Interfaces
{
    public interface IFixedAssetCategoryService
    {
        Task<List<AssetCategoryDto>> GetAllAsync(int? branchId = null);
        Task<AssetCategoryDto?> GetByIdAsync(int id);
        Task<(bool Success, string Message, int? CategoryId)> CreateAsync(CreateAssetCategoryDto dto, string userId);
        Task<(bool Success, string Message)> UpdateAsync(int id, CreateAssetCategoryDto dto, string userId);
        Task<(bool Success, string Message)> DeleteAsync(int id, string userId);
    }
}
