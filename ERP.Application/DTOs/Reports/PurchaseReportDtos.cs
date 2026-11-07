using System;
using System.Collections.Generic;

namespace ERP.Application.DTOs.Reports
{
    public class PurchaseReportFilterDto
    {
        public int? BranchId { get; set; }
        public int? WarehouseId { get; set; }
        public int? VendorId { get; set; }
        public int? ItemId { get; set; }
        public int? ItemGroupId { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int? TransactionTypeId { get; set; }
        public int? CurrencyId { get; set; }
    }

    public class PurchaseSummaryReportItemDto
    {
        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string Period { get; set; } = string.Empty;
        public int InvoiceCount { get; set; }
        public decimal TotalBeforeTax { get; set; }
        public decimal TotalDiscounts { get; set; }
        public decimal TotalTax { get; set; }
        public decimal TotalNetPurchases { get; set; }
        public int ReturnCount { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal NetPurchases => TotalNetPurchases - TotalReturns;
    }

    public class PurchaseSummaryReportDto
    {
        public PurchaseReportFilterDto Filter { get; set; } = new();
        public List<PurchaseSummaryReportItemDto> Items { get; set; } = new();
        public decimal GrandTotalPurchases { get; set; }
        public decimal GrandTotalReturns { get; set; }
        public decimal GrandNetPurchases { get; set; }
    }

    public class PurchaseInvoiceDetailReportItemDto
    {
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; }
        public string TransactionTypeName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string PaymentMethodName { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = string.Empty;
        public decimal ExchangeRate { get; set; }
        public decimal TotalBeforeTax { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalNet { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public List<PurchaseInvoiceLineDetailDto> Lines { get; set; } = new();
    }

    public class PurchaseInvoiceLineDetailDto
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

    public class PurchaseInvoiceDetailReportDto
    {
        public PurchaseReportFilterDto Filter { get; set; } = new();
        public List<PurchaseInvoiceDetailReportItemDto> Items { get; set; } = new();
        public decimal GrandTotal { get; set; }
    }

    public class PurchaseByItemReportItemDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public decimal PurchasedQuantity { get; set; }
        public decimal ReturnedQuantity { get; set; }
        public decimal NetQuantity => PurchasedQuantity - ReturnedQuantity;
        public decimal TotalPurchasesValue { get; set; }
        public decimal TotalReturnsValue { get; set; }
        public decimal NetPurchasesValue => TotalPurchasesValue - TotalReturnsValue;
        public decimal AveragePrice => NetQuantity > 0 ? (NetPurchasesValue / NetQuantity) : 0m;
    }

    public class PurchaseByItemReportDto
    {
        public PurchaseReportFilterDto Filter { get; set; } = new();
        public List<PurchaseByItemReportItemDto> Items { get; set; } = new();
        public decimal TotalQuantityPurchased { get; set; }
        public decimal TotalValuePurchased { get; set; }
    }

    public class PurchaseByVendorReportItemDto
    {
        public int VendorId { get; set; }
        public string VendorCode { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public int InvoiceCount { get; set; }
        public decimal GrossPurchases { get; set; }
        public decimal Discounts { get; set; }
        public decimal ReturnsAmount { get; set; }
        public decimal NetPurchases => GrossPurchases - Discounts - ReturnsAmount;
    }

    public class PurchaseByVendorReportDto
    {
        public PurchaseReportFilterDto Filter { get; set; } = new();
        public List<PurchaseByVendorReportItemDto> Items { get; set; } = new();
        public decimal GrandNetPurchases { get; set; }
    }

    public class PurchaseReturnsAndNetReportItemDto
    {
        public DateTime Date { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public decimal PurchaseAmount { get; set; }
        public decimal ReturnAmount { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public class PurchaseReturnsAndNetReportDto
    {
        public PurchaseReportFilterDto Filter { get; set; } = new();
        public List<PurchaseReturnsAndNetReportItemDto> Items { get; set; } = new();
        public decimal TotalPurchases { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal NetPurchases => TotalPurchases - TotalReturns;
        public decimal ReturnPercentage => TotalPurchases > 0 ? ((TotalReturns / TotalPurchases) * 100) : 0m;
    }

    public class PurchasePriceHistoryItemDto
    {
        public DateTime DocumentDate { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal NetPrice { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
    }

    public class PurchasePriceHistoryReportDto
    {
        public PurchaseReportFilterDto Filter { get; set; } = new();
        public List<PurchasePriceHistoryItemDto> Items { get; set; } = new();
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal AvgPrice { get; set; }
    }

    public class PurchasesPendingStockMovementItemDto
    {
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; }
        public string TransactionTypeName { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class PurchasesPendingStockMovementReportDto
    {
        public PurchaseReportFilterDto Filter { get; set; } = new();
        public List<PurchasesPendingStockMovementItemDto> Items { get; set; } = new();
    }

    public class PurchaseReceiptTrackingItemDto
    {
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; }
        public string VendorName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public decimal OrderedQuantity { get; set; }
        public decimal ReceivedQuantity { get; set; }
        public decimal RemainingQuantity => Math.Max(0, OrderedQuantity - ReceivedQuantity);
        public decimal ReceiptPercentage => OrderedQuantity > 0 ? ((ReceivedQuantity / OrderedQuantity) * 100) : 0m;
        public string ReceiptStatus => RemainingQuantity == 0 ? "مستلم بالكامل" : (ReceivedQuantity > 0 ? "استلام جزئي" : "معلق لم يستلم");
    }

    public class PurchaseReceiptTrackingReportDto
    {
        public PurchaseReportFilterDto Filter { get; set; } = new();
        public List<PurchaseReceiptTrackingItemDto> Items { get; set; } = new();
    }
}
