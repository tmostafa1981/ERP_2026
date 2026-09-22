using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Security.Claims;

namespace ERP.Infrastructure.Services
{
    public class CurrentUserContext : ICurrentUserContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

        public string? UserId => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        public string? Username => User?.Identity?.Name;
        public string? EmployeeCode => User?.FindFirst("EmployeeCode")?.Value ?? Username;

        public int CompanyId
        {
            get
            {
                var val = User?.FindFirst("CompanyId")?.Value;
                return int.TryParse(val, out var id) ? id : 0;
            }
        }

        public string CompanyName => User?.FindFirst("CompanyName")?.Value ?? string.Empty;

        public int BranchId
        {
            get
            {
                var val = User?.FindFirst("BranchId")?.Value;
                return int.TryParse(val, out var id) ? id : 0;
            }
        }

        public string BranchName => User?.FindFirst("BranchName")?.Value ?? (BranchId > 0 ? $"Branch {BranchId}" : "N/A");

        public int FiscalYearId
        {
            get
            {
                var val = User?.FindFirst("FiscalYearId")?.Value;
                return int.TryParse(val, out var id) ? id : 0;
            }
        }

        public string FiscalYearName => User?.FindFirst("FiscalYearName")?.Value ?? (FiscalYearId > 0 ? $"FY {FiscalYearId}" : "N/A");

        public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

        public bool HasAllBranches => User?.FindFirst("HasAllBranches")?.Value == "True" || User?.IsInRole("Admin") == true || User?.IsInRole("System Administrator") == true;

        public bool HasPermission(string permissionCode)
        {
            if (!IsAuthenticated) return false;
            if (User?.IsInRole("Admin") == true || User?.IsInRole("System Administrator") == true) return true;

            return User?.HasClaim("Permission", permissionCode) ?? false;
        }

        public bool HasPermission(string screen, string transactionType, string action)
        {
            if (!IsAuthenticated) return false;
            if (User?.IsInRole("Admin") == true || User?.IsInRole("System Administrator") == true) return true;

            var permClaim = $"{screen}.{transactionType}.{action}";
            if (User?.HasClaim("Permission", permClaim) == true) return true;

            var legacyClaim = $"{screen}:{transactionType}:{action}";
            return User?.HasClaim("Permission", legacyClaim) ?? false;
        }

        public bool CanAccessCompany(int companyId)
        {
            if (!IsAuthenticated) return false;
            if (HasAllBranches) return true;

            var allowedCompanies = User?.FindAll("AllowedCompanyId").Select(c => c.Value);
            return allowedCompanies != null && allowedCompanies.Contains(companyId.ToString());
        }

        public bool CanAccessBranch(int branchId)
        {
            if (!IsAuthenticated) return false;
            if (HasAllBranches) return true;

            var allowedBranches = User?.FindAll("AllowedBranchId").Select(c => c.Value);
            if (allowedBranches != null && allowedBranches.Contains(branchId.ToString())) return true;

            return BranchId == branchId;
        }
    }
}
