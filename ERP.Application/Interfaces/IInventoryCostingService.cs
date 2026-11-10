using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs.InventoryCosting;
using ERP.Domain.Entities;

namespace ERP.Application.Interfaces
{
    public interface IInventoryCostingService
    {
        Task<decimal> GetCurrentUnitCostAsync(int itemId, int warehouseId, int branchId, CancellationToken cancellationToken = default);

        Task<ItemWarehouseCostDto?> GetCostDetailsAsync(int itemId, int warehouseId, int branchId, CancellationToken cancellationToken = default);

        Task<(bool Success, string Message)> ProcessReceiptCostAsync(DocumentHeader doc, CancellationToken cancellationToken = default);

        Task<(bool Success, string Message, decimal TotalCogs)> ProcessIssueCostAsync(DocumentHeader doc, CancellationToken cancellationToken = default);

        Task<(bool Success, string Message)> ProcessTransferCostAsync(DocumentHeader doc, CancellationToken cancellationToken = default);

        Task<(bool Success, string Message, CostRecalculationSummaryDto? Summary)> RecalculateCostsAfterPurchaseModificationAsync(
            int purchaseInvoiceId,
            string userId,
            CancellationToken cancellationToken = default);

        Task<(bool CanModify, string? ErrorMessage)> ValidatePurchaseQuantityReductionAsync(
            int purchaseInvoiceId,
            int itemId,
            int warehouseId,
            decimal newQuantityBase,
            CancellationToken cancellationToken = default);
    }
}
