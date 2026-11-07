using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Web.Models;
using ERP.Web.Authorization;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class AccountsController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;
        private readonly IAccountImportService _importService;

        private const string SC_Code = "GL_ACC";
        private const string SC_Name = "ChartOfAccounts";
        private const string SC_Module = "Accounting";

        public AccountsController(
            ErpDbContext context,
            ICurrentUserContext currentUser,
            IAuditService audit,
            IAccountImportService importService)
        {
            _context = context;
            _currentUser = currentUser;
            _audit = audit;
            _importService = importService;
        }

        // ─── GET: Download Excel Template ────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.ChartOfAccounts.View")]
        public IActionResult DownloadTemplate()
        {
            var content = _importService.GenerateTemplate();
            var fileName = $"دليل_الحسابات_نموذج_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // ─── POST: Preview Excel Import ──────────────────────────────────────
        [HttpPost]
        [HasPermission("Accounting.ChartOfAccounts.Add")]
        public async Task<IActionResult> PreviewExcelImport(IFormFile? excelFile)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                return Json(new { success = false, message = "يرجى تحديد ملف Excel أو CSV للرفع (.xlsx / .csv)." });
            }

            if (!excelFile.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !excelFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "صيغة الملف غير مدعومة. يجب أن يكون الملف بصيغة (.xlsx) أو (.csv)." });
            }

            try
            {
                using var stream = excelFile.OpenReadStream();
                var preview = await _importService.ValidateAndPreviewAsync(stream);
                return Json(new { success = true, data = preview });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "حدث خطأ أثناء قراءة وفحص ملف الإكسيل: " + ex.Message });
            }
        }

        // ─── POST: Confirm Excel Import ──────────────────────────────────────
        [HttpPost]
        [HasPermission("Accounting.ChartOfAccounts.Add")]
        public async Task<IActionResult> ConfirmExcelImport(IFormFile? excelFile, bool updateExisting = false)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                return Json(new { success = false, message = "يرجى تحديد ملف Excel أو CSV للرفع (.xlsx / .csv)." });
            }

            try
            {
                using var stream = excelFile.OpenReadStream();
                var result = await _importService.ExecuteImportAsync(stream, updateExisting);

                if (result.Success)
                {
                    await _audit.LogAsync(new ERP.Application.Models.AuditLogEntry
                    {
                        ScreenCode = SC_Code,
                        ScreenName = SC_Name,
                        ModuleName = SC_Module,
                        EntityName = "GLAccount",
                        Action = AuditAction.Create,
                        Description = $"استيراد دليل الحسابات الموحد من Excel: مجموعات جديدة ({result.GroupsCreated})، مجموعات محدثة ({result.GroupsUpdated})، حسابات جديدة ({result.AccountsCreated})، حسابات محدثة ({result.AccountsUpdated})"
                    });
                }

                return Json(new { success = result.Success, message = result.Message, data = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "حدث خطأ أثناء تنفيذ عملية الاستيراد: " + ex.Message });
            }
        }

        // ─── GET: Index (DataTables & Tree View) ──────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.ChartOfAccounts.View")]
        public async Task<IActionResult> Index(int? parentId)
        {
            ViewBag.SelectedParentId = parentId;
            ViewBag.ParentAccounts = await _context.GLAccounts
                .Where(a => a.IsActive && a.IsGroup)
                .OrderBy(a => a.Code)
                .AsNoTracking()
                .ToListAsync();

            ViewBag.Currencies = await _context.Currencies
                .Where(c => c.IsActive)
                .OrderBy(c => c.Code)
                .AsNoTracking()
                .ToListAsync();

            return View();
        }

        // ─── POST: LoadTableData (DataTables Server-side Processing) ──────────
        [HttpPost]
        [HasPermission("Accounting.ChartOfAccounts.View")]
        public async Task<IActionResult> LoadTableData()
        {
            try
            {
                var form = Request.Form;
                int draw = int.TryParse(form["draw"].FirstOrDefault(), out var d) ? d : 1;
                int start = int.TryParse(form["start"].FirstOrDefault(), out var s) ? s : 0;
                int length = int.TryParse(form["length"].FirstOrDefault(), out var l) ? l : 10;
                string? searchValue = form["search[value]"].FirstOrDefault()?.Trim();

                // Custom filters
                int? parentId = int.TryParse(form["parentId"].FirstOrDefault(), out var pid) && pid > 0 ? pid : null;
                int? currencyId = int.TryParse(form["currencyId"].FirstOrDefault(), out var cid) && cid > 0 ? cid : null;
                bool? isGroup = bool.TryParse(form["isGroup"].FirstOrDefault(), out var ig) ? ig : null;
                int? categoryId = int.TryParse(form["categoryId"].FirstOrDefault(), out var cat) && cat > 0 ? cat : null;
                int? branchId = int.TryParse(form["branchId"].FirstOrDefault(), out var bid) && bid > 0 ? bid : null;

                var query = _context.GLAccounts
                    .Include(a => a.Parent)
                    .Include(a => a.Currency)
                    .Include(a => a.Branch)
                    .AsNoTracking();

                // Branch Security Scoping
                if (!_currentUser.HasAllBranches)
                {
                    var userBranchId = _currentUser.BranchId;
                    query = query.Where(a => a.BranchId == null || a.BranchId == userBranchId);
                }
                else if (branchId.HasValue)
                {
                    query = query.Where(a => a.BranchId == null || a.BranchId == branchId.Value);
                }

                // Filters
                if (parentId.HasValue)
                {
                    var childIds = await GetChildAccountIdsRecursiveAsync(parentId.Value);
                    childIds.Add(parentId.Value);
                    query = query.Where(a => a.ParentId.HasValue && childIds.Contains(a.ParentId.Value));
                }

                if (currencyId.HasValue)
                {
                    query = query.Where(a => a.CurrencyId == currencyId.Value);
                }

                if (isGroup.HasValue)
                {
                    query = query.Where(a => a.IsGroup == isGroup.Value);
                }

                if (categoryId.HasValue)
                {
                    query = query.Where(a => (int)a.AccountCategory == categoryId.Value);
                }

                int totalRecords = await query.CountAsync();

                // Global search
                if (!string.IsNullOrWhiteSpace(searchValue))
                {
                    query = query.Where(a =>
                        a.Code.Contains(searchValue) ||
                        a.NameAr.Contains(searchValue) ||
                        a.NameEn.Contains(searchValue) ||
                        (a.Parent != null && (a.Parent.NameAr.Contains(searchValue) || a.Parent.Code.Contains(searchValue))) ||
                        (a.Branch != null && (a.Branch.NameAr.Contains(searchValue) || a.Branch.Name.Contains(searchValue)))
                    );
                }

                int filteredRecords = await query.CountAsync();

                var data = await query
                    .OrderBy(a => a.Code)
                    .Skip(start)
                    .Take(length)
                    .Select(a => new
                    {
                        a.Id,
                        a.Code,
                        Name = a.LocalizedName,
                        NameAr = a.NameAr,
                        NameEn = a.NameEn,
                        ParentName = a.Parent != null ? $"{a.Parent.Code} - {a.Parent.LocalizedName}" : "-",
                        a.Level,
                        a.IsGroup,
                        AccountCategoryText = a.AccountCategory.ToArabicString(),
                        AccountNatureText = a.AccountNature == AccountNature.Debit ? "مدين" : "دائن",
                        AccountNatureCode = (int)a.AccountNature,
                        a.IsControlAccount,
                        SubLedgerTypeText = a.SubLedgerType != SubLedgerType.None ? a.SubLedgerType.ToString() : null,
                        CurrencyCode = a.Currency != null ? a.Currency.Code : (a.IsGroup ? "-" : "متعدد العملات"),
                        AccountUsageText = GetAccountUsageText(a.AccountUsage),
                        a.IsReconciliationAccount,
                        a.RequiresCostCenter,
                        a.BranchId,
                        BranchName = a.Branch != null ? a.Branch.LocalizedName : "عام (مشترك)",
                        a.IsActive
                    })
                    .ToListAsync();

                return Json(new
                {
                    draw = draw,
                    recordsTotal = totalRecords,
                    recordsFiltered = filteredRecords,
                    data = data
                });
            }
            catch (Exception ex)
            {
                return Json(new { draw = 1, recordsTotal = 0, recordsFiltered = 0, error = ex.Message });
            }
        }

        // ─── GET: Dedicated Tree View Screen ────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.ChartOfAccounts.View")]
        public async Task<IActionResult> Tree(int? selectedId)
        {
            ViewBag.SelectedAccountId = selectedId;
            ViewBag.CurrentBranchId = _currentUser.BranchId;
            ViewBag.HasAllBranches = _currentUser.HasAllBranches;
            await PopulateDropdownsAsync();
            return View();
        }

        // ─── GET: Export Complete Chart of Accounts to Excel ─────────────────
        [HttpGet]
        [HasPermission("Accounting.ChartOfAccounts.View")]
        public async Task<IActionResult> ExportTreeToExcel(int? branchId = null)
        {
            var query = _context.GLAccounts
                .Include(a => a.Parent)
                .Include(a => a.Currency)
                .Include(a => a.Branch)
                .AsNoTracking();

            // Branch Security & Filtering
            if (!_currentUser.HasAllBranches)
            {
                var userBranchId = _currentUser.BranchId;
                query = query.Where(a => a.BranchId == null || a.BranchId == userBranchId);
            }
            else if (branchId.HasValue && branchId.Value > 0)
            {
                query = query.Where(a => a.BranchId == null || a.BranchId == branchId.Value);
            }

            var accounts = await query
                .OrderBy(a => a.Code)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("شجرة الحسابات");
            ws.RightToLeft = true;
            ws.ShowGridLines = true;

            // 1. Header Banner
            var titleRange = ws.Range(1, 1, 1, 15);
            titleRange.Merge();
            titleRange.Value = "شجرة ودليل الحسابات الموحد (Chart of Accounts)";
            titleRange.Style.Font.Bold = true;
            titleRange.Style.Font.FontSize = 16;
            titleRange.Style.Font.FontColor = XLColor.White;
            titleRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A"); // Deep Navy
            titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(1).Height = 35;

            // 2. Info Banner
            var infoRange = ws.Range(2, 1, 2, 15);
            infoRange.Merge();
            infoRange.Value = $"تاريخ التصدير: {DateTime.Now:yyyy-MM-dd HH:mm} | إجمالي الحسابات: {accounts.Count} (رئيسية: {accounts.Count(a => a.IsGroup)} - فرعية: {accounts.Count(a => !a.IsGroup)}) | المستخدم: {_currentUser.Username}";
            infoRange.Style.Font.FontSize = 10;
            infoRange.Style.Font.FontColor = XLColor.FromHtml("#1E293B");
            infoRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E0F2FE"); // Light Blue
            infoRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            infoRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(2).Height = 22;

            ws.Row(3).Height = 10; // Spacer

            // 3. Table Column Headers
            string[] headers = new[]
            {
                "المستوى",
                "كود الحساب",
                "اسم الحساب (عربي)",
                "اسم الحساب (إنجليزي)",
                "كود الحساب الأب",
                "اسم الحساب الأب",
                "نوع الحساب",
                "التصنيف المحاسبي",
                "طبيعة الحساب",
                "حساب مراقبة",
                "دفتر مساعد",
                "مركز تكلفة",
                "العملة",
                "نطاق الفرع",
                "الحالة"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 11;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB"); // Royal Blue
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#1D4ED8");
            }
            ws.Row(4).Height = 28;

            // 4. Data Rows
            int currentRow = 5;
            foreach (var acc in accounts)
            {
                ws.Cell(currentRow, 1).SetValue(acc.Level);
                ws.Cell(currentRow, 2).SetValue(acc.Code);
                ws.Cell(currentRow, 3).SetValue(acc.NameAr);
                ws.Cell(currentRow, 4).SetValue(acc.NameEn ?? string.Empty);
                ws.Cell(currentRow, 5).SetValue(acc.Parent?.Code ?? string.Empty);
                ws.Cell(currentRow, 6).SetValue(acc.Parent?.LocalizedName ?? string.Empty);
                ws.Cell(currentRow, 7).SetValue(acc.IsGroup ? "مجموعة رئيسية" : "حساب حركة فرعي");
                ws.Cell(currentRow, 8).SetValue(acc.AccountCategory.ToArabicString());
                ws.Cell(currentRow, 9).SetValue(acc.AccountNature == AccountNature.Debit ? "مدين" : "دائن");
                ws.Cell(currentRow, 10).SetValue(acc.IsControlAccount ? "نعم" : "لا");
                ws.Cell(currentRow, 11).SetValue(acc.SubLedgerType != SubLedgerType.None ? acc.SubLedgerType.ToString() : "-");
                ws.Cell(currentRow, 12).SetValue(acc.RequiresCostCenter ? "إلزامي" : "لا");
                ws.Cell(currentRow, 13).SetValue(acc.Currency != null ? $"{acc.Currency.Code} - {acc.Currency.LocalizedName}" : (acc.IsGroup ? "-" : "متعدد العملات"));
                ws.Cell(currentRow, 14).SetValue(acc.Branch != null ? acc.Branch.LocalizedName : "كافة الفروع (عام)");
                ws.Cell(currentRow, 15).SetValue(acc.IsActive ? "نشط" : "معطل");

                // Styling row based on Group / Detail
                var rowRange = ws.Range(currentRow, 1, currentRow, 15);
                rowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                rowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                rowRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#CBD5E1");
                rowRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#E2E8F0");
                rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                // Center specific columns
                ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow, 2).Style.NumberFormat.Format = "@"; // Text format for Code
                ws.Cell(currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow, 5).Style.NumberFormat.Format = "@";
                ws.Cell(currentRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow, 13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow, 14).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow, 15).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (acc.IsGroup)
                {
                    rowRange.Style.Font.Bold = true;
                    rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9"); // Light slate
                    ws.Cell(currentRow, 3).Style.Font.FontColor = XLColor.FromHtml("#0F172A");
                }
                else
                {
                    rowRange.Style.Font.Bold = false;
                    if (currentRow % 2 == 0)
                    {
                        rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                    }
                }

                // If inactive
                if (!acc.IsActive)
                {
                    rowRange.Style.Font.FontColor = XLColor.FromHtml("#94A3B8");
                    ws.Cell(currentRow, 15).Style.Font.FontColor = XLColor.FromHtml("#DC2626");
                }

                ws.Row(currentRow).Height = 22;
                currentRow++;
            }

            // 5. Freeze rows & Enable Filter
            ws.SheetView.FreezeRows(4);
            if (accounts.Count > 0)
            {
                ws.Range(4, 1, currentRow - 1, 15).SetAutoFilter();
            }

            // 6. Adjust Columns width
            ws.Columns().AdjustToContents(12.0, 40.0);
            ws.Column(1).Width = 10; // Level
            ws.Column(2).Width = 16; // Code
            ws.Column(3).Width = 32; // NameAr
            ws.Column(4).Width = 28; // NameEn

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();
            var fileName = $"شجرة_الحسابات_الكاملة_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // ─── GET: Hierarchical Tree Data (for interactive Tree View) ──────────
        [HttpGet]
        [HasPermission("Accounting.ChartOfAccounts.View")]
        public async Task<IActionResult> GetTreeHierarchy(int? branchId = null)
        {
            var query = _context.GLAccounts
                .Include(a => a.Currency)
                .Include(a => a.Branch)
                .AsNoTracking();

            // Branch Security & Filtering
            if (!_currentUser.HasAllBranches)
            {
                var userBranchId = _currentUser.BranchId;
                query = query.Where(a => a.BranchId == null || a.BranchId == userBranchId);
            }
            else if (branchId.HasValue && branchId.Value > 0)
            {
                query = query.Where(a => a.BranchId == null || a.BranchId == branchId.Value);
            }

            var accounts = await query
                .OrderBy(a => a.Code)
                .ToListAsync();

            var parentIds = accounts.Where(a => a.ParentId.HasValue).Select(a => a.ParentId!.Value).ToHashSet();

            var nodes = accounts.Select(a => new AccountTreeNodeDto
            {
                Id = a.Id,
                Code = a.Code,
                NameAr = a.NameAr,
                NameEn = a.NameEn,
                ParentId = a.ParentId,
                Level = a.Level,
                IsGroup = a.IsGroup,
                IsActive = a.IsActive,
                BranchId = a.BranchId,
                BranchName = a.Branch != null ? a.Branch.LocalizedName : null,
                AccountCategory = a.AccountCategory,
                AccountCategoryText = a.AccountCategory.ToArabicString(),
                AccountNature = a.AccountNature,
                AccountNatureText = a.AccountNature == AccountNature.Debit ? "مدين" : "دائن",
                IsControlAccount = a.IsControlAccount,
                SubLedgerType = a.SubLedgerType,
                SubLedgerTypeText = a.SubLedgerType != SubLedgerType.None ? a.SubLedgerType.ToString() : null,
                RequiresCostCenter = a.RequiresCostCenter,
                IsReconciliationAccount = a.IsReconciliationAccount,
                AccountUsageText = GetAccountUsageText(a.AccountUsage),
                CurrencyId = a.CurrencyId,
                CurrencyCode = a.Currency != null ? a.Currency.Code : (a.IsGroup ? "-" : "متعدد العملات"),
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
                HasChildren = parentIds.Contains(a.Id)
            }).ToList();

            return Json(nodes);
        }

        // Legacy compatibility for any existing callers
        [HttpGet]
        [HasPermission("Accounting.ChartOfAccounts.View")]
        public async Task<IActionResult> GetTreeData()
        {
            var accounts = await _context.GLAccounts
                .OrderBy(a => a.Code)
                .Select(a => new
                {
                    id = a.Id.ToString(),
                    parent = a.ParentId.HasValue ? a.ParentId.Value.ToString() : "#",
                    text = $"{a.Code} - {a.LocalizedName}",
                    isGroup = a.IsGroup,
                    isActive = a.IsActive,
                    code = a.Code,
                    level = a.Level,
                    nature = a.AccountNature == AccountNature.Debit ? "مدين" : "دائن",
                    category = a.AccountCategory.ToArabicString(),
                    isControl = a.IsControlAccount
                })
                .AsNoTracking()
                .ToListAsync();

            return Json(accounts);
        }

        // ─── GET: Account Details (for Right-side Information Panel) ──────────
        [HttpGet]
        [HasPermission("Accounting.ChartOfAccounts.View")]
        public async Task<IActionResult> GetAccountDetails(int id)
        {
            var a = await _context.GLAccounts
                .Include(x => x.Parent)
                .Include(x => x.Currency)
                .Include(x => x.Branch)
                .Include(x => x.Children)
                .Include(x => x.JournalEntryLines)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (a == null)
                return Json(new { success = false, message = "الحساب المطلوب غير موجود." });

            var totalDebit = a.JournalEntryLines.Sum(l => l.DebitLocal);
            var totalCredit = a.JournalEntryLines.Sum(l => l.CreditLocal);

            var dto = new AccountDetailsDto
            {
                Id = a.Id,
                Code = a.Code,
                NameAr = a.NameAr,
                NameEn = a.NameEn,
                ParentId = a.ParentId,
                ParentCode = a.Parent != null ? a.Parent.Code : "-",
                ParentName = a.Parent != null ? a.Parent.LocalizedName : "حساب رئيسي جذري",
                Level = a.Level,
                IsGroup = a.IsGroup,
                IsActive = a.IsActive,
                BranchId = a.BranchId,
                BranchName = a.Branch != null ? a.Branch.LocalizedName : "حساب عام (مشترك لكافة الفروع)",
                AccountCategory = a.AccountCategory,
                AccountCategoryText = a.AccountCategory.ToArabicString(),
                AccountNature = a.AccountNature,
                AccountNatureText = a.AccountNature == AccountNature.Debit ? "مدين" : "دائن",
                IsControlAccount = a.IsControlAccount,
                SubLedgerType = a.SubLedgerType,
                SubLedgerTypeText = a.SubLedgerType != SubLedgerType.None ? a.SubLedgerType.ToString() : null,
                RequiresCostCenter = a.RequiresCostCenter,
                IsReconciliationAccount = a.IsReconciliationAccount,
                AccountUsageText = GetAccountUsageText(a.AccountUsage),
                CurrencyId = a.CurrencyId,
                CurrencyCode = a.Currency != null ? a.Currency.Code : (a.IsGroup ? "-" : "متعدد العملات"),
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
                HasChildren = a.Children.Any(),
                ChildrenCount = a.Children.Count,
                TransactionsCount = a.JournalEntryLines.Count,
                TotalDebit = totalDebit,
                TotalCredit = totalCredit
            };

            return Json(new { success = true, data = dto });
        }

        // ─── POST: Quick Create from Tree ────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.ChartOfAccounts.Add")]
        public async Task<IActionResult> QuickCreate([FromBody] QuickAccountCreateDto model)
        {
            if (model == null)
                return Json(new { success = false, message = "لم يتم استلام أي بيانات." });

            if (string.IsNullOrWhiteSpace(model.Code))
                return Json(new { success = false, message = "كود الحساب مطلوب." });

            if (string.IsNullOrWhiteSpace(model.NameAr) && string.IsNullOrWhiteSpace(model.NameEn))
                return Json(new { success = false, message = "اسم الحساب بالعربية أو الإنجليزية مطلوب." });

            string cleanCode = model.Code.Trim();
            bool codeExists = await _context.GLAccounts.AnyAsync(a => a.Code == cleanCode);
            if (codeExists)
                return Json(new { success = false, message = $"كود الحساب '{cleanCode}' مستخدم بالفعل." });

            int? branchId = model.BranchId > 0 ? model.BranchId : null;
            if (model.IsGroup)
            {
                branchId = null; // Groups are always general/enterprise-wide
            }
            else if (!_currentUser.HasAllBranches && branchId.HasValue && branchId != _currentUser.BranchId)
            {
                return Json(new { success = false, message = "لا يمكنك تخصيص الحساب لفرع آخر غير فرعك الحالي." });
            }

            int level = 1;
            if (model.ParentId.HasValue && model.ParentId.Value > 0)
            {
                var parent = await _context.GLAccounts.FindAsync(model.ParentId.Value);
                if (parent == null)
                    return Json(new { success = false, message = "الحساب الأب المحدد غير موجود." });

                if (!parent.IsGroup)
                    return Json(new { success = false, message = "لا يمكن إضافة حساب تحت حساب فرعي (حركة). يجب أن يكون الحساب الأب مجموعة رئيسية." });

                level = parent.Level + 1;
                if ((int)model.AccountCategory == 0)
                    model.AccountCategory = parent.AccountCategory;
                if ((int)model.AccountNature == 0)
                    model.AccountNature = parent.AccountNature;
            }

            var entity = new GLAccount
            {
                Code = cleanCode,
                NameAr = model.NameAr?.Trim() ?? string.Empty,
                NameEn = string.IsNullOrWhiteSpace(model.NameEn) ? (model.NameAr?.Trim() ?? string.Empty) : model.NameEn.Trim(),
                ParentId = model.ParentId > 0 ? model.ParentId : null,
                Level = level,
                IsGroup = model.IsGroup,
                BranchId = branchId,
                AccountCategory = model.AccountCategory,
                AccountNature = model.AccountNature,
                CurrencyId = model.CurrencyId > 0 ? model.CurrencyId : null,
                IsControlAccount = model.IsControlAccount,
                SubLedgerType = model.SubLedgerType,
                RequiresCostCenter = model.RequiresCostCenter,
                IsReconciliationAccount = model.IsReconciliationAccount,
                AccountUsage = model.AccountUsage,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.GLAccounts.Add(entity);
            await _context.SaveChangesAsync();

            await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "GLAccount",
                entityId: entity.Id.ToString(),
                newValues: new
                {
                    entity.Id,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.ParentId,
                    entity.Level,
                    entity.IsGroup,
                    entity.BranchId,
                    entity.AccountCategory,
                    entity.AccountNature,
                    entity.CurrencyId,
                    entity.IsControlAccount,
                    entity.SubLedgerType,
                    entity.RequiresCostCenter
                });

            return Json(new
            {
                success = true,
                message = $"تم إنشاء الحساب ({entity.Code} - {entity.LocalizedName}) بنجاح.",
                data = new
                {
                    id = entity.Id,
                    code = entity.Code,
                    nameAr = entity.NameAr,
                    nameEn = entity.NameEn,
                    parentId = entity.ParentId,
                    isGroup = entity.IsGroup,
                    branchId = entity.BranchId,
                    level = entity.Level
                }
            });
        }

        // ─── POST: Toggle Status (Activate / Deactivate) ─────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.ChartOfAccounts.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var account = await _context.GLAccounts
                .Include(a => a.Children)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (account == null)
                return Json(new { success = false, message = "الحساب غير موجود." });

            bool newState = !account.IsActive;

            if (!newState && account.IsGroup && account.Children.Any(c => c.IsActive))
            {
                return Json(new { success = false, message = "لا يمكن تجميد أو تعطيل مجموعة رئيسية تحتوي على حسابات فرعية نشطة. يرجى تعطيل أو نقل الحسابات التابعة أولاً." });
            }

            var oldValues = new { account.Id, account.Code, account.IsActive };
            account.IsActive = newState;
            account.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "GLAccount",
                entityId: account.Id.ToString(),
                oldValues: oldValues,
                newValues: new { account.Id, account.Code, account.IsActive },
                description: newState ? "تنشيط الحساب" : "إلغاء تنشيط/تجميد الحساب");

            return Json(new
            {
                success = true,
                isActive = account.IsActive,
                message = newState ? $"تم تنشيط الحساب ({account.Code}) بنجاح." : $"تم تجميد وتعطيل الحساب ({account.Code}) بنجاح."
            });
        }

        // ─── GET: Account Audit History (for Audit Trail Modal) ──────────────
        [HttpGet]
        [HasPermission("Accounting.ChartOfAccounts.View")]
        public async Task<IActionResult> GetAccountAudit(int id)
        {
            var auditLogs = await _context.AuditLogs
                .Where(l => l.EntityName == "GLAccount" && l.EntityId == id.ToString())
                .OrderByDescending(l => l.TimestampUtc)
                .Take(50)
                .Select(l => new AccountAuditHistoryItemDto
                {
                    Action = l.Action.ToString(),
                    UserName = l.UserName,
                    Timestamp = l.TimestampUtc,
                    Description = l.Description,
                    ChangedValues = l.ChangedValues
                })
                .AsNoTracking()
                .ToListAsync();

            return Json(new { success = true, data = auditLogs });
        }

        // ─── GET: Create ─────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.ChartOfAccounts.Add")]
        public async Task<IActionResult> Create(int? parentId, bool isGroup = false)
        {
            await PopulateDropdownsAsync();

            var model = new GLAccount
            {
                ParentId = parentId,
                IsGroup = isGroup,
                IsActive = true,
                AccountUsage = AccountUsage.All
            };

            if (parentId.HasValue)
            {
                var parent = await _context.GLAccounts.FindAsync(parentId.Value);
                if (parent != null)
                {
                    model.Level = parent.Level + 1;
                    model.AccountCategory = parent.AccountCategory;
                    model.AccountNature = parent.AccountNature;
                }
            }

            return View(model);
        }

        // ─── POST: Create ────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.ChartOfAccounts.Add")]
        public async Task<IActionResult> Create(GLAccount model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود الحساب مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameAr) && string.IsNullOrWhiteSpace(model.NameEn))
                ModelState.AddModelError("NameAr", "اسم الحساب بالعربية أو الإنجليزية مطلوب.");

            bool codeExists = await _context.GLAccounts.AnyAsync(a => a.Code == model.Code.Trim());
            if (codeExists)
                ModelState.AddModelError("Code", $"كود الحساب '{model.Code}' مستخدم بالفعل.");

            if (model.IsGroup)
            {
                model.BranchId = null;
            }
            else if (!_currentUser.HasAllBranches && model.BranchId.HasValue && model.BranchId != _currentUser.BranchId)
            {
                ModelState.AddModelError("BranchId", "لا يمكنك تخصيص الحساب لفرع آخر غير فرعك الحالي.");
            }

            if (ModelState.IsValid)
            {
                model.Code = model.Code.Trim();
                model.NameAr = model.NameAr?.Trim() ?? string.Empty;
                model.NameEn = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();

                if (model.ParentId.HasValue)
                {
                    var parent = await _context.GLAccounts.FindAsync(model.ParentId.Value);
                    if (parent != null)
                    {
                        model.Level = parent.Level + 1;
                    }
                }
                else
                {
                    model.Level = 1;
                }

                model.CreatedAt = DateTime.UtcNow;

                _context.GLAccounts.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "GLAccount",
                    entityId: model.Id.ToString(),
                    newValues: new
                    {
                        model.Id,
                        model.Code,
                        model.NameAr,
                        model.NameEn,
                        model.ParentId,
                        model.Level,
                        model.IsGroup,
                        model.BranchId,
                        model.AccountCategory,
                        model.AccountNature,
                        model.CurrencyId,
                        model.IsControlAccount,
                        model.SubLedgerType,
                        model.RequiresCostCenter,
                        model.AccountUsage
                    });

                TempData["SuccessMessage"] = $"تم حفظ الحساب ({model.Code} - {model.LocalizedName}) بنجاح.";
                return RedirectToAction(nameof(Index), new { parentId = model.ParentId });
            }

            await PopulateDropdownsAsync();
            return View(model);
        }

        // ─── GET: Edit ───────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.ChartOfAccounts.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var account = await _context.GLAccounts
                .Include(a => a.Parent)
                .Include(a => a.Currency)
                .Include(a => a.Branch)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (account == null)
            {
                TempData["ErrorMessage"] = "الحساب المطلوب غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            bool hasTransactions = await _context.JournalEntryLines.AnyAsync(l => l.AccountId == id);
            bool hasChildren = await _context.GLAccounts.AnyAsync(a => a.ParentId == id);

            ViewBag.HasTransactions = hasTransactions;
            ViewBag.HasChildren = hasChildren;

            await PopulateDropdownsAsync(excludeId: id);
            return View(account);
        }

        // ─── POST: Edit ──────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.ChartOfAccounts.Edit")]
        public async Task<IActionResult> Edit(int id, GLAccount model)
        {
            if (id != model.Id)
                return BadRequest();

            var account = await _context.GLAccounts.FindAsync(id);
            if (account == null)
            {
                TempData["ErrorMessage"] = "الحساب المطلوب غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            bool hasTransactions = await _context.JournalEntryLines.AnyAsync(l => l.AccountId == id);
            bool hasChildren = await _context.GLAccounts.AnyAsync(a => a.ParentId == id);
            ViewBag.HasTransactions = hasTransactions;
            ViewBag.HasChildren = hasChildren;

            // Integrity Checks
            if (hasTransactions)
            {
                if (account.CurrencyId != model.CurrencyId)
                    ModelState.AddModelError("CurrencyId", "لا يمكن تغيير عملة الحساب لوجود قيود وحركات مالية مسجلة عليه.");

                if (!account.IsGroup && model.IsGroup)
                    ModelState.AddModelError("IsGroup", "لا يمكن تحويل الحساب إلى مجموعة رئيسية تجميعية لوجود قيود وحركات مالية مسجلة عليه.");

                if (account.BranchId != model.BranchId)
                    ModelState.AddModelError("BranchId", "لا يمكن تعديل فرع الحساب لوجود حركات وقيود محاسبية مسجلة عليه.");
            }

            if (!model.IsGroup && !_currentUser.HasAllBranches && model.BranchId.HasValue && model.BranchId != _currentUser.BranchId)
            {
                ModelState.AddModelError("BranchId", "لا يمكنك تخصيص الحساب لفرع آخر غير فرعك الحالي.");
            }

            if (hasChildren && !model.IsGroup)
            {
                ModelState.AddModelError("IsGroup", "لا يمكن تحويل الحساب إلى حساب حركة فرعي لأنه يحتوي على حسابات تابعة له في الشجرة.");
            }

            if (model.ParentId == id)
            {
                ModelState.AddModelError("ParentId", "لا يمكن ربط الحساب بنفسه كحساب أب.");
            }

            bool codeExists = await _context.GLAccounts.AnyAsync(a => a.Code == model.Code.Trim() && a.Id != id);
            if (codeExists)
                ModelState.AddModelError("Code", $"كود الحساب '{model.Code}' مستخدم بالفعل في حساب آخر.");

            if (ModelState.IsValid)
            {
                var oldValues = new
                {
                    account.Code,
                    account.NameAr,
                    account.NameEn,
                    account.ParentId,
                    account.Level,
                    account.IsGroup,
                    account.BranchId,
                    account.AccountCategory,
                    account.AccountNature,
                    account.CurrencyId,
                    account.IsControlAccount,
                    account.SubLedgerType,
                    account.RequiresCostCenter,
                    account.AccountUsage,
                    account.IsActive
                };

                account.Code = model.Code.Trim();
                account.NameAr = model.NameAr?.Trim() ?? string.Empty;
                account.NameEn = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();
                account.ParentId = model.ParentId;

                if (model.ParentId.HasValue)
                {
                    var parent = await _context.GLAccounts.FindAsync(model.ParentId.Value);
                    account.Level = parent != null ? parent.Level + 1 : 1;
                }
                else
                {
                    account.Level = 1;
                }

                if (!hasChildren) account.IsGroup = model.IsGroup;
                if (!hasTransactions)
                {
                    account.CurrencyId = model.CurrencyId;
                    account.BranchId = model.IsGroup ? null : (model.BranchId > 0 ? model.BranchId : null);
                }

                account.AccountCategory = model.AccountCategory;
                account.AccountNature = model.AccountNature;
                account.IsControlAccount = model.IsControlAccount;
                account.SubLedgerType = model.SubLedgerType;
                account.IsReconciliationAccount = model.IsReconciliationAccount;
                account.RequiresCostCenter = model.RequiresCostCenter;
                account.AccountUsage = model.AccountUsage;
                account.IsActive = model.IsActive;
                account.UpdatedAt = DateTime.UtcNow;

                if (model.RowVersion != null && model.RowVersion.Length > 0)
                {
                    _context.Entry(account).Property(e => e.RowVersion).OriginalValue = model.RowVersion;
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    ModelState.AddModelError(string.Empty, "تم تعديل السجل بواسطة مستخدم آخر أثناء عملك عليه. يرجى إعادة تحميل البيانات قبل الحفظ.");
                    await PopulateDropdownsAsync(excludeId: id);
                    return View(model);
                }

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "GLAccount",
                    entityId: account.Id.ToString(),
                    oldValues: oldValues,
                    newValues: new
                    {
                        account.Code,
                        account.NameAr,
                        account.NameEn,
                        account.ParentId,
                        account.Level,
                        account.IsGroup,
                        account.BranchId,
                        account.AccountCategory,
                        account.AccountNature,
                        account.CurrencyId,
                        account.IsControlAccount,
                        account.SubLedgerType,
                        account.RequiresCostCenter,
                        account.AccountUsage,
                        account.IsActive
                    });

                TempData["SuccessMessage"] = $"تم تعديل الحساب ({account.Code} - {account.LocalizedName}) بنجاح.";
                return RedirectToAction(nameof(Index), new { parentId = account.ParentId });
            }

            await PopulateDropdownsAsync(excludeId: id);
            return View(model);
        }

        // ─── POST: Delete ────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.ChartOfAccounts.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var account = await _context.GLAccounts.FindAsync(id);
            if (account == null)
                return Json(new { success = false, message = "الحساب غير موجود." });

            bool hasChildren = await _context.GLAccounts.AnyAsync(a => a.ParentId == id);
            if (hasChildren)
                return Json(new { success = false, message = "لا يمكن حذف هذا الحساب لأنه يحتوي على حسابات فرعية تابعة له في الشجرة." });

            bool hasTransactions = await _context.JournalEntryLines.AnyAsync(l => l.AccountId == id);
            if (hasTransactions)
                return Json(new { success = false, message = "لا يمكن حذف هذا الحساب لوجود حركات وقيود محاسبية مسجلة عليه في دفتر اليومية." });

            bool linkedCustomerCat = await _context.CustomerCategories.AnyAsync(c => c.ControlAccountId == id);
            if (linkedCustomerCat)
                return Json(new { success = false, message = "لا يمكن حذف الحساب لأنه مرتبط كحساب مراقبة لفئات العملاء." });

            bool linkedVendorCat = await _context.VendorCategories.AnyAsync(v => v.ControlAccountId == id);
            if (linkedVendorCat)
                return Json(new { success = false, message = "لا يمكن حذف الحساب لأنه مرتبط كحساب مراقبة لفئات الموردين." });

            var oldValues = new { account.Id, account.Code, account.NameAr, account.NameEn };

            _context.GLAccounts.Remove(account);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "GLAccount",
                entityId: id.ToString(),
                oldValues: oldValues);

            return Json(new { success = true, message = "تم حذف الحساب بنجاح." });
        }

        // ─── GET: Select2 AJAX Search for Accounts ───────────────────────────
        [HttpGet]
        public async Task<IActionResult> SearchAccounts(string? q, int page = 1, int? currencyId = null, bool transactionalOnly = true, int? branchId = null)
        {
            int pageSize = 20;
            var query = _context.GLAccounts
                .Include(a => a.Branch)
                .Where(a => a.IsActive)
                .AsNoTracking();

            if (transactionalOnly)
                query = query.Where(a => !a.IsGroup);

            var targetBranchId = branchId ?? _currentUser.BranchId;
            if (!_currentUser.HasAllBranches || branchId.HasValue)
            {
                query = query.Where(a => a.BranchId == null || a.BranchId == targetBranchId);
            }

            if (currencyId.HasValue && currencyId.Value > 0)
                query = query.Where(a => a.CurrencyId == null || a.CurrencyId == currencyId.Value);

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(a => a.Code.Contains(q) || a.NameAr.Contains(q) || a.NameEn.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(a => a.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    id = a.Id,
                    text = $"{a.Code} - {a.LocalizedName}" + (a.Branch != null ? $" [{a.Branch.LocalizedName}]" : ""),
                    code = a.Code,
                    name = a.LocalizedName,
                    currencyId = a.CurrencyId,
                    branchId = a.BranchId,
                    branchName = a.Branch != null ? a.Branch.LocalizedName : null,
                    requiresCostCenter = a.RequiresCostCenter,
                    isReconciliation = a.IsReconciliationAccount,
                    isControl = a.IsControlAccount,
                    subLedger = (int)a.SubLedgerType
                })
                .ToListAsync();

            return Json(new
            {
                items = items,
                total_count = total
            });
        }

        // ─── Helpers ─────────────────────────────────────────────────────────
        private async Task PopulateDropdownsAsync(int? excludeId = null)
        {
            var parentQuery = _context.GLAccounts
                .Where(a => a.IsActive && a.IsGroup);

            if (excludeId.HasValue)
                parentQuery = parentQuery.Where(a => a.Id != excludeId.Value);

            ViewBag.ParentAccounts = await parentQuery
                .OrderBy(a => a.Level).ThenBy(a => a.Code)
                .AsNoTracking()
                .ToListAsync();

            ViewBag.Currencies = await _context.Currencies
                .Where(c => c.IsActive)
                .OrderBy(c => c.Code)
                .AsNoTracking()
                .ToListAsync();

            ViewBag.Branches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Code)
                .AsNoTracking()
                .ToListAsync();

            ViewBag.CurrentBranchId = _currentUser.BranchId;
            ViewBag.HasAllBranches = _currentUser.HasAllBranches;
        }

        private async Task<List<int>> GetChildAccountIdsRecursiveAsync(int parentId)
        {
            var childIds = await _context.GLAccounts
                .Where(a => a.ParentId == parentId)
                .Select(a => a.Id)
                .ToListAsync();

            var allIds = new List<int>(childIds);
            foreach (var childId in childIds)
            {
                var deeper = await GetChildAccountIdsRecursiveAsync(childId);
                allIds.AddRange(deeper);
            }
            return allIds;
        }

        private static string GetAccountUsageText(AccountUsage usage)
        {
            return usage switch
            {
                AccountUsage.All => "جميع الشاشات",
                AccountUsage.JournalVoucher => "قيود اليومية فقط",
                AccountUsage.Payment => "سندات الصرف فقط",
                AccountUsage.Receipt => "سندات القبض فقط",
                _ => usage.ToString()
            };
        }
    }
}
