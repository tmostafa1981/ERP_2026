using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs.Reports;

namespace ERP.Application.Interfaces
{
    public interface IInventoryReportService
    {
        Task<StockBalanceReportDto> GetStockBalancesReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<ItemStockCardReportDto> GetItemStockCardReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<InwardOutwardReportDto> GetInwardOutwardReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<WarehouseTransfersReportDto> GetWarehouseTransfersReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<ReturnsAndAdjustmentsReportDto> GetReturnsAndAdjustmentsReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<ReservedAndAvailableReportDto> GetReservedAndAvailableReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<ReorderLevelAlertReportDto> GetReorderLevelAlertReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<DeadStockReportDto> GetDeadStockReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<SerialNumbersReportDto> GetSerialNumbersReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<StockDiscrepancyReportDto> GetStockDiscrepancyReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default);
    }
}
