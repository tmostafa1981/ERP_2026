using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs.InventoryCosting;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class InventoryCostingController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly IInventoryCostingService _costingService;
        private readonly ICurrentUserContext _currentUser;

        public InventoryCostingController(
            ErpDbContext context,
            IInventoryCostingService costingService,
            ICurrentUserContext currentUser)
        {
            _context = context;
            _costingService = costingService;
            _currentUser = currentUser;
        }

        #region 1. Cost Inquiry & Valuation (استعلام متوسط التكلفة وتقييم المخزون)
        public async Task<IActionResult> Index(int? branchId, int? warehouseId, int? itemId, string? search)
        {
            await PopulateLookupsAsync(branchId, warehouseId);

            var query = _context.ItemWarehouseCosts
                .AsNoTracking()
                .Include(c => c.Branch)
                .Include(c => c.Warehouse)
                .Include(c => c.Item)
                .AsQueryable();

            if (branchId.HasValue && branchId.Value > 0)
                query = query.Where(c => c.BranchId == branchId.Value);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                query = query.Where(c => c.WarehouseId == warehouseId.Value);

            if (itemId.HasValue && itemId.Value > 0)
                query = query.Where(c => c.ItemId == itemId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(c => (c.Item != null && (c.Item.Code.Contains(s) || c.Item.NameAr.Contains(s) || c.Item.NameEn.Contains(s))));
            }

            var items = await query
                .OrderBy(c => c.Branch!.Code)
                .ThenBy(c => c.Warehouse!.Code)
                .ThenBy(c => c.Item!.Code)
                .Select(c => new ItemWarehouseCostDto
                {
                    Id = c.Id,
                    BranchId = c.BranchId,
                    BranchName = c.Branch != null ? c.Branch.LocalizedName : "",
                    WarehouseId = c.WarehouseId,
                    WarehouseName = c.Warehouse != null ? c.Warehouse.LocalizedName : "",
                    ItemId = c.ItemId,
                    ItemCode = c.Item != null ? c.Item.Code : "",
                    ItemName = c.Item != null ? c.Item.LocalizedName : "",
                    CurrentQuantity = c.CurrentQuantity,
                    TotalCost = c.TotalCost,
                    MovingAverageCost = c.MovingAverageCost,
                    LastUpdatedDate = c.LastUpdatedDate,
                    LastDocumentId = c.LastDocumentId
                })
                .ToListAsync();

            ViewBag.SelectedBranchId = branchId;
            ViewBag.SelectedWarehouseId = warehouseId;
            ViewBag.SelectedItemId = itemId;
            ViewBag.Search = search;

            ViewBag.TotalValuation = items.Sum(i => i.TotalCost);
            ViewBag.TotalQuantity = items.Sum(i => i.CurrentQuantity);
            ViewBag.TotalItemsCount = items.Count;

            return View(items);
        }
        #endregion

        #region 2. Cost History Movement Log (سجل تاريخ حركات التكلفة)
        public async Task<IActionResult> CostHistory(int? itemId, int? warehouseId, int? branchId, DateTime? fromDate, DateTime? toDate)
        {
            await PopulateLookupsAsync(branchId, warehouseId);

            var query = _context.ItemCostHistories
                .AsNoTracking()
                .Include(h => h.Branch)
                .Include(h => h.Warehouse)
                .Include(h => h.Item)
                .Include(h => h.DocumentHeader).ThenInclude(d => d!.TransactionType)
                .AsQueryable();

            if (itemId.HasValue && itemId.Value > 0)
                query = query.Where(h => h.ItemId == itemId.Value);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                query = query.Where(h => h.WarehouseId == warehouseId.Value);

            if (branchId.HasValue && branchId.Value > 0)
                query = query.Where(h => h.BranchId == branchId.Value);

            if (fromDate.HasValue)
                query = query.Where(h => h.MovementDate >= fromDate.Value.Date);

            if (toDate.HasValue)
                query = query.Where(h => h.MovementDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));

            var history = await query
                .OrderByDescending(h => h.MovementDate)
                .ThenByDescending(h => h.Id)
                .Take(500)
                .Select(h => new ItemCostHistoryDto
                {
                    Id = h.Id,
                    BranchId = h.BranchId,
                    BranchName = h.Branch != null ? h.Branch.LocalizedName : "",
                    WarehouseId = h.WarehouseId,
                    WarehouseName = h.Warehouse != null ? h.Warehouse.LocalizedName : "",
                    ItemId = h.ItemId,
                    ItemCode = h.Item != null ? h.Item.Code : "",
                    ItemName = h.Item != null ? h.Item.LocalizedName : "",
                    DocumentHeaderId = h.DocumentHeaderId,
                    DocumentNumber = h.DocumentHeader != null ? h.DocumentHeader.DocumentNumber : "",
                    MovementType = h.MovementType,
                    MovementDate = h.MovementDate,
                    PreQuantity = h.PreQuantity,
                    Quantity = h.Quantity,
                    PostQuantity = h.PostQuantity,
                    PreAverageCost = h.PreAverageCost,
                    UnitCost = h.UnitCost,
                    TotalCost = h.TotalCost,
                    PostAverageCost = h.PostAverageCost,
                    PreTotalCost = h.PreTotalCost,
                    PostTotalCost = h.PostTotalCost,
                    Notes = h.Notes,
                    CreatedAt = h.CreatedAt
                })
                .ToListAsync();

            ViewBag.SelectedItemId = itemId;
            ViewBag.SelectedWarehouseId = warehouseId;
            ViewBag.SelectedBranchId = branchId;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

            return View(history);
        }
        #endregion

        #region 3. Cost Adjustment Audit Trail (سجل تسويات التكلفة والقيود)
        public async Task<IActionResult> AdjustmentAuditTrail(string? batchNumber, int? sourceDocId, int? branchId, DateTime? fromDate, DateTime? toDate)
        {
            await PopulateLookupsAsync(branchId, null);

            var query = _context.CostAdjustmentRecords
                .AsNoTracking()
                .Include(r => r.Branch)
                .Include(r => r.Warehouse)
                .Include(r => r.Item)
                .Include(r => r.SourceDocumentHeader)
                .Include(r => r.AffectedDocumentHeader)
                .Include(r => r.AdjustmentJournalEntry)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(batchNumber))
                query = query.Where(r => r.BatchNumber.Contains(batchNumber.Trim()));

            if (sourceDocId.HasValue && sourceDocId.Value > 0)
                query = query.Where(r => r.SourceDocumentHeaderId == sourceDocId.Value);

            if (branchId.HasValue && branchId.Value > 0)
                query = query.Where(r => r.BranchId == branchId.Value);

            if (fromDate.HasValue)
                query = query.Where(r => r.MovementDate >= fromDate.Value.Date);

            if (toDate.HasValue)
                query = query.Where(r => r.MovementDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));

            var records = await query
                .OrderByDescending(r => r.CreatedAt)
                .Take(500)
                .Select(r => new CostAdjustmentRecordDto
                {
                    Id = r.Id,
                    BatchNumber = r.BatchNumber,
                    BranchId = r.BranchId,
                    BranchName = r.Branch != null ? r.Branch.LocalizedName : "",
                    WarehouseId = r.WarehouseId,
                    WarehouseName = r.Warehouse != null ? r.Warehouse.LocalizedName : "",
                    ItemId = r.ItemId,
                    ItemCode = r.Item != null ? r.Item.Code : "",
                    ItemName = r.Item != null ? r.Item.LocalizedName : "",
                    SourceDocumentHeaderId = r.SourceDocumentHeaderId,
                    SourceDocumentNumber = r.SourceDocumentHeader != null ? r.SourceDocumentHeader.DocumentNumber : "",
                    AffectedDocumentHeaderId = r.AffectedDocumentHeaderId,
                    AffectedDocumentNumber = r.AffectedDocumentHeader != null ? r.AffectedDocumentHeader.DocumentNumber : "",
                    AffectedMovementType = r.AffectedMovementType,
                    MovementDate = r.MovementDate,
                    OriginalUnitCost = r.OriginalUnitCost,
                    RecalculatedUnitCost = r.RecalculatedUnitCost,
                    OriginalTotalCost = r.OriginalTotalCost,
                    RecalculatedTotalCost = r.RecalculatedTotalCost,
                    CostDifference = r.CostDifference,
                    AdjustmentJournalEntryId = r.AdjustmentJournalEntryId,
                    AdjustmentJournalEntryNumber = r.AdjustmentJournalEntry != null ? r.AdjustmentJournalEntry.EntryNumber : "",
                    EffectiveDate = r.EffectiveDate,
                    IsClosedPeriod = r.IsClosedPeriod,
                    Reason = r.Reason,
                    CreatedByUserName = r.CreatedByUserId,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();

            ViewBag.BatchNumber = batchNumber;
            ViewBag.SourceDocId = sourceDocId;
            ViewBag.SelectedBranchId = branchId;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

            ViewBag.TotalDifference = records.Sum(r => r.CostDifference);
            ViewBag.ClosedPeriodsCount = records.Count(r => r.IsClosedPeriod);

            return View(records);
        }
        #endregion

        #region 4. Sales Profitability & COGS Report (تقرير ربحية المبيعات وتكلفة البضاعة المباعة)
        public async Task<IActionResult> Profitability(DateTime? fromDate, DateTime? toDate, int? branchId, int? customerId, int? itemId)
        {
            await PopulateLookupsAsync(branchId, null);

            var query = _context.DocumentLines
                .AsNoTracking()
                .Include(l => l.DocumentHeader).ThenInclude(d => d!.Branch)
                .Include(l => l.DocumentHeader).ThenInclude(d => d!.TransactionType)
                .Include(l => l.Item)
                .Where(l => l.DocumentHeader != null &&
                            l.DocumentHeader.Status == DocumentStatus.Posted &&
                            l.DocumentHeader.TransactionType != null &&
                            l.DocumentHeader.TransactionType.Module == TransactionModule.Sales &&
                            (l.Item == null || !l.Item.IsService))
                .AsQueryable();

            if (branchId.HasValue && branchId.Value > 0)
                query = query.Where(l => l.DocumentHeader!.BranchId == branchId.Value);

            if (customerId.HasValue && customerId.Value > 0)
                query = query.Where(l => l.DocumentHeader!.PartyId == customerId.Value);

            if (itemId.HasValue && itemId.Value > 0)
                query = query.Where(l => l.ItemId == itemId.Value);

            if (fromDate.HasValue)
                query = query.Where(l => l.DocumentHeader!.DocumentDate >= fromDate.Value.Date);

            if (toDate.HasValue)
                query = query.Where(l => l.DocumentHeader!.DocumentDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));

            var lines = await query
                .OrderByDescending(l => l.DocumentHeader!.DocumentDate)
                .Take(500)
                .Select(l => new
                {
                    DocumentId = l.DocumentHeader!.Id,
                    DocumentNumber = l.DocumentHeader.DocumentNumber,
                    DocumentDate = l.DocumentHeader.DocumentDate,
                    BranchName = l.DocumentHeader.Branch != null ? l.DocumentHeader.Branch.LocalizedName : "",
                    IsReturn = l.DocumentHeader.TransactionType!.BaseNature == DocumentBaseNature.Return,
                    ItemCode = l.Item != null ? l.Item.Code : "",
                    ItemName = l.Item != null ? l.Item.LocalizedName : "",
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    SalesAmount = l.UnitPrice * l.Quantity - l.DiscountAmount,
                    UnitCost = l.UnitCost,
                    TotalCost = l.TotalCost
                })
                .ToListAsync();

            var reportData = lines.Select(l =>
            {
                decimal sign = l.IsReturn ? -1m : 1m;
                decimal netSales = l.SalesAmount * sign;
                decimal netCogs = l.TotalCost * sign;
                decimal profit = netSales - netCogs;
                decimal margin = netSales > 0 ? Math.Round((profit / netSales) * 100, 2) : 0m;

                return new SalesProfitabilityLineDto
                {
                    DocumentId = l.DocumentId,
                    DocumentNumber = l.DocumentNumber,
                    DocumentDate = l.DocumentDate,
                    BranchName = l.BranchName,
                    ItemCode = l.ItemCode,
                    ItemName = l.ItemName,
                    Quantity = l.Quantity * sign,
                    SalesRevenue = netSales,
                    UnitCost = l.UnitCost,
                    CostOfGoodsSold = netCogs,
                    GrossProfit = profit,
                    ProfitMarginPercentage = margin
                };
            }).ToList();

            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
            ViewBag.SelectedBranchId = branchId;
            ViewBag.SelectedCustomerId = customerId;
            ViewBag.SelectedItemId = itemId;

            ViewBag.TotalSales = reportData.Sum(r => r.SalesRevenue);
            ViewBag.TotalCogs = reportData.Sum(r => r.CostOfGoodsSold);
            ViewBag.TotalProfit = reportData.Sum(r => r.GrossProfit);
            decimal overallMargin = ViewBag.TotalSales > 0 ? Math.Round((ViewBag.TotalProfit / ViewBag.TotalSales) * 100, 2) : 0m;
            ViewBag.OverallMargin = overallMargin;

            return View(reportData);
        }
        #endregion

        #region 5. Recalculate AJAX Action (إعادة الاحتساب اليدوي)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Recalculate(int purchaseInvoiceId)
        {
            if (purchaseInvoiceId <= 0)
                return Json(new { success = false, message = "رقم فاتورة الشراء غير صالح." });

            var res = await _costingService.RecalculateCostsAfterPurchaseModificationAsync(purchaseInvoiceId, _currentUser.UserId ?? "admin");
            return Json(new
            {
                success = res.Success,
                message = res.Message,
                summary = res.Summary
            });
        }
        #endregion

        #region Lookups Helper
        private async Task PopulateLookupsAsync(int? branchId, int? warehouseId)
        {
            var branchesQuery = _context.Branches.AsNoTracking().Where(b => b.IsActive);
            if (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
            {
                branchesQuery = branchesQuery.Where(b => b.Id == _currentUser.BranchId);
            }
            var branches = await branchesQuery.OrderBy(b => b.Code).ToListAsync();
            ViewBag.Branches = new SelectList(branches, "Id", "LocalizedName", branchId);

            var whQuery = _context.Warehouses.AsNoTracking().Where(w => w.IsActive);
            if (branchId.HasValue && branchId.Value > 0)
                whQuery = whQuery.Where(w => w.BranchId == branchId.Value);

            var warehouses = await whQuery.OrderBy(w => w.Code).ToListAsync();
            ViewBag.Warehouses = new SelectList(warehouses, "Id", "LocalizedName", warehouseId);

            var items = await _context.Items.AsNoTracking()
                .Where(i => i.IsActive && !i.IsService)
                .OrderBy(i => i.Code)
                .Select(i => new { i.Id, Name = $"{i.Code} - {i.LocalizedName}" })
                .Take(200)
                .ToListAsync();
            ViewBag.Items = new SelectList(items, "Id", "Name");

            var customers = await _context.Customers.AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Code)
                .Select(c => new { c.Id, Name = $"{c.Code} - {c.LocalizedName}" })
                .Take(200)
                .ToListAsync();
            ViewBag.Customers = new SelectList(customers, "Id", "Name");
        }
        #endregion
    }

    public class SalesProfitabilityLineDto
    {
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal SalesRevenue { get; set; }
        public decimal UnitCost { get; set; }
        public decimal CostOfGoodsSold { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal ProfitMarginPercentage { get; set; }
    }
}
