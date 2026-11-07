using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs.Reports;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class PurchaseReportService : IPurchaseReportService
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public PurchaseReportService(ErpDbContext context, ICurrentUserContext currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        private IQueryable<DocumentHeader> BuildBasePurchaseQuery(PurchaseReportFilterDto filter)
        {
            var query = _context.DocumentHeaders
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(h => h.TransactionType)
                .Include(h => h.Branch)
                .Include(h => h.Warehouse)
                .Include(h => h.Currency)
                .Where(h => h.TransactionType!.Module == TransactionModule.Purchase
                    && (h.TransactionType.BaseNature == DocumentBaseNature.Invoice || h.TransactionType.BaseNature == DocumentBaseNature.Return)
                    && h.Status == DocumentStatus.Posted);

            if (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
                query = query.Where(h => h.BranchId == _currentUser.BranchId);
            else if (filter.BranchId.HasValue && filter.BranchId > 0)
                query = query.Where(h => h.BranchId == filter.BranchId.Value);

            if (filter.WarehouseId.HasValue && filter.WarehouseId > 0)
                query = query.Where(h => h.WarehouseId == filter.WarehouseId.Value);

            if (filter.VendorId.HasValue && filter.VendorId > 0)
                query = query.Where(h => h.PartyType == DocumentPartyType.Vendor && h.PartyId == filter.VendorId.Value);

            if (filter.DateFrom.HasValue)
                query = query.Where(h => h.DocumentDate >= filter.DateFrom.Value);

            if (filter.DateTo.HasValue)
                query = query.Where(h => h.DocumentDate <= filter.DateTo.Value);

            if (filter.TransactionTypeId.HasValue && filter.TransactionTypeId > 0)
                query = query.Where(h => h.TransactionTypeId == filter.TransactionTypeId.Value);

            if (filter.CurrencyId.HasValue && filter.CurrencyId > 0)
                query = query.Where(h => h.CurrencyId == filter.CurrencyId.Value);

            return query;
        }

        public async Task<PurchaseSummaryReportDto> GetPurchaseSummaryReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var headers = await BuildBasePurchaseQuery(filter)
                .Select(h => new
                {
                    h.BranchId,
                    BranchName = h.Branch != null ? h.Branch.LocalizedName : "الفرع الرئيسي",
                    Date = h.DocumentDate,
                    IsReturn = h.TransactionType!.BaseNature == DocumentBaseNature.Return,
                    h.TotalAmountBeforeTax,
                    h.DiscountAmount,
                    h.TaxAmount,
                    h.TotalAmountNet
                })
                .ToListAsync(cancellationToken);

            var grouped = headers
                .GroupBy(h => new { h.BranchId, h.BranchName, Period = h.Date.ToString("yyyy-MM") })
                .Select(g => new PurchaseSummaryReportItemDto
                {
                    BranchId = g.Key.BranchId,
                    BranchName = g.Key.BranchName,
                    Period = g.Key.Period,
                    InvoiceCount = g.Count(x => !x.IsReturn),
                    TotalBeforeTax = g.Where(x => !x.IsReturn).Sum(x => x.TotalAmountBeforeTax),
                    TotalDiscounts = g.Where(x => !x.IsReturn).Sum(x => x.DiscountAmount),
                    TotalTax = g.Where(x => !x.IsReturn).Sum(x => x.TaxAmount),
                    TotalNetPurchases = g.Where(x => !x.IsReturn).Sum(x => x.TotalAmountNet),
                    ReturnCount = g.Count(x => x.IsReturn),
                    TotalReturns = g.Where(x => x.IsReturn).Sum(x => x.TotalAmountNet)
                })
                .OrderBy(x => x.BranchName)
                .ThenBy(x => x.Period)
                .ToList();

            return new PurchaseSummaryReportDto
            {
                Filter = filter,
                Items = grouped,
                GrandTotalPurchases = grouped.Sum(x => x.TotalNetPurchases),
                GrandTotalReturns = grouped.Sum(x => x.TotalReturns),
                GrandNetPurchases = grouped.Sum(x => x.NetPurchases)
            };
        }

        public async Task<PurchaseInvoiceDetailReportDto> GetPurchaseInvoiceDetailsReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = BuildBasePurchaseQuery(filter)
                .Include(h => h.Lines).ThenInclude(l => l.Item)
                .Include(h => h.Lines).ThenInclude(l => l.Unit)
                .OrderByDescending(h => h.DocumentDate)
                .ThenByDescending(h => h.Id);

            var list = await query.ToListAsync(cancellationToken);

            var vendorIds = list.Where(h => h.PartyType == DocumentPartyType.Vendor && h.PartyId.HasValue).Select(h => h.PartyId!.Value).Distinct().ToList();
            var vendors = await _context.Vendors.AsNoTracking().Where(v => vendorIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.LocalizedName, cancellationToken);

            var items = list.Select(h => new PurchaseInvoiceDetailReportItemDto
            {
                DocumentId = h.Id,
                DocumentNumber = h.DocumentNumber,
                DocumentDate = h.DocumentDate,
                TransactionTypeName = h.TransactionType?.LocalizedName ?? "",
                BranchName = h.Branch?.LocalizedName ?? "",
                WarehouseName = h.Warehouse?.LocalizedName ?? "",
                VendorName = (h.PartyId.HasValue && vendors.ContainsKey(h.PartyId.Value)) ? vendors[h.PartyId.Value] : "مورد نقدي",
                PaymentMethodName = h.PaymentMethod == VoucherPaymentMethod.Cash ? "نقداً" : "آجل",
                CurrencyCode = h.Currency?.Code ?? "EGP",
                ExchangeRate = h.ExchangeRate,
                TotalBeforeTax = h.TotalAmountBeforeTax,
                DiscountAmount = h.DiscountAmount,
                TaxAmount = h.TaxAmount,
                TotalNet = h.TotalAmountNet,
                StatusName = h.Status == DocumentStatus.Posted ? "مرحل" : "مسودة",
                Lines = h.Lines.Select(l => new PurchaseInvoiceLineDetailDto
                {
                    ItemCode = l.Item?.Code ?? "",
                    ItemName = l.Item?.LocalizedName ?? "",
                    UnitName = l.Unit?.LocalizedName ?? "",
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    DiscountAmount = l.DiscountAmount,
                    TaxAmount = l.TaxAmount,
                    LineTotal = l.LineTotal
                }).ToList()
            }).ToList();

            return new PurchaseInvoiceDetailReportDto
            {
                Filter = filter,
                Items = items,
                GrandTotal = items.Sum(x => x.TotalNet)
            };
        }

        public async Task<PurchaseByItemReportDto> GetPurchaseByItemReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = _context.DocumentLines
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.Branch)
                .Include(l => l.Item).ThenInclude(i => i!.ItemGroup)
                .Include(l => l.Unit)
                .Where(l => l.DocumentHeader!.TransactionType!.Module == TransactionModule.Purchase && l.DocumentHeader.Status == DocumentStatus.Posted);

            if (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
                query = query.Where(l => l.DocumentHeader!.BranchId == _currentUser.BranchId);
            else if (filter.BranchId.HasValue && filter.BranchId > 0)
                query = query.Where(l => l.DocumentHeader!.BranchId == filter.BranchId.Value);

            if (filter.WarehouseId.HasValue && filter.WarehouseId > 0)
                query = query.Where(l => (l.WarehouseId ?? l.DocumentHeader!.WarehouseId) == filter.WarehouseId.Value);

            if (filter.VendorId.HasValue && filter.VendorId > 0)
                query = query.Where(l => l.DocumentHeader!.PartyType == DocumentPartyType.Vendor && l.DocumentHeader.PartyId == filter.VendorId.Value);

            if (filter.DateFrom.HasValue)
                query = query.Where(l => l.DocumentHeader!.DocumentDate >= filter.DateFrom.Value);

            if (filter.DateTo.HasValue)
                query = query.Where(l => l.DocumentHeader!.DocumentDate <= filter.DateTo.Value);

            if (filter.ItemId.HasValue && filter.ItemId > 0)
                query = query.Where(l => l.ItemId == filter.ItemId.Value);

            if (filter.ItemGroupId.HasValue && filter.ItemGroupId > 0)
                query = query.Where(l => l.Item!.ItemGroupId == filter.ItemGroupId.Value);

            var lines = await query.Select(l => new
            {
                l.ItemId,
                ItemCode = l.Item != null ? l.Item.Code : "",
                ItemName = l.Item != null ? l.Item.LocalizedName : "",
                GroupName = l.Item != null && l.Item.ItemGroup != null ? l.Item.ItemGroup.LocalizedName : "",
                UnitName = l.Unit != null ? l.Unit.LocalizedName : "",
                IsReturn = l.DocumentHeader!.TransactionType!.BaseNature == DocumentBaseNature.Return,
                l.Quantity,
                l.LineTotal
            }).ToListAsync(cancellationToken);

            var grouped = lines
                .GroupBy(l => new { l.ItemId, l.ItemCode, l.ItemName, l.GroupName, l.UnitName })
                .Select(g => new PurchaseByItemReportItemDto
                {
                    ItemId = g.Key.ItemId,
                    ItemCode = g.Key.ItemCode,
                    ItemName = g.Key.ItemName,
                    GroupName = g.Key.GroupName,
                    UnitName = g.Key.UnitName,
                    PurchasedQuantity = g.Where(x => !x.IsReturn).Sum(x => x.Quantity),
                    ReturnedQuantity = g.Where(x => x.IsReturn).Sum(x => x.Quantity),
                    TotalPurchasesValue = g.Where(x => !x.IsReturn).Sum(x => x.LineTotal),
                    TotalReturnsValue = g.Where(x => x.IsReturn).Sum(x => x.LineTotal)
                })
                .OrderByDescending(x => x.NetPurchasesValue)
                .ToList();

            return new PurchaseByItemReportDto
            {
                Filter = filter,
                Items = grouped,
                TotalQuantityPurchased = grouped.Sum(x => x.NetQuantity),
                TotalValuePurchased = grouped.Sum(x => x.NetPurchasesValue)
            };
        }

        public async Task<PurchaseByVendorReportDto> GetPurchaseByVendorReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var headers = await BuildBasePurchaseQuery(filter)
                .Where(h => h.PartyType == DocumentPartyType.Vendor && h.PartyId.HasValue)
                .Select(h => new
                {
                    VendorId = h.PartyId!.Value,
                    IsReturn = h.TransactionType!.BaseNature == DocumentBaseNature.Return,
                    h.TotalAmountBeforeTax,
                    h.DiscountAmount,
                    h.TotalAmountNet
                })
                .ToListAsync(cancellationToken);

            var vendorIds = headers.Select(h => h.VendorId).Distinct().ToList();
            var vendors = await _context.Vendors.AsNoTracking()
                .Include(v => v.VendorCategory)
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v, cancellationToken);

            var grouped = headers
                .GroupBy(h => h.VendorId)
                .Select(g =>
                {
                    var vend = vendors.ContainsKey(g.Key) ? vendors[g.Key] : null;
                    return new PurchaseByVendorReportItemDto
                    {
                        VendorId = g.Key,
                        VendorCode = vend?.Code ?? "",
                        VendorName = vend?.LocalizedName ?? "",
                        CategoryName = vend?.VendorCategory?.LocalizedName ?? "",
                        Phone = vend?.Phone ?? vend?.Mobile ?? "",
                        InvoiceCount = g.Count(x => !x.IsReturn),
                        GrossPurchases = g.Where(x => !x.IsReturn).Sum(x => x.TotalAmountBeforeTax),
                        Discounts = g.Where(x => !x.IsReturn).Sum(x => x.DiscountAmount),
                        ReturnsAmount = g.Where(x => x.IsReturn).Sum(x => x.TotalAmountNet)
                    };
                })
                .OrderByDescending(x => x.NetPurchases)
                .ToList();

            return new PurchaseByVendorReportDto
            {
                Filter = filter,
                Items = grouped,
                GrandNetPurchases = grouped.Sum(x => x.NetPurchases)
            };
        }

        public async Task<PurchaseReturnsAndNetReportDto> GetPurchaseReturnsAndNetReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = BuildBasePurchaseQuery(filter)
                .OrderByDescending(h => h.DocumentDate)
                .ThenByDescending(h => h.Id);

            var list = await query.ToListAsync(cancellationToken);

            var vendorIds = list.Where(h => h.PartyType == DocumentPartyType.Vendor && h.PartyId.HasValue).Select(h => h.PartyId!.Value).Distinct().ToList();
            var vendors = await _context.Vendors.AsNoTracking().Where(v => vendorIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.LocalizedName, cancellationToken);

            var items = list.Select(h =>
            {
                bool isRet = h.TransactionType!.BaseNature == DocumentBaseNature.Return;
                return new PurchaseReturnsAndNetReportItemDto
                {
                    Date = h.DocumentDate,
                    DocumentNumber = h.DocumentNumber,
                    DocumentType = h.TransactionType?.LocalizedName ?? "",
                    VendorName = (h.PartyId.HasValue && vendors.ContainsKey(h.PartyId.Value)) ? vendors[h.PartyId.Value] : "مورد نقدي",
                    PurchaseAmount = isRet ? 0m : h.TotalAmountNet,
                    ReturnAmount = isRet ? h.TotalAmountNet : 0m,
                    Notes = h.Notes ?? ""
                };
            }).ToList();

            decimal totPurchases = items.Sum(x => x.PurchaseAmount);
            decimal totRet = items.Sum(x => x.ReturnAmount);

            return new PurchaseReturnsAndNetReportDto
            {
                Filter = filter,
                Items = items,
                TotalPurchases = totPurchases,
                TotalReturns = totRet
            };
        }

        public async Task<PurchasePriceHistoryReportDto> GetPurchasePriceHistoryReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = _context.DocumentLines
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.Currency)
                .Include(l => l.Item)
                .Include(l => l.Unit)
                .Where(l => l.DocumentHeader!.TransactionType!.Module == TransactionModule.Purchase 
                    && l.DocumentHeader.TransactionType.BaseNature == DocumentBaseNature.Invoice
                    && l.DocumentHeader.Status == DocumentStatus.Posted);

            if (filter.ItemId.HasValue && filter.ItemId > 0)
                query = query.Where(l => l.ItemId == filter.ItemId.Value);

            if (filter.VendorId.HasValue && filter.VendorId > 0)
                query = query.Where(l => l.DocumentHeader!.PartyType == DocumentPartyType.Vendor && l.DocumentHeader.PartyId == filter.VendorId.Value);

            if (filter.DateFrom.HasValue)
                query = query.Where(l => l.DocumentHeader!.DocumentDate >= filter.DateFrom.Value);

            if (filter.DateTo.HasValue)
                query = query.Where(l => l.DocumentHeader!.DocumentDate <= filter.DateTo.Value);

            var lines = await query.OrderByDescending(l => l.DocumentHeader!.DocumentDate).Take(100).ToListAsync(cancellationToken);

            var vendorIds = lines.Where(l => l.DocumentHeader!.PartyType == DocumentPartyType.Vendor && l.DocumentHeader.PartyId.HasValue)
                .Select(l => l.DocumentHeader!.PartyId!.Value).Distinct().ToList();
            var vendors = await _context.Vendors.AsNoTracking().Where(v => vendorIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.LocalizedName, cancellationToken);

            var items = lines.Select(l => new PurchasePriceHistoryItemDto
            {
                DocumentDate = l.DocumentHeader!.DocumentDate,
                DocumentNumber = l.DocumentHeader.DocumentNumber,
                VendorName = (l.DocumentHeader.PartyId.HasValue && vendors.ContainsKey(l.DocumentHeader.PartyId.Value)) ? vendors[l.DocumentHeader.PartyId.Value] : "مورد نقدي",
                ItemCode = l.Item?.Code ?? "",
                ItemName = l.Item?.LocalizedName ?? "",
                UnitName = l.Unit?.LocalizedName ?? "",
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                NetPrice = l.Quantity > 0 ? (l.LineTotal / l.Quantity) : l.UnitPrice,
                CurrencyCode = l.DocumentHeader.Currency?.Code ?? "EGP"
            }).ToList();

            var prices = items.Select(x => x.UnitPrice).ToList();

            return new PurchasePriceHistoryReportDto
            {
                Filter = filter,
                Items = items,
                MinPrice = prices.Any() ? prices.Min() : 0m,
                MaxPrice = prices.Any() ? prices.Max() : 0m,
                AvgPrice = prices.Any() ? prices.Average() : 0m
            };
        }

        public async Task<PurchasesPendingStockMovementReportDto> GetPurchasesPendingStockMovementReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            // Purchase documents with no inventory movement (e.g. POs or invoices without stock receipt effect)
            var query = _context.DocumentHeaders
                .AsNoTracking()
                .Include(h => h.TransactionType)
                .Include(h => h.Branch)
                .Where(h => h.TransactionType!.Module == TransactionModule.Purchase 
                    && (h.TransactionType.InventoryEffect == InventoryEffect.None || h.ConversionStatus != DocumentConversionStatus.FullyConverted)
                    && h.Status == DocumentStatus.Posted);

            if (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
                query = query.Where(h => h.BranchId == _currentUser.BranchId);
            else if (filter.BranchId.HasValue && filter.BranchId > 0)
                query = query.Where(h => h.BranchId == filter.BranchId.Value);

            if (filter.VendorId.HasValue && filter.VendorId > 0)
                query = query.Where(h => h.PartyType == DocumentPartyType.Vendor && h.PartyId == filter.VendorId.Value);

            if (filter.DateFrom.HasValue)
                query = query.Where(h => h.DocumentDate >= filter.DateFrom.Value);

            if (filter.DateTo.HasValue)
                query = query.Where(h => h.DocumentDate <= filter.DateTo.Value);

            var list = await query.OrderByDescending(h => h.DocumentDate).Take(100).ToListAsync(cancellationToken);

            var vendorIds = list.Where(h => h.PartyType == DocumentPartyType.Vendor && h.PartyId.HasValue).Select(h => h.PartyId!.Value).Distinct().ToList();
            var vendors = await _context.Vendors.AsNoTracking().Where(v => vendorIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.LocalizedName, cancellationToken);

            var items = list.Select(h => new PurchasesPendingStockMovementItemDto
            {
                DocumentId = h.Id,
                DocumentNumber = h.DocumentNumber,
                DocumentDate = h.DocumentDate,
                TransactionTypeName = h.TransactionType?.LocalizedName ?? "",
                VendorName = (h.PartyId.HasValue && vendors.ContainsKey(h.PartyId.Value)) ? vendors[h.PartyId.Value] : "مورد نقدي",
                BranchName = h.Branch?.LocalizedName ?? "",
                TotalAmount = h.TotalAmountNet,
                Reason = h.TransactionType?.InventoryEffect == InventoryEffect.None 
                    ? "نوع المستند مالي فقط بدون حركة مخزن (يتطلب إذن استلام مستقل)" 
                    : "أمر شراء معلق لم يستلم بالكامل بعد"
            }).ToList();

            return new PurchasesPendingStockMovementReportDto
            {
                Filter = filter,
                Items = items
            };
        }

        public async Task<PurchaseReceiptTrackingReportDto> GetPurchaseReceiptTrackingReportAsync(PurchaseReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = _context.DocumentLines
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.Branch)
                .Include(l => l.Item)
                .Where(l => l.DocumentHeader!.TransactionType!.Module == TransactionModule.Purchase
                    && (l.DocumentHeader.TransactionType.BaseNature == DocumentBaseNature.Order || l.DocumentHeader.ConversionStatus != DocumentConversionStatus.FullyConverted)
                    && l.DocumentHeader.Status == DocumentStatus.Posted);

            if (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
                query = query.Where(l => l.DocumentHeader!.BranchId == _currentUser.BranchId);
            else if (filter.BranchId.HasValue && filter.BranchId > 0)
                query = query.Where(l => l.DocumentHeader!.BranchId == filter.BranchId.Value);

            if (filter.VendorId.HasValue && filter.VendorId > 0)
                query = query.Where(l => l.DocumentHeader!.PartyType == DocumentPartyType.Vendor && l.DocumentHeader.PartyId == filter.VendorId.Value);

            if (filter.DateFrom.HasValue)
                query = query.Where(l => l.DocumentHeader!.DocumentDate >= filter.DateFrom.Value);

            if (filter.DateTo.HasValue)
                query = query.Where(l => l.DocumentHeader!.DocumentDate <= filter.DateTo.Value);

            var lines = await query.OrderByDescending(l => l.DocumentHeader!.DocumentDate).Take(150).ToListAsync(cancellationToken);

            var vendorIds = lines.Where(l => l.DocumentHeader!.PartyType == DocumentPartyType.Vendor && l.DocumentHeader.PartyId.HasValue)
                .Select(l => l.DocumentHeader!.PartyId!.Value).Distinct().ToList();
            var vendors = await _context.Vendors.AsNoTracking().Where(v => vendorIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.LocalizedName, cancellationToken);

            var items = lines.Select(l => new PurchaseReceiptTrackingItemDto
            {
                DocumentId = l.DocumentHeaderId,
                DocumentNumber = l.DocumentHeader!.DocumentNumber,
                DocumentDate = l.DocumentHeader.DocumentDate,
                VendorName = (l.DocumentHeader.PartyId.HasValue && vendors.ContainsKey(l.DocumentHeader.PartyId.Value)) ? vendors[l.DocumentHeader.PartyId.Value] : "مورد نقدي",
                BranchName = l.DocumentHeader.Branch?.LocalizedName ?? "",
                ItemName = l.Item?.LocalizedName ?? "",
                OrderedQuantity = l.Quantity,
                ReceivedQuantity = l.ConvertedQuantity
            }).ToList();

            return new PurchaseReceiptTrackingReportDto
            {
                Filter = filter,
                Items = items
            };
        }
    }
}
