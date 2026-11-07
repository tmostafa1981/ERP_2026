using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs.Reports;

namespace ERP.Application.Interfaces
{
    public interface IPurchaseReportService
    {
        Task<PurchaseSummaryReportDto> GetPurchaseSummaryReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<PurchaseInvoiceDetailReportDto> GetPurchaseInvoiceDetailsReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<PurchaseByItemReportDto> GetPurchaseByItemReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<PurchaseByVendorReportDto> GetPurchaseByVendorReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<PurchaseReturnsAndNetReportDto> GetPurchaseReturnsAndNetReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<PurchasePriceHistoryReportDto> GetPurchasePriceHistoryReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<PurchasesPendingStockMovementReportDto> GetPurchasesPendingStockMovementReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<PurchaseReceiptTrackingReportDto> GetPurchaseReceiptTrackingReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default);
    }
}
