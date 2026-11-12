using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class DocumentsController : Controller
    {
        private readonly IUniversalDocumentService _docService;
        private readonly ITransactionTypeService _ttService;
        private readonly ISerialNumberService _serialService;
        private readonly IDocumentConversionService _conversionService;
        private readonly IDocumentAdjustmentService _adjustmentService;
        private readonly ICurrentUserContext _currentUser;
        private readonly IFiscalPeriodService _fiscalPeriodService;
        private readonly IInventoryAvailabilityService _availabilityService;
        private readonly ErpDbContext _context;

        public DocumentsController(
            IUniversalDocumentService docService,
            ITransactionTypeService ttService,
            ISerialNumberService serialService,
            IDocumentConversionService conversionService,
            IDocumentAdjustmentService adjustmentService,
            ICurrentUserContext currentUser,
            IFiscalPeriodService fiscalPeriodService,
            IInventoryAvailabilityService availabilityService,
            ErpDbContext context)
        {
            _docService = docService;
            _ttService = ttService;
            _serialService = serialService;
            _conversionService = conversionService;
            _adjustmentService = adjustmentService;
            _currentUser = currentUser;
            _fiscalPeriodService = fiscalPeriodService;
            _availabilityService = availabilityService;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(TransactionModule? module, int? transactionTypeId, DocumentStatus? status, string? searchTerm)
        {
            int branchId = _currentUser.BranchId;
            if (branchId <= 0)
            {
                TempData["ErrorMessage"] = "يجب اختيار فرع صالح أولاً لمتابعة المستندات.";
                return RedirectToAction("Index", "Home");
            }

            // Always fetch all creatable types across all modules for the "إنشاء مستند جديد" dropdown
            var allCreatableTypes = await _ttService.GetAllowedTypesAsync(_currentUser.UserId ?? "", branchId, null);
            ViewBag.AllCreatableTypes = allCreatableTypes.Where(t => t.CanCreate).OrderBy(t => t.Module).ThenBy(t => t.Code).ToList();

            // Filter dropdown types (filtered by module if selected)
            var allowedTypes = await _ttService.GetAllowedTypesAsync(_currentUser.UserId ?? "", branchId, module);
            ViewBag.AllowedTypes = allowedTypes;
            ViewBag.SelectedModule = module;
            ViewBag.SelectedTransactionTypeId = transactionTypeId;
            ViewBag.SelectedStatus = status;
            ViewBag.SearchTerm = searchTerm;

            var docs = await _docService.GetDocumentsListAsync(branchId, module, transactionTypeId, status);
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                docs = docs.Where(d =>
                    (!string.IsNullOrEmpty(d.DocumentNumber) && d.DocumentNumber.ToLower().Contains(term)) ||
                    (!string.IsNullOrEmpty(d.PartyName) && d.PartyName.ToLower().Contains(term)) ||
                    (!string.IsNullOrEmpty(d.JournalEntryNumber) && d.JournalEntryNumber.ToLower().Contains(term)) ||
                    (!string.IsNullOrEmpty(d.TransactionTypeName) && d.TransactionTypeName.ToLower().Contains(term)) ||
                    (!string.IsNullOrEmpty(d.TransactionTypeCode) && d.TransactionTypeCode.ToLower().Contains(term))
                ).ToList();
            }

            return View(docs);
        }

        [HttpGet]
        public async Task<IActionResult> Create(int? transactionTypeId, TransactionModule module = TransactionModule.Sales)
        {
            int branchId = _currentUser.BranchId;
            if (branchId <= 0)
            {
                TempData["ErrorMessage"] = "يجب اختيار فرع صالح أولاً لإنشاء المستندات.";
                return RedirectToAction("Index", "Home");
            }

            var allowedTypes = await _ttService.GetAllowedTypesAsync(_currentUser.UserId ?? "", branchId, module);

            if (!allowedTypes.Any())
            {
                TempData["ErrorMessage"] = "ليس لديك صلاحية لإنشاء أي نوع من أنواع الحركات في هذا القسم.";
                return RedirectToAction(nameof(Index), new { module });
            }

            var selectedType = transactionTypeId.HasValue
                ? allowedTypes.FirstOrDefault(t => t.Id == transactionTypeId.Value)
                : allowedTypes.FirstOrDefault();

            if (selectedType == null)
            {
                selectedType = allowedTypes.First();
            }

            ViewBag.AllowedTypes = allowedTypes;
            ViewBag.SelectedType = selectedType;
            ViewBag.Module = module;
            ViewBag.CurrentBranchName = _currentUser.BranchName;
            ViewBag.CurrentBranchId = branchId;

            // Preload Customers, Vendors, Warehouses, Currencies (strictly filtered by active branch)
            ViewBag.Customers = await _context.Customers
                .Where(c => c.IsActive && c.BranchId == branchId)
                .OrderBy(c => c.Code)
                .Select(c => new { 
                    c.Id, 
                    c.Code, 
                    c.LocalizedName, 
                    c.CurrencyId,
                    CurrencyCode = c.Currency != null ? c.Currency.Code : null,
                    CurrencyName = c.Currency != null ? c.Currency.NameAr : null,
                    Phone = c.Phone ?? c.Mobile,
                    c.TaxNumber
                })
                .ToListAsync();

            ViewBag.Vendors = await _context.Vendors
                .Where(v => v.IsActive && v.BranchId == branchId)
                .OrderBy(v => v.Code)
                .Select(v => new { 
                    v.Id, 
                    v.Code, 
                    v.LocalizedName, 
                    v.CurrencyId,
                    CurrencyCode = v.Currency != null ? v.Currency.Code : null,
                    CurrencyName = v.Currency != null ? v.Currency.NameAr : null,
                    Phone = v.Phone ?? v.Mobile,
                    v.TaxNumber
                })
                .ToListAsync();

            ViewBag.Warehouses = await _context.Warehouses
                .Where(w => w.IsActive && w.BranchId == branchId)
                .OrderBy(w => w.Code)
                .Select(w => new { w.Id, w.Code, w.LocalizedName })
                .ToListAsync();

            ViewBag.Currencies = await _context.Currencies
                .Where(c => c.IsActive)
                .OrderBy(c => c.Code)
                .Select(c => new { c.Id, c.Code, c.LocalizedName, c.ExchangeRate })
                .ToListAsync();

            ViewBag.CashAccounts = await _context.GLAccounts
                .Where(a => a.IsActive && !a.IsGroup &&
                            (a.Code.StartsWith("120101") || a.Code.StartsWith("120102") || a.SubLedgerType == SubLedgerType.Bank) &&
                            (!a.BranchId.HasValue || a.BranchId == branchId))
                .OrderBy(a => a.Code)
                .Select(a => new { a.Id, a.Code, a.LocalizedName })
                .ToListAsync();

            ViewBag.AllowedAdjustments = await _adjustmentService.GetAllowedAdjustmentsByTransactionTypeAsync(selectedType.Id);

            return View(selectedType);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] DocumentHeaderDto model, [FromQuery] bool postImmediately = true)
        {
            if (model == null)
                return Json(new { success = false, message = "لم يتم استلام أي بيانات للمستند." });

            try
            {
                if (model.BranchId <= 0 && _currentUser.BranchId > 0)
                {
                    model.BranchId = _currentUser.BranchId;
                }

                var res = await _docService.CreateDocumentAsync(model, _currentUser.UserId ?? "", true);
                return Json(new
                {
                    success = res.Success,
                    message = res.Message,
                    documentId = res.DocumentId,
                    documentNumber = res.DocumentNumber
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"حدث خطأ غير متوقع أثناء معالجة المستند: {ex.InnerException?.Message ?? ex.Message}"
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var doc = await _docService.GetDocumentByIdAsync(id);
            if (doc == null) return NotFound();
            return View(doc);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var doc = await _docService.GetDocumentByIdAsync(id);
            if (doc == null) return NotFound();

            // 1. Fiscal period & year validation strictly on DocumentDate
            var (isAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(doc.DocumentDate);
            if (!isAllowed)
            {
                TempData["ErrorMessage"] = periodError;
                return RedirectToAction(nameof(Details), new { id });
            }

            // 2. Permission check
            bool canEdit = await _ttService.HasPermissionAsync(_currentUser.UserId ?? "", doc.TransactionTypeId, "edit");
            if (!canEdit)
            {
                TempData["ErrorMessage"] = "ليس لديك صلاحية لتعديل هذا المستند.";
                return RedirectToAction(nameof(Details), new { id });
            }

            int branchId = doc.BranchId;
            var targetTT = await _ttService.GetByIdAsync(doc.TransactionTypeId, _currentUser.UserId ?? "");
            if (targetTT == null) return NotFound();

            ViewBag.SelectedType = targetTT;
            ViewBag.Module = targetTT.Module;
            ViewBag.CurrentBranchName = doc.BranchName ?? _currentUser.BranchName;
            ViewBag.CurrentBranchId = branchId;
            ViewBag.ExistingDocument = doc;

            // Preload Customers, Vendors, Warehouses, Currencies, Cash Accounts, Adjustments
            ViewBag.Customers = await _context.Customers
                .Where(c => c.IsActive && (branchId == 0 || c.BranchId == branchId))
                .OrderBy(c => c.Code)
                .Select(c => new { 
                    c.Id, 
                    c.Code, 
                    c.LocalizedName, 
                    c.CurrencyId,
                    CurrencyCode = c.Currency != null ? c.Currency.Code : null,
                    CurrencyName = c.Currency != null ? c.Currency.NameAr : null,
                    Phone = c.Phone ?? c.Mobile,
                    c.TaxNumber
                })
                .ToListAsync();

            ViewBag.Vendors = await _context.Vendors
                .Where(v => v.IsActive && (branchId == 0 || v.BranchId == branchId))
                .OrderBy(v => v.Code)
                .Select(v => new { 
                    v.Id, 
                    v.Code, 
                    v.LocalizedName, 
                    v.CurrencyId,
                    CurrencyCode = v.Currency != null ? v.Currency.Code : null,
                    CurrencyName = v.Currency != null ? v.Currency.NameAr : null,
                    Phone = v.Phone ?? v.Mobile,
                    v.TaxNumber
                })
                .ToListAsync();

            ViewBag.Warehouses = await _context.Warehouses
                .Where(w => w.IsActive && w.BranchId == branchId)
                .OrderBy(w => w.Code)
                .Select(w => new { w.Id, w.Code, w.LocalizedName })
                .ToListAsync();

            ViewBag.Currencies = await _context.Currencies
                .Where(c => c.IsActive)
                .OrderBy(c => c.Code)
                .Select(c => new { c.Id, c.Code, c.LocalizedName, c.ExchangeRate })
                .ToListAsync();

            ViewBag.CashAccounts = await _context.GLAccounts
                .Where(a => a.IsActive && !a.IsGroup &&
                            (a.Code.StartsWith("120101") || a.Code.StartsWith("120102") || a.SubLedgerType == SubLedgerType.Bank) &&
                            (!a.BranchId.HasValue || a.BranchId == branchId))
                .OrderBy(a => a.Code)
                .Select(a => new { a.Id, a.Code, a.LocalizedName })
                .ToListAsync();

            ViewBag.AllowedAdjustments = await _adjustmentService.GetAllowedAdjustmentsByTransactionTypeAsync(targetTT.Id);

            return View(targetTT);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [FromBody] DocumentHeaderDto model)
        {
            if (model == null)
                return Json(new { success = false, message = "لم يتم استلام أي بيانات للمستند." });

            try
            {
                model.Id = id;
                if (model.BranchId <= 0 && _currentUser.BranchId > 0)
                {
                    model.BranchId = _currentUser.BranchId;
                }

                var res = await _docService.UpdateDocumentAsync(model, _currentUser.UserId ?? "");
                return Json(new
                {
                    success = res.Success,
                    message = res.Message,
                    documentId = id
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"حدث خطأ غير متوقع أثناء تعديل المستند: {ex.InnerException?.Message ?? ex.Message}"
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Post(int id)
        {
            var res = await _docService.PostDocumentAsync(id, _currentUser.UserId ?? "");
            return Json(new { success = res.Success, message = res.Message, journalEntryId = res.JournalEntryId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unpost(int id)
        {
            var res = await _docService.UnpostDocumentAsync(id, _currentUser.UserId ?? "");
            return Json(new { success = res.Success, message = res.Message });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var res = await _docService.DeleteDocumentAsync(id, _currentUser.UserId ?? "");
            return Json(new { success = res.Success, message = res.Message });
        }

        // ─── Document Conversion APIs ──────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Convert(int? targetTransactionTypeId, int? sourceId)
        {
            int branchId = _currentUser.BranchId;

            if ((!targetTransactionTypeId.HasValue || targetTransactionTypeId.Value <= 0) && sourceId.HasValue && sourceId.Value > 0)
            {
                var doc = await _context.DocumentHeaders.AsNoTracking().FirstOrDefaultAsync(d => d.Id == sourceId.Value);
                if (doc != null)
                {
                    var targets = await _conversionService.GetAllowedTargetTypesAsync(doc.TransactionTypeId);
                    if (targets.Any())
                    {
                        targetTransactionTypeId = targets.First().Id;
                    }
                }
            }

            if (!targetTransactionTypeId.HasValue || targetTransactionTypeId.Value <= 0)
            {
                TempData["ErrorMessage"] = "يرجى تحديد نوع المستند المستهدف للتحويل.";
                return RedirectToAction(nameof(Index));
            }

            var targetTT = await _ttService.GetByIdAsync(targetTransactionTypeId.Value, _currentUser.UserId ?? "");
            if (targetTT == null) return NotFound();

            ViewBag.TargetType = targetTT;
            ViewBag.PreSelectedSourceId = sourceId;
            ViewBag.Warehouses = await _context.Warehouses
                .Where(w => w.IsActive && w.BranchId == branchId)
                .OrderBy(w => w.Code)
                .Select(w => new { w.Id, w.Code, w.LocalizedName })
                .ToListAsync();
            var sources = await _conversionService.GetAvailableSourceDocumentsAsync(targetTransactionTypeId.Value, branchId);
            return View(sources);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllowedTargetsApi(int sourceDocumentId)
        {
            var doc = await _context.DocumentHeaders.AsNoTracking().FirstOrDefaultAsync(d => d.Id == sourceDocumentId);
            if (doc == null) return Json(new { success = false, message = "المستند غير موجود." });

            var targets = await _conversionService.GetAllowedTargetTypesAsync(doc.TransactionTypeId);
            return Json(new { success = true, targets });
        }

        [HttpGet]
        public async Task<IActionResult> GetAvailableSourcesApi(int targetTransactionTypeId, int? partyId)
        {
            int branchId = _currentUser.BranchId;
            var sources = await _conversionService.GetAvailableSourceDocumentsAsync(targetTransactionTypeId, branchId, partyId);
            return Json(new { success = true, data = sources });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Convert([FromBody] ConvertDocumentRequestDto request)
        {
            int branchId = _currentUser.BranchId;
            var res = await _conversionService.ConvertDocumentsAsync(request, _currentUser.UserId ?? "", branchId);
            return Json(new
            {
                success = res.Success,
                message = res.Message,
                newDocumentId = res.NewDocumentId,
                documentNumber = res.DocumentNumber
            });
        }

        // ─── Serial Number Lookups ───────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetAvailableSerialsApi(int itemId, int warehouseId)
        {
            int branchId = _currentUser.BranchId;
            var serials = await _serialService.GetAvailableSerialsAsync(itemId, warehouseId, branchId);
            return Json(new { success = true, data = serials });
        }

        [HttpGet]
        public async Task<IActionResult> GetAvailableSerialsPagedApi(
            int itemId,
            int warehouseId,
            string? search = null,
            int page = 1,
            int pageSize = 50,
            int? excludeDocumentId = null)
        {
            int branchId = _currentUser.BranchId;
            var paged = await _serialService.GetAvailableSerialsPagedAsync(new AvailableSerialsQueryDto
            {
                ItemId = itemId,
                WarehouseId = warehouseId,
                BranchId = branchId,
                Search = search,
                PageNumber = page,
                PageSize = pageSize,
                ExcludeDocumentId = excludeDocumentId
            });
            return Json(new { success = true, data = paged });
        }

        [HttpPost]
        public async Task<IActionResult> ValidateSerialsBatchApi([FromBody] SerialBatchValidationRequestDto req)
        {
            req ??= new SerialBatchValidationRequestDto();
            if (req.BranchId <= 0)
            {
                req.BranchId = _currentUser.BranchId;
            }
            var res = await _serialService.ValidateBatchForReceiveAsync(req);
            return Json(new { success = true, data = res });
        }

        [HttpPost]
        public async Task<IActionResult> ValidateSerialForIssueApi([FromBody] SerialValidationRequestDto req)
        {
            int branchId = _currentUser.BranchId;
            var val = await _serialService.ValidateSerialForIssueAsync(req.ItemId, req.WarehouseId, branchId, req.SerialNumber);
            return Json(new { success = val.IsValid, message = val.Message, serialId = val.SerialId });
        }

        // ─── Document Adjustments API ─────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetAllowedAdjustmentsApi(int transactionTypeId)
        {
            var adjustments = await _adjustmentService.GetAllowedAdjustmentsByTransactionTypeAsync(transactionTypeId);
            return Json(new { success = true, data = adjustments });
        }

        // ─── Inventory Availability API ────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> CheckAvailabilityApi(int itemId, int? warehouseId, int? unitId = null, decimal quantity = 1m, int? excludeDocumentId = null)
        {
            int branchId = _currentUser.BranchId;
            var avail = await _availabilityService.GetAvailabilityAsync(
                itemId, branchId, warehouseId, unitId, quantity, excludeDocumentId);
            return Json(new { success = true, data = avail });
        }
    }
}
