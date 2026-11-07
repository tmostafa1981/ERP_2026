using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs.Reports;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class PurchaseReportsController : Controller
    {
        private readonly IPurchaseReportService _purchaseReportService;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public PurchaseReportsController(
            IPurchaseReportService purchaseReportService,
            ErpDbContext context,
            ICurrentUserContext currentUser)
        {
            _purchaseReportService = purchaseReportService;
            _context = context;
            _currentUser = currentUser;
        }

        #region Helper & Cascading Lookups
        private async Task PopulateViewBagsAsync(PurchaseReportFilterDto filter)
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

            // 3. Vendor (Dynamic Lookup - only resolve selected vendor display)
            if (filter.VendorId.HasValue && filter.VendorId.Value > 0)
            {
                ViewBag.SelectedVendorDisplay = await _context.Vendors
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(v => v.Id == filter.VendorId.Value)
                    .Select(v => v.Code + " - " + (v.NameAr ?? v.NameEn))
                    .FirstOrDefaultAsync();
            }

            // 4. Item (Dynamic Lookup - only resolve selected item display)
            if (filter.ItemId.HasValue && filter.ItemId.Value > 0)
            {
                ViewBag.SelectedItemDisplay = await _context.Items
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(i => i.Id == filter.ItemId.Value)
                    .Select(i => i.Code + " - " + (i.NameAr ?? i.NameEn))
                    .FirstOrDefaultAsync();
            }

            // 5. Item Groups
            var groups = await _context.ItemGroups.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.NameAr).ToListAsync();
            ViewBag.ItemGroups = new SelectList(groups, "Id", "LocalizedName", filter.ItemGroupId);

            // 6. Transaction Types
            var purchaseTypes = await _context.TransactionTypes.AsNoTracking()
                .Where(t => t.Module == TransactionModule.Purchase && t.IsActive)
                .OrderBy(t => t.Code)
                .ToListAsync();
            ViewBag.TransactionTypes = new SelectList(purchaseTypes, "Id", "LocalizedName", filter.TransactionTypeId);
        }

        [HttpGet]
        [HasPermission("Reports.PurchaseReports.View,Purchases.PurchaseReportsCatalog.View,Purchases.PurchaseSummary.View,Purchases.InvoiceDetails.View,Purchases.ByItem.View,Purchases.ByVendor.View,Purchases.ReturnsAndNet.View,Purchases.PriceHistory.View,Purchases.PendingStock.View,Purchases.ReceiptTracking.View")]
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

        [HttpGet]
        [HasPermission("Reports.PurchaseReports.View,Purchases.PurchaseReportsCatalog.View,Purchases.PurchaseSummary.View,Purchases.InvoiceDetails.View,Purchases.ByItem.View,Purchases.ByVendor.View,Purchases.ReturnsAndNet.View,Purchases.PriceHistory.View,Purchases.PendingStock.View,Purchases.ReceiptTracking.View")]
        public async Task<IActionResult> GetVendorsByBranch(int? branchId)
        {
            var query = _context.Vendors.IgnoreQueryFilters().AsNoTracking().Where(v => v.IsActive);
            if (branchId.HasValue && branchId.Value > 0)
            {
                query = query.Where(v => v.BranchId == branchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                var allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUser.UserId && ub.Branch.IsActive)
                    .Select(ub => ub.BranchId)
                    .ToListAsync();
                if (allowedBranchIds.Any())
                {
                    query = query.Where(v => allowedBranchIds.Contains(v.BranchId));
                }
                else if (_currentUser.BranchId > 0)
                {
                    query = query.Where(v => v.BranchId == _currentUser.BranchId);
                }
            }

            var list = await query
                .OrderBy(v => v.Code)
                .Select(v => new { id = v.Id, text = v.Code + " - " + v.NameAr })
                .ToListAsync();

            return Json(list);
        }
        #endregion

        [HttpGet]
        [HasPermission("Purchases.ReportsCatalog.View,Reports.PurchaseReports.View,Reports.GeneralReports.View")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [HasPermission("Purchases.PurchaseSummary.View,Reports.PurchaseReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> PurchaseSummary(PurchaseReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _purchaseReportService.GetPurchaseSummaryReportAsync(filter)
                : new PurchaseSummaryReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Purchases.InvoiceDetails.View,Reports.PurchaseReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> InvoiceDetails(PurchaseReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _purchaseReportService.GetPurchaseInvoiceDetailsReportAsync(filter)
                : new PurchaseInvoiceDetailReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Purchases.ByItem.View,Reports.PurchaseReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> ByItem(PurchaseReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _purchaseReportService.GetPurchaseByItemReportAsync(filter)
                : new PurchaseByItemReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Purchases.ByVendor.View,Reports.PurchaseReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> ByVendor(PurchaseReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _purchaseReportService.GetPurchaseByVendorReportAsync(filter)
                : new PurchaseByVendorReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Purchases.ReturnsAndNet.View,Reports.PurchaseReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> ReturnsAndNet(PurchaseReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _purchaseReportService.GetPurchaseReturnsAndNetReportAsync(filter)
                : new PurchaseReturnsAndNetReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Purchases.PriceHistory.View,Reports.PurchaseReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> PriceHistory(PurchaseReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _purchaseReportService.GetPurchasePriceHistoryReportAsync(filter)
                : new PurchasePriceHistoryReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Purchases.PendingStock.View,Reports.PurchaseReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> PendingStock(PurchaseReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _purchaseReportService.GetPurchasesPendingStockMovementReportAsync(filter)
                : new PurchasesPendingStockMovementReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Purchases.ReceiptTracking.View,Reports.PurchaseReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> ReceiptTracking(PurchaseReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _purchaseReportService.GetPurchaseReceiptTrackingReportAsync(filter)
                : new PurchaseReceiptTrackingReportDto { Filter = filter };

            return View(report);
        }
    }
}
