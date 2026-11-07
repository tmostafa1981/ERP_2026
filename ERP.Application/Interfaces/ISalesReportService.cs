using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs.Reports;

namespace ERP.Application.Interfaces
{
    public interface ISalesReportService
    {
        Task<SalesSummaryReportDto> GetSalesSummaryReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<SalesInvoiceDetailReportDto> GetSalesInvoiceDetailsReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<SalesByItemReportDto> GetSalesByItemReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<SalesByCustomerReportDto> GetSalesByCustomerReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<SalesBySalesmanReportDto> GetSalesBySalesmanReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<SalesReturnsAndNetReportDto> GetSalesReturnsAndNetReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<SalesComparisonReportDto> GetSalesComparisonReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<TopSellingAndStagnantItemsReportDto> GetTopSellingAndStagnantItemsReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default);
        Task<SalesFulfillmentTrackingReportDto> GetSalesFulfillmentTrackingReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default);
    }
}
