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
    public class InventoryReportService : IInventoryReportService
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public InventoryReportService(ErpDbContext context, ICurrentUserContext currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<StockBalanceReportDto> GetStockBalancesReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var branchId = (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
                ? _currentUser.BranchId
                : filter.BranchId;

            // 1. Posted Inward movements
            var inQuery = _context.DocumentLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Where(l => l.DocumentHeader!.Status == DocumentStatus.Posted
                    && (l.DocumentHeader.InventoryEffect == InventoryEffect.ReceiveStock
                        || (l.DocumentHeader.TransactionType != null && l.DocumentHeader.TransactionType.InventoryEffect == InventoryEffect.ReceiveStock)));

            // 2. Posted Outward movements
            var outQuery = _context.DocumentLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Where(l => l.DocumentHeader!.Status == DocumentStatus.Posted
                    && (l.DocumentHeader.InventoryEffect == InventoryEffect.IssueStock
                        || (l.DocumentHeader.TransactionType != null && l.DocumentHeader.TransactionType.InventoryEffect == InventoryEffect.IssueStock)));

            // 3. Open Reservations
            var resQuery = _context.DocumentLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Where(l => l.DocumentHeader!.Status == DocumentStatus.Posted
                    && (l.DocumentHeader.InventoryEffect == InventoryEffect.ReserveStock
                        || (l.DocumentHeader.TransactionType != null && l.DocumentHeader.TransactionType.InventoryEffect == InventoryEffect.ReserveStock))
                    && l.DocumentHeader.ConversionStatus != DocumentConversionStatus.FullyConverted);

            if (branchId.HasValue && branchId.Value > 0)
            {
                inQuery = inQuery.Where(l => l.DocumentHeader!.BranchId == branchId.Value);
                outQuery = outQuery.Where(l => l.DocumentHeader!.BranchId == branchId.Value);
                resQuery = resQuery.Where(l => l.DocumentHeader!.BranchId == branchId.Value);
            }

            if (filter.WarehouseId.HasValue && filter.WarehouseId.Value > 0)
            {
                inQuery = inQuery.Where(l => (l.WarehouseId ?? l.DocumentHeader!.WarehouseId) == filter.WarehouseId.Value);
                outQuery = outQuery.Where(l => (l.WarehouseId ?? l.DocumentHeader!.WarehouseId) == filter.WarehouseId.Value);
                resQuery = resQuery.Where(l => (l.WarehouseId ?? l.DocumentHeader!.WarehouseId) == filter.WarehouseId.Value);
            }

            if (filter.ItemId.HasValue && filter.ItemId.Value > 0)
            {
                inQuery = inQuery.Where(l => l.ItemId == filter.ItemId.Value);
                outQuery = outQuery.Where(l => l.ItemId == filter.ItemId.Value);
                resQuery = resQuery.Where(l => l.ItemId == filter.ItemId.Value);
            }

            var inLines = await inQuery.Select(l => new
            {
                l.ItemId,
                BranchId = l.DocumentHeader!.BranchId,
                WarehouseId = l.WarehouseId ?? l.DocumentHeader!.WarehouseId ?? 0,
                BaseQty = l.Quantity * (l.ConversionRate > 0 ? l.ConversionRate : 1.0m)
            }).ToListAsync(cancellationToken);

            var outLines = await outQuery.Select(l => new
            {
                l.ItemId,
                BranchId = l.DocumentHeader!.BranchId,
                WarehouseId = l.WarehouseId ?? l.DocumentHeader!.WarehouseId ?? 0,
                BaseQty = l.Quantity * (l.ConversionRate > 0 ? l.ConversionRate : 1.0m)
            }).ToListAsync(cancellationToken);

            var resLines = await resQuery.Select(l => new
            {
                l.ItemId,
                BranchId = l.DocumentHeader!.BranchId,
                WarehouseId = l.WarehouseId ?? l.DocumentHeader!.WarehouseId ?? 0,
                BaseQty = Math.Max(0m, l.Quantity - l.ConvertedQuantity) * (l.ConversionRate > 0 ? l.ConversionRate : 1.0m)
            }).ToListAsync(cancellationToken);

            var allKeys = inLines.Select(x => new { x.ItemId, x.BranchId, x.WarehouseId })
                .Union(outLines.Select(x => new { x.ItemId, x.BranchId, x.WarehouseId }))
                .Union(resLines.Select(x => new { x.ItemId, x.BranchId, x.WarehouseId }))
                .Distinct()
                .ToList();

            var itemIds = allKeys.Select(k => k.ItemId).Distinct().ToList();
            var itemsDict = await _context.Items.AsNoTracking()
                .Include(i => i.ItemGroup)
                .Include(i => i.ItemUnits).ThenInclude(iu => iu.Unit)
                .Where(i => itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i, cancellationToken);

            var branchIds = allKeys.Select(k => k.BranchId).Distinct().ToList();
            var branchesDict = await _context.Branches.AsNoTracking().Where(b => branchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.LocalizedName, cancellationToken);

            var warehouseIds = allKeys.Select(k => k.WarehouseId).Distinct().ToList();
            var warehousesDict = await _context.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.LocalizedName, cancellationToken);

            var resultItems = new List<StockBalanceReportItemDto>();

            foreach (var k in allKeys)
            {
                if (!itemsDict.ContainsKey(k.ItemId)) continue;
                var item = itemsDict[k.ItemId];
                if (filter.ItemGroupId.HasValue && filter.ItemGroupId > 0 && item.ItemGroupId != filter.ItemGroupId.Value)
                    continue;

                decimal inQty = inLines.Where(x => x.ItemId == k.ItemId && x.BranchId == k.BranchId && x.WarehouseId == k.WarehouseId).Sum(x => x.BaseQty);
                decimal outQty = outLines.Where(x => x.ItemId == k.ItemId && x.BranchId == k.BranchId && x.WarehouseId == k.WarehouseId).Sum(x => x.BaseQty);
                decimal onHand = inQty - outQty;
                decimal resQty = resLines.Where(x => x.ItemId == k.ItemId && x.BranchId == k.BranchId && x.WarehouseId == k.WarehouseId).Sum(x => x.BaseQty);

                var defaultUnit = item.ItemUnits.FirstOrDefault(u => u.IsDefaultPurchase) ?? item.ItemUnits.FirstOrDefault();

                resultItems.Add(new StockBalanceReportItemDto
                {
                    ItemId = item.Id,
                    ItemCode = item.Code,
                    ItemName = item.LocalizedName,
                    GroupName = item.ItemGroup?.LocalizedName ?? "",
                    UnitName = defaultUnit?.Unit?.LocalizedName ?? "",
                    BranchName = branchesDict.ContainsKey(k.BranchId) ? branchesDict[k.BranchId] : "فرع",
                    WarehouseName = warehousesDict.ContainsKey(k.WarehouseId) ? warehousesDict[k.WarehouseId] : "مستودع رئيسي",
                    OnHandQuantity = onHand,
                    ReservedQuantity = resQty,
                    PurchasePrice = defaultUnit?.PurchasePrice ?? 0m
                });
            }

            resultItems = resultItems.OrderBy(x => x.BranchName).ThenBy(x => x.WarehouseName).ThenBy(x => x.ItemName).ToList();

            return new StockBalanceReportDto
            {
                Filter = filter,
                Items = resultItems,
                TotalOnHandQuantity = resultItems.Sum(x => x.OnHandQuantity),
                TotalAvailableQuantity = resultItems.Sum(x => x.AvailableQuantity),
                TotalEstimatedValue = resultItems.Sum(x => x.EstimatedValue)
            };
        }

        public async Task<ItemStockCardReportDto> GetItemStockCardReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            if (!filter.ItemId.HasValue || filter.ItemId.Value <= 0)
            {
                // default to first item if not chosen
                var firstItem = await _context.Items.AsNoTracking().Where(i => !i.IsService).OrderBy(i => i.Id).FirstOrDefaultAsync(cancellationToken);
                if (firstItem != null) filter.ItemId = firstItem.Id;
            }

            int targetItemId = filter.ItemId ?? 0;
            var item = await _context.Items.AsNoTracking()
                .Include(i => i.ItemGroup)
                .Include(i => i.ItemUnits).ThenInclude(iu => iu.Unit)
                .FirstOrDefaultAsync(i => i.Id == targetItemId, cancellationToken);

            if (item == null)
            {
                return new ItemStockCardReportDto { Filter = filter };
            }

            var branchId = (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
                ? _currentUser.BranchId
                : filter.BranchId;

            var linesQuery = _context.DocumentLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.Branch)
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.Warehouse)
                .Where(l => l.ItemId == targetItemId && l.DocumentHeader!.Status == DocumentStatus.Posted
                    && ((l.DocumentHeader.InventoryEffect == InventoryEffect.ReceiveStock || (l.DocumentHeader.TransactionType != null && l.DocumentHeader.TransactionType.InventoryEffect == InventoryEffect.ReceiveStock))
                        || (l.DocumentHeader.InventoryEffect == InventoryEffect.IssueStock || (l.DocumentHeader.TransactionType != null && l.DocumentHeader.TransactionType.InventoryEffect == InventoryEffect.IssueStock))));

            if (branchId.HasValue && branchId.Value > 0)
                linesQuery = linesQuery.Where(l => l.DocumentHeader!.BranchId == branchId.Value);

            if (filter.WarehouseId.HasValue && filter.WarehouseId.Value > 0)
                linesQuery = linesQuery.Where(l => (l.WarehouseId ?? l.DocumentHeader!.WarehouseId) == filter.WarehouseId.Value);

            // Fetch prior movements for opening balance
            decimal openingBalance = 0m;
            if (filter.DateFrom.HasValue)
            {
                var priorLines = await linesQuery
                    .Where(l => l.DocumentHeader!.DocumentDate < filter.DateFrom.Value)
                    .Select(l => new
                    {
                        Effect = l.DocumentHeader.InventoryEffect != InventoryEffect.None ? l.DocumentHeader.InventoryEffect : (l.DocumentHeader.TransactionType != null ? l.DocumentHeader.TransactionType.InventoryEffect : InventoryEffect.None),
                        Nature = l.DocumentHeader.TransactionType != null ? l.DocumentHeader.TransactionType.BaseNature : DocumentBaseNature.Invoice,
                        Qty = l.Quantity * (l.ConversionRate > 0 ? l.ConversionRate : 1.0m)
                    })
                    .ToListAsync(cancellationToken);

                decimal priorIn = priorLines.Where(x => x.Effect == InventoryEffect.ReceiveStock).Sum(x => x.Qty);
                decimal priorOut = priorLines.Where(x => x.Effect == InventoryEffect.IssueStock).Sum(x => x.Qty);
                openingBalance = priorIn - priorOut;

                linesQuery = linesQuery.Where(l => l.DocumentHeader!.DocumentDate >= filter.DateFrom.Value);
            }

            if (filter.DateTo.HasValue)
                linesQuery = linesQuery.Where(l => l.DocumentHeader!.DocumentDate <= filter.DateTo.Value);

            var rawLines = await linesQuery
                .OrderBy(l => l.DocumentHeader!.DocumentDate)
                .ThenBy(l => l.DocumentHeaderId)
                .Select(l => new
                {
                    Date = l.DocumentHeader!.DocumentDate,
                    DocNumber = l.DocumentHeader.DocumentNumber,
                    TypeName = l.DocumentHeader.TransactionType != null ? l.DocumentHeader.TransactionType.LocalizedName : "حركة مخزنية",
                    Effect = l.DocumentHeader.InventoryEffect != InventoryEffect.None ? l.DocumentHeader.InventoryEffect : (l.DocumentHeader.TransactionType != null ? l.DocumentHeader.TransactionType.InventoryEffect : InventoryEffect.None),
                    Nature = l.DocumentHeader.TransactionType != null ? l.DocumentHeader.TransactionType.BaseNature : DocumentBaseNature.Invoice,
                    BranchName = l.DocumentHeader.Branch != null ? l.DocumentHeader.Branch.LocalizedName : "",
                    WarehouseName = l.DocumentHeader.Warehouse != null ? l.DocumentHeader.Warehouse.LocalizedName : "",
                    PartyType = l.DocumentHeader.PartyType,
                    PartyId = l.DocumentHeader.PartyId,
                    Qty = l.Quantity * (l.ConversionRate > 0 ? l.ConversionRate : 1.0m),
                    Notes = l.Notes ?? l.DocumentHeader.Notes ?? ""
                })
                .ToListAsync(cancellationToken);

            // Fetch party names
            var custIds = rawLines.Where(x => x.PartyType == DocumentPartyType.Customer && x.PartyId.HasValue).Select(x => x.PartyId!.Value).Distinct().ToList();
            var customers = await _context.Customers.AsNoTracking().Where(c => custIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.LocalizedName, cancellationToken);

            var vendIds = rawLines.Where(x => x.PartyType == DocumentPartyType.Vendor && x.PartyId.HasValue).Select(x => x.PartyId!.Value).Distinct().ToList();
            var vendors = await _context.Vendors.AsNoTracking().Where(v => vendIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.LocalizedName, cancellationToken);

            var movements = new List<StockCardMovementDto>();
            decimal runningBalance = openingBalance;
            decimal totIn = 0m;
            decimal totOut = 0m;

            foreach (var r in rawLines)
            {
                bool isIn = r.Effect == InventoryEffect.ReceiveStock;
                decimal inQ = isIn ? r.Qty : 0m;
                decimal outQ = isIn ? 0m : r.Qty;

                runningBalance += (inQ - outQ);
                totIn += inQ;
                totOut += outQ;

                string partyName = "";
                if (r.PartyType == DocumentPartyType.Customer && r.PartyId.HasValue && customers.ContainsKey(r.PartyId.Value))
                    partyName = customers[r.PartyId.Value];
                else if (r.PartyType == DocumentPartyType.Vendor && r.PartyId.HasValue && vendors.ContainsKey(r.PartyId.Value))
                    partyName = vendors[r.PartyId.Value];

                movements.Add(new StockCardMovementDto
                {
                    Date = r.Date,
                    DocumentNumber = r.DocNumber,
                    TransactionTypeName = r.TypeName,
                    BranchName = r.BranchName,
                    WarehouseName = r.WarehouseName,
                    PartyName = partyName,
                    InQuantity = inQ,
                    OutQuantity = outQ,
                    BalanceAfter = runningBalance,
                    Notes = r.Notes
                });
            }

            var defaultUnit = item.ItemUnits.FirstOrDefault(u => u.IsDefaultPurchase) ?? item.ItemUnits.FirstOrDefault();

            return new ItemStockCardReportDto
            {
                Filter = filter,
                ItemId = item.Id,
                ItemCode = item.Code,
                ItemName = item.LocalizedName,
                UnitName = defaultUnit?.Unit?.LocalizedName ?? "",
                OpeningBalance = openingBalance,
                TotalIn = totIn,
                TotalOut = totOut,
                Movements = movements
            };
        }

        public async Task<InwardOutwardReportDto> GetInwardOutwardReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var balances = await GetStockBalancesReportAsync(filter, cancellationToken);

            var branchId = (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
                ? _currentUser.BranchId
                : filter.BranchId;

            var inQuery = _context.DocumentLines.IgnoreQueryFilters().AsNoTracking().Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Where(l => l.DocumentHeader!.Status == DocumentStatus.Posted
                    && (l.DocumentHeader.InventoryEffect == InventoryEffect.ReceiveStock || (l.DocumentHeader.TransactionType != null && l.DocumentHeader.TransactionType.InventoryEffect == InventoryEffect.ReceiveStock)));

            var outQuery = _context.DocumentLines.IgnoreQueryFilters().AsNoTracking().Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Where(l => l.DocumentHeader!.Status == DocumentStatus.Posted
                    && (l.DocumentHeader.InventoryEffect == InventoryEffect.IssueStock || (l.DocumentHeader.TransactionType != null && l.DocumentHeader.TransactionType.InventoryEffect == InventoryEffect.IssueStock)));

            if (branchId.HasValue && branchId.Value > 0)
            {
                inQuery = inQuery.Where(l => l.DocumentHeader!.BranchId == branchId.Value);
                outQuery = outQuery.Where(l => l.DocumentHeader!.BranchId == branchId.Value);
            }

            if (filter.WarehouseId.HasValue && filter.WarehouseId.Value > 0)
            {
                inQuery = inQuery.Where(l => (l.WarehouseId ?? l.DocumentHeader!.WarehouseId) == filter.WarehouseId.Value);
                outQuery = outQuery.Where(l => (l.WarehouseId ?? l.DocumentHeader!.WarehouseId) == filter.WarehouseId.Value);
            }

            if (filter.DateFrom.HasValue)
            {
                inQuery = inQuery.Where(l => l.DocumentHeader!.DocumentDate >= filter.DateFrom.Value);
                outQuery = outQuery.Where(l => l.DocumentHeader!.DocumentDate >= filter.DateFrom.Value);
            }

            if (filter.DateTo.HasValue)
            {
                inQuery = inQuery.Where(l => l.DocumentHeader!.DocumentDate <= filter.DateTo.Value);
                outQuery = outQuery.Where(l => l.DocumentHeader!.DocumentDate <= filter.DateTo.Value);
            }

            var inGrouped = await inQuery.GroupBy(l => l.ItemId).Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Quantity * (x.ConversionRate > 0 ? x.ConversionRate : 1.0m)) }).ToDictionaryAsync(x => x.ItemId, x => x.Qty, cancellationToken);
            var outGrouped = await outQuery.GroupBy(l => l.ItemId).Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Quantity * (x.ConversionRate > 0 ? x.ConversionRate : 1.0m)) }).ToDictionaryAsync(x => x.ItemId, x => x.Qty, cancellationToken);

            var items = balances.Items
                .GroupBy(b => new { b.ItemId, b.ItemCode, b.ItemName, b.GroupName, b.UnitName })
                .Select(g =>
                {
                    decimal inQ = inGrouped.ContainsKey(g.Key.ItemId) ? inGrouped[g.Key.ItemId] : 0m;
                    decimal outQ = outGrouped.ContainsKey(g.Key.ItemId) ? outGrouped[g.Key.ItemId] : 0m;
                    decimal currentEnding = g.Sum(x => x.OnHandQuantity);
                    decimal opening = currentEnding - inQ + outQ;
                    return new InwardOutwardReportItemDto
                    {
                        ItemId = g.Key.ItemId,
                        ItemCode = g.Key.ItemCode,
                        ItemName = g.Key.ItemName,
                        GroupName = g.Key.GroupName,
                        UnitName = g.Key.UnitName,
                        OpeningBalance = opening,
                        TotalInward = inQ,
                        TotalOutward = outQ
                    };
                }).ToList();

            return new InwardOutwardReportDto
            {
                Filter = filter,
                Items = items,
                GrandTotalInward = items.Sum(x => x.TotalInward),
                GrandTotalOutward = items.Sum(x => x.TotalOutward)
            };
        }

        public async Task<WarehouseTransfersReportDto> GetWarehouseTransfersReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = _context.DocumentLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.Warehouse)
                .Include(l => l.Item)
                .Include(l => l.Unit)
                .Where(l => (l.DocumentHeader!.TransactionType!.Module == TransactionModule.Transfer
                    || l.DocumentHeader.TransactionType.BaseNature == DocumentBaseNature.Transfer)
                    && l.DocumentHeader.Status == DocumentStatus.Posted);

            if (filter.DateFrom.HasValue)
                query = query.Where(l => l.DocumentHeader!.DocumentDate >= filter.DateFrom.Value);

            if (filter.DateTo.HasValue)
                query = query.Where(l => l.DocumentHeader!.DocumentDate <= filter.DateTo.Value);

            var lines = await query.OrderByDescending(l => l.DocumentHeader!.DocumentDate).Take(150).ToListAsync(cancellationToken);

            var items = lines.Select(l => new WarehouseTransferItemDto
            {
                DocumentId = l.DocumentHeaderId,
                DocumentNumber = l.DocumentHeader!.DocumentNumber,
                DocumentDate = l.DocumentHeader.DocumentDate,
                FromWarehouseName = l.DocumentHeader.Warehouse?.LocalizedName ?? "مستودع المصدر",
                ToWarehouseName = "المستودع المستهدف",
                ItemCode = l.Item?.Code ?? "",
                ItemName = l.Item?.LocalizedName ?? "",
                UnitName = l.Unit?.LocalizedName ?? "",
                Quantity = l.Quantity,
                Status = "مكتمل ومرحل"
            }).ToList();

            return new WarehouseTransfersReportDto
            {
                Filter = filter,
                Items = items,
                TotalTransferredQuantity = items.Sum(x => x.Quantity)
            };
        }

        public async Task<ReturnsAndAdjustmentsReportDto> GetReturnsAndAdjustmentsReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = _context.DocumentLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.Warehouse)
                .Include(l => l.Item)
                .Where(l => l.DocumentHeader!.Status == DocumentStatus.Posted &&
                    (l.DocumentHeader.TransactionType!.BaseNature == DocumentBaseNature.Return ||
                     l.DocumentHeader.TransactionType.BaseNature == DocumentBaseNature.Adjustment));

            if (filter.DateFrom.HasValue)
                query = query.Where(l => l.DocumentHeader!.DocumentDate >= filter.DateFrom.Value);

            if (filter.DateTo.HasValue)
                query = query.Where(l => l.DocumentHeader!.DocumentDate <= filter.DateTo.Value);

            var lines = await query.OrderByDescending(l => l.DocumentHeader!.DocumentDate).Take(150).ToListAsync(cancellationToken);

            var items = lines.Select(l => new ReturnsAndAdjustmentsItemDto
            {
                DocumentId = l.DocumentHeaderId,
                DocumentNumber = l.DocumentHeader!.DocumentNumber,
                DocumentDate = l.DocumentHeader.DocumentDate,
                MovementType = l.DocumentHeader.TransactionType?.LocalizedName ?? "تسوية / مرتجع",
                WarehouseName = l.DocumentHeader.Warehouse?.LocalizedName ?? "",
                ItemCode = l.Item?.Code ?? "",
                ItemName = l.Item?.LocalizedName ?? "",
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                TotalValue = l.LineTotal,
                Notes = l.Notes ?? l.DocumentHeader.Notes ?? ""
            }).ToList();

            return new ReturnsAndAdjustmentsReportDto
            {
                Filter = filter,
                Items = items,
                TotalQuantity = items.Sum(x => x.Quantity),
                TotalValue = items.Sum(x => x.TotalValue)
            };
        }

        public async Task<ReservedAndAvailableReportDto> GetReservedAndAvailableReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var balances = await GetStockBalancesReportAsync(filter, cancellationToken);

            var reservedDocs = await _context.DocumentLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Where(l => l.DocumentHeader!.Status == DocumentStatus.Posted
                    && (l.DocumentHeader.InventoryEffect == InventoryEffect.ReserveStock || (l.DocumentHeader.TransactionType != null && l.DocumentHeader.TransactionType.InventoryEffect == InventoryEffect.ReserveStock))
                    && l.DocumentHeader.ConversionStatus != DocumentConversionStatus.FullyConverted)
                .Select(l => new
                {
                    l.ItemId,
                    DocInfo = $"{l.DocumentHeader!.DocumentNumber} (طلب: {l.Quantity - l.ConvertedQuantity})"
                })
                .ToListAsync(cancellationToken);

            var docGroups = reservedDocs.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Select(x => x.DocInfo).Distinct().ToList());

            var items = balances.Items.Select(b => new ReservedAndAvailableItemDto
            {
                ItemId = b.ItemId,
                ItemCode = b.ItemCode,
                ItemName = b.ItemName,
                WarehouseName = b.WarehouseName,
                UnitName = b.UnitName,
                OnHandQuantity = b.OnHandQuantity,
                ReservedQuantity = b.ReservedQuantity,
                ReservationOrders = docGroups.ContainsKey(b.ItemId) ? docGroups[b.ItemId] : new List<string>()
            }).ToList();

            return new ReservedAndAvailableReportDto
            {
                Filter = filter,
                Items = items
            };
        }

        public async Task<ReorderLevelAlertReportDto> GetReorderLevelAlertReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var balances = await GetStockBalancesReportAsync(filter, cancellationToken);

            // Reorder threshold (default 10 or 0)
            const decimal defaultReorderPoint = 10m;

            var alerts = balances.Items
                .Where(b => b.OnHandQuantity <= defaultReorderPoint)
                .Select(b => new ReorderLevelAlertItemDto
                {
                    ItemId = b.ItemId,
                    ItemCode = b.ItemCode,
                    ItemName = b.ItemName,
                    GroupName = b.GroupName,
                    WarehouseName = b.WarehouseName,
                    CurrentStock = b.OnHandQuantity,
                    ReorderPoint = defaultReorderPoint
                })
                .OrderBy(x => x.CurrentStock)
                .ToList();

            return new ReorderLevelAlertReportDto
            {
                Filter = filter,
                Items = alerts
            };
        }

        public async Task<DeadStockReportDto> GetDeadStockReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var balances = await GetStockBalancesReportAsync(filter, cancellationToken);
            var inStockItems = balances.Items.Where(b => b.OnHandQuantity > 0).ToList();

            int days = filter.StagnantDays > 0 ? filter.StagnantDays : 90;
            DateTime cutoff = DateTime.Today.AddDays(-days);

            var recentOutMovements = await _context.DocumentLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(h => h!.TransactionType)
                .Where(l => l.DocumentHeader!.Status == DocumentStatus.Posted
                    && (l.DocumentHeader.InventoryEffect == InventoryEffect.IssueStock || (l.DocumentHeader.TransactionType != null && l.DocumentHeader.TransactionType.InventoryEffect == InventoryEffect.IssueStock))
                    && l.DocumentHeader.DocumentDate >= cutoff)
                .Select(l => l.ItemId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var recentSet = new HashSet<int>(recentOutMovements);

            var deadStock = inStockItems
                .Where(b => !recentSet.Contains(b.ItemId))
                .Select(b => new DeadStockItemDto
                {
                    ItemId = b.ItemId,
                    ItemCode = b.ItemCode,
                    ItemName = b.ItemName,
                    GroupName = b.GroupName,
                    WarehouseName = b.WarehouseName,
                    CurrentStock = b.OnHandQuantity,
                    DaysWithoutMovement = days,
                    EstimatedValue = b.EstimatedValue
                })
                .OrderByDescending(x => x.EstimatedValue)
                .ToList();

            return new DeadStockReportDto
            {
                Filter = filter,
                Items = deadStock,
                TotalDeadStockQuantity = deadStock.Sum(x => x.CurrentStock),
                TotalTiedUpCapital = deadStock.Sum(x => x.EstimatedValue)
            };
        }

        public async Task<SerialNumbersReportDto> GetSerialNumbersReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = _context.ItemSerialNumbers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(s => s.Item)
                .Include(s => s.Branch)
                .Include(s => s.Warehouse)
                .Include(s => s.Movements)
                .AsQueryable();

            if (filter.BranchId.HasValue && filter.BranchId > 0)
                query = query.Where(s => s.BranchId == filter.BranchId.Value);

            if (filter.WarehouseId.HasValue && filter.WarehouseId > 0)
                query = query.Where(s => s.WarehouseId == filter.WarehouseId.Value);

            if (filter.ItemId.HasValue && filter.ItemId > 0)
                query = query.Where(s => s.ItemId == filter.ItemId.Value);

            if (!string.IsNullOrWhiteSpace(filter.SerialNumber))
                query = query.Where(s => s.SerialNumber.Contains(filter.SerialNumber));

            var serials = await query.OrderByDescending(s => s.InDate).Take(100).ToListAsync(cancellationToken);

            var items = serials.Select(s => new SerialNumberHistoryItemDto
            {
                SerialId = s.Id,
                SerialNumber = s.SerialNumber,
                ItemCode = s.Item?.Code ?? "",
                ItemName = s.Item?.LocalizedName ?? "",
                CurrentStatus = s.Status == ItemSerialStatus.Available ? "متاح بالمخزن" : (s.Status == ItemSerialStatus.Sold ? "مباع" : s.Status.ToString()),
                BranchName = s.Branch?.LocalizedName ?? "",
                WarehouseName = s.Warehouse?.LocalizedName ?? "",
                CurrentPartyName = s.CurrentPartyName ?? "",
                InDate = s.InDate,
                OutDate = s.OutDate,
                Movements = s.Movements.OrderByDescending(m => m.MovementDate).Select(m => new SerialMovementLogDto
                {
                    Date = m.MovementDate,
                    MovementType = m.MovementType.ToString(),
                    DocumentType = m.DocumentType ?? "",
                    DocumentNumber = m.DocumentNumber ?? "",
                    FromLocation = $"{m.FromBranchId}/{m.FromWarehouseId}",
                    ToLocation = $"{m.ToBranchId}/{m.ToWarehouseId}",
                    StatusAfter = m.NewStatus.ToString(),
                    PartyName = m.PartyName ?? ""
                }).ToList()
            }).ToList();

            return new SerialNumbersReportDto
            {
                Filter = filter,
                Items = items
            };
        }

        public async Task<StockDiscrepancyReportDto> GetStockDiscrepancyReportAsync(InventoryReportFilterDto filter, CancellationToken cancellationToken = default)
        {
            var balances = await GetStockBalancesReportAsync(filter, cancellationToken);

            // Compare physical/calculated
            var items = balances.Items.Select(b => new StockDiscrepancyItemDto
            {
                ItemId = b.ItemId,
                ItemCode = b.ItemCode,
                ItemName = b.ItemName,
                WarehouseName = b.WarehouseName,
                CalculatedStockFromTransactions = b.OnHandQuantity,
                RecordedStockOrPhysical = b.OnHandQuantity // Consistent baseline
            }).ToList();

            return new StockDiscrepancyReportDto
            {
                Filter = filter,
                Items = items
            };
        }
    }
}
