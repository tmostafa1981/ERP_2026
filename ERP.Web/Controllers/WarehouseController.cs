using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using ERP.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class WarehouseController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;
        private readonly ICodeSequenceService _sequenceService;

        private const string SC_Code   = "WH001";
        private const string SC_Name   = "Warehouses";
        private const string SC_Module = "MasterData";
        private const string EntityName = "Warehouse";

        public WarehouseController(
            ErpDbContext context,
            ICurrentUserContext currentUser,
            IAuditService audit,
            ICodeSequenceService sequenceService)
        {
            _context         = context;
            _currentUser     = currentUser;
            _audit           = audit;
            _sequenceService = sequenceService;
        }

        [HttpGet]
        [HasPermission("MasterData.Warehouses.View")]
        public async Task<IActionResult> Index(string? searchString, int? branchId, int page = 1)
        {
            int pageSize = 15;

            var branchesQuery = _currentUser.HasAllBranches
                ? _context.Branches.Where(b => b.IsActive)
                : _context.Branches.Where(b => b.IsActive && b.CompanyId == _currentUser.CompanyId);

            var allBranches = await branchesQuery.OrderBy(b => b.Code).ToListAsync();
            if (!_currentUser.HasAllBranches)
            {
                allBranches = allBranches.Where(b => _currentUser.CanAccessBranch(b.Id)).ToList();
            }
            ViewBag.Branches = allBranches;

            int currentBranchId = _currentUser.BranchId;
            int? effectiveBranchId = currentBranchId > 0 ? currentBranchId : branchId;

            var query = _context.Warehouses
                .Include(w => w.Branch)
                .AsNoTracking();

            if (currentBranchId > 0)
            {
                query = query.Where(w => w.BranchId == currentBranchId);
            }
            else if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                if (!_currentUser.CanAccessBranch(effectiveBranchId.Value))
                    return Forbid();

                query = query.Where(w => w.BranchId == effectiveBranchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                query = query.Where(w => w.BranchId == _currentUser.BranchId);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(w =>
                    w.Code.Contains(searchString) ||
                    w.NameAr.Contains(searchString) ||
                    w.NameEn.Contains(searchString));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(w => w.BranchId).ThenBy(w => w.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            int? defaultBranchId = effectiveBranchId.HasValue && effectiveBranchId.Value > 0
                ? effectiveBranchId.Value
                : (allBranches.FirstOrDefault()?.Id ?? _currentUser.BranchId);

            var seqConfig = defaultBranchId.HasValue
                ? await _sequenceService.GetConfigAsync(EntityName, defaultBranchId.Value)
                : await _sequenceService.GetConfigAsync(EntityName);

            ViewBag.SearchString    = searchString;
            ViewBag.BranchId        = effectiveBranchId;
            ViewBag.CurrentPage     = page;
            ViewBag.TotalPages      = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount      = totalCount;
            ViewBag.NextCode        = seqConfig.PreviewCode;
            ViewBag.IsAutoCode      = seqConfig.IsAuto;
            ViewBag.IsManualAllowed = seqConfig.IsManualAllowed;
            ViewBag.DefaultBranchId = defaultBranchId;

            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Warehouses.Add")]
        public async Task<IActionResult> Create(Warehouse model, bool? isManualCode = null)
        {
            if (model.BranchId <= 0 || !_currentUser.CanAccessBranch(model.BranchId))
                ModelState.AddModelError("BranchId", "يرجى تحديد فرع صحيح تملك صلاحية الوصول إليه.");

            // Branch-based auto-number logic
            bool shouldAutoCode = isManualCode.HasValue
                ? (!isManualCode.Value && await _sequenceService.IsAutoEnabledAsync(EntityName, model.BranchId))
                : (string.IsNullOrWhiteSpace(model.Code) || (await _sequenceService.IsAutoEnabledAsync(EntityName, model.BranchId) && !await _sequenceService.IsManualAllowedAsync(EntityName, model.BranchId)));

            if (model.BranchId > 0 && shouldAutoCode)
            {
                model.Code = await _sequenceService.GetNextCodeAsync(EntityName, model.BranchId);
            }

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود المستودع مطلوب.");
            else if (await _context.Warehouses.AnyAsync(w => w.BranchId == model.BranchId && w.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "كود المستودع مسجل مسبقاً في هذا الفرع.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "الاسم العربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (ModelState.IsValid)
            {
                model.Code      = model.Code.Trim();
                model.NameAr    = model.NameAr?.Trim() ?? string.Empty;
                model.NameEn    = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();
                model.CreatedAt = DateTime.UtcNow;

                _context.Warehouses.Add(model);
                await _context.SaveChangesAsync();

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Warehouse",
                    entityId:   model.Id.ToString(),
                    newValues: new
                    {
                        model.Id,
                        model.BranchId,
                        model.Code,
                        model.NameAr,
                        model.NameEn,
                        model.InventoryAccountId,
                        model.IsActive
                    });

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم حفظ بيانات المستودع بنجاح.", Url.Action(nameof(Index), new { branchId = model.BranchId }));

                TempData["SuccessMessage"] = "Warehouse created successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure("لا يمكن حفظ المستودع. يرجى مراجعة الأخطاء الموضحة وتصحيحها.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId > 0 ? (int?)model.BranchId : null });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Warehouses.Edit")]
        public async Task<IActionResult> Edit(Warehouse model)
        {
            var entity = await _context.Warehouses.FindAsync(model.Id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId) || !_currentUser.CanAccessBranch(model.BranchId))
                return Forbid();

            if (model.BranchId <= 0 || !_currentUser.CanAccessBranch(model.BranchId))
                ModelState.AddModelError("BranchId", "يرجى تحديد فرع صحيح تملك صلاحية الوصول إليه.");

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود المستودع مطلوب.");
            else if (await _context.Warehouses.AnyAsync(w => w.BranchId == model.BranchId && w.Code == model.Code.Trim() && w.Id != model.Id))
                ModelState.AddModelError("Code", "كود المستودع مسجل مسبقاً في هذا الفرع.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "الاسم العربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (ModelState.IsValid)
            {
                var oldValues = new
                {
                    entity.Id,
                    entity.BranchId,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.InventoryAccountId,
                    entity.IsActive,
                    OldCode = entity.Code
                };

                entity.BranchId           = model.BranchId;
                entity.Code               = model.Code.Trim();
                entity.NameAr             = model.NameAr?.Trim() ?? string.Empty;
                entity.NameEn             = string.IsNullOrWhiteSpace(model.NameEn) ? entity.NameAr : model.NameEn.Trim();
                entity.InventoryAccountId = model.InventoryAccountId;
                entity.IsActive           = model.IsActive;
                entity.UpdatedAt          = DateTime.UtcNow;

                if (model.RowVersion != null && model.RowVersion.Length > 0)
                {
                    _context.Entry(entity).Property(e => e.RowVersion).OriginalValue = model.RowVersion;
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    var conflictMsg = "تم تعديل السجل بواسطة مستخدم آخر أثناء عملك عليه. يرجى إعادة تحميل البيانات قبل الحفظ.";
                    if (Request.IsAjaxRequest())
                        return this.JsonFailure(conflictMsg);

                    TempData["ErrorMessage"] = conflictMsg;
                    return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
                }
                catch (Exception ex)
                {
                    var msg = ex.InnerException?.Message ?? ex.Message;
                    if (Request.IsAjaxRequest())
                        return this.JsonFailure("حدث خطأ أثناء حفظ تعديلات المستودع: " + msg);

                    TempData["ErrorMessage"] = "حدث خطأ أثناء حفظ تعديلات المستودع: " + msg;
                    return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
                }

                var newValues = new
                {
                    entity.Id,
                    entity.BranchId,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.InventoryAccountId,
                    entity.IsActive,
                    NewCode = entity.Code
                };

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues:  newValues);

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم تحديث بيانات المستودع بنجاح.", Url.Action(nameof(Index), new { branchId = entity.BranchId }));

                TempData["SuccessMessage"] = "Warehouse updated successfully.";
                return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure("لا يمكن حفظ التعديلات. يرجى مراجعة الأخطاء الموضحة وتصحيحها.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Warehouses.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Warehouses.FindAsync(id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            // Delete Protection: Check if warehouse is referenced in any database foreign keys
            if (await IsRecordReferencedAsync("Warehouses", id))
            {
                TempData["ErrorMessage"] = "لا يمكن حذف المستودع لوجود أرصدة أو حركات مخزنية أو سجلات مرتبطة به.";
                return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
            }

            var oldValues = new
            {
                entity.Id,
                entity.BranchId,
                entity.Code,
                entity.NameAr,
                entity.NameEn,
                entity.InventoryAccountId,
                entity.IsActive
            };

            try
            {
                _context.Warehouses.Remove(entity);
                await _context.SaveChangesAsync();

                await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   id.ToString(),
                    oldValues:  oldValues);

                TempData["SuccessMessage"] = "Warehouse deleted successfully.";
            }
            catch (DbUpdateException)
            {
                TempData["ErrorMessage"] = "لا يمكن حذف المستودع لوجود حركات أو سجلات مرتبطة به.";
            }

            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        private async Task<bool> IsRecordReferencedAsync(string tableName, int recordId)
        {
            try
            {
                if (_context.Database.IsSqlServer())
                {
                    var sql = @"
                        DECLARE @RefFound BIT = 0;
                        DECLARE @Sql NVARCHAR(MAX);
                        DECLARE cur LOCAL FAST_FORWARD FOR
                        SELECT 'IF EXISTS (SELECT 1 FROM [' + s.name + '].[' + t.name + '] WHERE [' + c.name + '] = ' + CAST(@RecId AS NVARCHAR(20)) + ') SET @Found = 1;'
                        FROM sys.foreign_key_columns fkc
                        JOIN sys.tables t ON fkc.parent_object_id = t.object_id
                        JOIN sys.schemas s ON t.schema_id = s.schema_id
                        JOIN sys.columns c ON fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
                        JOIN sys.tables rt ON fkc.referenced_object_id = rt.object_id
                        WHERE rt.name = @TblName;

                        OPEN cur;
                        FETCH NEXT FROM cur INTO @Sql;
                        WHILE @@FETCH_STATUS = 0 AND @RefFound = 0
                        BEGIN
                            EXEC sp_executesql @Sql, N'@Found BIT OUTPUT', @Found = @RefFound OUTPUT;
                            IF @RefFound = 1 BREAK;
                            FETCH NEXT FROM cur INTO @Sql;
                        END
                        CLOSE cur;
                        DEALLOCATE cur;
                        SELECT @RefFound;";

                    using var cmd = _context.Database.GetDbConnection().CreateCommand();
                    cmd.CommandText = sql;
                    var paramTbl = cmd.CreateParameter();
                    paramTbl.ParameterName = "@TblName";
                    paramTbl.Value = tableName;
                    cmd.Parameters.Add(paramTbl);

                    var paramId = cmd.CreateParameter();
                    paramId.ParameterName = "@RecId";
                    paramId.Value = recordId;
                    cmd.Parameters.Add(paramId);

                    if (cmd.Connection!.State != System.Data.ConnectionState.Open)
                        await cmd.Connection.OpenAsync();

                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value && Convert.ToBoolean(result))
                        return true;
                }
            }
            catch
            {
                // Fall back to DbUpdateException catch
            }

            return false;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Warehouses.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.Warehouses.FindAsync(id);
            if (entity == null) return NotFound();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            var oldValues = new { entity.Id, entity.BranchId, entity.Code, entity.IsActive };

            entity.IsActive  = !entity.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                entityName: EntityName,
                entityId:   id.ToString(),
                oldValues:  oldValues,
                newValues: new { entity.Id, entity.BranchId, entity.Code, entity.IsActive });

            TempData["SuccessMessage"] = $"Warehouse status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        [HttpGet]
        [HasPermission("MasterData.Warehouses.View")]
        public async Task<IActionResult> GetByBranch(int branchId)
        {
            if (!_currentUser.CanAccessBranch(branchId))
                return Forbid();

            var warehouses = await _context.Warehouses
                .Where(w => w.BranchId == branchId && w.IsActive)
                .OrderBy(w => w.Code)
                .Select(w => new
                {
                    w.Id,
                    w.Code,
                    Name = w.LocalizedName
                })
                .ToListAsync();

            return Json(warehouses);
        }
    }
}
