using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Web.Authorization
{
    public class PermissionAuthorizationFilter : IAsyncAuthorizationFilter
    {
        private readonly string _permissionCode;
        private readonly ICurrentUserContext _userContext;

        public PermissionAuthorizationFilter(string permissionCode, ICurrentUserContext userContext)
        {
            _permissionCode = permissionCode;
            _userContext = userContext;
        }

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var isAjax = context.HttpContext.Request.IsAjaxRequest();

            // 1. Check Authentication
            if (!_userContext.IsAuthenticated)
            {
                if (isAjax)
                {
                    context.Result = new JsonResult(new { success = false, message = "انتهت الجلسة. يرجى تسجيل الدخول مرة أخرى." }) { StatusCode = 401 };
                    return Task.CompletedTask;
                }
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return Task.CompletedTask;
            }

            // 2. Validate Context (Company, Branch, FiscalYear)
            if (_userContext.CompanyId <= 0 || _userContext.BranchId <= 0 || _userContext.FiscalYearId <= 0)
            {
                // Super admins may bypass if logging in first time without context, but normally required
                if (!_userContext.HasAllBranches)
                {
                    if (isAjax)
                    {
                        context.Result = new JsonResult(new { success = false, message = "سياق الجلسة غير صالح (الشركة أو الفرع أو السنة المالية)." }) { StatusCode = 400 };
                        return Task.CompletedTask;
                    }
                    context.Result = new RedirectToActionResult("Login", "Account", new { message = "SessionContextInvalid" });
                    return Task.CompletedTask;
                }
            }

            // 3. Check Specific Permission(s) - Supports comma-separated OR list
            var requiredPerms = _permissionCode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool hasAccess = requiredPerms.Any(p => _userContext.HasPermission(p));

            if (!hasAccess)
            {
                if (isAjax)
                {
                    context.Result = new JsonResult(new
                    {
                        success = false,
                        message = $"غير مصرح لك بتنفيذ هذا الإجراء. ليس لديك صلاحية ({_permissionCode})."
                    }) { StatusCode = 403 };
                    return Task.CompletedTask;
                }
                context.Result = new RedirectToActionResult("AccessDenied", "Account", new { permission = _permissionCode });
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }
    }
}
