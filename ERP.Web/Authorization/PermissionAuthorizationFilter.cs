using System;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
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
            // 1. Check Authentication
            if (!_userContext.IsAuthenticated)
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return Task.CompletedTask;
            }

            // 2. Validate Context (Company, Branch, FiscalYear)
            if (_userContext.CompanyId <= 0 || _userContext.BranchId <= 0 || _userContext.FiscalYearId <= 0)
            {
                // Super admins may bypass if logging in first time without context, but normally required
                if (!_userContext.HasAllBranches)
                {
                    context.Result = new RedirectToActionResult("Login", "Account", new { message = "SessionContextInvalid" });
                    return Task.CompletedTask;
                }
            }

            // 3. Check Specific Permission
            if (!_userContext.HasPermission(_permissionCode))
            {
                context.Result = new RedirectToActionResult("AccessDenied", "Account", new { permission = _permissionCode });
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }
    }
}
