using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace ERP.Infrastructure.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

        public string? UserId => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        public int BranchId
        {
            get
            {
                var val = User?.FindFirst("BranchId")?.Value;
                return int.TryParse(val, out var id) ? id : 0;
            }
        }

        public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

        public bool IsSuperAdmin
        {
            get
            {
                // When there is no HTTP context or user is not authenticated (e.g. background jobs, migrations, seeding),
                // do not restrict branch filtering.
                if (!IsAuthenticated)
                    return true;

                // Check Admin roles or explicit flags
                if (User?.IsInRole("Admin") == true || User?.IsInRole("System Administrator") == true)
                    return true;

                if (User?.FindFirst("HasAllBranches")?.Value == "True" || User?.FindFirst("IsSuperAdmin")?.Value == "True")
                    return true;

                return false;
            }
        }
    }
}
