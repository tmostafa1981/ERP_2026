using System;
using System.Collections.Generic;

namespace ERP.Application.DTOs.Reports
{
    public class SalesReportFilterDto
    {
        public int? BranchId { get; set; }
        public int? WarehouseId { get; set; }
        public int? CustomerId { get; set; }
        public int? SalesmanId { get; set; }
        public int? ItemId { get; set; }
        public int? ItemGroupId { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int? TransactionTypeId { get; set; }
        public int? CurrencyId { get; set; }
        public int TopN { get; set; } = 10;
        public int StagnantDays { get; set; } = 90;
    }

    public class SalesSummaryReportItemDto
    {
        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string Period { get; set; } = string.Empty;
        public int InvoiceCount { get; set; }
        public decimal TotalBeforeTax { get; set; }
        public decimal TotalDiscounts { get; set; }
        public decimal TotalTax { get; set; }
        public decimal TotalNetSales { get; set; }
        public int ReturnCount { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal NetRevenue => TotalNetSales - TotalReturns;
    }

    public class SalesSummaryReportDto
    {
        public SalesReportFilterDto Filter { get; set; } = new();
        public List<SalesSummaryReportItemDto> Items { get; set; } = new();
        public decimal GrandTotalSales { get; set; }
        public decimal GrandTotalReturns { get; set; }
        public decimal GrandNetRevenue { get; set; }
    }

    public class SalesInvoiceDetailReportItemDto
    {
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; }
        public string TransactionTypeName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string PaymentMethodName { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = string.Empty;
        public decimal ExchangeRate { get; set; }
        public decimal TotalBeforeTax { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalNet { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public List<SalesInvoiceLineDetailDto> Lines { get; set; } = new();
    }

    public class SalesInvoiceLineDetailDto
    {
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class SalesInvoiceDetailReportDto
    {
        public SalesReportFilterDto Filter { get; set; } = new();
        public List<SalesInvoiceDetailReportItemDto> Items { get; set; } = new();
        public decimal GrandTotal { get; set; }
    }

    public class SalesByItemReportItemDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public decimal SoldQuantity { get; set; }
        public decimal ReturnedQuantity { get; set; }
        public decimal NetQuantity => SoldQuantity - ReturnedQuantity;
        public decimal TotalSalesValue { get; set; }
        public decimal TotalReturnsValue { get; set; }
        public decimal NetSalesValue => TotalSalesValue - TotalReturnsValue;
        public decimal AveragePrice => NetQuantity > 0 ? (NetSalesValue / NetQuantity) : 0m;
    }

    public class SalesByItemReportDto
    {
        public SalesReportFilterDto Filter { get; set; } = new();
        public List<SalesByItemReportItemDto> Items { get; set; } = new();
        public decimal TotalQuantitySold { get; set; }
        public decimal TotalValueSold { get; set; }
    }

    public class SalesByCustomerReportItemDto
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public int InvoiceCount { get; set; }
        public decimal GrossSales { get; set; }
        public decimal Discounts { get; set; }
        public decimal ReturnsAmount { get; set; }
        public decimal NetSales => GrossSales - Discounts - ReturnsAmount;
    }

    public class SalesByCustomerReportDto
    {
        public SalesReportFilterDto Filter { get; set; } = new();
        public List<SalesByCustomerReportItemDto> Items { get; set; } = new();
        public decimal GrandNetSales { get; set; }
    }

    public class SalesBySalesmanReportItemDto
    {
        public int SalesmanId { get; set; }
        public string SalesmanCode { get; set; } = string.Empty;
        public string SalesmanName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public int InvoiceCount { get; set; }
        public decimal TotalSales { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal NetSales => TotalSales - TotalReturns;
    }

    public class SalesBySalesmanReportDto
    {
        public SalesReportFilterDto Filter { get; set; } = new();
        public List<SalesBySalesmanReportItemDto> Items { get; set; } = new();
        public decimal GrandNetSales { get; set; }
    }

    public class SalesReturnsAndNetReportItemDto
    {
        public DateTime Date { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public decimal SalesAmount { get; set; }
        public decimal ReturnAmount { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public class SalesReturnsAndNetReportDto
    {
        public SalesReportFilterDto Filter { get; set; } = new();
        public List<SalesReturnsAndNetReportItemDto> Items { get; set; } = new();
        public decimal TotalSales { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal NetRevenue => TotalSales - TotalReturns;
        public decimal ReturnPercentage => TotalSales > 0 ? ((TotalReturns / TotalSales) * 100) : 0m;
    }

    public class SalesComparisonReportItemDto
    {
        public string EntityName { get; set; } = string.Empty;
        public decimal Period1Sales { get; set; }
        public decimal Period2Sales { get; set; }
        public decimal Difference => Period2Sales - Period1Sales;
        public decimal GrowthRate => Period1Sales > 0 ? ((Difference / Period1Sales) * 100) : 0m;
    }

    public class SalesComparisonReportDto
    {
        public string ComparisonBasis { get; set; } = "Branches"; // Branches or Periods
        public List<SalesComparisonReportItemDto> Items { get; set; } = new();
    }

    public class TopSellingAndStagnantItemsReportDto
    {
        public SalesReportFilterDto Filter { get; set; } = new();
        public List<SalesByItemReportItemDto> TopSellingItems { get; set; } = new();
        public List<SalesByItemReportItemDto> NonMovingItems { get; set; } = new();
    }

    public class SalesFulfillmentTrackingItemDto
    {
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; }
        public string TransactionTypeName { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string ConversionStatus { get; set; } = string.Empty;
        public decimal TotalOrderedQuantity { get; set; }
        public decimal TotalFulfilledQuantity { get; set; }
        public decimal RemainingQuantity => TotalOrderedQuantity - TotalFulfilledQuantity;
        public decimal FulfillmentPercentage => TotalOrderedQuantity > 0 ? ((TotalFulfilledQuantity / TotalOrderedQuantity) * 100) : 0m;
        public bool IsFullyFulfilled => RemainingQuantity <= 0;
    }

    public class SalesFulfillmentTrackingReportDto
    {
        public SalesReportFilterDto Filter { get; set; } = new();
        public List<SalesFulfillmentTrackingItemDto> Items { get; set; } = new();
    }
}
