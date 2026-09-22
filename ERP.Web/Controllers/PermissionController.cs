using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class PermissionController : Controller
    {
        private readonly ErpDbContext _context;

        public PermissionController(ErpDbContext context)
        {
            _context = context;
        }

        // ─── GET: Categorized Permissions List ───────────────────────────────────
        [HasPermission("Administration.Permissions.View")]
        public async Task<IActionResult> Index()
        {
            var permissions = await _context.Permissions
                .AsNoTracking()
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.Module)
                .ThenBy(p => p.Screen)
                .ToListAsync();

            var groupedModules = permissions.GroupBy(p => p.Module).ToList();
            return View(groupedModules);
        }
    }
}
