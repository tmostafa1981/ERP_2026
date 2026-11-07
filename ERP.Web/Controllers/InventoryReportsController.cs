using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs.Reports;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class InventoryReportsController : Controller
    {
        private readonly IInventoryReportService _inventoryReportService;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public InventoryReportsController(
            IInventoryReportService inventoryReportService,
            ErpDbContext context,
            ICurrentUserContext currentUser)
        {
            _inventoryReportService = inventoryReportService;
            _context = context;
            _currentUser = currentUser;
        }

        #region Helper & Cascading Lookups
        private async Task PopulateViewBagsAsync(InventoryReportFilterDto filter)
        {
            // 1. Branches based on user permissions
            var branchesQuery = _context.Branches.AsNoTracking().Where(b => b.IsActive);
            if (!_currentUser.HasAllBranches)
            {
                var allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUser.UserId && ub.Branch.IsActive)
                    .Select(ub => ub.BranchId)
                    .ToListAsync();

                if (allowedBranchIds.Any())
                {
                    branchesQuery = branchesQuery.Where(b => allowedBranchIds.Contains(b.Id));
                }
                else if (_currentUser.BranchId > 0)
                {
                    branchesQuery = branchesQuery.Where(b => b.Id == _currentUser.BranchId);
                }
            }
            var branches = await branchesQuery.OrderBy(b => b.Code).ToListAsync();
            ViewBag.Branches = new SelectList(branches, "Id", "LocalizedName", filter.BranchId);

            // 2. Warehouses (filtered by selected Branch or user's authorized branches)
            var warehousesQuery = _context.Warehouses.AsNoTracking().Where(w => w.IsActive);
            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                warehousesQuery = warehousesQuery.Where(w => w.BranchId == filter.BranchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                var allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUser.UserId && ub.Branch.IsActive)
                    .Select(ub => ub.BranchId)
                    .ToListAsync();

                if (allowedBranchIds.Any())
                {
                    warehousesQuery = warehousesQuery.Where(w => allowedBranchIds.Contains(w.BranchId));
                }
                else if (_currentUser.BranchId > 0)
                {
                    warehousesQuery = warehousesQuery.Where(w => w.BranchId == _currentUser.BranchId);
                }
            }
            var warehouses = await warehousesQuery.OrderBy(w => w.Code).ToListAsync();
            ViewBag.Warehouses = new SelectList(warehouses, "Id", "LocalizedName", filter.WarehouseId);

            // 3. Item (Dynamic Lookup - only resolve selected item display)
            if (filter.ItemId.HasValue && filter.ItemId.Value > 0)
            {
                ViewBag.SelectedItemDisplay = await _context.Items
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(i => i.Id == filter.ItemId.Value)
                    .Select(i => i.Code + " - " + (i.NameAr ?? i.NameEn))
                    .FirstOrDefaultAsync();
            }

            // 4. Item Groups
            var groups = await _context.ItemGroups.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.NameAr).ToListAsync();
            ViewBag.ItemGroups = new SelectList(groups, "Id", "LocalizedName", filter.ItemGroupId);
        }

        [HttpGet]
        [HasPermission("Reports.InventoryReports.View,Inventory.ReportsCatalog.View,Inventory.StockBalances.View,Inventory.ItemStockCard.View,Inventory.InwardOutward.View,Inventory.WarehouseTransfers.View,Inventory.ReturnsAndAdjustments.View,Inventory.ReservedAndAvailable.View,Inventory.ReorderLevelAlert.View,Inventory.DeadStock.View,Inventory.SerialNumbersReport.View,Inventory.StockDiscrepancy.View")]
        public async Task<IActionResult> GetWarehousesByBranch(int? branchId)
        {
            var query = _context.Warehouses.AsNoTracking().Where(w => w.IsActive);
            if (branchId.HasValue && branchId.Value > 0)
            {
                query = query.Where(w => w.BranchId == branchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                var allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUser.UserId && ub.Branch.IsActive)
                    .Select(ub => ub.BranchId)
                    .ToListAsync();
                if (allowedBranchIds.Any())
                {
                    query = query.Where(w => allowedBranchIds.Contains(w.BranchId));
                }
                else if (_currentUser.BranchId > 0)
                {
                    query = query.Where(w => w.BranchId == _currentUser.BranchId);
                }
            }

            var list = await query
                .OrderBy(w => w.Code)
                .Select(w => new { id = w.Id, text = w.Code + " - " + w.NameAr })
                .ToListAsync();

            return Json(list);
        }
        #endregion

        [HttpGet]
        [HasPermission("Inventory.ReportsCatalog.View,Reports.InventoryReports.View,Reports.GeneralReports.View")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [HasPermission("Inventory.StockBalances.View,Reports.InventoryReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> StockBalances(InventoryReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _inventoryReportService.GetStockBalancesReportAsync(filter)
                : new StockBalanceReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Inventory.ItemStockCard.View,Reports.InventoryReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> ItemStockCard(InventoryReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = (Request.Query.ContainsKey("submitted") || filter.ItemId.HasValue) && filter.ItemId.HasValue && filter.ItemId.Value > 0;
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _inventoryReportService.GetItemStockCardReportAsync(filter)
                : new ItemStockCardReportDto { Filter = filter, ItemId = filter.ItemId ?? 0 };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Inventory.InwardOutward.View,Reports.InventoryReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> InwardOutward(InventoryReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _inventoryReportService.GetInwardOutwardReportAsync(filter)
                : new InwardOutwardReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Inventory.WarehouseTransfers.View,Reports.InventoryReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> WarehouseTransfers(InventoryReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _inventoryReportService.GetWarehouseTransfersReportAsync(filter)
                : new WarehouseTransfersReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Inventory.ReturnsAndAdjustments.View,Reports.InventoryReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> ReturnsAndAdjustments(InventoryReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _inventoryReportService.GetReturnsAndAdjustmentsReportAsync(filter)
                : new ReturnsAndAdjustmentsReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Inventory.ReservedAndAvailable.View,Reports.InventoryReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> ReservedAndAvailable(InventoryReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _inventoryReportService.GetReservedAndAvailableReportAsync(filter)
                : new ReservedAndAvailableReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Inventory.ReorderLevelAlert.View,Reports.InventoryReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> ReorderLevelAlert(InventoryReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _inventoryReportService.GetReorderLevelAlertReportAsync(filter)
                : new ReorderLevelAlertReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Inventory.DeadStock.View,Reports.InventoryReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> DeadStock(InventoryReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _inventoryReportService.GetDeadStockReportAsync(filter)
                : new DeadStockReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Inventory.SerialNumbersReport.View,Reports.InventoryReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> SerialNumbers(InventoryReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _inventoryReportService.GetSerialNumbersReportAsync(filter)
                : new SerialNumbersReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Inventory.StockDiscrepancy.View,Reports.InventoryReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> StockDiscrepancy(InventoryReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _inventoryReportService.GetStockDiscrepancyReportAsync(filter)
                : new StockDiscrepancyReportDto { Filter = filter };

            return View(report);
        }
    }
}
