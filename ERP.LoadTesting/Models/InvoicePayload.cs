using System;
using System.Collections.Generic;

namespace ERP.LoadTesting.Models
{
    public class InvoiceHeaderPayload
    {
        public int BranchId { get; set; }
        public int FiscalYearId { get; set; } = 1;
        public int TransactionTypeId { get; set; } = 2; // Sales Invoice
        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; } = DateTime.Today;
        public int CustomerId { get; set; }
        public int WarehouseId { get; set; }
        public int CurrencyId { get; set; } = 1;
        public decimal ExchangeRate { get; set; } = 1.0000m;
        public int PaymentMethod { get; set; } = 1; // 1 = Cash, 2 = Credit
        public decimal TotalAmountBeforeTax { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmountNet { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string CreatedByUserId { get; set; } = "NBomber-Agent";

        public List<InvoiceLinePayload> Lines { get; set; } = new();
    }

    public class InvoiceLinePayload
    {
        public int LineNumber { get; set; }
        public int ItemId { get; set; }
        public int UnitId { get; set; } = 1;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxRate { get; set; } = 0.14m; // 14% VAT
        public decimal TaxAmount { get; set; }
        public decimal LineTotal { get; set; }
        public int WarehouseId { get; set; }
    }
}
