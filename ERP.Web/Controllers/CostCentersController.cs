using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class CostCentersController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code = "COST_CTR";
        private const string SC_Name = "CostCenters";
        private const string SC_Module = "Accounting";

        public CostCentersController(
            ErpDbContext context,
            ICurrentUserContext currentUser,
            IAuditService audit)
        {
            _context = context;
            _currentUser = currentUser;
            _audit = audit;
        }

        // ─── GET: Index (Tree View) ──────────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.CostCenters.View")]
        public async Task<IActionResult> Index()
        {
            var centers = await _context.CostCenters
                .Include(c => c.Parent)
                .Include(c => c.Children)
                .OrderBy(c => c.Code)
                .AsNoTracking()
                .ToListAsync();

            ViewBag.ParentCenters = centers.Where(c => c.IsActive).ToList();
            return View(centers);
        }

        // ─── GET: GetTreeData (JSON for jstree) ───────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.CostCenters.View")]
        public async Task<IActionResult> GetTreeData()
        {
            var centers = await _context.CostCenters
                .Include(c => c.JournalEntryLines)
                .AsNoTracking()
                .OrderBy(c => c.Code)
                .ToListAsync();

            var treeNodes = centers.Select(c => new
            {
                id = c.Id.ToString(),
                parent = c.ParentId.HasValue ? c.ParentId.Value.ToString() : "#",
                text = $"{c.Code} - {c.LocalizedName}" + (c.IsTransactional ? "" : " [رئيسي]"),
                icon = c.IsTransactional ? "fa-solid fa-bullseye text-success" : "fa-solid fa-diagram-project text-primary",
                state = new { opened = true },
                data = new
                {
                    id = c.Id,
                    code = c.Code,
                    nameAr = c.NameAr,
                    nameEn = c.NameEn,
                    parentId = c.ParentId,
                    isTransactional = c.IsTransactional,
                    isActive = c.IsActive,
                    linesCount = c.JournalEntryLines.Count
                }
            });

            return Json(treeNodes);
        }

        // ─── GET: GetById ────────────────────────────────────────────────────
        [HttpGet]
        [HasPermission("Accounting.CostCenters.View")]
        public async Task<IActionResult> GetById(int id)
        {
            var center = await _context.CostCenters
                .Include(c => c.Parent)
                .Include(c => c.JournalEntryLines)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            if (center == null)
                return NotFound(new { success = false, message = "مركز التكلفة غير موجود." });

            return Json(new
            {
                success = true,
                data = new
                {
                    center.Id,
                    center.Code,
                    center.NameAr,
                    center.NameEn,
                    center.ParentId,
                    parentName = center.Parent != null ? $"{center.Parent.Code} - {center.Parent.LocalizedName}" : "مركز رئيسي (مستوى أول)",
                    center.IsTransactional,
                    center.IsActive,
                    linesCount = center.JournalEntryLines.Count,
                    rowVersion = center.RowVersion != null ? Convert.ToBase64String(center.RowVersion) : null
                }
            });
        }

        // ─── POST: Create ────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.CostCenters.Add")]
        public async Task<IActionResult> Create([FromBody] CostCenter model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                return Json(new { success = false, message = "كود مركز التكلفة مطلوب." });

            if (string.IsNullOrWhiteSpace(model.NameAr) && string.IsNullOrWhiteSpace(model.NameEn))
                return Json(new { success = false, message = "اسم مركز التكلفة مطلوب." });

            model.Code = model.Code.Trim();
            model.NameAr = model.NameAr?.Trim() ?? string.Empty;
            model.NameEn = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();

            bool codeExists = await _context.CostCenters.AnyAsync(c => c.Code == model.Code);
            if (codeExists)
                return Json(new { success = false, message = $"كود مركز التكلفة '{model.Code}' مستخدم بالفعل." });

            if (model.ParentId.HasValue && model.ParentId.Value <= 0)
                model.ParentId = null;

            model.CreatedAt = DateTime.UtcNow;

            _context.CostCenters.Add(model);
            await _context.SaveChangesAsync();

            await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "CostCenter",
                entityId: model.Id.ToString(),
                newValues: new { model.Id, model.Code, model.NameAr, model.NameEn, model.ParentId, model.IsTransactional });

            return Json(new { success = true, message = "تم إنشاء مركز التكلفة بنجاح.", id = model.Id });
        }

        // ─── POST: Edit ──────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.CostCenters.Edit")]
        public async Task<IActionResult> Edit([FromBody] CostCenter model)
        {
            var center = await _context.CostCenters.FindAsync(model.Id);
            if (center == null)
                return Json(new { success = false, message = "مركز التكلفة غير موجود." });

            if (string.IsNullOrWhiteSpace(model.Code))
                return Json(new { success = false, message = "كود مركز التكلفة مطلوب." });

            model.Code = model.Code.Trim();
            model.NameAr = model.NameAr?.Trim() ?? string.Empty;
            model.NameEn = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();

            bool codeExists = await _context.CostCenters.AnyAsync(c => c.Code == model.Code && c.Id != model.Id);
            if (codeExists)
                return Json(new { success = false, message = $"كود مركز التكلفة '{model.Code}' مستخدم بالفعل في مركز آخر." });

            // Circular Dependency Protection!
            if (model.ParentId.HasValue && model.ParentId.Value > 0)
            {
                if (model.ParentId.Value == model.Id)
                    return Json(new { success = false, message = "لا يمكن تعيين مركز التكلفة ليكون أباً لنفسه." });

                bool isCircular = await IsDescendantAsync(model.ParentId.Value, model.Id);
                if (isCircular)
                    return Json(new { success = false, message = "خطأ منطقي (Circular Dependency): لا يمكن جعل المركز ابناً لأحد المراكز التابعة له." });

                center.ParentId = model.ParentId.Value;
            }
            else
            {
                center.ParentId = null;
            }

            if (model.RowVersion != null && model.RowVersion.Length > 0)
            {
                _context.Entry(center).Property(p => p.RowVersion).OriginalValue = model.RowVersion;
            }

            var oldValues = new { center.Code, center.NameAr, center.NameEn, center.ParentId, center.IsTransactional, center.IsActive };

            center.Code = model.Code;
            center.NameAr = model.NameAr;
            center.NameEn = model.NameEn;
            center.IsTransactional = model.IsTransactional;
            center.IsActive = model.IsActive;
            center.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Json(new { success = false, message = "تم تعديل السجل بواسطة مستخدم آخر أثناء عملك عليه. يرجى إعادة تحميل البيانات قبل الحفظ." });
            }

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "CostCenter",
                entityId: center.Id.ToString(),
                oldValues: oldValues,
                newValues: new { center.Code, center.NameAr, center.NameEn, center.ParentId, center.IsTransactional, center.IsActive });

            return Json(new { success = true, message = "تم تعديل مركز التكلفة بنجاح." });
        }

        // ─── POST: Delete ────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("Accounting.CostCenters.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var center = await _context.CostCenters
                .Include(c => c.Children)
                .Include(c => c.JournalEntryLines)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (center == null)
                return Json(new { success = false, message = "مركز التكلفة غير موجود." });

            if (center.Children.Any())
                return Json(new { success = false, message = "لا يمكن حذف المركز لاحتوائه على مراكز فرعية تابعة. يرجى نقلها أو حذفها أولاً." });

            if (center.JournalEntryLines.Any())
                return Json(new { success = false, message = $"لا يمكن حذف المركز لارتباطه بـ {center.JournalEntryLines.Count} حركة محاسبية بالقيود اليومية." });

            var oldValues = new { center.Id, center.Code, center.NameAr, center.NameEn };

            _context.CostCenters.Remove(center);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "CostCenter",
                entityId: id.ToString(),
                oldValues: oldValues);

            return Json(new { success = true, message = "تم حذف مركز التكلفة بنجاح." });
        }

        // ─── Helper: Circular Dependency Checker ─────────────────────────────
        private async Task<bool> IsDescendantAsync(int targetId, int potentialAncestorId)
        {
            int? currentParentId = targetId;
            var visited = new HashSet<int>();

            while (currentParentId.HasValue)
            {
                if (currentParentId.Value == potentialAncestorId)
                    return true;

                if (!visited.Add(currentParentId.Value))
                    break;

                var parent = await _context.CostCenters
                    .AsNoTracking()
                    .Where(c => c.Id == currentParentId.Value)
                    .Select(c => c.ParentId)
                    .FirstOrDefaultAsync();

                currentParentId = parent;
            }

            return false;
        }
    }
}
