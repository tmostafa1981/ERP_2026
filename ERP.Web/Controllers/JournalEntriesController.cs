using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class JournalEntriesController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;
        private readonly IJournalEntryValidationService _validationService;
        private readonly IFiscalPeriodService _fiscalPeriodService;
        private readonly IConfiguration _configuration;
        private readonly ICurrencyRateService _rateService;

        private const string SC_Code = "GL_JV";
        private const string SC_Name = "JournalEntries";
        private const string SC_Module = "Accounting";

        private static readonly System.Threading.SemaphoreSlim _entryNumberLock = ERP.Infrastructure.Services.UniversalDocumentService.JournalNumberLock;

        public JournalEntriesController(
            ErpDbContext context,
            ICurrentUserContext currentUser,
            IAuditService audit,
            IJournalEntryValidationService validationService,
            IFiscalPeriodService fiscalPeriodService,
            IConfiguration configuration,
            ICurrencyRateService? rateService = null)
        {
            _context = context;
            _currentUser = currentUser;
            _audit = audit;
            _validationService = validationService;
            _fiscalPeriodService = fiscalPeriodService;
            _configuration = configuration;
            _rateService = rateService ?? new ERP.Infrastructure.Services.CurrencyRateService(context, audit, currentUser);
        }

        private async Task<string> GenerateNextEntryNumberAsync(int branchId, int fiscalYearId, string fiscalYear)
        {
            string prefix = $"JV-{branchId}-{fiscalYear}-";

            var existingNumbers = await _context.JournalEntryHeaders
                .IgnoreQueryFilters()
                .Where(h => h.BranchId == branchId && h.FiscalYearId == fiscalYearId && h.EntryNumber.StartsWith(prefix))
                .Select(h => h.EntryNumber)
                .ToListAsync();

            long maxNumber = 0;
            foreach (var num in existingNumbers)
            {
                if (num.Length > prefix.Length)
                {
                    var suffix = num.Substring(prefix.Length);
                    if (long.TryParse(suffix, out long n) && n > maxNumber)
                    {
                        maxNumber = n;
                    }
                }
            }

            int totalInFY = await _context.JournalEntryHeaders
                .IgnoreQueryFilters()
                .Where(h => h.BranchId == branchId && h.FiscalYearId == fiscalYearId)
                .CountAsync();

            if (totalInFY > maxNumber)
                maxNumber = totalInFY;

            long candidate = maxNumber + 1;
            string candidateCode = $"{prefix}{candidate:D5}";

            while (await _context.JournalEntryHeaders.IgnoreQueryFilters().AnyAsync(h => h.BranchId == branchId && h.EntryNumber == candidateCode))
            {
                candidate++;
                candidateCode = $"{prefix}{candidate:D5}";
            }

            return candidateCode;
        }

        // ─── GET: Index ──────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.JournalVoucher.View")]
        public async Task<IActionResult> Index()
        {
            ViewBag.FiscalYears = await _context.FiscalYears
                .OrderByDescending(f => f.Year)
                .AsNoTracking()
                .ToListAsync();

            return View();
        }

        // ─── POST: LoadTableData (DataTables Server-side) ─────────────────────
        [HttpPost]
        [HasPermission("Accounting.JournalVoucher.View")]
        public async Task<IActionResult> LoadTableData()
        {
            try
            {
                var form = Request.Form;
                int draw = int.TryParse(form["draw"].FirstOrDefault(), out var d) ? d : 1;
                int start = int.TryParse(form["start"].FirstOrDefault(), out var s) ? s : 0;
                int length = int.TryParse(form["length"].FirstOrDefault(), out var l) ? l : 10;
                string? searchValue = form["search[value]"].FirstOrDefault()?.Trim();

                // Filters
                DateTime? fromDate = DateTime.TryParse(form["fromDate"].FirstOrDefault(), out var fd) ? fd.Date : null;
                DateTime? toDate = DateTime.TryParse(form["toDate"].FirstOrDefault(), out var td) ? td.Date : null;
                int? status = int.TryParse(form["status"].FirstOrDefault(), out var st) && st > 0 ? st : null;
                int? sourceDocType = int.TryParse(form["sourceDocType"].FirstOrDefault(), out var sdt) && sdt > 0 ? sdt : null;

                var query = _context.JournalEntryHeaders
                    .Include(h => h.Branch)
                    .Include(h => h.FiscalYear)
                    .Include(h => h.Lines)
                    .AsNoTracking();

                // Single-Active-Branch Session Rule: Always isolate by active branch
                int currentBranchId = _currentUser.BranchId;
                if (currentBranchId > 0)
                {
                    query = query.Where(h => h.BranchId == currentBranchId);
                }

                if (fromDate.HasValue)
                    query = query.Where(h => h.EntryDate >= fromDate.Value);

                if (toDate.HasValue)
                    query = query.Where(h => h.EntryDate <= toDate.Value);

                if (status.HasValue)
                    query = query.Where(h => (int)h.Status == status.Value);

                if (sourceDocType.HasValue)
                    query = query.Where(h => (int)h.SourceDocumentType == sourceDocType.Value);

                int totalRecords = await query.CountAsync();

                if (!string.IsNullOrWhiteSpace(searchValue))
                {
                    query = query.Where(h =>
                        h.EntryNumber.Contains(searchValue) ||
                        h.Description.Contains(searchValue) ||
                        (h.ReferenceNumber != null && h.ReferenceNumber.Contains(searchValue)));
                }

                int filteredRecords = await query.CountAsync();

                var data = await query
                    .OrderByDescending(h => h.EntryDate)
                    .ThenByDescending(h => h.Id)
                    .Skip(start)
                    .Take(length)
                    .Select(h => new
                    {
                        h.Id,
                        h.EntryNumber,
                        EntryDate = h.EntryDate.ToString("yyyy-MM-dd"),
                        h.Description,
                        h.ReferenceNumber,
                        BranchName = h.Branch != null ? h.Branch.LocalizedName : "الفرع الحالي",
                        FiscalYearName = h.FiscalYear != null ? h.FiscalYear.LocalizedName : "-",
                        h.TotalDebitLocal,
                        h.TotalCreditLocal,
                        Status = (int)h.Status,
                        StatusText = h.Status == JournalEntryStatus.Posted ? "مرحل" : "مسودة",
                        SourceDocType = (int)h.SourceDocumentType,
                        SourceDocTypeText = GetSourceDocumentTypeText(h.SourceDocumentType),
                        LinesCount = h.Lines.Count,
                        IsManual = h.SourceDocumentType == SourceDocumentType.ManualJV,
                        IsPosted = h.Status == JournalEntryStatus.Posted
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

        // ─── GET: Create ─────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.JournalVoucher.Add")]
        public async Task<IActionResult> Create()
        {
            var activeFiscalYear = await _context.FiscalYears
                .FirstOrDefaultAsync(f => f.IsActive && !f.IsClosed);

            if (activeFiscalYear == null)
            {
                TempData["ErrorMessage"] = "لا توجد سنة مالية نشطة ومفتوحة حالياً. يرجى تفعيل أو فتح سنة مالية أولاً.";
                return RedirectToAction(nameof(Index));
            }

            await PopulateLookupDataAsync();

            var currentBranch = await _context.Branches.FindAsync(_currentUser.BranchId);
            ViewBag.CurrentBranchName = currentBranch?.LocalizedName ?? "الفرع الحالي";
            ViewBag.CurrentFiscalYear = activeFiscalYear;

            // Generate Next Preview Number
            ViewBag.NextEntryNumber = await GenerateNextEntryNumberAsync(_currentUser.BranchId, activeFiscalYear.Id, activeFiscalYear.Year);
            ViewBag.EnableCostCenter = _configuration.GetValue<bool>("AccountingSettings:EnableCostCenters", false);

            return View();
        }

        // ─── POST: Create (JSON API) ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.JournalVoucher.Add")]
        public async Task<IActionResult> Create([FromBody] JournalEntryHeader model)
        {
            if (model == null)
                return Json(new { success = false, message = "لم يتم استلام أي بيانات للقيد." });

            if (model.Lines == null || model.Lines.Count < 2)
                return Json(new { success = false, message = "يجب إدخال سطرين على الأقل لتحقيق القيد المزدوج." });

            // Single-Active-Branch Session Rule: Entry MUST always belong to the active branch
            model.BranchId = _currentUser.BranchId;

            // Period Validation on EntryDate
            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(model.EntryDate);
            if (!isPeriodAllowed)
                return Json(new { success = false, message = periodError });

            // Clean data
            model.Description = model.Description?.Trim() ?? string.Empty;
            model.ReferenceNumber = model.ReferenceNumber?.Trim();
            model.Notes = model.Notes?.Trim();
            model.Status = JournalEntryStatus.Posted;
            model.PostedAt = DateTime.UtcNow;
            model.PostedByUserId = _currentUser.UserId;
            model.CreatedAt = DateTime.UtcNow;

            // Clean, sequence lines, and compute local equivalents with line exchange rate
            int lineNum = 1;
            foreach (var line in model.Lines)
            {
                line.LineNumber = lineNum++;
                line.Description = string.IsNullOrWhiteSpace(line.Description) ? model.Description : line.Description.Trim();
                line.Notes = line.Notes?.Trim();
                line.CreatedAt = DateTime.UtcNow;

                // Validate Party & Account restricted currency
                if (line.SubLedgerType == SubLedgerType.Customer && line.SubLedgerId.HasValue && line.SubLedgerId.Value > 0)
                {
                    var cust = await _context.Customers.FindAsync(line.SubLedgerId.Value);
                    if (cust != null && cust.CurrencyId.HasValue && cust.CurrencyId.Value != line.CurrencyId)
                    {
                        var custCurr = await _context.Currencies.FindAsync(cust.CurrencyId.Value);
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: عملة القيد لا تطابق عملة العميل المعتمدة ({custCurr?.Code ?? "العملة"})." });
                    }
                }
                else if (line.SubLedgerType == SubLedgerType.Vendor && line.SubLedgerId.HasValue && line.SubLedgerId.Value > 0)
                {
                    var vend = await _context.Vendors.FindAsync(line.SubLedgerId.Value);
                    if (vend != null && vend.CurrencyId.HasValue && vend.CurrencyId.Value != line.CurrencyId)
                    {
                        var vendCurr = await _context.Currencies.FindAsync(vend.CurrencyId.Value);
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: عملة القيد لا تطابق عملة المورد المعتمدة ({vendCurr?.Code ?? "العملة"})." });
                    }
                }

                if (line.AccountId > 0)
                {
                    var acc = await _context.GLAccounts.FindAsync(line.AccountId);
                    if (acc != null && acc.CurrencyId.HasValue && acc.CurrencyId.Value != line.CurrencyId)
                    {
                        var accCurr = await _context.Currencies.FindAsync(acc.CurrencyId.Value);
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: الحساب ({acc.Code}) مقيد بعملة ({accCurr?.Code}) ولا يقبل قيداً بعملة أخرى." });
                    }
                }

                // Resolve exchange rate for line.CurrencyId on model.EntryDate
                var rateRes = await _rateService.ResolveExchangeRateAsync(line.CurrencyId, model.EntryDate);
                if (!rateRes.Success)
                {
                    return Json(new { success = false, message = $"السطر {line.LineNumber}: {rateRes.Message}" });
                }
                line.ExchangeRate = rateRes.Rate;

                line.DebitLocal = NumberRound(line.DebitForeign * line.ExchangeRate);
                line.CreditLocal = NumberRound(line.CreditForeign * line.ExchangeRate);
            }

            // Calculate totals from recalculated lines
            model.TotalDebitLocal = NumberRound(model.Lines.Sum(l => l.DebitLocal));
            model.TotalCreditLocal = NumberRound(model.Lines.Sum(l => l.CreditLocal));

            // Accounting Business Rules Validation Engine
            try
            {
                await _validationService.ValidateAsync(model, _currentUser.UserId, isManualAction: true);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }

            // Concurrency-safe sequential EntryNumber generation and save with retry
            var fy = await _context.FiscalYears.FindAsync(model.FiscalYearId);
            string fyYear = fy?.Year ?? DateTime.UtcNow.Year.ToString();

            int maxRetries = 5;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                await _entryNumberLock.WaitAsync();
                try
                {
                    var strategy = _context.Database.CreateExecutionStrategy();
                    await strategy.ExecuteAsync(async () =>
                    {
                        using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
                        try
                        {
                            if (_context.Database.IsSqlServer())
                            {
                                var lockResource = $"JV_Seq_{model.BranchId}_{model.FiscalYearId}";
                                object[] lockParams = new object[] { lockResource };
                                await _context.Database.ExecuteSqlRawAsync(
                                    "EXEC sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000",
                                    lockParams);
                            }

                            // Always compute authoritative entry number on the server right before commit
                            if (model.SourceDocumentType == SourceDocumentType.ManualJV || string.IsNullOrWhiteSpace(model.EntryNumber) || attempt > 1)
                            {
                                model.EntryNumber = await GenerateNextEntryNumberAsync(model.BranchId, model.FiscalYearId, fyYear);
                            }

                            if (attempt == 1)
                            {
                                _context.JournalEntryHeaders.Add(model);
                            }

                            await _context.SaveChangesAsync();
                            await transaction.CommitAsync();
                        }
                        catch
                        {
                            await transaction.RollbackAsync();
                            throw;
                        }
                    });

                    break; // Save succeeded!
                }
                catch (DbUpdateException) when (attempt < maxRetries)
                {
                    _context.Entry(model).State = EntityState.Detached;
                    foreach (var line in model.Lines)
                    {
                        line.Id = 0;
                        _context.Entry(line).State = EntityState.Detached;
                    }
                    model.Id = 0;
                    await Task.Delay(50 * attempt);
                    continue;
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "حدث خطأ أثناء حفظ القيد: " + ex.Message });
                }
                finally
                {
                    _entryNumberLock.Release();
                }
            }

            try
            {
                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "JournalEntryHeader",
                    entityId: model.Id.ToString(),
                    newValues: new
                    {
                        model.Id,
                        model.BranchId,
                        model.FiscalYearId,
                        model.EntryNumber,
                        model.EntryDate,
                        model.Description,
                        model.TotalDebitLocal,
                        model.TotalCreditLocal,
                        LinesCount = model.Lines.Count
                    });
            }
            catch
            {
                // Suppress audit failure so it doesn't block the user
            }

            return Json(new
            {
                success = true,
                message = $"تم إنشاء وترحيل قيد اليومية ({model.EntryNumber}) بنجاح.",
                id = model.Id,
                entryNumber = model.EntryNumber
            });
        }

        // ─── GET: Edit ───────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.JournalVoucher.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var header = await _context.JournalEntryHeaders
                .Include(h => h.Branch)
                .Include(h => h.FiscalYear)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.Account)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.Currency)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.CostCenter)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null)
            {
                TempData["ErrorMessage"] = "قيد اليومية غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            if (_currentUser.BranchId > 0 && header.BranchId != _currentUser.BranchId)
            {
                TempData["ErrorMessage"] = "غير مصرح لك بتعديل قيود فرع آخر.";
                return RedirectToAction(nameof(Index));
            }

            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(header.EntryDate);
            if (!isPeriodAllowed)
            {
                TempData["ErrorMessage"] = periodError;
                return RedirectToAction(nameof(Details), new { id });
            }

            if (header.SourceDocumentType != SourceDocumentType.ManualJV)
            {
                TempData["ErrorMessage"] = $"لا يمكن تعديل هذا القيد يدوياً لأنه تم توليده آلياً من مستند ({GetSourceDocumentTypeText(header.SourceDocumentType)}).";
                return RedirectToAction(nameof(Details), new { id });
            }

            await PopulateLookupDataAsync();

            var currentBranch = header.Branch ?? await _context.Branches.FindAsync(header.BranchId);
            ViewBag.CurrentBranchName = currentBranch?.LocalizedName ?? "الفرع الحالي";
            ViewBag.CurrentFiscalYear = header.FiscalYear;

            // Preload SubLedgers for lines
            var customerIds = header.Lines.Where(l => l.SubLedgerType == SubLedgerType.Customer && l.SubLedgerId.HasValue).Select(l => l.SubLedgerId!.Value).Distinct().ToList();
            var vendorIds = header.Lines.Where(l => l.SubLedgerType == SubLedgerType.Vendor && l.SubLedgerId.HasValue).Select(l => l.SubLedgerId!.Value).Distinct().ToList();

            var customersDict = await _context.Customers
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => new { id = c.Id, code = c.Code, name = c.LocalizedName });

            var vendorsDict = await _context.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => new { id = v.Id, code = v.Code, name = v.LocalizedName });

            // Prepare client-side payload
            var linesDto = header.Lines.OrderBy(l => l.LineNumber).Select(l =>
            {
                string lineType = l.SubLedgerType switch
                {
                    SubLedgerType.Customer => "Customer",
                    SubLedgerType.Vendor => "Vendor",
                    _ => "Account"
                };

                string subLedgerQuery = "";
                object? subLedgerObj = null;
                if (l.SubLedgerType == SubLedgerType.Customer && l.SubLedgerId.HasValue && customersDict.TryGetValue(l.SubLedgerId.Value, out var c))
                {
                    subLedgerQuery = $"{c.code} - {c.name}";
                    subLedgerObj = c;
                }
                else if (l.SubLedgerType == SubLedgerType.Vendor && l.SubLedgerId.HasValue && vendorsDict.TryGetValue(l.SubLedgerId.Value, out var v))
                {
                    subLedgerQuery = $"{v.code} - {v.name}";
                    subLedgerObj = v;
                }

                return new
                {
                    id = l.Id,
                    lineNumber = l.LineNumber,
                    lineType = lineType,
                    subLedgerType = (int)l.SubLedgerType,
                    subLedgerId = l.SubLedgerId,
                    subLedgerSearchQuery = subLedgerQuery,
                    subLedgerDisplayName = subLedgerQuery,
                    selectedSubLedger = subLedgerObj,
                    accountId = l.AccountId,
                    accountDisplayName = l.Account != null ? $"{l.Account.Code} - {l.Account.LocalizedName}" : "",
                    accountSearchQuery = l.Account != null ? $"{l.Account.Code} - {l.Account.LocalizedName}" : "",
                    rawAccountCode = l.Account != null ? l.Account.Code : "",
                    selectedAccount = l.Account != null ? new
                    {
                        id = l.Account.Id,
                        code = l.Account.Code,
                        nameAr = l.Account.NameAr,
                        nameEn = l.Account.NameEn,
                        currencyId = l.Account.CurrencyId,
                        requiresCostCenter = l.Account.RequiresCostCenter,
                        isControlAccount = l.Account.IsControlAccount,
                        subLedgerType = (int)l.Account.SubLedgerType
                    } : null,
                    costCenterId = l.CostCenterId,
                    costCenterDisplayName = l.CostCenter != null ? $"{l.CostCenter.Code} - {l.CostCenter.LocalizedName}" : "",
                    costCenterSearchQuery = l.CostCenter != null ? $"{l.CostCenter.Code} - {l.CostCenter.LocalizedName}" : "",
                    selectedCostCenter = l.CostCenter != null ? new
                    {
                        id = l.CostCenter.Id,
                        code = l.CostCenter.Code,
                        nameAr = l.CostCenter.NameAr,
                        nameEn = l.CostCenter.NameEn
                    } : null,
                    currencyId = l.CurrencyId,
                    exchangeRate = l.ExchangeRate,
                    debitForeign = l.DebitForeign,
                    creditForeign = l.CreditForeign,
                    debitLocal = l.DebitLocal,
                    creditLocal = l.CreditLocal,
                    description = l.Description,
                    notes = l.Notes,
                    currencyManuallySet = false,
                    errors = new
                    {
                        accountNotFound = false,
                        controlAccountMissing = false,
                        costCenterInvalid = false,
                        requiresCostCenterMissing = false,
                        invalidAmount = false,
                        hasError = false
                    }
                };
            }).ToList();

            ViewBag.ExistingLinesJson = System.Text.Json.JsonSerializer.Serialize(linesDto);
            ViewBag.EnableCostCenter = _configuration.GetValue<bool>("AccountingSettings:EnableCostCenters", false);
            return View(header);
        }

        // ─── POST: Edit (JSON API) ───────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.JournalVoucher.Edit")]
        public async Task<IActionResult> Edit([FromBody] JournalEntryHeader model)
        {
            if (model == null || model.Id <= 0)
                return Json(new { success = false, message = "لم يتم استلام أي بيانات صالحة للقيد." });

            var existing = await _context.JournalEntryHeaders
                .Include(h => h.Lines)
                .FirstOrDefaultAsync(h => h.Id == model.Id);

            if (existing == null)
                return Json(new { success = false, message = "قيد اليومية غير موجود." });

            if (_currentUser.BranchId > 0 && existing.BranchId != _currentUser.BranchId)
                return Json(new { success = false, message = "غير مصرح لك بتعديل قيود فرع آخر." });

            var (isOrigPeriodAllowed, origPeriodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(existing.EntryDate);
            if (!isOrigPeriodAllowed)
                return Json(new { success = false, message = origPeriodError });

            if (model.EntryDate != existing.EntryDate)
            {
                var (isNewPeriodAllowed, newPeriodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(model.EntryDate);
                if (!isNewPeriodAllowed)
                    return Json(new { success = false, message = newPeriodError });
            }

            if (existing.SourceDocumentType != SourceDocumentType.ManualJV)
                return Json(new { success = false, message = "لا يمكن تعديل هذا القيد يدوياً لأنه مولد آلياً من مستند آخر." });

            if (model.Lines == null || model.Lines.Count < 2)
                return Json(new { success = false, message = "يجب إدخال سطرين على الأقل لتحقيق القيد المزدوج." });

            // Capture old values for audit
            var oldValues = new
            {
                existing.Id,
                existing.EntryNumber,
                existing.EntryDate,
                existing.Description,
                existing.TotalDebitLocal,
                existing.TotalCreditLocal,
                LinesCount = existing.Lines.Count
            };

            // Update Header fields
            existing.EntryDate = model.EntryDate;
            existing.Description = model.Description?.Trim() ?? string.Empty;
            existing.ReferenceNumber = model.ReferenceNumber?.Trim();
            existing.Notes = model.Notes?.Trim();
            existing.UpdatedAt = DateTime.UtcNow;

            // Remove existing lines and replace with new lines
            _context.JournalEntryLines.RemoveRange(existing.Lines);

            int lineNum = 1;
            var newLines = new List<JournalEntryLine>();
            foreach (var line in model.Lines)
            {
                // Validate Party & Account restricted currency
                if (line.SubLedgerType == SubLedgerType.Customer && line.SubLedgerId.HasValue && line.SubLedgerId.Value > 0)
                {
                    var cust = await _context.Customers.FindAsync(line.SubLedgerId.Value);
                    if (cust != null && cust.CurrencyId.HasValue && cust.CurrencyId.Value != line.CurrencyId)
                    {
                        var custCurr = await _context.Currencies.FindAsync(cust.CurrencyId.Value);
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: عملة القيد لا تطابق عملة العميل المعتمدة ({custCurr?.Code ?? "العملة"})." });
                    }
                }
                else if (line.SubLedgerType == SubLedgerType.Vendor && line.SubLedgerId.HasValue && line.SubLedgerId.Value > 0)
                {
                    var vend = await _context.Vendors.FindAsync(line.SubLedgerId.Value);
                    if (vend != null && vend.CurrencyId.HasValue && vend.CurrencyId.Value != line.CurrencyId)
                    {
                        var vendCurr = await _context.Currencies.FindAsync(vend.CurrencyId.Value);
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: عملة القيد لا تطابق عملة المورد المعتمدة ({vendCurr?.Code ?? "العملة"})." });
                    }
                }

                if (line.AccountId > 0)
                {
                    var acc = await _context.GLAccounts.FindAsync(line.AccountId);
                    if (acc != null && acc.CurrencyId.HasValue && acc.CurrencyId.Value != line.CurrencyId)
                    {
                        var accCurr = await _context.Currencies.FindAsync(acc.CurrencyId.Value);
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: الحساب ({acc.Code}) مقيد بعملة ({accCurr?.Code}) ولا يقبل قيداً بعملة أخرى." });
                    }
                }

                decimal lineRate = line.ExchangeRate;
                if (model.EntryDate != existing.EntryDate || lineRate <= 0)
                {
                    var rateRes = await _rateService.ResolveExchangeRateAsync(line.CurrencyId, model.EntryDate);
                    if (!rateRes.Success)
                    {
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: {rateRes.Message}" });
                    }
                    lineRate = rateRes.Rate;
                }

                var newLine = new JournalEntryLine
                {
                    JournalEntryHeaderId = existing.Id,
                    LineNumber = lineNum++,
                    AccountId = line.AccountId,
                    SubLedgerType = line.SubLedgerType,
                    SubLedgerId = line.SubLedgerId,
                    CurrencyId = line.CurrencyId > 0 ? line.CurrencyId : 1,
                    ExchangeRate = lineRate <= 0 ? 1.000000m : lineRate,
                    DebitForeign = line.DebitForeign,
                    CreditForeign = line.CreditForeign,
                    CostCenterId = line.CostCenterId,
                    Description = string.IsNullOrWhiteSpace(line.Description) ? existing.Description : line.Description.Trim(),
                    Notes = line.Notes?.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                newLine.DebitLocal = NumberRound(newLine.DebitForeign * newLine.ExchangeRate);
                newLine.CreditLocal = NumberRound(newLine.CreditForeign * newLine.ExchangeRate);
                newLines.Add(newLine);
            }

            existing.Lines = newLines;
            existing.TotalDebitLocal = NumberRound(newLines.Sum(l => l.DebitLocal));
            existing.TotalCreditLocal = NumberRound(newLines.Sum(l => l.CreditLocal));

            // Optimistic concurrency control check
            if (model.RowVersion != null && model.RowVersion.Length > 0)
            {
                _context.Entry(existing).Property(e => e.RowVersion).OriginalValue = model.RowVersion;
            }

            // Validate against Accounting Rules
            try
            {
                await _validationService.ValidateAsync(existing, _currentUser.UserId, isManualAction: true);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Json(new
                {
                    success = false,
                    message = "تم تعديل قيد اليومية بواسطة مستخدم آخر أثناء عملك عليه. يرجى إعادة تحميل البيانات قبل الحفظ."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "حدث خطأ أثناء حفظ التعديلات: " + ex.Message });
            }

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "JournalEntryHeader",
                entityId: existing.Id.ToString(),
                oldValues: oldValues,
                newValues: new
                {
                    existing.Id,
                    existing.EntryNumber,
                    existing.EntryDate,
                    existing.Description,
                    existing.TotalDebitLocal,
                    existing.TotalCreditLocal,
                    LinesCount = existing.Lines.Count
                });

            return Json(new
            {
                success = true,
                message = $"تم تحديث قيد اليومية ({existing.EntryNumber}) بنجاح.",
                id = existing.Id,
                entryNumber = existing.EntryNumber
            });
        }

        // ─── GET: Details ────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.JournalVoucher.View")]
        public async Task<IActionResult> Details(int id)
        {
            var header = await _context.JournalEntryHeaders
                .Include(h => h.Branch)
                .Include(h => h.FiscalYear)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.Account)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.Currency)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.CostCenter)
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null)
            {
                TempData["ErrorMessage"] = "قيد اليومية غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            if (_currentUser.BranchId > 0 && header.BranchId != _currentUser.BranchId)
            {
                TempData["ErrorMessage"] = "غير مصرح لك بالاطلاع على قيود فرع آخر.";
                return RedirectToAction(nameof(Index));
            }

            // Preload SubLedger names
            var customerIds = header.Lines.Where(l => l.SubLedgerType == SubLedgerType.Customer && l.SubLedgerId.HasValue).Select(l => l.SubLedgerId!.Value).Distinct().ToList();
            var vendorIds = header.Lines.Where(l => l.SubLedgerType == SubLedgerType.Vendor && l.SubLedgerId.HasValue).Select(l => l.SubLedgerId!.Value).Distinct().ToList();

            ViewBag.Customers = await _context.Customers
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => $"{c.Code} - {c.LocalizedName}");

            ViewBag.Vendors = await _context.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => $"{v.Code} - {v.LocalizedName}");

            ViewBag.EnableCostCenter = _configuration.GetValue<bool>("AccountingSettings:EnableCostCenters", false);

            return View(header);
        }

        // ─── POST: Post (ترحيل القيد بموجب قفل المعاملة ومنع الترحيل المزدوج) ───
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.JournalVoucher.Post")]
        public async Task<IActionResult> Post(int id)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
                try
                {
                    JournalEntryHeader? header = null;
                    if (_context.Database.IsSqlServer())
                    {
                        header = await _context.JournalEntryHeaders
                            .FromSqlRaw("SELECT * FROM JournalEntryHeaders WITH (UPDLOCK, ROWLOCK) WHERE Id = {0}", id)
                            .Include(h => h.Lines)
                            .FirstOrDefaultAsync();
                    }
                    else
                    {
                        header = await _context.JournalEntryHeaders
                            .Include(h => h.Lines)
                            .FirstOrDefaultAsync(h => h.Id == id);
                    }

                    if (header == null)
                        return Json(new { success = false, message = "قيد اليومية غير موجود." });

                    if (_currentUser.BranchId > 0 && header.BranchId != _currentUser.BranchId)
                        return Json(new { success = false, message = "غير مصرح لك بترحيل قيود فرع آخر." });

                    if (header.Status == JournalEntryStatus.Posted)
                        return Json(new { success = false, message = "هذا القيد مرحل بالفعل ولا يمكن ترحيله مرة أخرى." });

                    try
                    {
                        await _validationService.ValidateAsync(header, _currentUser.UserId, isManualAction: false);
                    }
                    catch (Exception ex)
                    {
                        return Json(new { success = false, message = $"فشل الترحيل لعدم استيفاء الشروط المحاسبية: {ex.Message}" });
                    }

                    header.Status = JournalEntryStatus.Posted;
                    header.PostedAt = DateTime.UtcNow;
                    header.PostedByUserId = _currentUser.UserId;
                    header.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                        entityName: "JournalEntryHeader",
                        entityId: header.Id.ToString(),
                        oldValues: new { Status = JournalEntryStatus.Draft },
                        newValues: new { Status = JournalEntryStatus.Posted, header.PostedAt, header.PostedByUserId });

                    return Json(new { success = true, message = $"تم ترحيل قيد اليومية ({header.EntryNumber}) بنجاح وأصبح نافذاً في الحسابات العامة." });
                }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "حدث تعارض: تم تعديل أو ترحيل هذا القيد بواسطة مستخدم آخر بالتزامن." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = $"فشل الترحيل: {ex.Message}" });
                }
            });
        }

        // ─── POST: Delete ────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.JournalVoucher.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var header = await _context.JournalEntryHeaders
                .Include(h => h.Lines)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null)
                return Json(new { success = false, message = "قيد اليومية غير موجود." });

            if (_currentUser.BranchId > 0 && header.BranchId != _currentUser.BranchId)
                return Json(new { success = false, message = "غير مصرح لك بحذف قيود فرع آخر." });

            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(header.EntryDate);
            if (!isPeriodAllowed)
                return Json(new { success = false, message = periodError });

            if (header.SourceDocumentType != SourceDocumentType.ManualJV)
                return Json(new { success = false, message = $"لا يمكن حذف هذا القيد يدوياً لأنه تم توليده آلياً من مستند ({header.SourceDocumentType}). الحذف يتم من المستند المصدر." });

            var oldValues = new { header.Id, header.EntryNumber, header.TotalDebitLocal, LinesCount = header.Lines.Count };

            try
            {
                _context.JournalEntryHeaders.Remove(header);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Json(new { success = false, message = "تم تعديل أو ترحيل هذا القيد بواسطة مستخدم آخر. لا يمكن حذفه." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "حدث خطأ أثناء حذف القيد: " + ex.Message });
            }

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "JournalEntryHeader",
                entityId: id.ToString(),
                oldValues: oldValues);

            return Json(new { success = true, message = "تم حذف قيد اليومية بنجاح." });
        }

        // ─── Helper: Populate Lookup Data for Client-Side (Vue.js) ───────────
        private async Task PopulateLookupDataAsync()
        {
            // Currencies
            ViewBag.Currencies = await _context.Currencies
                .Where(c => c.IsActive)
                .OrderBy(c => c.Code)
                .AsNoTracking()
                .ToListAsync();
        }

        private static decimal NumberRound(decimal val) => Math.Round(val, 4);

        private static string GetSourceDocumentTypeText(SourceDocumentType type)
        {
            return type switch
            {
                SourceDocumentType.ManualJV => "قيد يدوي",
                SourceDocumentType.SalesInvoice => "فاتورة مبيعات",
                SourceDocumentType.PurchaseInvoice => "فاتورة مشتريات",
                SourceDocumentType.ReceiptVoucher => "سند قبض",
                SourceDocumentType.PaymentVoucher => "سند صرف",
                SourceDocumentType.CurrencyRevaluation => "إعادة تقييم عملة",
                _ => type.ToString()
            };
        }
    }
}
