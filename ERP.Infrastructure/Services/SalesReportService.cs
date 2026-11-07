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
    public class SalesReportService : ISalesReportService
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public SalesReportService(ErpDbContext context, ICurrentUserContext currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        private IQueryable<DocumentHeader> BuildBaseSalesQuery(SalesReportFilterDto filter)
        {
            var query = _context.DocumentHeaders
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(h => h.TransactionType)
                .Include(h => h.Branch)
                .Include(h => h.Warehouse)
                .Include(h => h.Currency)
                .Where(h => h.TransactionType!.Module == TransactionModule.Sales
                    && (h.TransactionType.BaseNature == DocumentBaseNature.Invoice || h.TransactionType.BaseNature == DocumentBaseNature.Return)
                    && h.Status == DocumentStatus.Posted);

            if (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
            {
                query = query.Where(h => h.BranchId == _currentUser.BranchId);
            }
            else if (filter.BranchId.HasValue && filter.BranchId > 0)
            {
                query = query.Where(h => h.BranchId == filter.BranchId.Value);
            }

            if (filter.WarehouseId.HasValue && filter.WarehouseId > 0)
                query = query.Where(h => h.WarehouseId == filter.WarehouseId.Value);

            if (filter.CustomerId.HasValue && filter.CustomerId > 0)
                query = query.Where(h => h.PartyType == DocumentPartyType.Customer && h.PartyId == filter.CustomerId.Value);

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

        public async Task<SalesSummaryReportDto> GetSalesSummaryReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var headers = await BuildBaseSalesQuery(filter)
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
                .Select(g => new SalesSummaryReportItemDto
                {
                    BranchId = g.Key.BranchId,
                    BranchName = g.Key.BranchName,
                    Period = g.Key.Period,
                    InvoiceCount = g.Count(x => !x.IsReturn),
                    TotalBeforeTax = g.Where(x => !x.IsReturn).Sum(x => x.TotalAmountBeforeTax),
                    TotalDiscounts = g.Where(x => !x.IsReturn).Sum(x => x.DiscountAmount),
                    TotalTax = g.Where(x => !x.IsReturn).Sum(x => x.TaxAmount),
                    TotalNetSales = g.Where(x => !x.IsReturn).Sum(x => x.TotalAmountNet),
                    ReturnCount = g.Count(x => x.IsReturn),
                    TotalReturns = g.Where(x => x.IsReturn).Sum(x => x.TotalAmountNet)
                })
                .OrderBy(x => x.BranchName)
                .ThenBy(x => x.Period)
                .ToList();

            return new SalesSummaryReportDto
            {
                Filter = filter,
                Items = grouped,
                GrandTotalSales = grouped.Sum(x => x.TotalNetSales),
                GrandTotalReturns = grouped.Sum(x => x.TotalReturns),
                GrandNetRevenue = grouped.Sum(x => x.NetRevenue)
            };
        }

        public async Task<SalesInvoiceDetailReportDto> GetSalesInvoiceDetailsReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = BuildBaseSalesQuery(filter)
                .Include(h => h.Lines).ThenInclude(l => l.Item)
                .Include(h => h.Lines).ThenInclude(l => l.Unit)
                .OrderByDescending(h => h.DocumentDate)
                .ThenByDescending(h => h.Id);

            var list = await query.ToListAsync(cancellationToken);

            // Fetch Customers
            var custIds = list.Where(h => h.PartyType == DocumentPartyType.Customer && h.PartyId.HasValue).Select(h => h.PartyId!.Value).Distinct().ToList();
            var customers = await _context.Customers.AsNoTracking().Where(c => custIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.LocalizedName, cancellationToken);

            var items = list.Select(h => new SalesInvoiceDetailReportItemDto
            {
                DocumentId = h.Id,
                DocumentNumber = h.DocumentNumber,
                DocumentDate = h.DocumentDate,
                TransactionTypeName = h.TransactionType?.LocalizedName ?? "",
                BranchName = h.Branch?.LocalizedName ?? "",
                WarehouseName = h.Warehouse?.LocalizedName ?? "",
                CustomerName = (h.PartyId.HasValue && customers.ContainsKey(h.PartyId.Value)) ? customers[h.PartyId.Value] : "عميل نقدي",
                PaymentMethodName = h.PaymentMethod == VoucherPaymentMethod.Cash ? "نقداً" : "آجل",
                CurrencyCode = h.Currency?.Code ?? "EGP",
                ExchangeRate = h.ExchangeRate,
                TotalBeforeTax = h.TotalAmountBeforeTax,
                DiscountAmount = h.DiscountAmount,
                TaxAmount = h.TaxAmount,
                TotalNet = h.TotalAmountNet,
                StatusName = h.Status == DocumentStatus.Posted ? "مرحل" : "مسودة",
                Lines = h.Lines.Select(l => new SalesInvoiceLineDetailDto
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

            return new SalesInvoiceDetailReportDto
            {
                Filter = filter,
                Items = items,
                GrandTotal = items.Sum(x => x.TotalNet)
            };
        }

        public async Task<SalesByItemReportDto> GetSalesByItemReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = _context.DocumentLines
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.Branch)
                .Include(l => l.Item).ThenInclude(i => i!.ItemGroup)
                .Include(l => l.Unit)
                .Where(l => l.DocumentHeader!.TransactionType!.Module == TransactionModule.Sales && l.DocumentHeader.Status == DocumentStatus.Posted);

            if (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
                query = query.Where(l => l.DocumentHeader!.BranchId == _currentUser.BranchId);
            else if (filter.BranchId.HasValue && filter.BranchId > 0)
                query = query.Where(l => l.DocumentHeader!.BranchId == filter.BranchId.Value);

            if (filter.WarehouseId.HasValue && filter.WarehouseId > 0)
                query = query.Where(l => (l.WarehouseId ?? l.DocumentHeader!.WarehouseId) == filter.WarehouseId.Value);

            if (filter.CustomerId.HasValue && filter.CustomerId > 0)
                query = query.Where(l => l.DocumentHeader!.PartyType == DocumentPartyType.Customer && l.DocumentHeader.PartyId == filter.CustomerId.Value);

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
                .Select(g => new SalesByItemReportItemDto
                {
                    ItemId = g.Key.ItemId,
                    ItemCode = g.Key.ItemCode,
                    ItemName = g.Key.ItemName,
                    GroupName = g.Key.GroupName,
                    UnitName = g.Key.UnitName,
                    SoldQuantity = g.Where(x => !x.IsReturn).Sum(x => x.Quantity),
                    ReturnedQuantity = g.Where(x => x.IsReturn).Sum(x => x.Quantity),
                    TotalSalesValue = g.Where(x => !x.IsReturn).Sum(x => x.LineTotal),
                    TotalReturnsValue = g.Where(x => x.IsReturn).Sum(x => x.LineTotal)
                })
                .OrderByDescending(x => x.NetSalesValue)
                .ToList();

            return new SalesByItemReportDto
            {
                Filter = filter,
                Items = grouped,
                TotalQuantitySold = grouped.Sum(x => x.NetQuantity),
                TotalValueSold = grouped.Sum(x => x.NetSalesValue)
            };
        }

        public async Task<SalesByCustomerReportDto> GetSalesByCustomerReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var headers = await BuildBaseSalesQuery(filter)
                .Where(h => h.PartyType == DocumentPartyType.Customer && h.PartyId.HasValue)
                .Select(h => new
                {
                    CustomerId = h.PartyId!.Value,
                    IsReturn = h.TransactionType!.BaseNature == DocumentBaseNature.Return,
                    h.TotalAmountBeforeTax,
                    h.DiscountAmount,
                    h.TotalAmountNet
                })
                .ToListAsync(cancellationToken);

            var custIds = headers.Select(h => h.CustomerId).Distinct().ToList();
            var customers = await _context.Customers.AsNoTracking()
                .Include(c => c.CustomerCategory)
                .Where(c => custIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c, cancellationToken);

            var grouped = headers
                .GroupBy(h => h.CustomerId)
                .Select(g =>
                {
                    var cust = customers.ContainsKey(g.Key) ? customers[g.Key] : null;
                    return new SalesByCustomerReportItemDto
                    {
                        CustomerId = g.Key,
                        CustomerCode = cust?.Code ?? "",
                        CustomerName = cust?.LocalizedName ?? "",
                        CategoryName = cust?.CustomerCategory?.LocalizedName ?? "",
                        Phone = cust?.Phone ?? cust?.Mobile ?? "",
                        InvoiceCount = g.Count(x => !x.IsReturn),
                        GrossSales = g.Where(x => !x.IsReturn).Sum(x => x.TotalAmountBeforeTax),
                        Discounts = g.Where(x => !x.IsReturn).Sum(x => x.DiscountAmount),
                        ReturnsAmount = g.Where(x => x.IsReturn).Sum(x => x.TotalAmountNet)
                    };
                })
                .OrderByDescending(x => x.NetSales)
                .ToList();

            return new SalesByCustomerReportDto
            {
                Filter = filter,
                Items = grouped,
                GrandNetSales = grouped.Sum(x => x.NetSales)
            };
        }

        public async Task<SalesBySalesmanReportDto> GetSalesBySalesmanReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var headers = await BuildBaseSalesQuery(filter)
                .Where(h => h.PartyType == DocumentPartyType.Customer && h.PartyId.HasValue)
                .Select(h => new
                {
                    CustomerId = h.PartyId!.Value,
                    IsReturn = h.TransactionType!.BaseNature == DocumentBaseNature.Return,
                    h.TotalAmountNet
                })
                .ToListAsync(cancellationToken);

            var custIds = headers.Select(h => h.CustomerId).Distinct().ToList();
            var custSalesmen = await _context.Customers.AsNoTracking()
                .Include(c => c.Salesman).ThenInclude(s => s!.Branch)
                .Where(c => custIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Salesman, cancellationToken);

            var grouped = headers
                .GroupBy(h =>
                {
                    var s = custSalesmen.ContainsKey(h.CustomerId) ? custSalesmen[h.CustomerId] : null;
                    return new
                    {
                        SalesmanId = s?.Id ?? 0,
                        SalesmanCode = s?.Code ?? "NONE",
                        SalesmanName = s != null ? s.LocalizedName : "بدون مندوب معين",
                        BranchName = s?.Branch?.LocalizedName ?? ""
                    };
                })
                .Select(g => new SalesBySalesmanReportItemDto
                {
                    SalesmanId = g.Key.SalesmanId,
                    SalesmanCode = g.Key.SalesmanCode,
                    SalesmanName = g.Key.SalesmanName,
                    BranchName = g.Key.BranchName,
                    InvoiceCount = g.Count(x => !x.IsReturn),
                    TotalSales = g.Where(x => !x.IsReturn).Sum(x => x.TotalAmountNet),
                    TotalReturns = g.Where(x => x.IsReturn).Sum(x => x.TotalAmountNet)
                })
                .OrderByDescending(x => x.NetSales)
                .ToList();

            return new SalesBySalesmanReportDto
            {
                Filter = filter,
                Items = grouped,
                GrandNetSales = grouped.Sum(x => x.NetSales)
            };
        }

        public async Task<SalesReturnsAndNetReportDto> GetSalesReturnsAndNetReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = BuildBaseSalesQuery(filter)
                .OrderByDescending(h => h.DocumentDate)
                .ThenByDescending(h => h.Id);

            var list = await query.ToListAsync(cancellationToken);

            var custIds = list.Where(h => h.PartyType == DocumentPartyType.Customer && h.PartyId.HasValue).Select(h => h.PartyId!.Value).Distinct().ToList();
            var customers = await _context.Customers.AsNoTracking().Where(c => custIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.LocalizedName, cancellationToken);

            var items = list.Select(h =>
            {
                bool isRet = h.TransactionType!.BaseNature == DocumentBaseNature.Return;
                return new SalesReturnsAndNetReportItemDto
                {
                    Date = h.DocumentDate,
                    DocumentNumber = h.DocumentNumber,
                    DocumentType = h.TransactionType?.LocalizedName ?? "",
                    CustomerName = (h.PartyId.HasValue && customers.ContainsKey(h.PartyId.Value)) ? customers[h.PartyId.Value] : "عميل نقدي",
                    SalesAmount = isRet ? 0m : h.TotalAmountNet,
                    ReturnAmount = isRet ? h.TotalAmountNet : 0m,
                    Notes = h.Notes ?? ""
                };
            }).ToList();

            decimal totSales = items.Sum(x => x.SalesAmount);
            decimal totRet = items.Sum(x => x.ReturnAmount);

            return new SalesReturnsAndNetReportDto
            {
                Filter = filter,
                Items = items,
                TotalSales = totSales,
                TotalReturns = totRet
            };
        }

        public async Task<SalesComparisonReportDto> GetSalesComparisonReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            DateTime now = DateTime.Today;
            DateTime p1Start = filter.DateFrom ?? now.AddMonths(-1);
            DateTime p1End = filter.DateTo ?? now;
            var duration = p1End - p1Start;
            DateTime p2Start = p1Start.Subtract(duration);
            DateTime p2End = p1Start.AddDays(-1);

            var branchesQuery = _context.Branches.AsNoTracking().Where(b => b.IsActive);
            if (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
                branchesQuery = branchesQuery.Where(b => b.Id == _currentUser.BranchId);

            var branches = await branchesQuery.ToListAsync(cancellationToken);

            var docsP1 = await _context.DocumentHeaders.AsNoTracking()
                .Include(h => h.TransactionType)
                .Where(h => h.TransactionType!.Module == TransactionModule.Sales && h.Status == DocumentStatus.Posted
                    && h.DocumentDate >= p1Start && h.DocumentDate <= p1End)
                .Select(h => new { h.BranchId, IsReturn = h.TransactionType!.BaseNature == DocumentBaseNature.Return, h.TotalAmountNet })
                .ToListAsync(cancellationToken);

            var docsP2 = await _context.DocumentHeaders.AsNoTracking()
                .Include(h => h.TransactionType)
                .Where(h => h.TransactionType!.Module == TransactionModule.Sales && h.Status == DocumentStatus.Posted
                    && h.DocumentDate >= p2Start && h.DocumentDate <= p2End)
                .Select(h => new { h.BranchId, IsReturn = h.TransactionType!.BaseNature == DocumentBaseNature.Return, h.TotalAmountNet })
                .ToListAsync(cancellationToken);

            var comparison = branches.Select(b =>
            {
                decimal p1Net = docsP1.Where(x => x.BranchId == b.Id).Sum(x => x.IsReturn ? -x.TotalAmountNet : x.TotalAmountNet);
                decimal p2Net = docsP2.Where(x => x.BranchId == b.Id).Sum(x => x.IsReturn ? -x.TotalAmountNet : x.TotalAmountNet);
                return new SalesComparisonReportItemDto
                {
                    EntityName = b.LocalizedName,
                    Period1Sales = p2Net, // previous
                    Period2Sales = p1Net  // current
                };
            }).ToList();

            return new SalesComparisonReportDto
            {
                ComparisonBasis = $"مقارنة الفروع بين الفترة الحالية ({p1Start:yyyy/MM/dd} - {p1End:yyyy/MM/dd}) والسابقة",
                Items = comparison
            };
        }

        public async Task<TopSellingAndStagnantItemsReportDto> GetTopSellingAndStagnantItemsReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var byItem = await GetSalesByItemReportAsync(filter, cancellationToken);
            var soldItemIds = byItem.Items.Where(i => i.SoldQuantity > 0).Select(i => i.ItemId).ToHashSet();

            var topItems = byItem.Items.OrderByDescending(i => i.SoldQuantity).Take(filter.TopN > 0 ? filter.TopN : 10).ToList();

            // Active items that were NOT sold
            var allItemsQuery = _context.Items.AsNoTracking()
                .Include(i => i.ItemGroup)
                .Include(i => i.ItemUnits).ThenInclude(iu => iu.Unit)
                .Where(i => i.IsActive && !i.IsService);

            if (filter.ItemGroupId.HasValue && filter.ItemGroupId > 0)
                allItemsQuery = allItemsQuery.Where(i => i.ItemGroupId == filter.ItemGroupId.Value);

            var stagnant = await allItemsQuery
                .Where(i => !soldItemIds.Contains(i.Id))
                .Select(i => new SalesByItemReportItemDto
                {
                    ItemId = i.Id,
                    ItemCode = i.Code,
                    ItemName = i.LocalizedName,
                    GroupName = i.ItemGroup != null ? i.ItemGroup.LocalizedName : "",
                    UnitName = i.ItemUnits.FirstOrDefault() != null && i.ItemUnits.FirstOrDefault()!.Unit != null ? i.ItemUnits.FirstOrDefault()!.Unit!.LocalizedName : "",
                    SoldQuantity = 0,
                    ReturnedQuantity = 0,
                    TotalSalesValue = 0,
                    TotalReturnsValue = 0
                })
                .Take(50)
                .ToListAsync(cancellationToken);

            return new TopSellingAndStagnantItemsReportDto
            {
                Filter = filter,
                TopSellingItems = topItems,
                NonMovingItems = stagnant
            };
        }

        public async Task<SalesFulfillmentTrackingReportDto> GetSalesFulfillmentTrackingReportAsync(SalesReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = _context.DocumentHeaders
                .AsNoTracking()
                .Include(h => h.TransactionType)
                .Include(h => h.Branch)
                .Include(h => h.Lines)
                .Where(h => h.TransactionType!.Module == TransactionModule.Sales &&
                    (h.TransactionType.InventoryEffect == InventoryEffect.ReserveStock || h.TransactionType.BaseNature == DocumentBaseNature.Order || h.ConversionStatus != DocumentConversionStatus.FullyConverted));

            if (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
                query = query.Where(h => h.BranchId == _currentUser.BranchId);
            else if (filter.BranchId.HasValue && filter.BranchId > 0)
                query = query.Where(h => h.BranchId == filter.BranchId.Value);

            if (filter.CustomerId.HasValue && filter.CustomerId > 0)
                query = query.Where(h => h.PartyType == DocumentPartyType.Customer && h.PartyId == filter.CustomerId.Value);

            if (filter.DateFrom.HasValue)
                query = query.Where(h => h.DocumentDate >= filter.DateFrom.Value);

            if (filter.DateTo.HasValue)
                query = query.Where(h => h.DocumentDate <= filter.DateTo.Value);

            var list = await query.OrderByDescending(h => h.DocumentDate).Take(100).ToListAsync(cancellationToken);

            var custIds = list.Where(h => h.PartyType == DocumentPartyType.Customer && h.PartyId.HasValue).Select(h => h.PartyId!.Value).Distinct().ToList();
            var customers = await _context.Customers.AsNoTracking().Where(c => custIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.LocalizedName, cancellationToken);

            var items = list.Select(h => new SalesFulfillmentTrackingItemDto
            {
                DocumentId = h.Id,
                DocumentNumber = h.DocumentNumber,
                DocumentDate = h.DocumentDate,
                TransactionTypeName = h.TransactionType?.LocalizedName ?? "",
                CustomerName = (h.PartyId.HasValue && customers.ContainsKey(h.PartyId.Value)) ? customers[h.PartyId.Value] : "عميل نقدي",
                BranchName = h.Branch?.LocalizedName ?? "",
                ConversionStatus = h.ConversionStatus == DocumentConversionStatus.FullyConverted ? "مكتمل التسليم" : (h.ConversionStatus == DocumentConversionStatus.PartiallyConverted ? "تسليم جزئي" : "غير محول / معلق"),
                TotalOrderedQuantity = h.Lines.Sum(l => l.Quantity),
                TotalFulfilledQuantity = h.Lines.Sum(l => l.ConvertedQuantity)
            }).ToList();

            return new SalesFulfillmentTrackingReportDto
            {
                Filter = filter,
                Items = items
            };
        }
    }
}
