using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Identity;
using ERP.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class PaymentMethodPermissionsController : Controller
    {
        private readonly IPaymentMethodPermissionService _pmPermService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code = "PMP001";
        private const string SC_Name = "PaymentMethodPermissions";
        private const string SC_Module = "Administration";

        public PaymentMethodPermissionsController(
            IPaymentMethodPermissionService pmPermService,
            UserManager<ApplicationUser> userManager,
            ICurrentUserContext currentUser,
            IAuditService audit)
        {
            _pmPermService = pmPermService;
            _userManager = userManager;
            _currentUser = currentUser;
            _audit = audit;
        }

        [HttpGet]
        [HasPermission("Administration.PaymentMethodPermissions.View")]
        public async Task<IActionResult> Index(string? userId, int? voucherType)
        {
            var users = await _userManager.Users
                .OrderBy(u => u.UserName)
                .Select(u => new { u.Id, u.UserName, u.FullName, u.IsActive })
                .ToListAsync();

            ViewBag.Users = users;

            if (string.IsNullOrEmpty(userId) && users.Any())
            {
                userId = users.First().Id;
            }

            VoucherType? vType = voucherType.HasValue ? (VoucherType)voucherType.Value : null;
            var matrix = !string.IsNullOrEmpty(userId)
                ? await _pmPermService.GetUserMatrixAsync(userId, vType)
                : new UserPaymentMethodMatrixDto();

            ViewBag.SelectedUserId = userId;
            ViewBag.SelectedVoucherType = voucherType;

            return View(matrix);
        }

        [HttpGet]
        [HasPermission("Administration.PaymentMethodPermissions.View")]
        public async Task<IActionResult> GetMatrix(string userId, int? voucherType)
        {
            if (string.IsNullOrEmpty(userId))
                return Json(new { success = false, message = "معرف المستخدم مطلوب." });

            VoucherType? vType = voucherType.HasValue ? (VoucherType)voucherType.Value : null;
            var matrix = await _pmPermService.GetUserMatrixAsync(userId, vType);

            return Json(new { success = true, data = matrix });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Administration.PaymentMethodPermissions.Edit")]
        public async Task<IActionResult> Save([FromBody] UserPaymentMethodMatrixDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.UserId))
                return Json(new { success = false, message = "بيانات الصلاحيات غير مكتملة." });

            var user = await _userManager.FindByIdAsync(dto.UserId);
            if (user == null)
                return Json(new { success = false, message = "المستخدم غير موجود." });

            try
            {
                await _pmPermService.SavePermissionsAsync(dto.UserId, dto.VoucherType, dto.Permissions);

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "UserPaymentMethodPermission",
                    entityId: dto.UserId,
                    oldValues: null,
                    newValues: new
                    {
                        dto.UserId,
                        UserName = user.UserName,
                        PermissionsCount = dto.Permissions.Count
                    });

                return Json(new { success = true, message = $"تم حفظ صلاحيات وسائل الدفع للمستخدم ({user.UserName}) بنجاح." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"حدث خطأ أثناء الحفظ: {ex.Message}" });
            }
        }
    }
}
