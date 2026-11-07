using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ERP.Application.DTOs.Reports;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
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
    public class ReportsController : Controller
    {
        private readonly IAccountingReportService _reportService;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public ReportsController(
            IAccountingReportService reportService, 
            ErpDbContext context, 
            ICurrentUserContext currentUser)
        {
            _reportService = reportService;
            _context = context;
            _currentUser = currentUser;
        }

        #region Helper: Populate Common Filters ViewBag
        private async Task PopulateFilterViewBagsAsync(ReportFilterDto filter)
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

            // 2. Fiscal Years
            var fiscalYears = await _context.FiscalYears
                .AsNoTracking()
                .Where(f => f.IsActive)
                .OrderByDescending(f => f.Year)
                .ToListAsync();
            ViewBag.FiscalYears = new SelectList(fiscalYears, "Id", "LocalizedName", filter.FiscalYearId ?? _currentUser.FiscalYearId);

            // 3. Currencies
            var currencies = await _context.Currencies
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Code)
                .ToListAsync();
            ViewBag.Currencies = new SelectList(currencies, "Id", "Code", filter.CurrencyId);

            // 4. Accounts (leaf / posting accounts)
            var accounts = await _context.GLAccounts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(a => a.IsActive && !a.IsGroup)
                .OrderBy(a => a.Code)
                .Select(a => new { a.Id, Display = a.Code + " - " + a.NameAr })
                .ToListAsync();
            ViewBag.Accounts = new SelectList(accounts, "Id", "Display", filter.AccountId);

            // 5. Customers (Filtered by selected Branch or user's authorized branches)
            var customersQuery = _context.Customers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.IsActive);

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                customersQuery = customersQuery.Where(c => c.BranchId == filter.BranchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                var allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUser.UserId && ub.Branch.IsActive)
                    .Select(ub => ub.BranchId)
                    .ToListAsync();

                if (allowedBranchIds.Any())
                {
                    customersQuery = customersQuery.Where(c => allowedBranchIds.Contains(c.BranchId));
                }
                else if (_currentUser.BranchId > 0)
                {
                    customersQuery = customersQuery.Where(c => c.BranchId == _currentUser.BranchId);
                }
            }

            var customers = await customersQuery
                .OrderBy(c => c.Code)
                .Select(c => new { c.Id, Display = c.Code + " - " + c.NameAr })
                .ToListAsync();
            ViewBag.Customers = new SelectList(customers, "Id", "Display", filter.CustomerId);

            if (filter.CustomerId.HasValue && filter.CustomerId.Value > 0)
            {
                ViewBag.SelectedCustomerDisplay = await _context.Customers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(c => c.Id == filter.CustomerId.Value)
                    .Select(c => c.Code + " - " + (c.NameAr ?? c.NameEn))
                    .FirstOrDefaultAsync();
            }

            // 6. Vendors (Filtered by selected Branch or user's authorized branches)
            var vendorsQuery = _context.Vendors
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(v => v.IsActive);

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                vendorsQuery = vendorsQuery.Where(v => v.BranchId == filter.BranchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                var allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUser.UserId && ub.Branch.IsActive)
                    .Select(ub => ub.BranchId)
                    .ToListAsync();

                if (allowedBranchIds.Any())
                {
                    vendorsQuery = vendorsQuery.Where(v => allowedBranchIds.Contains(v.BranchId));
                }
                else if (_currentUser.BranchId > 0)
                {
                    vendorsQuery = vendorsQuery.Where(v => v.BranchId == _currentUser.BranchId);
                }
            }

            var vendors = await vendorsQuery
                .OrderBy(v => v.Code)
                .Select(v => new { v.Id, Display = v.Code + " - " + v.NameAr })
                .ToListAsync();
            ViewBag.Vendors = new SelectList(vendors, "Id", "Display", filter.VendorId);

            if (filter.VendorId.HasValue && filter.VendorId.Value > 0)
            {
                ViewBag.SelectedVendorDisplay = await _context.Vendors
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(v => v.Id == filter.VendorId.Value)
                    .Select(v => v.Code + " - " + (v.NameAr ?? v.NameEn))
                    .FirstOrDefaultAsync();
            }
        }

        private void SetDefaultDates(ReportFilterDto filter)
        {
            if (!Request.Query.ContainsKey("IncludeDrafts"))
            {
                filter.IncludeDrafts = true;
            }

            if (!Request.Query.ContainsKey("submitted"))
            {
                filter.OnlyWithTransactions = true;
            }

            if (!filter.FiscalYearId.HasValue)
            {
                if (_currentUser.FiscalYearId > 0)
                {
                    filter.FiscalYearId = _currentUser.FiscalYearId;
                }
                else
                {
                    filter.FiscalYearId = _context.FiscalYears.Where(f => f.IsActive).Select(f => (int?)f.Id).FirstOrDefault();
                }
            }

            if (!filter.DateFrom.HasValue)
            {
                filter.DateFrom = new DateTime(2026, 1, 1);
            }

            if (!filter.DateTo.HasValue)
            {
                filter.DateTo = new DateTime(2026, 12, 31);
            }
        }
        #endregion

        #region AJAX Cascading Lookups
        [HttpGet]
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
            }

            var list = await query
                .OrderBy(c => c.Code)
                .Select(c => new { id = c.Id, text = c.Code + " - " + c.NameAr })
                .ToListAsync();

            return Json(list);
        }

        [HttpGet]
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
            }

            var list = await query
                .OrderBy(v => v.Code)
                .Select(v => new { id = v.Id, text = v.Code + " - " + v.NameAr })
                .ToListAsync();

            return Json(list);
        }
        #endregion

        // GET: /Reports
        [HasPermission("Reports.GeneralReports.View,Reports.AccountStatement.View,Reports.CustomerStatement.View,Reports.VendorStatement.View,Reports.TrialBalance.View,Reports.GeneralLedger.View")]
        public IActionResult Index()
        {
            return View();
        }

        #region 1. Account Statement
        [HasPermission("Reports.AccountStatement.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> AccountStatement([FromQuery] ReportFilterDto filter)
        {
            SetDefaultDates(filter);
            await PopulateFilterViewBagsAsync(filter);

            bool isRequested = (Request.Query.ContainsKey("submitted") || filter.AccountId.HasValue) && filter.AccountId.HasValue && filter.AccountId.Value > 0;
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            AccountStatementDto model;
            if (isRequested)
            {
                model = await _reportService.GetAccountStatementAsync(filter);
            }
            else
            {
                model = new AccountStatementDto
                {
                    DateFrom = filter.DateFrom,
                    DateTo = filter.DateTo,
                    Mode = filter.Mode
                };
            }

            return View(model);
        }
        #endregion

        #region 2. Customer Statement
        [HasPermission("Reports.CustomerStatement.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> CustomerStatement([FromQuery] ReportFilterDto filter)
        {
            SetDefaultDates(filter);
            await PopulateFilterViewBagsAsync(filter);

            bool isRequested = (Request.Query.ContainsKey("submitted") || filter.CustomerId.HasValue) && filter.CustomerId.HasValue && filter.CustomerId.Value > 0;
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            SubLedgerStatementDto model;
            if (isRequested)
            {
                model = await _reportService.GetCustomerStatementAsync(filter);
            }
            else
            {
                model = new SubLedgerStatementDto
                {
                    SubLedgerType = "Customer",
                    DateFrom = filter.DateFrom,
                    DateTo = filter.DateTo,
                    Mode = filter.Mode
                };
            }

            return View(model);
        }
        #endregion

        #region 3. Vendor Statement
        [HasPermission("Reports.VendorStatement.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> VendorStatement([FromQuery] ReportFilterDto filter)
        {
            SetDefaultDates(filter);
            await PopulateFilterViewBagsAsync(filter);

            bool isRequested = (Request.Query.ContainsKey("submitted") || filter.VendorId.HasValue) && filter.VendorId.HasValue && filter.VendorId.Value > 0;
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            SubLedgerStatementDto model;
            if (isRequested)
            {
                model = await _reportService.GetVendorStatementAsync(filter);
            }
            else
            {
                model = new SubLedgerStatementDto
                {
                    SubLedgerType = "Vendor",
                    DateFrom = filter.DateFrom,
                    DateTo = filter.DateTo,
                    Mode = filter.Mode
                };
            }

            return View(model);
        }
        #endregion

        #region 4. Trial Balance
        [HasPermission("Reports.TrialBalance.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> TrialBalance([FromQuery] ReportFilterDto filter)
        {
            SetDefaultDates(filter);
            await PopulateFilterViewBagsAsync(filter);

            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            TrialBalanceDto model;
            if (isRequested)
            {
                model = await _reportService.GetTrialBalanceAsync(filter);
            }
            else
            {
                model = new TrialBalanceDto
                {
                    DateFrom = filter.DateFrom,
                    DateTo = filter.DateTo,
                    Mode = filter.Mode
                };
            }

            return View(model);
        }
        #endregion

        #region 5. General Ledger
        [HasPermission("Reports.GeneralLedger.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> GeneralLedger([FromQuery] ReportFilterDto filter)
        {
            SetDefaultDates(filter);
            await PopulateFilterViewBagsAsync(filter);

            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            GeneralLedgerDto model;
            if (isRequested)
            {
                model = await _reportService.GetGeneralLedgerAsync(filter);
            }
            else
            {
                model = new GeneralLedgerDto
                {
                    DateFrom = filter.DateFrom,
                    DateTo = filter.DateTo,
                    Mode = filter.Mode
                };
            }

            return View(model);
        }
        #endregion

        #region 6. Income Statement (قائمة الدخل)
        [HasPermission("Reports.IncomeStatement.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> IncomeStatement([FromQuery] ReportFilterDto filter)
        {
            SetDefaultDates(filter);
            await PopulateFilterViewBagsAsync(filter);

            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            IncomeStatementDto model;
            if (isRequested)
            {
                model = await _reportService.GetIncomeStatementAsync(filter);
            }
            else
            {
                model = new IncomeStatementDto
                {
                    DateFrom = filter.DateFrom,
                    DateTo = filter.DateTo,
                    Mode = filter.Mode
                };
            }

            return View(model);
        }
        #endregion

        #region 7. Balance Sheet (قائمة المركز المالي)
        [HasPermission("Reports.BalanceSheet.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> BalanceSheet([FromQuery] ReportFilterDto filter)
        {
            SetDefaultDates(filter);
            await PopulateFilterViewBagsAsync(filter);

            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            BalanceSheetDto model;
            if (isRequested)
            {
                model = await _reportService.GetBalanceSheetAsync(filter);
            }
            else
            {
                model = new BalanceSheetDto
                {
                    AsOfDate = filter.DateTo,
                    Mode = filter.Mode
                };
            }

            return View(model);
        }
        #endregion

        #region 8. SubLedger Summary (إجمالي وأرصدة حسابات العملاء والموردين)
        [HasPermission("Reports.SubLedgerSummary.View,Reports.CustomerStatement.View,Reports.VendorStatement.View,Reports.GeneralReports.View")]
        public async Task<IActionResult> SubLedgerSummary([FromQuery] SubLedgerSummaryFilterDto filter)
        {
            SetDefaultDates(filter);
            await PopulateFilterViewBagsAsync(filter);

            bool isRequested = Request.Query.ContainsKey("submitted");
            ViewBag.IsRequested = isRequested;
            ViewBag.Filter = filter;

            SubLedgerSummaryDto model;
            if (isRequested)
            {
                model = await _reportService.GetSubLedgerSummaryAsync(filter);
            }
            else
            {
                model = new SubLedgerSummaryDto
                {
                    DateFrom = filter.DateFrom,
                    DateTo = filter.DateTo,
                    Mode = filter.Mode,
                    PartyType = filter.PartyType ?? "All"
                };
            }

            return View(model);
        }

        [HttpGet]
        [HasPermission("Reports.SubLedgerSummary.Export,Reports.CustomerStatement.Export,Reports.VendorStatement.Export,Reports.GeneralReports.Export")]
        public async Task<IActionResult> ExportSubLedgerSummaryToExcel([FromQuery] SubLedgerSummaryFilterDto filter)
        {
            SetDefaultDates(filter);
            var summary = await _reportService.GetSubLedgerSummaryAsync(filter);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("أرصدة العملاء والموردين");
            ws.RightToLeft = true;
            ws.ShowGridLines = true;

            // Report Title and Meta
            ws.Cell(1, 1).Value = "تقرير ميزان وإجمالي أرصدة وحسابات العملاء والموردين";
            ws.Range(1, 1, 1, 13).Merge();
            ws.Row(1).Height = 32;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(27, 54, 93);
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // Filter summary row
            string partyTypeText = filter.PartyType == "Customer" ? "العملاء فقط" : (filter.PartyType == "Vendor" ? "الموردين فقط" : "كافة العملاء والموردين");
            string branchText = string.IsNullOrWhiteSpace(summary.BranchName) ? "كافة الفروع" : summary.BranchName;
            string dateRangeText = $"من: {(summary.DateFrom.HasValue ? summary.DateFrom.Value.ToString("yyyy/MM/dd") : "-")} إلى: {(summary.DateTo.HasValue ? summary.DateTo.Value.ToString("yyyy/MM/dd") : "-")}";

            ws.Cell(2, 1).Value = $"النوع: {partyTypeText} | الفرع: {branchText} | الفترة: {dateRangeText} | تاريخ الاستخراج: {DateTime.Now:yyyy/MM/dd HH:mm}";
            ws.Range(2, 1, 2, 13).Merge();
            ws.Row(2).Height = 22;
            ws.Cell(2, 1).Style.Font.FontSize = 10;
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(240, 244, 248);
            ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Headers
            string[] headers = new[]
            {
                "#",
                "نوع الطرف",
                "الكود",
                "اسم العميل / المورد",
                "الفرع",
                "العملة",
                "حساب المراقبة",
                "رصيد أول مدة (مدين)",
                "رصيد أول مدة (دائن)",
                "حركات الفترة (مدين)",
                "حركات الفترة (دائن)",
                "الرصيد الختامي المحلي",
                "حالة الرصيد"
            };

            int headerRow = 4;
            ws.Row(headerRow).Height = 26;
            for (int col = 0; col < headers.Length; col++)
            {
                var cell = ws.Cell(headerRow, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 11;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(41, 128, 185);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            int row = 5;
            int counter = 1;
            foreach (var item in summary.Items)
            {
                ws.Cell(row, 1).SetValue(counter++);
                ws.Cell(row, 2).SetValue(item.PartyTypeText);
                ws.Cell(row, 3).SetValue(item.Code);
                ws.Cell(row, 4).SetValue(item.NameAr);
                ws.Cell(row, 5).SetValue(item.BranchName);
                ws.Cell(row, 6).SetValue(item.CurrencyCode);
                ws.Cell(row, 7).SetValue($"{item.ControlAccountCode} - {item.ControlAccountName}");

                ws.Cell(row, 8).SetValue((double)item.OpeningDebitLocal);
                ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00";

                ws.Cell(row, 9).SetValue((double)item.OpeningCreditLocal);
                ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00";

                ws.Cell(row, 10).SetValue((double)item.PeriodDebitLocal);
                ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";

                ws.Cell(row, 11).SetValue((double)item.PeriodCreditLocal);
                ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0.00";

                ws.Cell(row, 12).SetValue((double)item.ClosingBalanceLocal);
                ws.Cell(row, 12).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 12).Style.Font.Bold = true;

                ws.Cell(row, 13).SetValue(item.BalanceStatusText);

                if (row % 2 == 0)
                {
                    ws.Range(row, 1, row, 13).Style.Fill.BackgroundColor = XLColor.FromArgb(249, 250, 251);
                }

                row++;
            }

            // Totals Row
            ws.Row(row).Height = 24;
            ws.Cell(row, 1).Value = "الإجمالي العام";
            ws.Range(row, 1, row, 7).Merge();
            ws.Range(row, 1, row, 7).Style.Font.Bold = true;
            ws.Range(row, 1, row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 235, 245);

            ws.Cell(row, 8).SetValue((double)summary.TotalOpeningDebitLocal);
            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 8).Style.Font.Bold = true;
            ws.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 235, 245);

            ws.Cell(row, 9).SetValue((double)summary.TotalOpeningCreditLocal);
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 9).Style.Font.Bold = true;
            ws.Cell(row, 9).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 235, 245);

            ws.Cell(row, 10).SetValue((double)summary.TotalPeriodDebitLocal);
            ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 10).Style.Font.Bold = true;
            ws.Cell(row, 10).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 235, 245);

            ws.Cell(row, 11).SetValue((double)summary.TotalPeriodCreditLocal);
            ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 11).Style.Font.Bold = true;
            ws.Cell(row, 11).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 235, 245);

            ws.Cell(row, 12).SetValue((double)summary.TotalClosingBalanceLocal);
            ws.Cell(row, 12).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 12).Style.Font.Bold = true;
            ws.Cell(row, 12).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 235, 245);

            ws.Cell(row, 13).Value = "-";
            ws.Cell(row, 13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 13).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 235, 245);

            ws.Columns(1, 13).AdjustToContents();

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            string fileName = $"اجمالي_ارصدة_العملاء_والموردين_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        #endregion
    }
}
