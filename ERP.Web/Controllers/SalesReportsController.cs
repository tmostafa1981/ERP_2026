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
    public class SalesReportsController : Controller
    {
        private readonly ISalesReportService _salesReportService;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public SalesReportsController(
            ISalesReportService salesReportService,
            ErpDbContext context,
            ICurrentUserContext currentUser)
        {
            _salesReportService = salesReportService;
            _context = context;
            _currentUser = currentUser;
        }

        #region Helper & Cascading Lookups
        private async Task PopulateViewBagsAsync(SalesReportFilterDto filter)
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

            // 3. Customer (Dynamic Lookup - only resolve selected customer display)
            if (filter.CustomerId.HasValue && filter.CustomerId.Value > 0)
            {
                ViewBag.SelectedCustomerDisplay = await _context.Customers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(c => c.Id == filter.CustomerId.Value)
                    .Select(c => c.Code + " - " + (c.NameAr ?? c.NameEn))
                    .FirstOrDefaultAsync();
            }

            // 4. Salesmen
            var salesmen = await _context.Salesmen.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.NameAr).ToListAsync();
            ViewBag.Salesmen = new SelectList(salesmen, "Id", "LocalizedName", filter.SalesmanId);

            // 5. Item (Dynamic Lookup - only resolve selected item display)
            if (filter.ItemId.HasValue && filter.ItemId.Value > 0)
            {
                ViewBag.SelectedItemDisplay = await _context.Items
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(i => i.Id == filter.ItemId.Value)
                    .Select(i => i.Code + " - " + (i.NameAr ?? i.NameEn))
                    .FirstOrDefaultAsync();
            }

            // 6. Item Groups
            var groups = await _context.ItemGroups.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.NameAr).ToListAsync();
            ViewBag.ItemGroups = new SelectList(groups, "Id", "LocalizedName", filter.ItemGroupId);

            // 7. Transaction Types
            var salesTypes = await _context.TransactionTypes.AsNoTracking()
                .Where(t => t.Module == TransactionModule.Sales && t.IsActive)
                .OrderBy(t => t.Code)
                .ToListAsync();
            ViewBag.TransactionTypes = new SelectList(salesTypes, "Id", "LocalizedName", filter.TransactionTypeId);
        }

        [HttpGet]
        [HasPermission("Reports.SalesReports.View,Sales.SalesReportsCatalog.View,Sales.SalesSummary.View,Sales.InvoiceDetails.View,Sales.ByItem.View,Sales.ByCustomer.View,Sales.BySalesman.View,Sales.ReturnsAndNet.View,Sales.BranchComparison.View,Sales.TopSellingAndStagnant.View,Sales.FulfillmentTracking.View")]
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
        [HasPermission("Reports.SalesReports.View,Sales.SalesReportsCatalog.View,Sales.SalesSummary.View,Sales.InvoiceDetails.View,Sales.ByItem.View,Sales.ByCustomer.View,Sales.BySalesman.View,Sales.ReturnsAndNet.View,Sales.BranchComparison.View,Sales.TopSellingAndStagnant.View,Sales.FulfillmentTracking.View")]
        public async Task<IActionResult> GetCustomersByBranch(int? branchId)
        {
            var query = _context.Customers.IgnoreQueryFilters().AsNoTracking().Where(c => c.IsActive);
            if (branchId.HasValue && branchId.Value > 0)
            {
                query = query.Where(c => c.BranchId == branchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                var allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUser.UserId && ub.Branch.IsActive)
                    .Select(ub => ub.BranchId)
                    .ToListAsync();
                if (allowedBranchIds.Any())
                {
                    query = query.Where(c => allowedBranchIds.Contains(c.BranchId));
                }
                else if (_currentUser.BranchId > 0)
                {
                    query = query.Where(c => c.BranchId == _currentUser.BranchId);
                }
            }

            var list = await query
                .OrderBy(c => c.Code)
                .Select(c => new { id = c.Id, text = c.Code + " - " + c.NameAr })
                .ToListAsync();

            return Json(list);
        }
        #endregion

        [HttpGet]
        [HasPermission("Sales.ReportsCatalog.View,Reports.SalesReports.View,Reports.GeneralReports.View")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [HasPermission("Sales.SalesSummary.View,Reports.SalesReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> SalesSummary(SalesReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _salesReportService.GetSalesSummaryReportAsync(filter)
                : new SalesSummaryReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Sales.InvoiceDetails.View,Reports.SalesReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> InvoiceDetails(SalesReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _salesReportService.GetSalesInvoiceDetailsReportAsync(filter)
                : new SalesInvoiceDetailReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Sales.ByItem.View,Reports.SalesReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> ByItem(SalesReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _salesReportService.GetSalesByItemReportAsync(filter)
                : new SalesByItemReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Sales.ByCustomer.View,Reports.SalesReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> ByCustomer(SalesReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _salesReportService.GetSalesByCustomerReportAsync(filter)
                : new SalesByCustomerReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Sales.BySalesman.View,Reports.SalesReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> BySalesman(SalesReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _salesReportService.GetSalesBySalesmanReportAsync(filter)
                : new SalesBySalesmanReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Sales.ReturnsAndNet.View,Reports.SalesReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> ReturnsAndNet(SalesReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _salesReportService.GetSalesReturnsAndNetReportAsync(filter)
                : new SalesReturnsAndNetReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Sales.BranchComparison.View,Reports.SalesReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> BranchComparison(SalesReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _salesReportService.GetSalesComparisonReportAsync(filter)
                : new SalesComparisonReportDto { ComparisonBasis = "Branches" };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Sales.TopSellingAndStagnant.View,Reports.SalesReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> TopSellingAndStagnant(SalesReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _salesReportService.GetTopSellingAndStagnantItemsReportAsync(filter)
                : new TopSellingAndStagnantItemsReportDto { Filter = filter };

            return View(report);
        }

        [HttpGet]
        [HasPermission("Sales.FulfillmentTracking.View,Reports.SalesReports.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> FulfillmentTracking(SalesReportFilterDto filter)
        {
            await PopulateViewBagsAsync(filter);
            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            var report = isRequested
                ? await _salesReportService.GetSalesFulfillmentTrackingReportAsync(filter)
                : new SalesFulfillmentTrackingReportDto { Filter = filter };

            return View(report);
        }
    }
}
