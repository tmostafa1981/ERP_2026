using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace ERP.Web.Middleware
{
    /// <summary>
    /// Enforces that any authenticated ERP user has an active Branch, Company, and Fiscal Year context.
    /// If an authenticated session lacks any of these essential context claims (e.g. stale cookie or
    /// session corruption), the user is signed out and redirected to the Login screen to select their branch.
    /// </summary>
    public class ErpSessionValidationMiddleware
    {
        private readonly RequestDelegate _next;

        public ErpSessionValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var path = context.Request.Path.Value ?? string.Empty;

                // Skip validation for auth-related endpoints and static resources
                if (IsExcludedPath(path))
                {
                    await _next(context);
                    return;
                }

                var branchIdClaim = context.User.FindFirst("BranchId")?.Value;
                var companyIdClaim = context.User.FindFirst("CompanyId")?.Value;
                var fiscalYearIdClaim = context.User.FindFirst("FiscalYearId")?.Value;

                bool hasValidBranch = int.TryParse(branchIdClaim, out var bId) && bId > 0;
                bool hasValidCompany = int.TryParse(companyIdClaim, out var cId) && cId > 0;
                bool hasValidFiscalYear = int.TryParse(fiscalYearIdClaim, out var fyId) && fyId > 0;

                if (!hasValidBranch || !hasValidCompany || !hasValidFiscalYear)
                {
                    // Incomplete / invalid ERP session context. Force sign out and redirect to login.
                    await context.SignOutAsync(IdentityConstants.ApplicationScheme);

                    if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                        path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("{\"error\":\"SessionExpired\",\"message\":\"سياق الجلسة غير صالح (الفرع أو الشركة أو السنة المالية غير محددة). يرجى إعادة تسجيل الدخول.\"}");
                        return;
                    }

                    context.Response.Redirect("/Account/Login");
                    return;
                }
            }

            await _next(context);
        }

        private static bool IsExcludedPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            return path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/Account/GetCompanies", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/Account/GetBranches", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/Account/SetLanguage", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/Account/AccessDenied", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/images/", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase);
        }
    }
}
