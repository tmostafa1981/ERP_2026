using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Helpers;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class CashBankVouchersController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;
        private readonly IJournalEntryValidationService _validationService;
        private readonly IFiscalPeriodService _fiscalPeriodService;
        private readonly IConfiguration _configuration;
        private readonly IPaymentMethodPermissionService _pmPermService;
        private readonly ICurrencyRateService _rateService;

        private const string SC_Code = "CB_VOUCHER";
        private const string SC_Name = "CashBankVouchers";
        private const string SC_Module = "Cash";

        private static readonly System.Threading.SemaphoreSlim _numberLock = new(1, 1);

        public CashBankVouchersController(
            ErpDbContext context,
            ICurrentUserContext currentUser,
            IAuditService audit,
            IJournalEntryValidationService validationService,
            IFiscalPeriodService fiscalPeriodService,
            IConfiguration configuration,
            IPaymentMethodPermissionService pmPermService,
            ICurrencyRateService? rateService = null)
        {
            _context = context;
            _currentUser = currentUser;
            _audit = audit;
            _validationService = validationService;
            _fiscalPeriodService = fiscalPeriodService;
            _configuration = configuration;
            _pmPermService = pmPermService;
            _rateService = rateService ?? new ERP.Infrastructure.Services.CurrencyRateService(context, audit, currentUser);
        }

        // ─── Number Generation ───────────────────────────────────────────────
        private async Task<string> GenerateNextVoucherNumberAsync(int branchId, int fiscalYearId, string fiscalYear, VoucherType type)
        {
            string typePrefix = type == VoucherType.Receipt ? "RV" : "PV";
            string prefix = $"{typePrefix}-{branchId}-{fiscalYear}-";

            var existingNumbers = await _context.VoucherHeaders
                .IgnoreQueryFilters()
                .Where(h => h.BranchId == branchId && h.FiscalYearId == fiscalYearId && h.VoucherType == type && h.VoucherNumber.StartsWith(prefix))
                .Select(h => h.VoucherNumber)
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

            int totalInFY = await _context.VoucherHeaders
                .IgnoreQueryFilters()
                .Where(h => h.BranchId == branchId && h.FiscalYearId == fiscalYearId && h.VoucherType == type)
                .CountAsync();

            if (totalInFY > maxNumber)
                maxNumber = totalInFY;

            long candidate = maxNumber + 1;
            string candidateCode = $"{prefix}{candidate:D5}";

            while (await _context.VoucherHeaders.IgnoreQueryFilters().AnyAsync(h => h.BranchId == branchId && h.VoucherNumber == candidateCode))
            {
                candidate++;
                candidateCode = $"{prefix}{candidate:D5}";
            }

            return candidateCode;
        }

        // ─── GET: Index ──────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Cash.ReceiptVoucher.View,Cash.PaymentVoucher.View")]
        public async Task<IActionResult> Index(int? type)
        {
            ViewBag.FiscalYears = await _context.FiscalYears
                .OrderByDescending(f => f.Year)
                .AsNoTracking()
                .ToListAsync();

            ViewBag.CashBankAccounts = await _context.GLAccounts
                .Where(a => a.IsActive && !a.IsGroup && (a.Code.StartsWith("120101") || a.Code.StartsWith("120102") || a.SubLedgerType == SubLedgerType.Bank))
                .Where(a => a.BranchId == null || a.BranchId == _currentUser.BranchId)
                .OrderBy(a => a.Code)
                .AsNoTracking()
                .ToListAsync();

            ViewBag.InitialType = type ?? 0;
            return View();
        }

        // ─── POST: LoadTableData (DataTables Server-side) ─────────────────────
        [HttpPost]
        [HasPermission("Cash.ReceiptVoucher.View,Cash.PaymentVoucher.View")]
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
                int? voucherType = int.TryParse(form["voucherType"].FirstOrDefault(), out var vt) && vt > 0 ? vt : null;
                DateTime? fromDate = DateTime.TryParse(form["fromDate"].FirstOrDefault(), out var fd) ? fd.Date : null;
                DateTime? toDate = DateTime.TryParse(form["toDate"].FirstOrDefault(), out var td) ? td.Date : null;
                int? status = int.TryParse(form["status"].FirstOrDefault(), out var st) && st > 0 ? st : null;
                int? cashBankAccountId = int.TryParse(form["cashBankAccountId"].FirstOrDefault(), out var ca) && ca > 0 ? ca : null;

                var query = _context.VoucherHeaders
                    .Include(h => h.Branch)
                    .Include(h => h.FiscalYear)
                    .Include(h => h.CashBankAccount)
                    .Include(h => h.Currency)
                    .Include(h => h.JournalEntryHeader)
                    .Include(h => h.Lines)
                    .AsNoTracking();

                // Single-Active-Branch Session Rule: Always isolate by active branch
                int currentBranchId = _currentUser.BranchId;
                if (currentBranchId > 0)
                {
                    query = query.Where(h => h.BranchId == currentBranchId);
                }

                // Permission-based voucher type filtering
                bool canViewReceipt = _currentUser.HasPermission("Cash.ReceiptVoucher.View");
                bool canViewPayment = _currentUser.HasPermission("Cash.PaymentVoucher.View");
                if (canViewReceipt && !canViewPayment)
                {
                    query = query.Where(h => h.VoucherType == VoucherType.Receipt);
                }
                else if (!canViewReceipt && canViewPayment)
                {
                    query = query.Where(h => h.VoucherType == VoucherType.Payment);
                }

                if (voucherType.HasValue)
                    query = query.Where(h => (int)h.VoucherType == voucherType.Value);

                if (fromDate.HasValue)
                    query = query.Where(h => h.VoucherDate >= fromDate.Value);

                if (toDate.HasValue)
                    query = query.Where(h => h.VoucherDate <= toDate.Value);

                if (status.HasValue)
                    query = query.Where(h => (int)h.Status == status.Value);

                if (cashBankAccountId.HasValue)
                    query = query.Where(h => h.CashBankAccountId == cashBankAccountId.Value);

                int totalRecords = await query.CountAsync();

                if (!string.IsNullOrWhiteSpace(searchValue))
                {
                    query = query.Where(h =>
                        h.VoucherNumber.Contains(searchValue) ||
                        (h.Notes != null && h.Notes.Contains(searchValue)) ||
                        (h.ReferenceNumber != null && h.ReferenceNumber.Contains(searchValue)) ||
                        (h.ChequeNumber != null && h.ChequeNumber.Contains(searchValue)) ||
                        (h.CashBankAccount != null && (h.CashBankAccount.NameAr.Contains(searchValue) || h.CashBankAccount.Code.Contains(searchValue))));
                }

                int filteredRecords = await query.CountAsync();

                var data = await query
                    .OrderByDescending(h => h.VoucherDate)
                    .ThenByDescending(h => h.Id)
                    .Skip(start)
                    .Take(length)
                    .Select(h => new
                    {
                        h.Id,
                        VoucherType = (int)h.VoucherType,
                        VoucherTypeText = h.VoucherType == VoucherType.Receipt ? "سند قبض" : "سند صرف",
                        h.VoucherNumber,
                        VoucherDate = h.VoucherDate.ToString("yyyy-MM-dd"),
                        CashBankAccountName = h.CashBankAccount != null ? $"{h.CashBankAccount.Code} - {h.CashBankAccount.LocalizedName}" : "-",
                        PaymentMethodText = h.PaymentMethod == VoucherPaymentMethod.Cash ? "نقداً" : (h.PaymentMethod == VoucherPaymentMethod.Cheque ? "شيك" : "تحويل"),
                        CurrencyCode = h.Currency != null ? h.Currency.Code : "EGP",
                        h.ExchangeRate,
                        h.TotalAmountForeign,
                        h.TotalAmountLocal,
                        h.ReferenceNumber,
                        h.Notes,
                        Status = (int)h.Status,
                        StatusText = h.Status == VoucherStatus.Posted ? "مرحل" : (h.Status == VoucherStatus.Cancelled ? "ملغى" : "مسودة"),
                        IsPosted = h.Status == VoucherStatus.Posted,
                        JournalEntryId = h.JournalEntryHeaderId,
                        JournalEntryNumber = h.JournalEntryHeader != null ? h.JournalEntryHeader.EntryNumber : null,
                        BranchName = h.Branch != null ? h.Branch.LocalizedName : "الفرع الحالي",
                        LinesCount = h.Lines.Count
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
        [HasPermission("Cash.ReceiptVoucher.Add,Cash.PaymentVoucher.Add")]
        public async Task<IActionResult> Create(VoucherType type = VoucherType.Receipt)
        {
            if (type == VoucherType.Receipt && !_currentUser.HasPermission("Cash.ReceiptVoucher.Add"))
                return RedirectToAction("AccessDenied", "Account", new { permission = "Cash.ReceiptVoucher.Add" });

            if (type == VoucherType.Payment && !_currentUser.HasPermission("Cash.PaymentVoucher.Add"))
                return RedirectToAction("AccessDenied", "Account", new { permission = "Cash.PaymentVoucher.Add" });

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
            ViewBag.CurrentBranchId = _currentUser.BranchId;
            ViewBag.CurrentFiscalYear = activeFiscalYear;
            ViewBag.VoucherType = type;

            ViewBag.NextVoucherNumber = await GenerateNextVoucherNumberAsync(_currentUser.BranchId, activeFiscalYear.Id, activeFiscalYear.Year, type);
            ViewBag.EnableCostCenter = _configuration.GetValue<bool>("AccountingSettings:EnableCostCenters", false);
            ViewBag.CanSaveDraft = _configuration.GetValue<bool>("AccountingSettings:AllowVoucherDraft", false);

            var pmPerms = await _pmPermService.GetPermissionsAsync(_currentUser.UserId, type);
            ViewBag.PaymentMethodPermissions = pmPerms;
            var serializerOptions = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            ViewBag.PaymentMethodPermissionsJson = System.Text.Json.JsonSerializer.Serialize(pmPerms, serializerOptions);

            return View();
        }

        // ─── POST: Create (JSON API) ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Cash.ReceiptVoucher.Add,Cash.PaymentVoucher.Add")]
        public async Task<IActionResult> Create([FromBody] VoucherHeader model, [FromQuery] bool postImmediately = false)
        {
            if (model == null)
                return Json(new { success = false, message = "لم يتم استلام أي بيانات للسند." });

            // If draft saving is disabled globally, enforce immediate posting
            if (!postImmediately && !_configuration.GetValue<bool>("AccountingSettings:AllowVoucherDraft", false))
            {
                postImmediately = true;
            }

            if (model.VoucherType == VoucherType.Receipt)
            {
                if (!_currentUser.HasPermission("Cash.ReceiptVoucher.Add"))
                    return Json(new { success = false, message = "غير مصرح لك بإضافة سند قبض." });

                if (postImmediately && !_currentUser.HasPermission("Cash.ReceiptVoucher.Post"))
                    return Json(new { success = false, message = "غير مصرح لك بترحيل سند قبض." });
            }
            else if (model.VoucherType == VoucherType.Payment)
            {
                if (!_currentUser.HasPermission("Cash.PaymentVoucher.Add"))
                    return Json(new { success = false, message = "غير مصرح لك بإضافة سند صرف." });

                if (postImmediately && !_currentUser.HasPermission("Cash.PaymentVoucher.Post"))
                    return Json(new { success = false, message = "غير مصرح لك بترحيل سند صرف." });
            }

            if (model.Lines == null || !model.Lines.Any())
                return Json(new { success = false, message = "يجب إضافة بند واحد على الأقل في السند." });

            // Validate Payment Method Permissions for each line
            var linesMethods = model.Lines.Select(l => l.PaymentMethod).Distinct();
            var permCheck = await _pmPermService.ValidateVoucherLinesPermissionAsync(
                _currentUser.UserId, model.VoucherType, linesMethods, "create");
            if (!permCheck.Allowed)
            {
                return Json(new { success = false, message = permCheck.ErrorMessage });
            }

            if (postImmediately)
            {
                var postPermCheck = await _pmPermService.ValidateVoucherLinesPermissionAsync(
                    _currentUser.UserId, model.VoucherType, linesMethods, "post");
                if (!postPermCheck.Allowed)
                {
                    return Json(new { success = false, message = postPermCheck.ErrorMessage });
                }
            }

            if (model.CashBankAccountId <= 0)
                return Json(new { success = false, message = "يرجى تحديد حساب الخزينة أو البنك." });

            // Single-Active-Branch Session Rule: Voucher MUST always belong to the active branch
            model.BranchId = _currentUser.BranchId;

            var cashAccount = await _context.GLAccounts.FindAsync(model.CashBankAccountId);
            if (cashAccount != null && cashAccount.BranchId.HasValue && cashAccount.BranchId != model.BranchId)
            {
                return Json(new { success = false, message = $"حساب الخزينة/البنك ({cashAccount.Code} - {cashAccount.LocalizedName}) مخصص لفرع آخر ولا يمكن استخدامه في هذا الفرع." });
            }

            // Fiscal Year
            var activeFY = await _context.FiscalYears
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == model.FiscalYearId || (f.IsActive && !f.IsClosed));

            if (activeFY == null || activeFY.IsClosed)
                return Json(new { success = false, message = "السنة المالية المحددة مغلقة أو غير نشطة." });

            model.FiscalYearId = activeFY.Id;

            // ─── Currency & Exchange Rate Validation (Party & Account Currency Locking) ───
            foreach (var line in model.Lines)
            {
                if (line.LineType == VoucherLineType.Customer && line.SubLedgerId.HasValue && line.SubLedgerId.Value > 0)
                {
                    var cust = await _context.Customers.FindAsync(line.SubLedgerId.Value);
                    if (cust != null && cust.CurrencyId.HasValue && cust.CurrencyId.Value != model.CurrencyId)
                    {
                        var custCurr = await _context.Currencies.FindAsync(cust.CurrencyId.Value);
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: عملة السند لا تطابق عملة العميل المعتمدة ({custCurr?.Code ?? "العملة"}). تم تعيين حركة العميل بعملته الأصلية." });
                    }
                }
                else if (line.LineType == VoucherLineType.Vendor && line.SubLedgerId.HasValue && line.SubLedgerId.Value > 0)
                {
                    var vend = await _context.Vendors.FindAsync(line.SubLedgerId.Value);
                    if (vend != null && vend.CurrencyId.HasValue && vend.CurrencyId.Value != model.CurrencyId)
                    {
                        var vendCurr = await _context.Currencies.FindAsync(vend.CurrencyId.Value);
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: عملة السند لا تطابق عملة المورد المعتمدة ({vendCurr?.Code ?? "العملة"}). تم تعيين حركة المورد بعملته الأصلية." });
                    }
                }

                if (line.AccountId > 0)
                {
                    var lineAcc = await _context.GLAccounts.FindAsync(line.AccountId);
                    if (lineAcc != null && lineAcc.CurrencyId.HasValue && lineAcc.CurrencyId.Value != model.CurrencyId)
                    {
                        var accCurr = await _context.Currencies.FindAsync(lineAcc.CurrencyId.Value);
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: الحساب المالي ({lineAcc.Code}) مقيد بعملة ({accCurr?.Code}) ولا يقبل حركة بعملة السند." });
                    }
                }
            }

            if (cashAccount != null && cashAccount.CurrencyId.HasValue && cashAccount.CurrencyId.Value != model.CurrencyId)
            {
                var cashCurr = await _context.Currencies.FindAsync(cashAccount.CurrencyId.Value);
                return Json(new { success = false, message = $"حساب الخزينة/البنك ({cashAccount.Code}) مقيد بعملة ({cashCurr?.Code}) ولا يقبل حركة بعملة السند." });
            }

            // Resolve exchange rate for VoucherDate
            var rateResult = await _rateService.ResolveExchangeRateAsync(model.CurrencyId, model.VoucherDate);
            if (!rateResult.Success)
            {
                return Json(new { success = false, message = rateResult.Message });
            }
            model.ExchangeRate = rateResult.Rate;

            // Clean lines and compute amounts
            int lineNum = 1;
            decimal totalForeign = 0;
            foreach (var line in model.Lines)
            {
                if (line.AmountForeign <= 0)
                    return Json(new { success = false, message = $"السطر {lineNum}: يجب إدخال مبلغ أكبر من الصفر." });

                if (line.PaymentMethod == VoucherPaymentMethod.Cheque)
                {
                    if (string.IsNullOrWhiteSpace(line.ChequeNumber))
                        return Json(new { success = false, message = $"السطر {lineNum}: يجب إدخال رقم الشيك عند اختيار طريقة الدفع شيك." });
                    if (!line.DueDate.HasValue)
                        return Json(new { success = false, message = $"السطر {lineNum}: يجب إدخال تاريخ استحقاق الشيك." });

                    line.ChequeNumber = line.ChequeNumber?.Trim();
                    line.BankName = line.BankName?.Trim();
                    line.DrawerName = line.DrawerName?.Trim();
                    if (line.BankId.HasValue && string.IsNullOrWhiteSpace(line.BankName))
                    {
                        var b = await _context.Banks.FindAsync(line.BankId.Value);
                        line.BankName = b?.NameAr;
                    }
                    if (line.ChequeStatus == ChequeStatus.None && model.VoucherType == VoucherType.Receipt)
                    {
                        line.ChequeStatus = ChequeStatus.UnderCollection;
                    }
                }
                else
                {
                    line.ChequeNumber = null;
                    line.ChequeDate = null;
                    line.DueDate = null;
                    line.BankId = null;
                    line.BankName = null;
                    line.DrawerName = null;
                    line.ChequeStatus = ChequeStatus.None;
                }

                line.LineNumber = lineNum++;
                line.AmountLocal = Math.Round(line.AmountForeign * model.ExchangeRate, 4);
                totalForeign += line.AmountForeign;
                line.CreatedAt = DateTime.UtcNow;
                line.Description = line.Description?.Trim();
                line.Notes = line.Notes?.Trim();
            }

            // Period Validation on VoucherDate
            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(model.VoucherDate);
            if (!isPeriodAllowed)
                return Json(new { success = false, message = periodError });

            model.TotalAmountForeign = Math.Round(totalForeign, 4);
            model.TotalAmountLocal = Math.Round(totalForeign * model.ExchangeRate, 4);
            model.Status = VoucherStatus.Posted;
            model.PostedAt = DateTime.UtcNow;
            model.PostedByUserId = _currentUser.UserId;
            model.CreatedAt = DateTime.UtcNow;
            model.CreatedByUserId = _currentUser.UserId;
            model.Notes = model.Notes?.Trim();
            model.ReferenceNumber = model.ReferenceNumber?.Trim();

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                await _numberLock.WaitAsync();
                try
                {
                    model.VoucherNumber = await GenerateNextVoucherNumberAsync(model.BranchId, activeFY.Id, activeFY.Year, model.VoucherType);
                    _context.VoucherHeaders.Add(model);
                    await _context.SaveChangesAsync();

                    var jvResult = await GenerateAndSaveJournalEntryForVoucherAsync(model);
                    if (!jvResult.Success)
                    {
                        await transaction.RollbackAsync();
                        return Json(new { success = false, message = $"فشل الترحيل المحاسبي للسند: {jvResult.Message}" });
                    }

                    model.JournalEntryHeaderId = jvResult.JournalEntryId;
                    await _context.SaveChangesAsync();

                    await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                        entityName: "VoucherHeader",
                        entityId: model.Id.ToString(),
                        newValues: new
                        {
                            model.Id,
                            model.VoucherType,
                            model.VoucherNumber,
                            model.VoucherDate,
                            model.CashBankAccountId,
                            model.TotalAmountForeign,
                            model.TotalAmountLocal,
                            model.JournalEntryHeaderId,
                            LinesCount = model.Lines.Count
                        });

                    await transaction.CommitAsync();

                    return Json(new
                    {
                        success = true,
                        message = $"تم حفظ وترحيل السند ({model.VoucherNumber}) والقيد المحاسبي المرتبط به بنجاح.",
                        id = model.Id,
                        voucherNumber = model.VoucherNumber,
                        posted = true
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "حدث خطأ أثناء حفظ وترحيل السند: " + ex.Message });
                }
                finally
                {
                    _numberLock.Release();
                }
            });
        }

        // ─── GET: Edit ───────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Cash.ReceiptVoucher.Edit,Cash.PaymentVoucher.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var header = await _context.VoucherHeaders
                .Include(h => h.Branch)
                .Include(h => h.FiscalYear)
                .Include(h => h.CashBankAccount)
                .Include(h => h.Currency)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.Account)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.CostCenter)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.Bank)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null)
            {
                TempData["ErrorMessage"] = "السند غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            if (_currentUser.BranchId > 0 && header.BranchId != _currentUser.BranchId)
            {
                TempData["ErrorMessage"] = "غير مصرح لك بالوصول إلى سندات فرع آخر.";
                return RedirectToAction(nameof(Index));
            }

            string requiredPerm = header.VoucherType == VoucherType.Receipt
                ? "Cash.ReceiptVoucher.Edit"
                : "Cash.PaymentVoucher.Edit";

            if (!_currentUser.HasPermission(requiredPerm))
            {
                TempData["ErrorMessage"] = "غير مصرح لك بتعديل هذا السند.";
                return RedirectToAction(nameof(Index));
            }

            if (header.Status == VoucherStatus.Cancelled)
            {
                TempData["ErrorMessage"] = "لا يمكن تعديل سند ملغي.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(header.VoucherDate);
            if (!isPeriodAllowed)
            {
                TempData["ErrorMessage"] = periodError;
                return RedirectToAction(nameof(Details), new { id });
            }

            await PopulateLookupDataAsync();

            var currentBranch = header.Branch ?? await _context.Branches.FindAsync(header.BranchId);
            ViewBag.CurrentBranchName = currentBranch?.LocalizedName ?? "الفرع الحالي";
            ViewBag.CurrentFiscalYear = header.FiscalYear;
            ViewBag.VoucherType = header.VoucherType;
            ViewBag.EnableCostCenter = _configuration.GetValue<bool>("AccountingSettings:EnableCostCenters", false);

            var pmPerms = await _pmPermService.GetPermissionsAsync(_currentUser.UserId, header.VoucherType);
            ViewBag.PaymentMethodPermissions = pmPerms;
            var serializerOptions = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            ViewBag.PaymentMethodPermissionsJson = System.Text.Json.JsonSerializer.Serialize(pmPerms, serializerOptions);

            // Preload SubLedgers names
            var customerIds = header.Lines.Where(l => l.LineType == VoucherLineType.Customer && l.SubLedgerId.HasValue).Select(l => l.SubLedgerId!.Value).Distinct().ToList();
            var vendorIds = header.Lines.Where(l => l.LineType == VoucherLineType.Vendor && l.SubLedgerId.HasValue).Select(l => l.SubLedgerId!.Value).Distinct().ToList();

            var customersDict = await _context.Customers
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => new { id = c.Id, code = c.Code, name = c.LocalizedName });

            var vendorsDict = await _context.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => new { id = v.Id, code = v.Code, name = v.LocalizedName });

            var linesDto = header.Lines.OrderBy(l => l.LineNumber).Select(l =>
            {
                string lineTypeText = l.LineType switch
                {
                    VoucherLineType.Customer => "Customer",
                    VoucherLineType.Vendor => "Vendor",
                    VoucherLineType.Bank => "Bank",
                    _ => "Account"
                };

                string subLedgerText = "";
                if (l.LineType == VoucherLineType.Customer && l.SubLedgerId.HasValue && customersDict.TryGetValue(l.SubLedgerId.Value, out var c))
                {
                    subLedgerText = $"{c.code} - {c.name}";
                }
                else if (l.LineType == VoucherLineType.Vendor && l.SubLedgerId.HasValue && vendorsDict.TryGetValue(l.SubLedgerId.Value, out var v))
                {
                    subLedgerText = $"{v.code} - {v.name}";
                }
                else if (l.Account != null)
                {
                    subLedgerText = $"{l.Account.Code} - {l.Account.LocalizedName}";
                }

                string bankDisplay = l.Bank != null ? $"{l.Bank.Code} - {l.Bank.NameAr}" : (l.BankName ?? "");

                return new
                {
                    id = l.Id,
                    lineNumber = l.LineNumber,
                    lineType = lineTypeText,
                    lineTypeValue = (int)l.LineType,
                    subLedgerId = l.SubLedgerId,
                    subLedgerText = subLedgerText,
                    accountId = l.AccountId,
                    accountCode = l.Account?.Code ?? "",
                    accountName = l.Account?.LocalizedName ?? "",
                    paymentMethod = (int)l.PaymentMethod,
                    chequeNumber = l.ChequeNumber ?? "",
                    chequeDate = l.ChequeDate?.ToString("yyyy-MM-dd") ?? "",
                    dueDate = l.DueDate?.ToString("yyyy-MM-dd") ?? "",
                    bankId = l.BankId,
                    bankCode = l.Bank?.Code ?? "",
                    bankName = l.BankName ?? l.Bank?.NameAr ?? "",
                    bankDisplayName = bankDisplay,
                    drawerName = l.DrawerName ?? "",
                    chequeStatus = (int)l.ChequeStatus,
                    amountForeign = l.AmountForeign,
                    amountLocal = l.AmountLocal,
                    costCenterId = l.CostCenterId,
                    costCenterCode = l.CostCenter?.Code ?? "",
                    costCenterName = l.CostCenter?.LocalizedName ?? "",
                    description = l.Description ?? "",
                    notes = l.Notes ?? ""
                };
            }).ToList();

            ViewBag.PreloadedLinesJson = System.Text.Json.JsonSerializer.Serialize(linesDto);

            return View(header);
        }

        // ─── POST: Edit (JSON API) ───────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Cash.ReceiptVoucher.Edit,Cash.PaymentVoucher.Edit")]
        public async Task<IActionResult> Edit([FromBody] VoucherHeader model)
        {
            if (model == null || model.Id <= 0)
                return Json(new { success = false, message = "معرف السند غير صالح." });

            if (model.Lines == null || !model.Lines.Any())
                return Json(new { success = false, message = "يجب إضافة بند واحد على الأقل." });

            var existing = await _context.VoucherHeaders
                .Include(h => h.Lines)
                .Include(h => h.FiscalYear)
                .FirstOrDefaultAsync(h => h.Id == model.Id);

            if (existing == null)
                return Json(new { success = false, message = "السند غير موجود." });

            if (_currentUser.BranchId > 0 && existing.BranchId != _currentUser.BranchId)
                return Json(new { success = false, message = "غير مصرح لك بتعديل سندات فرع آخر." });

            string requiredPerm = existing.VoucherType == VoucherType.Receipt
                ? "Cash.ReceiptVoucher.Edit"
                : "Cash.PaymentVoucher.Edit";

            if (!_currentUser.HasPermission(requiredPerm))
                return Json(new { success = false, message = "غير مصرح لك بتعديل هذا السند." });

            if (existing.Status == VoucherStatus.Cancelled)
                return Json(new { success = false, message = "لا يمكن تعديل سند ملغي." });

            var (isOrigPeriodAllowed, origPeriodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(existing.VoucherDate);
            if (!isOrigPeriodAllowed)
                return Json(new { success = false, message = origPeriodError });

            if (model.VoucherDate != existing.VoucherDate)
            {
                var (isNewPeriodAllowed, newPeriodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(model.VoucherDate);
                if (!isNewPeriodAllowed)
                    return Json(new { success = false, message = newPeriodError });
            }

            if (existing.FiscalYear == null || existing.FiscalYear.IsClosed)
                return Json(new { success = false, message = "لا يمكن تعديل سند في سنة مالية مغلقة." });

            // Validate Payment Method Permissions for existing lines (to edit/replace them) and new lines
            var allMethods = existing.Lines.Select(l => l.PaymentMethod)
                .Concat(model.Lines.Select(l => l.PaymentMethod))
                .Distinct();

            var permCheck = await _pmPermService.ValidateVoucherLinesPermissionAsync(
                _currentUser.UserId, existing.VoucherType, allMethods, "edit");
            if (!permCheck.Allowed)
            {
                return Json(new { success = false, message = permCheck.ErrorMessage });
            }

            var oldValues = new
            {
                existing.Id,
                existing.VoucherNumber,
                existing.VoucherDate,
                existing.CashBankAccountId,
                existing.TotalAmountForeign,
                existing.TotalAmountLocal,
                LinesCount = existing.Lines.Count
            };

            // Check cash bank account branch
            var cashAccount = await _context.GLAccounts.FindAsync(model.CashBankAccountId);
            if (cashAccount != null && cashAccount.BranchId.HasValue && cashAccount.BranchId != existing.BranchId)
            {
                return Json(new { success = false, message = $"حساب الخزينة/البنك ({cashAccount.Code} - {cashAccount.LocalizedName}) مخصص لفرع آخر ولا يمكن استخدامه في هذا الفرع." });
            }

            // ─── Currency & Exchange Rate Validation (Party & Account Currency Locking) ───
            foreach (var line in model.Lines)
            {
                if (line.LineType == VoucherLineType.Customer && line.SubLedgerId.HasValue && line.SubLedgerId.Value > 0)
                {
                    var cust = await _context.Customers.FindAsync(line.SubLedgerId.Value);
                    if (cust != null && cust.CurrencyId.HasValue && cust.CurrencyId.Value != model.CurrencyId)
                    {
                        var custCurr = await _context.Currencies.FindAsync(cust.CurrencyId.Value);
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: عملة السند لا تطابق عملة العميل المعتمدة ({custCurr?.Code ?? "العملة"})." });
                    }
                }
                else if (line.LineType == VoucherLineType.Vendor && line.SubLedgerId.HasValue && line.SubLedgerId.Value > 0)
                {
                    var vend = await _context.Vendors.FindAsync(line.SubLedgerId.Value);
                    if (vend != null && vend.CurrencyId.HasValue && vend.CurrencyId.Value != model.CurrencyId)
                    {
                        var vendCurr = await _context.Currencies.FindAsync(vend.CurrencyId.Value);
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: عملة السند لا تطابق عملة المورد المعتمدة ({vendCurr?.Code ?? "العملة"})." });
                    }
                }

                if (line.AccountId > 0)
                {
                    var lineAcc = await _context.GLAccounts.FindAsync(line.AccountId);
                    if (lineAcc != null && lineAcc.CurrencyId.HasValue && lineAcc.CurrencyId.Value != model.CurrencyId)
                    {
                        var accCurr = await _context.Currencies.FindAsync(lineAcc.CurrencyId.Value);
                        return Json(new { success = false, message = $"السطر {line.LineNumber}: الحساب المالي ({lineAcc.Code}) مقيد بعملة ({accCurr?.Code}) ولا يقبل حركة بعملة السند." });
                    }
                }
            }

            if (cashAccount != null && cashAccount.CurrencyId.HasValue && cashAccount.CurrencyId.Value != model.CurrencyId)
            {
                var cashCurr = await _context.Currencies.FindAsync(cashAccount.CurrencyId.Value);
                return Json(new { success = false, message = $"حساب الخزينة/البنك ({cashAccount.Code}) مقيد بعملة ({cashCurr?.Code}) ولا يقبل حركة بعملة السند." });
            }

            decimal targetExchangeRate = existing.ExchangeRate;
            if (model.VoucherDate != existing.VoucherDate || model.CurrencyId != existing.CurrencyId)
            {
                var rateResult = await _rateService.ResolveExchangeRateAsync(model.CurrencyId, model.VoucherDate);
                if (!rateResult.Success)
                {
                    return Json(new { success = false, message = rateResult.Message });
                }
                targetExchangeRate = rateResult.Rate;
            }
            model.ExchangeRate = targetExchangeRate;

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Remove existing linked Journal Entry to regenerate with updated voucher figures
                if (existing.JournalEntryHeaderId.HasValue)
                {
                    var oldJv = await _context.JournalEntryHeaders
                        .Include(j => j.Lines)
                        .FirstOrDefaultAsync(j => j.Id == existing.JournalEntryHeaderId.Value);
                    if (oldJv != null)
                    {
                        _context.JournalEntryLines.RemoveRange(oldJv.Lines);
                        _context.JournalEntryHeaders.Remove(oldJv);
                    }
                    existing.JournalEntryHeaderId = null;
                    await _context.SaveChangesAsync();
                }

                // Update Header values
                existing.VoucherDate = model.VoucherDate;
                existing.CashBankAccountId = model.CashBankAccountId;
                existing.CurrencyId = model.CurrencyId;
                existing.ExchangeRate = model.ExchangeRate <= 0 ? 1.000000m : model.ExchangeRate;
                existing.ReferenceNumber = model.ReferenceNumber?.Trim();
                existing.Notes = model.Notes?.Trim();
                existing.UpdatedAt = DateTime.UtcNow;

                // Remove existing lines and replace with new lines
                _context.VoucherLines.RemoveRange(existing.Lines);

                int lineNum = 1;
                decimal totalForeign = 0;
                var newLines = new List<VoucherLine>();
                foreach (var line in model.Lines)
                {
                    if (line.AmountForeign <= 0)
                    {
                        await transaction.RollbackAsync();
                        return Json(new { success = false, message = $"السطر {lineNum}: يجب إدخال مبلغ أكبر من الصفر." });
                    }

                    var newLine = new VoucherLine
                    {
                        VoucherHeaderId = existing.Id,
                        LineNumber = lineNum++,
                        LineType = line.LineType,
                        SubLedgerId = line.SubLedgerId,
                        AccountId = line.AccountId,
                        CostCenterId = line.CostCenterId,
                        AmountForeign = line.AmountForeign,
                        AmountLocal = Math.Round(line.AmountForeign * existing.ExchangeRate, 4),
                        Description = line.Description?.Trim(),
                        Notes = line.Notes?.Trim(),
                        PaymentMethod = line.PaymentMethod,
                        CreatedAt = DateTime.UtcNow
                    };

                    if (line.PaymentMethod == VoucherPaymentMethod.Cheque)
                    {
                        if (string.IsNullOrWhiteSpace(line.ChequeNumber))
                        {
                            await transaction.RollbackAsync();
                            return Json(new { success = false, message = $"السطر {newLine.LineNumber}: يجب إدخال رقم الشيك عند اختيار طريقة الدفع شيك." });
                        }
                        if (!line.DueDate.HasValue)
                        {
                            await transaction.RollbackAsync();
                            return Json(new { success = false, message = $"السطر {newLine.LineNumber}: يجب إدخال تاريخ استحقاق الشيك." });
                        }

                        newLine.ChequeNumber = line.ChequeNumber?.Trim();
                        newLine.ChequeDate = line.ChequeDate;
                        newLine.DueDate = line.DueDate;
                        newLine.BankId = line.BankId;
                        newLine.BankName = line.BankName?.Trim();
                        if (newLine.BankId.HasValue && string.IsNullOrWhiteSpace(newLine.BankName))
                        {
                            var b = await _context.Banks.FindAsync(newLine.BankId.Value);
                            newLine.BankName = b?.NameAr;
                        }
                        newLine.DrawerName = line.DrawerName?.Trim();
                        newLine.ChequeStatus = line.ChequeStatus == ChequeStatus.None && existing.VoucherType == VoucherType.Receipt
                            ? ChequeStatus.UnderCollection
                            : line.ChequeStatus;
                    }
                    else
                    {
                        newLine.ChequeNumber = null;
                        newLine.ChequeDate = null;
                        newLine.DueDate = null;
                        newLine.BankId = null;
                        newLine.BankName = null;
                        newLine.DrawerName = null;
                        newLine.ChequeStatus = ChequeStatus.None;
                    }

                    totalForeign += newLine.AmountForeign;
                    newLines.Add(newLine);
                }

                existing.Lines = newLines;
                existing.TotalAmountForeign = Math.Round(totalForeign, 4);
                existing.TotalAmountLocal = Math.Round(totalForeign * existing.ExchangeRate, 4);

                // Optimistic Concurrency check
                if (model.RowVersion != null && model.RowVersion.Length > 0)
                {
                    _context.Entry(existing).Property(e => e.RowVersion).OriginalValue = model.RowVersion;
                }

                await _context.SaveChangesAsync();

                // Regenerate linked Journal Entry
                var jvResult = await GenerateAndSaveJournalEntryForVoucherAsync(existing);
                if (!jvResult.Success)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = $"فشل الترحيل المحاسبي للسند: {jvResult.Message}" });
                }

                existing.JournalEntryHeaderId = jvResult.JournalEntryId;
                await _context.SaveChangesAsync();

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "VoucherHeader",
                    entityId: existing.Id.ToString(),
                    oldValues: oldValues,
                    newValues: new
                    {
                        existing.Id,
                        existing.VoucherNumber,
                        existing.VoucherDate,
                        existing.TotalAmountForeign,
                        existing.TotalAmountLocal,
                        existing.JournalEntryHeaderId,
                        LinesCount = existing.Lines.Count
                    });

                await transaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = $"تم تحديث وترحيل السند ({existing.VoucherNumber}) والقيد المحاسبي المرتبط به بنجاح.",
                    id = existing.Id,
                    voucherNumber = existing.VoucherNumber
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "تم تعديل هذا السند بواسطة مستخدم آخر بالتزامن. يرجى إعادة تحميل البيانات." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "حدث خطأ أثناء حفظ التعديلات: " + ex.Message });
            }
            });
        }

        // ─── GET: Details ────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Cash.ReceiptVoucher.View,Cash.PaymentVoucher.View")]
        public async Task<IActionResult> Details(int id)
        {
            var header = await _context.VoucherHeaders
                .Include(h => h.Branch)
                .Include(h => h.FiscalYear)
                .Include(h => h.CashBankAccount)
                .Include(h => h.Currency)
                .Include(h => h.JournalEntryHeader)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.Account)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.CostCenter)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.Bank)
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null)
            {
                TempData["ErrorMessage"] = "السند غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            if (_currentUser.BranchId > 0 && header.BranchId != _currentUser.BranchId)
            {
                TempData["ErrorMessage"] = "غير مصرح لك بالوصول إلى سندات فرع آخر.";
                return RedirectToAction(nameof(Index));
            }

            string requiredPerm = header.VoucherType == VoucherType.Receipt
                ? "Cash.ReceiptVoucher.View"
                : "Cash.PaymentVoucher.View";

            if (!_currentUser.HasPermission(requiredPerm))
            {
                TempData["ErrorMessage"] = "غير مصرح لك بعرض هذا السند.";
                return RedirectToAction(nameof(Index));
            }

            var customerIds = header.Lines.Where(l => l.LineType == VoucherLineType.Customer && l.SubLedgerId.HasValue).Select(l => l.SubLedgerId!.Value).Distinct().ToList();
            var vendorIds = header.Lines.Where(l => l.LineType == VoucherLineType.Vendor && l.SubLedgerId.HasValue).Select(l => l.SubLedgerId!.Value).Distinct().ToList();

            ViewBag.Customers = await _context.Customers
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => $"{c.Code} - {c.LocalizedName}");

            ViewBag.Vendors = await _context.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => $"{v.Code} - {v.LocalizedName}");

            ViewBag.AmountInWords = CurrencyTafqeetHelper.ConvertToWords(header.TotalAmountForeign, header.Currency?.Code ?? "EGP", true);

            return View(header);
        }

        // ─── POST: Post (ترحيل السند وتوليد قيد اليومية آلياً) ────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Cash.ReceiptVoucher.Post,Cash.PaymentVoucher.Post")]
        public async Task<IActionResult> Post(int id)
        {
            var result = await PerformPostAsync(id);
            return Json(new { success = result.Success, message = result.Message, journalEntryId = result.JournalEntryId });
        }

        private async Task<(bool Success, string Message, int? JournalEntryId)> GenerateAndSaveJournalEntryForVoucherAsync(VoucherHeader voucher)
        {
            if (voucher.CashBankAccountId <= 0)
                return (false, "حساب الخزينة أو البنك غير محدد.", null);

            var cashBankAccount = voucher.CashBankAccount ?? await _context.GLAccounts.FindAsync(voucher.CashBankAccountId);

            string jvTypeDesc = voucher.VoucherType == VoucherType.Receipt ? "سند قبض" : "سند صرف";
            var jvHeader = new JournalEntryHeader
            {
                BranchId = voucher.BranchId,
                FiscalYearId = voucher.FiscalYearId,
                EntryDate = voucher.VoucherDate,
                EntryNumber = $"JV-{voucher.VoucherNumber}",
                Description = $"{jvTypeDesc} رقم {voucher.VoucherNumber} - {voucher.Notes ?? ""}".Trim(),
                ReferenceNumber = voucher.VoucherNumber,
                Notes = $"تم التوليد التلقائي من {jvTypeDesc} رقم {voucher.VoucherNumber}",
                Status = JournalEntryStatus.Posted,
                SourceDocumentType = voucher.VoucherType == VoucherType.Receipt ? SourceDocumentType.ReceiptVoucher : SourceDocumentType.PaymentVoucher,
                SourceDocumentId = voucher.Id,
                TotalDebitLocal = voucher.TotalAmountLocal,
                TotalCreditLocal = voucher.TotalAmountLocal,
                PostedAt = DateTime.UtcNow,
                PostedByUserId = _currentUser.UserId,
                CreatedAt = DateTime.UtcNow
            };

            int jvLineNum = 1;
            var jvLines = new List<JournalEntryLine>();

            if (voucher.VoucherType == VoucherType.Receipt)
            {
                jvLines.Add(new JournalEntryLine
                {
                    LineNumber = jvLineNum++,
                    AccountId = voucher.CashBankAccountId,
                    SubLedgerType = SubLedgerType.None,
                    CurrencyId = voucher.CurrencyId,
                    ExchangeRate = voucher.ExchangeRate,
                    DebitForeign = voucher.TotalAmountForeign,
                    DebitLocal = voucher.TotalAmountLocal,
                    CreditForeign = 0,
                    CreditLocal = 0,
                    Description = $"قبض إلى {cashBankAccount?.LocalizedName ?? "الخزينة/البنك"}",
                    CreatedAt = DateTime.UtcNow
                });

                foreach (var vl in voucher.Lines)
                {
                    string desc = vl.Description ?? jvHeader.Description;
                    if (vl.PaymentMethod == VoucherPaymentMethod.Cheque && !string.IsNullOrWhiteSpace(vl.ChequeNumber))
                    {
                        string bankInfo = !string.IsNullOrWhiteSpace(vl.BankName) ? $" - {vl.BankName}" : "";
                        desc = $"{desc} [شيك {vl.ChequeNumber}{bankInfo}]".Trim();
                    }

                    jvLines.Add(new JournalEntryLine
                    {
                        LineNumber = jvLineNum++,
                        AccountId = vl.AccountId,
                        SubLedgerType = MapLineType(vl.LineType),
                        SubLedgerId = vl.SubLedgerId,
                        CurrencyId = voucher.CurrencyId,
                        ExchangeRate = voucher.ExchangeRate,
                        DebitForeign = 0,
                        DebitLocal = 0,
                        CreditForeign = vl.AmountForeign,
                        CreditLocal = vl.AmountLocal,
                        CostCenterId = vl.CostCenterId,
                        Description = desc,
                        Notes = vl.Notes,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            else
            {
                foreach (var vl in voucher.Lines)
                {
                    string desc = vl.Description ?? jvHeader.Description;
                    if (vl.PaymentMethod == VoucherPaymentMethod.Cheque && !string.IsNullOrWhiteSpace(vl.ChequeNumber))
                    {
                        string bankInfo = !string.IsNullOrWhiteSpace(vl.BankName) ? $" - {vl.BankName}" : "";
                        desc = $"{desc} [شيك {vl.ChequeNumber}{bankInfo}]".Trim();
                    }

                    jvLines.Add(new JournalEntryLine
                    {
                        LineNumber = jvLineNum++,
                        AccountId = vl.AccountId,
                        SubLedgerType = MapLineType(vl.LineType),
                        SubLedgerId = vl.SubLedgerId,
                        CurrencyId = voucher.CurrencyId,
                        ExchangeRate = voucher.ExchangeRate,
                        DebitForeign = vl.AmountForeign,
                        DebitLocal = vl.AmountLocal,
                        CreditForeign = 0,
                        CreditLocal = 0,
                        CostCenterId = vl.CostCenterId,
                        Description = desc,
                        Notes = vl.Notes,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                jvLines.Add(new JournalEntryLine
                {
                    LineNumber = jvLineNum++,
                    AccountId = voucher.CashBankAccountId,
                    SubLedgerType = SubLedgerType.None,
                    CurrencyId = voucher.CurrencyId,
                    ExchangeRate = voucher.ExchangeRate,
                    DebitForeign = 0,
                    DebitLocal = 0,
                    CreditForeign = voucher.TotalAmountForeign,
                    CreditLocal = voucher.TotalAmountLocal,
                    Description = $"صرف من {cashBankAccount?.LocalizedName ?? "الخزينة/البنك"}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            jvHeader.Lines = jvLines;

            try
            {
                await _validationService.ValidateAsync(jvHeader, _currentUser.UserId, isManualAction: false);
            }
            catch (Exception valEx)
            {
                return (false, $"فشل الترحيل المحاسبي: {valEx.Message}", null);
            }

            _context.JournalEntryHeaders.Add(jvHeader);
            await _context.SaveChangesAsync();

            return (true, "تم إنشاء القيد بنجاح.", jvHeader.Id);
        }

        private async Task<(bool Success, string Message, int? JournalEntryId)> PerformPostAsync(int id)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
                try
                {
                    VoucherHeader? voucher = null;
                    if (_context.Database.IsSqlServer())
                    {
                        voucher = await _context.VoucherHeaders
                            .FromSqlRaw("SELECT * FROM VoucherHeaders WITH (UPDLOCK, ROWLOCK) WHERE Id = {0}", id)
                            .Include(v => v.Lines)
                            .Include(v => v.FiscalYear)
                            .Include(v => v.CashBankAccount)
                            .FirstOrDefaultAsync();
                    }
                    else
                    {
                        voucher = await _context.VoucherHeaders
                            .Include(v => v.Lines)
                            .Include(v => v.FiscalYear)
                            .Include(v => v.CashBankAccount)
                            .FirstOrDefaultAsync(v => v.Id == id);
                    }

                    if (voucher == null)
                        return (false, "السند غير موجود.", null);

                    if (_currentUser.BranchId > 0 && voucher.BranchId != _currentUser.BranchId)
                        return (false, "غير مصرح لك بترحيل سندات فرع آخر.", null);

                    string requiredPerm = voucher.VoucherType == VoucherType.Receipt
                        ? "Cash.ReceiptVoucher.Post"
                        : "Cash.PaymentVoucher.Post";

                    if (!_currentUser.HasPermission(requiredPerm))
                        return (false, "غير مصرح لك بترحيل هذا السند.", null);

                    if (voucher.Status == VoucherStatus.Posted)
                        return (false, "هذا السند مرحل بالفعل ولا يمكن ترحيله مرة أخرى.", voucher.JournalEntryHeaderId);

                    if (voucher.FiscalYear == null || voucher.FiscalYear.IsClosed || !voucher.FiscalYear.IsActive)
                        return (false, "لا يمكن ترحيل سند في سنة مالية مغلقة أو غير نشطة.", null);

                    if (voucher.Lines == null || !voucher.Lines.Any())
                        return (false, "لا يمكن ترحيل سند لا يحتوي على أي بنود.", null);

                    // Validate payment method post permissions
                    var methodsToPost = voucher.Lines.Select(l => l.PaymentMethod).Distinct();
                    var postPmCheck = await _pmPermService.ValidateVoucherLinesPermissionAsync(
                        _currentUser.UserId, voucher.VoucherType, methodsToPost, "post");
                    if (!postPmCheck.Allowed)
                        return (false, postPmCheck.ErrorMessage, null);

                    if (voucher.CashBankAccountId <= 0)
                        return (false, "حساب الخزينة أو البنك غير محدد.", null);

                    // 1. Generate Journal Entry Header
                    string jvTypeDesc = voucher.VoucherType == VoucherType.Receipt ? "سند قبض" : "سند صرف";
                    var jvHeader = new JournalEntryHeader
                    {
                        BranchId = voucher.BranchId,
                        FiscalYearId = voucher.FiscalYearId,
                        EntryDate = voucher.VoucherDate,
                        EntryNumber = $"JV-{voucher.VoucherNumber}",
                        Description = $"{jvTypeDesc} رقم {voucher.VoucherNumber} - {voucher.Notes ?? ""}".Trim(),
                        ReferenceNumber = voucher.VoucherNumber,
                        Notes = $"تم التوليد التلقائي من {jvTypeDesc} رقم {voucher.VoucherNumber}",
                        Status = JournalEntryStatus.Posted,
                        SourceDocumentType = voucher.VoucherType == VoucherType.Receipt ? SourceDocumentType.ReceiptVoucher : SourceDocumentType.PaymentVoucher,
                        SourceDocumentId = voucher.Id,
                        TotalDebitLocal = voucher.TotalAmountLocal,
                        TotalCreditLocal = voucher.TotalAmountLocal,
                        PostedAt = DateTime.UtcNow,
                        PostedByUserId = _currentUser.UserId,
                        CreatedAt = DateTime.UtcNow
                    };

                    int jvLineNum = 1;
                    var jvLines = new List<JournalEntryLine>();

                    if (voucher.VoucherType == VoucherType.Receipt)
                    {
                        // ── Receipt Voucher: Debit Cash/Bank, Credit Detail Lines ──
                        jvLines.Add(new JournalEntryLine
                        {
                            LineNumber = jvLineNum++,
                            AccountId = voucher.CashBankAccountId,
                            SubLedgerType = SubLedgerType.None,
                            CurrencyId = voucher.CurrencyId,
                            ExchangeRate = voucher.ExchangeRate,
                            DebitForeign = voucher.TotalAmountForeign,
                            DebitLocal = voucher.TotalAmountLocal,
                            CreditForeign = 0,
                            CreditLocal = 0,
                            Description = $"قبض إلى {voucher.CashBankAccount?.LocalizedName ?? "الخزينة/البنك"}",
                            CreatedAt = DateTime.UtcNow
                        });

                        foreach (var vl in voucher.Lines)
                        {
                            string desc = vl.Description ?? jvHeader.Description;
                            if (vl.PaymentMethod == VoucherPaymentMethod.Cheque && !string.IsNullOrWhiteSpace(vl.ChequeNumber))
                            {
                                string bankInfo = !string.IsNullOrWhiteSpace(vl.BankName) ? $" - {vl.BankName}" : "";
                                desc = $"{desc} [شيك {vl.ChequeNumber}{bankInfo}]".Trim();
                            }

                            jvLines.Add(new JournalEntryLine
                            {
                                LineNumber = jvLineNum++,
                                AccountId = vl.AccountId,
                                SubLedgerType = MapLineType(vl.LineType),
                                SubLedgerId = vl.SubLedgerId,
                                CurrencyId = voucher.CurrencyId,
                                ExchangeRate = voucher.ExchangeRate,
                                DebitForeign = 0,
                                DebitLocal = 0,
                                CreditForeign = vl.AmountForeign,
                                CreditLocal = vl.AmountLocal,
                                CostCenterId = vl.CostCenterId,
                                Description = desc,
                                Notes = vl.Notes,
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                    }
                    else
                    {
                        // ── Payment Voucher: Debit Detail Lines, Credit Cash/Bank ──
                        foreach (var vl in voucher.Lines)
                        {
                            string desc = vl.Description ?? jvHeader.Description;
                            if (vl.PaymentMethod == VoucherPaymentMethod.Cheque && !string.IsNullOrWhiteSpace(vl.ChequeNumber))
                            {
                                string bankInfo = !string.IsNullOrWhiteSpace(vl.BankName) ? $" - {vl.BankName}" : "";
                                desc = $"{desc} [شيك {vl.ChequeNumber}{bankInfo}]".Trim();
                            }

                            jvLines.Add(new JournalEntryLine
                            {
                                LineNumber = jvLineNum++,
                                AccountId = vl.AccountId,
                                SubLedgerType = MapLineType(vl.LineType),
                                SubLedgerId = vl.SubLedgerId,
                                CurrencyId = voucher.CurrencyId,
                                ExchangeRate = voucher.ExchangeRate,
                                DebitForeign = vl.AmountForeign,
                                DebitLocal = vl.AmountLocal,
                                CreditForeign = 0,
                                CreditLocal = 0,
                                CostCenterId = vl.CostCenterId,
                                Description = desc,
                                Notes = vl.Notes,
                                CreatedAt = DateTime.UtcNow
                            });
                        }

                        jvLines.Add(new JournalEntryLine
                        {
                            LineNumber = jvLineNum++,
                            AccountId = voucher.CashBankAccountId,
                            SubLedgerType = SubLedgerType.None,
                            CurrencyId = voucher.CurrencyId,
                            ExchangeRate = voucher.ExchangeRate,
                            DebitForeign = 0,
                            DebitLocal = 0,
                            CreditForeign = voucher.TotalAmountForeign,
                            CreditLocal = voucher.TotalAmountLocal,
                            Description = $"صرف من {voucher.CashBankAccount?.LocalizedName ?? "الخزينة/البنك"}",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    jvHeader.Lines = jvLines;

                    // Validate generated entry
                    try
                    {
                        await _validationService.ValidateAsync(jvHeader, _currentUser.UserId, isManualAction: false);
                    }
                    catch (Exception valEx)
                    {
                        return (false, $"فشل الترحيل المحاسبي: {valEx.Message}", null);
                    }

                    _context.JournalEntryHeaders.Add(jvHeader);
                    await _context.SaveChangesAsync();

                    // Update Voucher Header
                    voucher.Status = VoucherStatus.Posted;
                    voucher.JournalEntryHeaderId = jvHeader.Id;
                    voucher.PostedAt = DateTime.UtcNow;
                    voucher.PostedByUserId = _currentUser.UserId;
                    voucher.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                        entityName: "VoucherHeader",
                        entityId: voucher.Id.ToString(),
                        oldValues: new { Status = VoucherStatus.Draft },
                        newValues: new { Status = VoucherStatus.Posted, voucher.JournalEntryHeaderId, voucher.PostedAt });

                    return (true, $"تم ترحيل السند ({voucher.VoucherNumber}) بنجاح وإنشاء قيد اليومية العام المرتبط.", jvHeader.Id);
                }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync();
                    return (false, "حدث تعارض أثناء الترحيل بواسطة مستخدم آخر بالتزامن.", null);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return (false, $"فشل الترحيل: {ex.Message}", null);
                }
            });
        }

        // ─── POST: Unpost (إلغاء ترحيل السند وإلغاء القيد المرتبط) ───────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Cash.ReceiptVoucher.Unpost,Cash.PaymentVoucher.Unpost")]
        public async Task<IActionResult> Unpost(int id)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
                try
                {
                    VoucherHeader? voucher = null;
                    if (_context.Database.IsSqlServer())
                    {
                        voucher = await _context.VoucherHeaders
                            .FromSqlRaw("SELECT * FROM VoucherHeaders WITH (UPDLOCK, ROWLOCK) WHERE Id = {0}", id)
                            .Include(v => v.FiscalYear)
                            .Include(v => v.Lines)
                            .FirstOrDefaultAsync();
                    }
                    else
                    {
                        voucher = await _context.VoucherHeaders
                            .Include(v => v.FiscalYear)
                            .Include(v => v.Lines)
                            .FirstOrDefaultAsync(v => v.Id == id);
                    }

                    if (voucher == null)
                        return Json(new { success = false, message = "السند غير موجود." });

                    if (_currentUser.BranchId > 0 && voucher.BranchId != _currentUser.BranchId)
                        return Json(new { success = false, message = "غير مصرح لك بإلغاء ترحيل سندات فرع آخر." });

                    string requiredPerm = voucher.VoucherType == VoucherType.Receipt
                        ? "Cash.ReceiptVoucher.Unpost"
                        : "Cash.PaymentVoucher.Unpost";

                    if (!_currentUser.HasPermission(requiredPerm))
                        return Json(new { success = false, message = "غير مصرح لك بإلغاء ترحيل هذا السند." });

                    // Validate payment method unpost permissions
                    var methodsToUnpost = voucher.Lines.Select(l => l.PaymentMethod).Distinct();
                    var unpostPmCheck = await _pmPermService.ValidateVoucherLinesPermissionAsync(
                        _currentUser.UserId, voucher.VoucherType, methodsToUnpost, "unpost");
                    if (!unpostPmCheck.Allowed)
                        return Json(new { success = false, message = unpostPmCheck.ErrorMessage });

                    if (voucher.Status != VoucherStatus.Posted)
                        return Json(new { success = false, message = "السند غير مرحل بالفعل." });

                    if (voucher.FiscalYear == null || voucher.FiscalYear.IsClosed)
                        return Json(new { success = false, message = "لا يمكن إلغاء ترحيل سند في سنة مالية مغلقة." });

                    // Find and remove generated Journal Entry
                    if (voucher.JournalEntryHeaderId.HasValue)
                    {
                        var jv = await _context.JournalEntryHeaders
                            .Include(j => j.Lines)
                            .FirstOrDefaultAsync(j => j.Id == voucher.JournalEntryHeaderId.Value);

                        if (jv != null)
                        {
                            _context.JournalEntryHeaders.Remove(jv);
                        }
                    }

                    voucher.Status = VoucherStatus.Draft;
                    voucher.JournalEntryHeaderId = null;
                    voucher.PostedAt = null;
                    voucher.PostedByUserId = null;
                    voucher.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                        entityName: "VoucherHeader",
                        entityId: voucher.Id.ToString(),
                        oldValues: new { Status = VoucherStatus.Posted },
                        newValues: new { Status = VoucherStatus.Draft, UnpostedAt = DateTime.UtcNow });

                    return Json(new { success = true, message = $"تم إلغاء ترحيل السند ({voucher.VoucherNumber}) وإعادة حالته إلى مسودة." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = $"فشل إلغاء الترحيل: {ex.Message}" });
                }
            });
        }

        // ─── POST: Delete ────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Cash.ReceiptVoucher.Delete,Cash.PaymentVoucher.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var voucher = await _context.VoucherHeaders
                .Include(h => h.Lines)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (voucher == null)
                return Json(new { success = false, message = "السند غير موجود." });

            if (_currentUser.BranchId > 0 && voucher.BranchId != _currentUser.BranchId)
                return Json(new { success = false, message = "غير مصرح لك بحذف سندات فرع آخر." });

            string requiredPerm = voucher.VoucherType == VoucherType.Receipt
                ? "Cash.ReceiptVoucher.Delete"
                : "Cash.PaymentVoucher.Delete";

            if (!_currentUser.HasPermission(requiredPerm))
                return Json(new { success = false, message = "غير مصرح لك بحذف هذا السند." });

            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(voucher.VoucherDate);
            if (!isPeriodAllowed)
                return Json(new { success = false, message = periodError });

            // Validate Payment Method Delete permissions
            var deleteMethods = voucher.Lines.Select(l => l.PaymentMethod).Distinct();
            var deletePmCheck = await _pmPermService.ValidateVoucherLinesPermissionAsync(
                _currentUser.UserId, voucher.VoucherType, deleteMethods, "delete");
            if (!deletePmCheck.Allowed)
                return Json(new { success = false, message = deletePmCheck.ErrorMessage });

            var oldValues = new { voucher.Id, voucher.VoucherNumber, voucher.TotalAmountForeign, LinesCount = voucher.Lines.Count };

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    if (voucher.JournalEntryHeaderId.HasValue)
                    {
                        var jv = await _context.JournalEntryHeaders
                            .Include(j => j.Lines)
                            .FirstOrDefaultAsync(j => j.Id == voucher.JournalEntryHeaderId.Value);
                        if (jv != null)
                        {
                            _context.JournalEntryLines.RemoveRange(jv.Lines);
                            _context.JournalEntryHeaders.Remove(jv);
                        }
                        voucher.JournalEntryHeaderId = null;
                    }

                    _context.VoucherHeaders.Remove(voucher);
                    await _context.SaveChangesAsync();

                    await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                        entityName: "VoucherHeader",
                        entityId: id.ToString(),
                        oldValues: oldValues);

                    await transaction.CommitAsync();
                    return Json(new { success = true, message = "تم حذف السند وإلغاء القيد المحاسبي المرتبط به بنجاح." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "حدث خطأ أثناء حذف السند: " + ex.Message });
                }
            });
        }

        // ─── POST: Cancel ────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Cash.ReceiptVoucher.Cancel,Cash.PaymentVoucher.Cancel")]
        public async Task<IActionResult> Cancel(int id)
        {
            var voucher = await _context.VoucherHeaders
                .Include(h => h.Lines)
                .FirstOrDefaultAsync(h => h.Id == id);
            if (voucher == null)
                return Json(new { success = false, message = "السند غير موجود." });

            if (_currentUser.BranchId > 0 && voucher.BranchId != _currentUser.BranchId)
                return Json(new { success = false, message = "غير مصرح لك بإلغاء سندات فرع آخر." });

            string requiredPerm = voucher.VoucherType == VoucherType.Receipt
                ? "Cash.ReceiptVoucher.Cancel"
                : "Cash.PaymentVoucher.Cancel";

            if (!_currentUser.HasPermission(requiredPerm))
                return Json(new { success = false, message = "غير مصرح لك بإلغاء هذا السند." });

            if (voucher.Status == VoucherStatus.Cancelled)
                return Json(new { success = false, message = "السند ملغى بالفعل." });

            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(voucher.VoucherDate);
            if (!isPeriodAllowed)
                return Json(new { success = false, message = periodError });

            // Validate Payment Method Delete/Cancel permissions
            var cancelMethods = voucher.Lines.Select(l => l.PaymentMethod).Distinct();
            var cancelPmCheck = await _pmPermService.ValidateVoucherLinesPermissionAsync(
                _currentUser.UserId, voucher.VoucherType, cancelMethods, "delete");
            if (!cancelPmCheck.Allowed)
                return Json(new { success = false, message = cancelPmCheck.ErrorMessage });

            var cancelStrategy = _context.Database.CreateExecutionStrategy();
            return await cancelStrategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    if (voucher.JournalEntryHeaderId.HasValue)
                    {
                        var jv = await _context.JournalEntryHeaders
                            .Include(j => j.Lines)
                            .FirstOrDefaultAsync(j => j.Id == voucher.JournalEntryHeaderId.Value);
                        if (jv != null)
                        {
                            _context.JournalEntryLines.RemoveRange(jv.Lines);
                            _context.JournalEntryHeaders.Remove(jv);
                        }
                        voucher.JournalEntryHeaderId = null;
                    }

                    var oldStatus = voucher.Status;
                    voucher.Status = VoucherStatus.Cancelled;
                    voucher.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();

                    await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                        entityName: "VoucherHeader",
                        entityId: voucher.Id.ToString(),
                        oldValues: new { Status = oldStatus },
                        newValues: new { Status = VoucherStatus.Cancelled });

                    await transaction.CommitAsync();
                    return Json(new { success = true, message = $"تم إلغاء السند ({voucher.VoucherNumber}) وإلغاء القيد المحاسبي المرتبط به بنجاح." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "حدث خطأ أثناء إلغاء السند: " + ex.Message });
                }
            });
        }

        // ─── GET: Print ──────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Cash.ReceiptVoucher.Print,Cash.PaymentVoucher.Print")]
        public async Task<IActionResult> Print(int id)
        {
            var header = await _context.VoucherHeaders
                .Include(h => h.Branch)
                    .ThenInclude(b => b!.Company)
                .Include(h => h.FiscalYear)
                .Include(h => h.CashBankAccount)
                .Include(h => h.Currency)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.Account)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.CostCenter)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.Bank)
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null)
            {
                TempData["ErrorMessage"] = "السند غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            if (_currentUser.BranchId > 0 && header.BranchId != _currentUser.BranchId)
            {
                TempData["ErrorMessage"] = "غير مصرح لك بطباعة سندات فرع آخر.";
                return RedirectToAction(nameof(Index));
            }

            string requiredPerm = header.VoucherType == VoucherType.Receipt
                ? "Cash.ReceiptVoucher.Print"
                : "Cash.PaymentVoucher.Print";

            if (!_currentUser.HasPermission(requiredPerm))
            {
                TempData["ErrorMessage"] = "غير مصرح لك بطباعة هذا السند.";
                return RedirectToAction(nameof(Index));
            }

            var customerIds = header.Lines.Where(l => l.LineType == VoucherLineType.Customer && l.SubLedgerId.HasValue).Select(l => l.SubLedgerId!.Value).Distinct().ToList();
            var vendorIds = header.Lines.Where(l => l.LineType == VoucherLineType.Vendor && l.SubLedgerId.HasValue).Select(l => l.SubLedgerId!.Value).Distinct().ToList();

            ViewBag.Customers = await _context.Customers
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => $"{c.Code} - {c.LocalizedName}");

            ViewBag.Vendors = await _context.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => $"{v.Code} - {v.LocalizedName}");

            ViewBag.AmountInWords = CurrencyTafqeetHelper.ConvertToWords(header.TotalAmountForeign, header.Currency?.Code ?? "EGP", true);

            return View(header);
        }

        // ─── GET: GetBanksSearch (AJAX Bank Search Lookup) ───────────────────
        [HttpGet]
        public async Task<IActionResult> GetBanksSearch(string? term, int page = 1, int pageSize = 20)
        {
            var query = _context.Banks.Where(b => b.IsActive);
            if (!string.IsNullOrWhiteSpace(term))
            {
                var t = term.Trim();
                query = query.Where(b => b.Code.Contains(t) || b.NameAr.Contains(t) || b.NameEn.Contains(t) || (b.SwiftCode != null && b.SwiftCode.Contains(t)));
            }

            var total = await query.CountAsync();
            var items = await query.OrderBy(b => b.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(b => new
                {
                    id = b.Id,
                    code = b.Code,
                    nameAr = b.NameAr,
                    nameEn = b.NameEn,
                    swiftCode = b.SwiftCode,
                    text = $"{b.Code} - {b.NameAr}"
                })
                .ToListAsync();

            return Json(new { items, total });
        }

        // ─── Helpers ─────────────────────────────────────────────────────────
        private async Task PopulateLookupDataAsync()
        {
            ViewBag.Currencies = await _context.Currencies
                .Where(c => c.IsActive)
                .OrderBy(c => c.Code)
                .AsNoTracking()
                .ToListAsync();

            int? targetBranchId = _currentUser.BranchId;
            ViewBag.CashBankAccounts = await _context.GLAccounts
                .Where(a => a.IsActive && !a.IsGroup && (a.Code.StartsWith("120101") || a.Code.StartsWith("120102") || a.SubLedgerType == SubLedgerType.Bank))
                .Where(a => a.BranchId == null || a.BranchId == targetBranchId)
                .OrderBy(a => a.Code)
                .AsNoTracking()
                .ToListAsync();
        }

        private static SubLedgerType MapLineType(VoucherLineType lineType)
        {
            return lineType switch
            {
                VoucherLineType.Customer => SubLedgerType.Customer,
                VoucherLineType.Vendor => SubLedgerType.Vendor,
                VoucherLineType.Bank => SubLedgerType.Bank,
                _ => SubLedgerType.None
            };
        }
    }
}
