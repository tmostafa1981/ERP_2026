using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Web.Authorization;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class DocumentAdjustmentsController : Controller
    {
        private readonly IDocumentAdjustmentService _adjustmentService;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public DocumentAdjustmentsController(
            IDocumentAdjustmentService adjustmentService,
            ErpDbContext context,
            ICurrentUserContext currentUser)
        {
            _adjustmentService = adjustmentService;
            _context = context;
            _currentUser = currentUser;
        }

        [HttpGet]
        [HasPermission("Inventory.DocumentAdjustments.View,Sales.SalesInvoice.View,Purchases.PurchaseInvoice.View")]
        public async Task<IActionResult> Index()
        {
            var adjustments = await _adjustmentService.GetAllAdjustmentsAsync();
            var transactionTypes = await _context.TransactionTypes
                .Where(t => t.IsActive)
                .OrderBy(t => t.Module)
                .ThenBy(t => t.Code)
                .Select(t => new AdjustmentTransactionTypeLookupDto
                {
                    Id = t.Id,
                    Code = t.Code,
                    NameAr = t.NameAr,
                    NameEn = t.NameEn,
                    Module = t.Module,
                    ModuleName = t.Module == TransactionModule.Sales ? "مبيعات" : (t.Module == TransactionModule.Purchase ? "مشتريات" : "أخرى")
                })
                .ToListAsync();

            var glAccounts = await _context.GLAccounts
                .Where(a => a.IsActive && !a.IsGroup)
                .OrderBy(a => a.Code)
                .Select(a => new AdjustmentGLAccountLookupDto
                {
                    Id = a.Id,
                    Code = a.Code,
                    LocalizedName = a.LocalizedName,
                    AccountCategory = a.AccountCategory
                })
                .ToListAsync();

            ViewBag.TransactionTypes = transactionTypes;
            ViewBag.GLAccounts = glAccounts;

            return View(adjustments);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Inventory.DocumentAdjustments.Add,Sales.SalesInvoice.Edit,Purchases.PurchaseInvoice.Edit")]
        public async Task<IActionResult> Create(CreateDocumentAdjustmentDto dto)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "يرجى التأكد من ملء جميع الحقول الإلزامية بشكل صحيح.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await _adjustmentService.CreateAdjustmentAsync(dto);
                TempData["SuccessMessage"] = "تم إضافة المصروف / الخصم بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Inventory.DocumentAdjustments.Add,Sales.SalesInvoice.Edit,Purchases.PurchaseInvoice.Edit")]
        public async Task<IActionResult> Edit(CreateDocumentAdjustmentDto dto)
        {
            if (!dto.Id.HasValue || dto.Id.Value <= 0)
            {
                TempData["ErrorMessage"] = "معرف المصروف / الخصم غير صحيح.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await _adjustmentService.UpdateAdjustmentAsync(dto);
                TempData["SuccessMessage"] = "تم تحديث بيانات المصروف / الخصم بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Inventory.DocumentAdjustments.Add,Sales.SalesInvoice.Edit,Purchases.PurchaseInvoice.Edit")]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var adj = await _context.DocumentAdjustments.FindAsync(id);
            if (adj == null) return NotFound();

            adj.IsActive = !adj.IsActive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = adj.IsActive ? "تم تفعيل المصروف / الخصم." : "تم تعطيل المصروف / الخصم.";
            return RedirectToAction(nameof(Index));
        }

        // ─── Transaction Type Configuration APIs ─────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetConfig(int transactionTypeId)
        {
            var config = await _adjustmentService.GetTransactionTypeAdjustmentConfigAsync(transactionTypeId);
            return Json(new { success = true, data = config });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Inventory.DocumentAdjustments.Add,Sales.SalesInvoice.Edit,Purchases.PurchaseInvoice.Edit")]
        public async Task<IActionResult> SaveConfig([FromBody] TransactionTypeAdjustmentConfigDto dto)
        {
            if (dto == null || dto.TransactionTypeId <= 0)
                return Json(new { success = false, message = "بيانات التكوين غير صحيحة." });

            try
            {
                await _adjustmentService.SaveTransactionTypeAdjustmentConfigAsync(dto);
                return Json(new { success = true, message = "تم حفظ إعدادات المصاريف والخصومات لنوع الحركة بنجاح." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAllowed(int transactionTypeId)
        {
            var items = await _adjustmentService.GetAllowedAdjustmentsByTransactionTypeAsync(transactionTypeId);
            return Json(new { success = true, data = items });
        }
    }
}
