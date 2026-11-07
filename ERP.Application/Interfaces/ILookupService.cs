using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs.Lookups;

namespace ERP.Application.Interfaces
{
    public interface ILookupService
    {
        Task<LookupPagedResult<LookupResultDto>> SearchAsync(LookupSearchRequest request, CancellationToken cancellationToken = default);
        Task<LookupResultDto?> GetByIdAsync(string entityType, int id, CancellationToken cancellationToken = default);
        Task<LookupResultDto?> GetByStringIdAsync(string entityType, string id, CancellationToken cancellationToken = default);
        Task<LookupResultDto?> GetByCodeAsync(string entityType, string code, int? branchId = null, CancellationToken cancellationToken = default);
        Task<BatchResolveResponseDto> ResolveBatchAsync(BatchResolveRequestDto request, CancellationToken cancellationToken = default);
    }
}
