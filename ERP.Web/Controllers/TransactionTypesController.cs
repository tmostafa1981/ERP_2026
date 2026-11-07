using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using ERP.Infrastructure.Data;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class TransactionTypesController : Controller
    {
        private readonly ITransactionTypeService _ttService;
        private readonly UserManager<ERP.Infrastructure.Identity.ApplicationUser> _userManager;
        private readonly ICurrentUserContext _currentUser;
        private readonly ErpDbContext _context;

        public TransactionTypesController(
            ITransactionTypeService ttService,
            UserManager<ERP.Infrastructure.Identity.ApplicationUser> userManager,
            ICurrentUserContext currentUser,
            ErpDbContext context)
        {
            _ttService = ttService;
            _userManager = userManager;
            _currentUser = currentUser;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(TransactionModule? module)
        {
            var types = await _ttService.GetAllConfiguredTypesAsync(module);
            ViewBag.SelectedModule = module;
            ViewBag.Warehouses = await _context.Warehouses
                .Where(w => w.IsActive)
                .OrderBy(w => w.Code)
                .Select(w => new { w.Id, w.Code, w.LocalizedName })
                .ToListAsync();
            ViewBag.GLAccounts = await _context.GLAccounts
                .Where(a => a.IsActive && !a.IsGroup)
                .OrderBy(a => a.Code)
                .Select(a => new { a.Id, a.Code, a.LocalizedName, a.AccountCategory })
                .ToListAsync();
            ViewBag.Customers = await _context.Customers
                .Where(c => c.IsActive)
                .OrderBy(c => c.Code)
                .Select(c => new { c.Id, c.Code, c.LocalizedName })
                .ToListAsync();
            ViewBag.Vendors = await _context.Vendors
                .Where(v => v.IsActive)
                .OrderBy(v => v.Code)
                .Select(v => new { v.Id, v.Code, v.LocalizedName })
                .ToListAsync();
            ViewBag.CashAccounts = await _context.GLAccounts
                .Where(a => a.IsActive && !a.IsGroup && a.AccountCategory == AccountCategory.Assets)
                .OrderBy(a => a.Code)
                .Select(a => new { a.Id, a.Code, a.LocalizedName })
                .ToListAsync();
            return View(types);
        }

        [HttpGet]
        public async Task<IActionResult> Permissions(string? userId, TransactionModule? module)
        {
            var users = await _userManager.Users
                .OrderBy(u => u.UserName)
                .Select(u => new { u.Id, u.UserName, u.FullName })
                .ToListAsync();

            ViewBag.Users = users;

            if (string.IsNullOrEmpty(userId) && users.Any())
            {
                userId = users.First().Id;
            }

            var matrix = !string.IsNullOrEmpty(userId)
                ? await _ttService.GetUserPermissionMatrixAsync(userId, module)
                : new UserTransactionTypePermissionMatrixDto();

            ViewBag.SelectedUserId = userId;
            ViewBag.SelectedModule = module;

            return View(matrix);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePermissions([FromBody] UserTransactionTypePermissionMatrixDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.UserId))
                return Json(new { success = false, message = "بيانات الصلاحيات غير مكتملة." });

            await _ttService.SaveUserPermissionsAsync(dto.UserId, dto.Permissions);
            return Json(new { success = true, message = "تم حفظ صلاحيات أنواع الحركات بنجاح." });
        }

        [HttpGet]
        public async Task<IActionResult> GetAllowedTypesApi(TransactionModule? module)
        {
            var types = await _ttService.GetAllowedTypesAsync(_currentUser.UserId ?? "", _currentUser.BranchId, module);
            return Json(new { success = true, data = types });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TransactionType model)
        {
            if (string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.NameAr))
            {
                TempData["ErrorMessage"] = "الكود والاسم العربي مطلوبان.";
                return RedirectToAction(nameof(Index), new { module = model.Module });
            }

            bool isCash = Request.Form["IsCashAllowed"].Count > 0 && Request.Form["IsCashAllowed"].ToString().Contains("true", StringComparison.OrdinalIgnoreCase);
            bool isCredit = Request.Form["IsCreditAllowed"].Count > 0 && Request.Form["IsCreditAllowed"].ToString().Contains("true", StringComparison.OrdinalIgnoreCase);

            if (!isCash && !isCredit)
            {
                TempData["ErrorMessage"] = "يجب السماح بالتعامل النقدي أو الآجل على الأقل (لا يمكن إلغاء الاثنين معاً).";
                return RedirectToAction(nameof(Index), new { module = model.Module });
            }

            model.Code = model.Code.Trim().ToUpper();
            model.NameAr = model.NameAr.Trim();
            model.NameEn = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();
            model.IsCashAllowed = isCash;
            model.IsCreditAllowed = isCredit;
            model.AutoPost = Request.Form["AutoPost"].Count > 0 && Request.Form["AutoPost"].ToString().Contains("true", StringComparison.OrdinalIgnoreCase);
            model.ShowLineNotes = Request.Form["ShowLineNotes"].Count > 0 && Request.Form["ShowLineNotes"].ToString().Contains("true", StringComparison.OrdinalIgnoreCase);
            model.IsActive = Request.Form["IsActive"].Count > 0 && Request.Form["IsActive"].ToString().Contains("true", StringComparison.OrdinalIgnoreCase);
            model.CreatedAt = DateTime.UtcNow;

            await _ttService.CreateTransactionTypeAsync(model);
            TempData["SuccessMessage"] = "تم إضافة نوع الحركة بنجاح.";
            return RedirectToAction(nameof(Index), new { module = model.Module });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(TransactionType model)
        {
            var existing = await _context.TransactionTypes.FindAsync(model.Id);
            if (existing == null) return NotFound();

            bool isCash = Request.Form["IsCashAllowed"].Count > 0 && Request.Form["IsCashAllowed"].ToString().Contains("true", StringComparison.OrdinalIgnoreCase);
            bool isCredit = Request.Form["IsCreditAllowed"].Count > 0 && Request.Form["IsCreditAllowed"].ToString().Contains("true", StringComparison.OrdinalIgnoreCase);

            if (!isCash && !isCredit)
            {
                TempData["ErrorMessage"] = "يجب السماح بالتعامل النقدي أو الآجل على الأقل (لا يمكن إلغاء الاثنين معاً).";
                return RedirectToAction(nameof(Index), new { module = model.Module });
            }

            existing.NameAr = model.NameAr.Trim();
            existing.NameEn = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();
            existing.Module = model.Module;
            existing.BaseNature = model.BaseNature;
            existing.InventoryEffect = model.InventoryEffect;
            existing.AccountingEffect = model.AccountingEffect;
            existing.IsCashAllowed = isCash;
            existing.IsCreditAllowed = isCredit;
            existing.AutoPost = Request.Form["AutoPost"].Count > 0 && Request.Form["AutoPost"].ToString().Contains("true", StringComparison.OrdinalIgnoreCase);
            existing.ShowLineNotes = Request.Form["ShowLineNotes"].Count > 0 && Request.Form["ShowLineNotes"].ToString().Contains("true", StringComparison.OrdinalIgnoreCase);
            existing.CodeSequencePrefix = model.CodeSequencePrefix;
            existing.DefaultWarehouseId = model.DefaultWarehouseId;
            existing.DefaultDebitAccountId = model.DefaultDebitAccountId;
            existing.DefaultCreditAccountId = model.DefaultCreditAccountId;
            existing.DefaultTaxAccountId = model.DefaultTaxAccountId;
            existing.DefaultDiscountAccountId = model.DefaultDiscountAccountId;
            existing.DefaultCustomerId = model.DefaultCustomerId;
            existing.DefaultVendorId = model.DefaultVendorId;
            existing.DefaultCashBankAccountId = model.DefaultCashBankAccountId;
            existing.IsActive = Request.Form["IsActive"].Count > 0 && Request.Form["IsActive"].ToString().Contains("true", StringComparison.OrdinalIgnoreCase);

            await _ttService.UpdateTransactionTypeAsync(existing);
            TempData["SuccessMessage"] = "تم تحديث نوع الحركة بنجاح.";
            return RedirectToAction(nameof(Index), new { module = model.Module });
        }
    }
}
