using System;
using System.Collections.Generic;
using ERP.LoadTesting.Models;

namespace ERP.LoadTesting.Services
{
    public static class PayloadGenerator
    {
        [ThreadStatic]
        private static Random? _random;
        private static Random Rng => _random ??= new Random(Guid.NewGuid().GetHashCode());

        public static InvoiceHeaderPayload GenerateRandomInvoice(int minBranch = 1, int maxBranch = 42)
        {
            int branchId = Rng.Next(minBranch, maxBranch + 1);
            int warehouseId = branchId; // Assuming 1 warehouse per branch in testing
            int customerId = Rng.Next(1, 101); // 100 customer test pool
            int itemCount = Rng.Next(1, 6);     // 1 to 5 lines per invoice

            var payload = new InvoiceHeaderPayload
            {
                BranchId = branchId,
                WarehouseId = warehouseId,
                CustomerId = customerId,
                DocumentDate = DateTime.Today,
                PaymentMethod = Rng.Next(1, 3), // Cash or Credit
                Notes = $"NBomber Load Test - Branch {branchId} - Thread {Environment.CurrentManagedThreadId}",
                CreatedByUserId = $"User_B{branchId}"
            };

            decimal totalBeforeTax = 0m;
            decimal totalDiscount = 0m;
            decimal totalTax = 0m;
            decimal totalNet = 0m;

            var usedItemIds = new HashSet<int>();

            for (int i = 1; i <= itemCount; i++)
            {
                int itemId;
                do
                {
                    itemId = Rng.Next(1, 31); // 30 items test pool
                } while (!usedItemIds.Add(itemId));

                decimal qty = Rng.Next(1, 8);               // 1 to 7 quantity
                decimal unitPrice = Rng.Next(50, 600);      // 50 to 600 EGP
                decimal discount = (Rng.Next(0, 10) > 7) ? Rng.Next(5, 20) : 0m;
                decimal taxRate = 0.14m;                    // 14% VAT

                decimal baseAmount = (qty * unitPrice) - discount;
                if (baseAmount < 0) baseAmount = 0;

                decimal tax = Math.Round(baseAmount * taxRate, 4);
                decimal lineTotal = Math.Round(baseAmount + tax, 4);

                payload.Lines.Add(new InvoiceLinePayload
                {
                    LineNumber = i,
                    ItemId = itemId,
                    WarehouseId = warehouseId,
                    Quantity = qty,
                    UnitPrice = unitPrice,
                    DiscountAmount = discount,
                    TaxRate = taxRate,
                    TaxAmount = tax,
                    LineTotal = lineTotal
                });

                totalBeforeTax += (qty * unitPrice);
                totalDiscount += discount;
                totalTax += tax;
                totalNet += lineTotal;
            }

            payload.TotalAmountBeforeTax = Math.Round(totalBeforeTax, 4);
            payload.DiscountAmount = Math.Round(totalDiscount, 4);
            payload.TaxAmount = Math.Round(totalTax, 4);
            payload.TotalAmountNet = Math.Round(totalNet, 4);

            return payload;
        }
    }
}
