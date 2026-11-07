using System.Threading.Tasks;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface ICodeSequenceService
    {
        /// <summary>
        /// Atomically increments the sequence and returns the next formatted code.
        /// Concurrency-safe across multi-user environments.
        /// </summary>
        Task<string> GetNextCodeAsync(string entityName, int? branchId = null);

        /// <summary>
        /// Previews the prospective next code without advancing the sequence counter.
        /// </summary>
        Task<string> PeekNextCodeAsync(string entityName, int? branchId = null);

        /// <summary>
        /// Checks if auto-numbering is enabled for the entity (and optional branch).
        /// </summary>
        Task<bool> IsAutoEnabledAsync(string entityName, int? branchId = null);

        /// <summary>
        /// Checks if manual code entry is allowed when auto-numbering is configured.
        /// </summary>
        Task<bool> IsManualAllowedAsync(string entityName, int? branchId = null);

        /// <summary>
        /// Retrieves the current configuration and prospective code for an entity and branch.
        /// </summary>
        Task<CodeSequenceConfigDto> GetConfigAsync(string entityName, int? branchId = null);
    }
}
