using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ERP.Application.DTOs.FixedAssets;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class FixedAssetReportsController : Controller
    {
        private readonly IFixedAssetReportService _reportService;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public FixedAssetReportsController(
            IFixedAssetReportService reportService,
            ErpDbContext context,
            ICurrentUserContext currentUser)
        {
            _reportService = reportService;
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<IActionResult> Index(string reportType = "Register", AssetReportFilterDto? filter = null)
        {
            filter ??= new AssetReportFilterDto();
            filter.BranchId ??= _currentUser.HasAllBranches ? null : _currentUser.BranchId;

            await PopulateFilterDropDownsAsync();

            ViewBag.ReportType = reportType;
            ViewBag.Filter = filter;

            switch (reportType)
            {
                case "DepreciationHistory":
                    var depHist = await _reportService.GetDepreciationHistoryReportAsync(filter, _currentUser.UserId ?? "SYSTEM");
                    return View("DepreciationHistoryReport", depHist);

                case "Movements":
                    var movements = await _reportService.GetAssetMovementReportAsync(filter, _currentUser.UserId ?? "SYSTEM");
                    return View("MovementsReport", movements);

                case "Disposed":
                    var disposed = await _reportService.GetDisposedAssetsReportAsync(filter, _currentUser.UserId ?? "SYSTEM");
                    return View("RegisterReport", disposed);

                case "CIP":
                    var cip = await _reportService.GetCipAssetsReportAsync(filter, _currentUser.UserId ?? "SYSTEM");
                    return View("RegisterReport", cip);

                case "PendingDepreciation":
                    var pending = await _reportService.GetUnapprovedOrPendingDepreciationReportAsync(filter, _currentUser.UserId ?? "SYSTEM");
                    return View("RegisterReport", pending);

                case "Register":
                default:
                    var register = await _reportService.GetAssetRegisterReportAsync(filter, _currentUser.UserId ?? "SYSTEM");
                    return View("RegisterReport", register);
            }
        }

        public async Task<IActionResult> ExportCsv(string reportType = "Register", AssetReportFilterDto? filter = null)
        {
            filter ??= new AssetReportFilterDto();
            filter.BranchId ??= _currentUser.HasAllBranches ? null : _currentUser.BranchId;

            var sb = new StringBuilder();
            sb.AppendLine("AssetCode,AssetName,Category,Branch,CostCenter,Status,PurchaseDate,Cost,AccumulatedDepreciation,BookValue");

            var data = await _reportService.GetAssetRegisterReportAsync(filter, _currentUser.UserId ?? "SYSTEM");
            foreach (var item in data)
            {
                sb.AppendLine($"\"{item.AssetCode}\",\"{item.AssetName}\",\"{item.CategoryName}\",\"{item.BranchName}\",\"{item.CostCenterName}\",\"{item.StatusName}\",\"{item.PurchaseDate:yyyy-MM-dd}\",{item.CurrentCost},{item.AccumulatedDepreciation},{item.BookValue}");
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv; charset=utf-8", $"FixedAssets_{reportType}_{DateTime.Today:yyyyMMdd}.csv");
        }

        private async Task PopulateFilterDropDownsAsync()
        {
            var branches = await _context.Branches.IgnoreQueryFilters().AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Code).ToListAsync();
            ViewBag.Branches = new SelectList(branches, "Id", "LocalizedName");

            var categories = await _context.AssetCategories.IgnoreQueryFilters().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "LocalizedName");

            var costCenters = await _context.CostCenters.IgnoreQueryFilters().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();
            ViewBag.CostCenters = new SelectList(costCenters, "Id", "LocalizedName");
        }
    }
}
