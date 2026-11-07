using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IInventoryAvailabilityService
    {
        bool PreventNegativeStock { get; set; }

        /// <summary>
        /// Calculates on-hand, reserved, and available stock for a specific item, branch, warehouse, and unit.
        /// </summary>
        Task<ItemAvailabilityDto> GetAvailabilityAsync(
            int itemId,
            int branchId,
            int? warehouseId,
            int? unitId = null,
            decimal requestedQuantity = 0m,
            int? excludeDocumentId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates whether the requested quantity can be issued/reserved based on current availability.
        /// </summary>
        Task<ItemAvailabilityValidationResultDto> ValidateLineAvailabilityAsync(
            int itemId,
            int branchId,
            int? warehouseId,
            int? unitId,
            decimal requestedQuantity,
            int? excludeDocumentId = null,
            bool allowNegativeStock = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates availability for all lines in a document before save/post.
        /// </summary>
        Task<(bool IsValid, string Message, List<ItemAvailabilityValidationResultDto> LineResults)> ValidateDocumentAvailabilityAsync(
            DocumentHeaderDto document,
            int? excludeDocumentId = null,
            bool allowNegativeStock = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Fast batch lookup of stock availability for multiple items in a warehouse.
        /// </summary>
        Task<Dictionary<int, ItemAvailabilityDto>> GetBulkAvailabilityAsync(
            IEnumerable<int> itemIds,
            int branchId,
            int? warehouseId,
            CancellationToken cancellationToken = default);
    }
}
