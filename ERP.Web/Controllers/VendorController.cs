using ClosedXML.Excel;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using ERP.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class VendorController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;
        private readonly ICodeSequenceService _sequenceService;
        private readonly IPartyGroupService _partyGroupService;

        private const string SC_Code   = "VEN001";
        private const string SC_Name   = "Vendors";
        private const string SC_Module = "MasterData";
        private const string EntityName = "Vendor";

        public VendorController(
            ErpDbContext context,
            ICurrentUserContext currentUser,
            IAuditService audit,
            ICodeSequenceService sequenceService,
            IPartyGroupService partyGroupService)
        {
            _context           = context;
            _currentUser       = currentUser;
            _audit             = audit;
            _sequenceService   = sequenceService;
            _partyGroupService = partyGroupService;
        }

        [HttpGet]
        [HasPermission("MasterData.Vendors.View")]
        public async Task<IActionResult> Index(string searchString, int? branchId, int? categoryId, int page = 1)
        {
            const int pageSize = 15;
            page = Math.Max(1, page);

            var allBranches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.NameAr)
                .ToListAsync();

            var accessibleBranches = allBranches
                .Where(b => _currentUser.CanAccessBranch(b.Id))
                .ToList();

            ViewBag.Branches = accessibleBranches;

            int currentBranchId = _currentUser.BranchId;
            int? effectiveBranchId = currentBranchId > 0 ? currentBranchId : branchId;
            if (effectiveBranchId.HasValue && !_currentUser.CanAccessBranch(effectiveBranchId.Value))
            {
                effectiveBranchId = null;
            }

            var catQuery = _context.VendorCategories
                .Include(c => c.ControlAccount)
                .Where(c => c.IsActive);
            if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
                catQuery = catQuery.Where(c => c.BranchId == effectiveBranchId.Value);

            ViewBag.Categories   = await catQuery.OrderBy(c => c.IsDefault ? 0 : 1).ThenBy(c => c.Code).ToListAsync();
            ViewBag.Governorates = await _context.Governorates.Where(g => g.IsActive).OrderBy(g => g.NameAr).ToListAsync();
            ViewBag.Currencies   = await _context.Currencies.Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();

            var query = _context.Vendors
                .Include(v => v.Branch)
                .Include(v => v.VendorCategory)
                    .ThenInclude(vc => vc.ControlAccount)
                .Include(v => v.Governorate)
                .Include(v => v.City)
                .Include(v => v.Currency)
                .Include(v => v.PartyGroup)
                .Include(v => v.FinancialAccount)
                .AsQueryable();

            if (currentBranchId > 0)
            {
                query = query.Where(v => v.BranchId == currentBranchId);
            }
            else if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                query = query.Where(v => v.BranchId == effectiveBranchId.Value);
            }
            else
            {
                var accessibleBranchIds = accessibleBranches.Select(b => b.Id).ToList();
                query = query.Where(v => accessibleBranchIds.Contains(v.BranchId));
            }

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                query = query.Where(v => v.VendorCategoryId == categoryId.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();

                var matchingGroupIds = await _context.PartyGroups
                    .AsNoTracking()
                    .Where(pg => pg.Code.Contains(searchString) || pg.NameAr.Contains(searchString) || pg.NameEn.Contains(searchString))
                    .Select(pg => pg.Id)
                    .Union(_context.Customers.AsNoTracking()
                        .Where(c => c.PartyGroupId != null && (c.Code.Contains(searchString) || c.NameAr.Contains(searchString) || c.NameEn.Contains(searchString)))
                        .Select(c => c.PartyGroupId!.Value))
                    .Union(_context.Vendors.AsNoTracking()
                        .Where(v => v.PartyGroupId != null && (v.Code.Contains(searchString) || v.NameAr.Contains(searchString) || v.NameEn.Contains(searchString)))
                        .Select(v => v.PartyGroupId!.Value))
                    .Distinct()
                    .ToListAsync();

                query = query.Where(v =>
                    v.Code.Contains(searchString) ||
                    v.NameAr.Contains(searchString) ||
                    v.NameEn.Contains(searchString) ||
                    (v.TaxNumber != null && v.TaxNumber.Contains(searchString)) ||
                    (v.Phone != null && v.Phone.Contains(searchString)) ||
                    (v.Mobile != null && v.Mobile.Contains(searchString)) ||
                    (v.Mobile2 != null && v.Mobile2.Contains(searchString)) ||
                    (v.Email != null && v.Email.Contains(searchString)) ||
                    (v.PartyGroupId != null && matchingGroupIds.Contains(v.PartyGroupId.Value)));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(v => v.BranchId).ThenBy(v => v.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            int? defaultBranchId = effectiveBranchId.HasValue && effectiveBranchId.Value > 0
                ? effectiveBranchId.Value
                : (accessibleBranches.FirstOrDefault()?.Id ?? _currentUser.BranchId);

            var seqConfig = defaultBranchId.HasValue
                ? await _sequenceService.GetConfigAsync(EntityName, defaultBranchId.Value)
                : await _sequenceService.GetConfigAsync(EntityName);

            ViewBag.SearchString    = searchString;
            ViewBag.BranchId        = effectiveBranchId;
            ViewBag.CategoryId      = categoryId;
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
        [HasPermission("MasterData.Vendors.Add")]
        public async Task<IActionResult> Create(Vendor model, bool? isManualCode = null)
        {
            if (model.BranchId > 0 && !_currentUser.CanAccessBranch(model.BranchId))
            {
                ModelState.AddModelError("BranchId", "يرجى تحديد فرع صحيح تملك صلاحية الوصول إليه.");
            }
            else if (_currentUser.BranchId > 0 && model.BranchId > 0 && model.BranchId != _currentUser.BranchId)
            {
                ModelState.AddModelError("BranchId", "غير مصرح بإنشاء مورد في فرع مختلف عن فرع الجلسة الحالي.");
            }
            else if (_currentUser.BranchId > 0)
            {
                model.BranchId = _currentUser.BranchId;
            }
            else if (model.BranchId <= 0)
            {
                ModelState.AddModelError("BranchId", "يرجى تحديد فرع صحيح تملك صلاحية الوصول إليه.");
            }

            // Branch-based auto-number logic
            bool isAuto = await _sequenceService.IsAutoEnabledAsync(EntityName, model.BranchId);
            bool isManualAllowed = await _sequenceService.IsManualAllowedAsync(EntityName, model.BranchId);
            bool shouldGenerateAuto = (isManualCode == false && isAuto) ||
                                     (isManualCode == null && string.IsNullOrWhiteSpace(model.Code) && isAuto);

            if (model.BranchId > 0 && shouldGenerateAuto)
            {
                model.Code = await _sequenceService.GetNextCodeAsync(EntityName, model.BranchId);
            }

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود المورد مطلوب.");
            else if (await _context.Vendors.AnyAsync(v => v.BranchId == model.BranchId && v.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "كود المورد مسجل مسبقاً في هذا الفرع.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "الاسم العربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (model.VendorCategoryId <= 0 || !await _context.VendorCategories.AnyAsync(vc => vc.Id == model.VendorCategoryId && vc.BranchId == model.BranchId))
                ModelState.AddModelError("VendorCategoryId", "يرجى اختيار تصنيف موردين تابع لنفس الفرع.");

            if (model.CityId.HasValue && model.CityId.Value > 0)
            {
                var city = await _context.Cities.FindAsync(model.CityId.Value);
                if (city == null || (model.GovernorateId.HasValue && city.GovernorateId != model.GovernorateId.Value))
                    ModelState.AddModelError("CityId", "المدينة المحددة لا تنتمي إلى المحافظة المختارة.");
            }

            // Financial Account validation
            if (!model.FinancialAccountId.HasValue || model.FinancialAccountId.Value <= 0)
            {
                var cat = await _context.VendorCategories.FindAsync(model.VendorCategoryId);
                if (cat?.ControlAccountId != null)
                {
                    model.FinancialAccountId = cat.ControlAccountId;
                }
            }

            if (model.FinancialAccountId.HasValue && model.FinancialAccountId.Value > 0)
            {
                var acc = await _context.GLAccounts.FindAsync(model.FinancialAccountId.Value);
                if (acc == null)
                    ModelState.AddModelError("FinancialAccountId", "الحساب المالي المحدد غير موجود في دليل الحسابات.");
                else if (acc.IsGroup)
                    ModelState.AddModelError("FinancialAccountId", "لا يمكن ربط المورد بحساب رئيسي (تجميعي)، يرجى اختيار حساب فرعي يقبل الحركات.");
            }

            if (ModelState.IsValid)
            {
                model.Code               = model.Code.Trim();
                model.NameAr             = model.NameAr?.Trim() ?? string.Empty;
                model.NameEn             = string.IsNullOrWhiteSpace(model.NameEn) ? model.NameAr : model.NameEn.Trim();
                model.Address            = model.Address?.Trim();
                model.TaxNumber          = model.TaxNumber?.Trim();
                model.CommercialRegister = model.CommercialRegister?.Trim();
                model.TaxPayerType       = model.TaxPayerType?.Trim();
                model.Phone              = model.Phone?.Trim();
                model.Mobile             = model.Mobile?.Trim();
                model.Mobile2            = model.Mobile2?.Trim();
                model.Email              = model.Email?.Trim();
                model.Notes              = model.Notes?.Trim();
                model.CreatedAt          = DateTime.UtcNow;

                _context.Vendors.Add(model);

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    var msg = ex.InnerException?.Message ?? ex.Message;
                    if (Request.IsAjaxRequest())
                        return this.JsonFailure("حدث خطأ أثناء حفظ بيانات المورد: " + msg);

                    TempData["ErrorMessage"] = "حدث خطأ أثناء حفظ بيانات المورد: " + msg;
                    return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
                }

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Vendor",
                    entityId:   model.Id.ToString(),
                    newValues: new
                    {
                        model.Id,
                        model.BranchId,
                        model.Code,
                        model.NameAr,
                        model.NameEn,
                        model.VendorCategoryId,
                        model.Address,
                        model.GovernorateId,
                        model.CityId,
                        model.TaxNumber,
                        model.CommercialRegister,
                        model.TaxPayerType,
                        model.CurrencyId,
                        model.FinancialAccountId,
                        model.Phone,
                        model.Mobile,
                        model.Mobile2,
                        model.Email,
                        model.Notes,
                        model.IsActive
                    });

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم حفظ بيانات المورد بنجاح.", Url.Action(nameof(Index), new { branchId = model.BranchId }));

                TempData["SuccessMessage"] = "Vendor created successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure("لا يمكن حفظ المورد. يرجى مراجعة الأخطاء الموضحة وتصحيحها.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Vendors.Edit")]
        public async Task<IActionResult> Edit(Vendor model)
        {
            var entity = await _context.Vendors.FindAsync(model.Id);
            if (entity == null) return NotFound();

            if (_currentUser.BranchId > 0 && (entity.BranchId != _currentUser.BranchId || model.BranchId != _currentUser.BranchId))
                return Forbid();

            if (!_currentUser.CanAccessBranch(entity.BranchId) || !_currentUser.CanAccessBranch(model.BranchId))
                return Forbid();

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود المورد مطلوب.");
            else if (await _context.Vendors.AnyAsync(v => v.BranchId == model.BranchId && v.Code == model.Code.Trim() && v.Id != model.Id))
                ModelState.AddModelError("Code", "كود المورد مسجل مسبقاً في هذا الفرع.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "الاسم العربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (model.VendorCategoryId <= 0 || !await _context.VendorCategories.AnyAsync(vc => vc.Id == model.VendorCategoryId && vc.BranchId == model.BranchId))
                ModelState.AddModelError("VendorCategoryId", "يرجى اختيار تصنيف موردين تابع لنفس الفرع.");

            if (model.CityId.HasValue && model.CityId.Value > 0)
            {
                var city = await _context.Cities.FindAsync(model.CityId.Value);
                if (city == null || (model.GovernorateId.HasValue && city.GovernorateId != model.GovernorateId.Value))
                    ModelState.AddModelError("CityId", "المدينة المحددة لا تنتمي إلى المحافظة المختارة.");
            }

            // Validate no duplicate currency within the same PartyGroup
            var (currValid, currErr) = await _partyGroupService.ValidatePartyCurrencyChangeAsync("Vendor", entity.Id, entity.PartyGroupId, model.CurrencyId);
            if (!currValid)
            {
                ModelState.AddModelError("CurrencyId", currErr);
            }

            // Financial Account validation
            if (!model.FinancialAccountId.HasValue || model.FinancialAccountId.Value <= 0)
            {
                var cat = await _context.VendorCategories.FindAsync(model.VendorCategoryId);
                if (cat?.ControlAccountId != null)
                {
                    model.FinancialAccountId = cat.ControlAccountId;
                }
            }

            if (model.FinancialAccountId.HasValue && model.FinancialAccountId.Value > 0)
            {
                var acc = await _context.GLAccounts.FindAsync(model.FinancialAccountId.Value);
                if (acc == null)
                    ModelState.AddModelError("FinancialAccountId", "الحساب المالي المحدد غير موجود في دليل الحسابات.");
                else if (acc.IsGroup)
                    ModelState.AddModelError("FinancialAccountId", "لا يمكن ربط المورد بحساب رئيسي (تجميعي)، يرجى اختيار حساب فرعي يقبل الحركات.");
            }

            if (ModelState.IsValid)
            {
                var oldValues = new
                {
                    entity.Id,
                    entity.BranchId,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.VendorCategoryId,
                    entity.Address,
                    entity.GovernorateId,
                    entity.CityId,
                    entity.TaxNumber,
                    entity.CommercialRegister,
                    entity.TaxPayerType,
                    entity.CurrencyId,
                    entity.FinancialAccountId,
                    entity.Phone,
                    entity.Mobile,
                    entity.Mobile2,
                    entity.Email,
                    entity.Notes,
                    entity.IsActive,
                    OldCode = entity.Code
                };

                entity.BranchId            = model.BranchId;
                entity.Code                = model.Code.Trim();
                entity.NameAr              = model.NameAr?.Trim() ?? string.Empty;
                entity.NameEn              = string.IsNullOrWhiteSpace(model.NameEn) ? entity.NameAr : model.NameEn.Trim();
                entity.VendorCategoryId    = model.VendorCategoryId;
                entity.Address             = model.Address?.Trim();
                entity.GovernorateId       = model.GovernorateId;
                entity.CityId              = model.CityId;
                entity.TaxNumber           = model.TaxNumber?.Trim();
                entity.CommercialRegister  = model.CommercialRegister?.Trim();
                entity.TaxPayerType        = model.TaxPayerType?.Trim();
                entity.CurrencyId          = model.CurrencyId;
                entity.FinancialAccountId  = model.FinancialAccountId;
                entity.Phone               = model.Phone?.Trim();
                entity.Mobile              = model.Mobile?.Trim();
                entity.Mobile2             = model.Mobile2?.Trim();
                entity.Email               = model.Email?.Trim();
                entity.Notes               = model.Notes?.Trim();
                entity.IsActive            = model.IsActive;
                entity.UpdatedAt           = DateTime.UtcNow;

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
                    return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
                }
                catch (Exception ex)
                {
                    var msg = ex.InnerException?.Message ?? ex.Message;
                    if (Request.IsAjaxRequest())
                        return this.JsonFailure("حدث خطأ أثناء حفظ تعديلات المورد: " + msg);

                    TempData["ErrorMessage"] = "حدث خطأ أثناء حفظ تعديلات المورد: " + msg;
                    return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
                }

                var newValues = new
                {
                    entity.Id,
                    entity.BranchId,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.VendorCategoryId,
                    entity.Address,
                    entity.GovernorateId,
                    entity.CityId,
                    entity.TaxNumber,
                    entity.CommercialRegister,
                    entity.TaxPayerType,
                    entity.CurrencyId,
                    entity.FinancialAccountId,
                    entity.Phone,
                    entity.Mobile,
                    entity.Mobile2,
                    entity.Email,
                    entity.Notes,
                    entity.IsActive,
                    NewCode = entity.Code
                };

                await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   entity.Id.ToString(),
                    oldValues:  oldValues,
                    newValues:  newValues);

                if (Request.IsAjaxRequest())
                    return this.JsonSuccess("تم تحديث بيانات المورد بنجاح.", Url.Action(nameof(Index), new { branchId = model.BranchId }));

                TempData["SuccessMessage"] = "Vendor updated successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure("لا يمكن حفظ التعديلات. يرجى مراجعة الأخطاء الموضحة وتصحيحها.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Vendors.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Vendors.FindAsync(id);
            if (entity == null) return NotFound();

            if (_currentUser.BranchId > 0 && entity.BranchId != _currentUser.BranchId)
                return Forbid();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            // Delete Protection: Check if vendor is referenced in any database foreign keys
            if (await IsRecordReferencedAsync("Vendors", id))
            {
                TempData["ErrorMessage"] = "لا يمكن حذف المورد لوجود حركات مالية أو فواتير مشتريات مرتبطة به.";
                return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
            }

            var oldValues = new
            {
                entity.Id,
                entity.BranchId,
                entity.Code,
                entity.NameAr,
                entity.NameEn,
                entity.VendorCategoryId,
                entity.Address,
                entity.GovernorateId,
                entity.CityId,
                entity.TaxNumber,
                entity.CommercialRegister,
                entity.TaxPayerType,
                entity.CurrencyId,
                entity.Phone,
                entity.Mobile,
                entity.Mobile2,
                entity.Email,
                entity.Notes,
                entity.IsActive
            };

            try
            {
                _context.Vendors.Remove(entity);
                await _context.SaveChangesAsync();

                await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   id.ToString(),
                    oldValues:  oldValues);

                TempData["SuccessMessage"] = "Vendor deleted successfully.";
            }
            catch (DbUpdateException)
            {
                TempData["ErrorMessage"] = "لا يمكن حذف المورد لوجود حركات وسجلات مرتبطة به.";
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
                        DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
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
        [HasPermission("MasterData.Vendors.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.Vendors.FindAsync(id);
            if (entity == null) return NotFound();

            if (_currentUser.BranchId > 0 && entity.BranchId != _currentUser.BranchId)
                return Forbid();

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

            TempData["SuccessMessage"] = $"Vendor status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        // ─── GET: Download Excel Template for Vendors ─────────────────────────
        [HttpGet]
        [HasPermission("MasterData.Vendors.View")]
        public async Task<IActionResult> DownloadTemplate()
        {
            var branches = await _context.Branches.IgnoreQueryFilters().Where(b => b.IsActive).OrderBy(b => b.Code).AsNoTracking().ToListAsync();
            var currencies = await _context.Currencies.IgnoreQueryFilters().Where(c => c.IsActive).OrderBy(c => c.Code).AsNoTracking().ToListAsync();

            using var workbook = new XLWorkbook();

            // Sheet 1: الموردين
            var ws = workbook.Worksheets.Add("الموردين");
            ws.RightToLeft = true;
            ws.ShowGridLines = true;

            string[] headers = {
                "كود الفرع",
                "كود المورد",
                "اسم المورد (عربي)",
                "اسم المورد (إنجليزي)",
                "فئة المورد",
                "كود الحساب المالي",
                "كود العملة",
                "الرقم الضريبي",
                "السجل التجاري",
                "نوع الممول",
                "المحافظة",
                "المدينة",
                "العنوان",
                "الموبايل 1",
                "الموبايل 2",
                "الهاتف",
                "البريد الإلكتروني",
                "ملاحظات"
            };

            for (int col = 0; col < headers.Length; col++)
            {
                var cell = ws.Cell(1, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Font.FontSize = 11;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(27, 54, 93);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            }
            ws.Row(1).Height = 28;

            string defaultBranchCode = branches.FirstOrDefault(b => b.Id == _currentUser.BranchId)?.Code
                                    ?? branches.FirstOrDefault()?.Code ?? "B001";

            var samples = new[]
            {
                new[] { defaultBranchCode, "", "الشركة الدولية لتوريد الماكينات الصناعية", "International Industrial Machines Co.", "", "21010101001", "EGP", "410-520-630", "11223", "شركة", "القاهرة", "مصر الجديدة", "شارع جسر السويس - القاهرة", "01009988776", "01229988776", "0226345678", "supply@intl-machines.com", "مورد ماكينات محلي" },
                new[] { defaultBranchCode, "", "مؤسسة الأمل لقطع غيار ماكينات الخياطة", "Al-Amal Sewing Spare Parts Est.", "", "21010201001", "EGP", "510-620-730", "33445", "شركة", "القاهرة", "مدينة نصر", "شارع الرويعي - العتبة", "01119988776", "", "0225912345", "sales@alamal-parts.com", "مورد قطع غيار محلي" },
                new[] { defaultBranchCode, "", "شركة جاك العالمية للماكينات - الصين", "JACK Sewing Machine Co. Ltd", "", "21010301001", "USD", "", "", "أجنبي", "", "", "Taizhou, Zhejiang, China", "+8613800138000", "", "", "export@chinajack.com", "مورد خارجي بالدولار - ماكينات" },
                new[] { defaultBranchCode, "", "شركة ستروبل الألمانية للماكينات", "Strobel Spezialmaschinen GmbH", "", "21010501001", "EUR", "", "", "أجنبي", "", "", "Munich, Germany", "+4989123456", "", "", "info@strobel.de", "مورد خارجي باليورو - ماكينات" }
            };

            for (int r = 0; r < samples.Length; r++)
            {
                int rowIdx = r + 2;
                for (int c = 0; c < samples[r].Length; c++)
                {
                    var cell = ws.Cell(rowIdx, c + 1);
                    cell.Style.NumberFormat.Format = "@";
                    cell.SetValue(samples[r][c]);
                    cell.Style.Font.FontName = "Segoe UI";
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(220, 224, 230);
                }
                ws.Row(rowIdx).Height = 22;
            }

            // Dropdown validations
            ws.Range(2, 7, 1000, 7).CreateDataValidation().List("\"EGP,USD,EUR,SAR,AED,CNY,GBP\"", true);
            ws.Range(2, 10, 1000, 10).CreateDataValidation().List("\"شركة,فرد,هيئة حكومية,أجنبي\"", true);

            ws.Columns(1, headers.Length).AdjustToContents();
            ws.Column(3).Width = 34;
            ws.Column(4).Width = 30;
            ws.Column(13).Width = 30;
            ws.Column(18).Width = 25;

            // Sheet 2: تعليمات وإرشادات الاستيراد
            var wsHelp = workbook.Worksheets.Add("تعليمات وإرشادات الاستيراد");
            wsHelp.RightToLeft = true;
            wsHelp.ShowGridLines = true;
            wsHelp.Cell(1, 1).Value = "دليل وإرشادات ملء ملف استيراد الموردين (Vendors)";
            wsHelp.Range(1, 1, 1, 3).Merge();
            wsHelp.Row(1).Height = 30;
            wsHelp.Cell(1, 1).Style.Font.Bold = true;
            wsHelp.Cell(1, 1).Style.Font.FontSize = 14;
            wsHelp.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsHelp.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(27, 54, 93);
            wsHelp.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var instructions = new (string Col, string Field, string Desc)[]
            {
                ("A", "كود الفرع (اختياري)", "كود الفرع (مثل: B001, B002) أو اسم الفرع. إذا تُرك فارغاً يتم الاستيراد على الفرع الحالي للمستخدم."),
                ("B", "كود المورد (اختياري)", "يُترك فارغاً ليقوم النظام بتوليد كود تلقائي متسلسل للفرع، أو يُكتب الكود اليدوي بشرط عدم تكراره داخل نفس الفرع."),
                ("C", "اسم المورد عربي (إلزامي)", "الاسم التجاري أو الرسمي للمورد باللغة العربية."),
                ("D", "اسم المورد إنجليزي (اختياري)", "الاسم باللغة الإنجليزية (إذا تُرك فارغاً يتم نسخه تلقائياً من الاسم العربي)."),
                ("E", "فئة المورد (اختياري)", "كود أو اسم فئة الموردين. إذا تُرك فارغاً يتم ربط المورد تلقائياً بالفئة الافتراضية للفرع."),
                ("F", "كود الحساب المالي (اختياري)", "كود حساب المورد في شجرة الحسابات (مثل: 21010101001 الموردين المصري ماكينات، أو 21010301001 الموردين الدولار ماكينات)."),
                ("G", "كود العملة (اختياري)", "رمز العملة الافتراضية للمورد (EGP, USD, EUR, SAR, AED, CNY). افتراضياً EGP."),
                ("H", "الرقم الضريبي (اختياري)", "رقم التسجيل الضريبي للمورد (مثلاً: 410-520-630)."),
                ("I", "السجل التجاري (اختياري)", "رقم السجل التجاري للمورد."),
                ("J", "نوع الممول (اختياري)", "شركة، فرد، هيئة حكومية، أو أجنبي."),
                ("K", "المحافظة (اختياري)", "اسم أو كود المحافظة (للموردين المحليين)."),
                ("L", "المدينة (اختياري)", "اسم أو كود المدينة التابعة للمحافظة."),
                ("M", "العنوان (اختياري)", "العنوان التفصيلي للمورد."),
                ("N", "الموبايل 1 (اختياري)", "رقم الهاتف المحمول الأساسي."),
                ("O", "الموبايل 2 (اختياري)", "رقم الهاتف المحمول الإضافي."),
                ("P", "الهاتف (اختياري)", "رقم الهاتف الأرضي أو الدولي."),
                ("Q", "البريد الإلكتروني (اختياري)", "البريد الإلكتروني للمورد."),
                ("R", "ملاحظات (اختياري)", "أي ملاحظات إضافية خاصة بالمورد.")
            };

            wsHelp.Cell(3, 1).Value = "العمود";
            wsHelp.Cell(3, 2).Value = "اسم الحقل";
            wsHelp.Cell(3, 3).Value = "الشرح وقواعد الإدخال";
            wsHelp.Range(3, 1, 3, 3).Style.Font.Bold = true;
            wsHelp.Range(3, 1, 3, 3).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 235, 245);
            wsHelp.Row(3).Height = 24;

            for (int i = 0; i < instructions.Length; i++)
            {
                int r = i + 4;
                wsHelp.Cell(r, 1).SetValue(instructions[i].Col);
                wsHelp.Cell(r, 2).SetValue(instructions[i].Field);
                wsHelp.Cell(r, 3).SetValue(instructions[i].Desc);
                wsHelp.Row(r).Height = 22;
            }
            wsHelp.Column(1).Width = 10;
            wsHelp.Column(2).Width = 28;
            wsHelp.Column(3).Width = 75;

            // Sheet 3: الأكواد المرجعية
            var wsRef = workbook.Worksheets.Add("الأكواد المرجعية");
            wsRef.RightToLeft = true;
            wsRef.Cell(1, 1).Value = "كود الفرع";
            wsRef.Cell(1, 2).Value = "اسم الفرع";
            wsRef.Cell(1, 4).Value = "كود العملة";
            wsRef.Cell(1, 5).Value = "اسم العملة";
            wsRef.Cell(1, 7).Value = "كود حساب الموردين";
            wsRef.Cell(1, 8).Value = "اسم الحساب في شجرة الحسابات";
            wsRef.Range(1, 1, 1, 8).Style.Font.Bold = true;

            for (int i = 0; i < branches.Count; i++)
            {
                wsRef.Cell(i + 2, 1).SetValue(branches[i].Code);
                wsRef.Cell(i + 2, 2).SetValue(branches[i].NameAr ?? branches[i].Name);
            }
            for (int i = 0; i < currencies.Count; i++)
            {
                wsRef.Cell(i + 2, 4).SetValue(currencies[i].Code);
                wsRef.Cell(i + 2, 5).SetValue(currencies[i].NameAr);
            }
            var vendorGlRefs = new (string Code, string Name)[]
            {
                ("21010101001", "الموردين المصري ماكينات"),
                ("21010201001", "الموردين المصري قطع غيار"),
                ("21010301001", "الموردين الدولار ماكينات"),
                ("21010401001", "الموردين الدولار قطع غيار"),
                ("21010501001", "الموردين اليورو ماكينات"),
                ("21010601001", "الموردين اليورو قطع غيار")
            };
            for (int i = 0; i < vendorGlRefs.Length; i++)
            {
                wsRef.Cell(i + 2, 7).SetValue(vendorGlRefs[i].Code);
                wsRef.Cell(i + 2, 8).SetValue(vendorGlRefs[i].Name);
            }
            wsRef.Columns(1, 8).AdjustToContents();

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"نموذج_استيراد_الموردين_{DateTime.Now:yyyyMMdd}.xlsx");
        }

        // ─── POST: Preview Excel Import for Vendors ───────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Vendors.Add")]
        public async Task<IActionResult> PreviewExcelImport(IFormFile? excelFile)
        {
            if (excelFile == null || excelFile.Length == 0)
                return Json(new { success = false, message = "يرجى تحديد ملف Excel أو CSV للرفع (.xlsx / .csv)." });

            if (!excelFile.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !excelFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "صيغة الملف غير مدعومة. يجب أن يكون الملف بصيغة (.xlsx) أو (.csv)." });

            try
            {
                using var stream = excelFile.OpenReadStream();
                var preview = await ValidateVendorImportAsync(stream);
                return Json(new { success = true, data = preview });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "حدث خطأ أثناء قراءة وفحص ملف الموردين: " + ex.Message });
            }
        }

        // ─── POST: Confirm Excel Import for Vendors ───────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Vendors.Add")]
        public async Task<IActionResult> ConfirmExcelImport(IFormFile? excelFile, bool updateExisting = true)
        {
            if (excelFile == null || excelFile.Length == 0)
                return Json(new { success = false, message = "يرجى تحديد ملف Excel أو CSV للرفع (.xlsx / .csv)." });

            try
            {
                using var stream = excelFile.OpenReadStream();
                var preview = await ValidateVendorImportAsync(stream);

                if (preview.ErrorCount > 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"فشل الاستيراد: يوجد {preview.ErrorCount} سطر به أخطاء في الملف. يرجى معالجتها أولاً.",
                        data = preview
                    });
                }

                if (preview.ValidCount == 0)
                {
                    return Json(new { success = false, message = "الملف لا يحتوي على أي سطور صالحة للاستيراد." });
                }

                int created = 0;
                int updated = 0;
                var strategy = _context.Database.CreateExecutionStrategy();

                await strategy.ExecuteAsync(async () =>
                {
                    created = 0;
                    updated = 0;
                    using var tx = await _context.Database.BeginTransactionAsync();

                    var existingVendors = await _context.Vendors
                        .IgnoreQueryFilters()
                        .ToListAsync();

                    var existingByBranchAndCode = existingVendors
                        .GroupBy(v => $"{v.BranchId}:{v.Code}", StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    foreach (var r in preview.Rows.Where(x => x.IsValid))
                    {
                        string code = r.Code;
                        if (string.IsNullOrWhiteSpace(code))
                        {
                            code = await _sequenceService.GetNextCodeAsync(EntityName, r.BranchId);
                            r.Code = code;
                        }

                        string key = $"{r.BranchId}:{code}";
                        if (existingByBranchAndCode.TryGetValue(key, out var existing))
                        {
                            if (updateExisting)
                            {
                                existing.NameAr = r.NameAr;
                                existing.NameEn = string.IsNullOrWhiteSpace(r.NameEn) ? r.NameAr : r.NameEn;
                                existing.VendorCategoryId = r.CategoryId;
                                existing.FinancialAccountId = r.FinancialAccountId;
                                existing.CurrencyId = r.CurrencyId;
                                existing.TaxNumber = r.TaxNumber;
                                existing.CommercialRegister = r.CommercialRegister;
                                existing.TaxPayerType = r.TaxPayerType;
                                existing.GovernorateId = r.GovernorateId;
                                existing.CityId = r.CityId;
                                existing.Address = r.Address;
                                existing.Mobile = r.Mobile;
                                existing.Mobile2 = r.Mobile2;
                                existing.Phone = r.Phone;
                                existing.Email = r.Email;
                                existing.Notes = r.Notes;
                                existing.UpdatedAt = DateTime.UtcNow;
                                updated++;
                            }
                        }
                        else
                        {
                            var entity = new Vendor
                            {
                                BranchId = r.BranchId,
                                Code = code,
                                NameAr = r.NameAr,
                                NameEn = string.IsNullOrWhiteSpace(r.NameEn) ? r.NameAr : r.NameEn,
                                VendorCategoryId = r.CategoryId,
                                FinancialAccountId = r.FinancialAccountId,
                                CurrencyId = r.CurrencyId,
                                TaxNumber = r.TaxNumber,
                                CommercialRegister = r.CommercialRegister,
                                TaxPayerType = r.TaxPayerType,
                                GovernorateId = r.GovernorateId,
                                CityId = r.CityId,
                                Address = r.Address,
                                Mobile = r.Mobile,
                                Mobile2 = r.Mobile2,
                                Phone = r.Phone,
                                Email = r.Email,
                                Notes = r.Notes,
                                IsActive = true,
                                CreatedAt = DateTime.UtcNow
                            };
                            _context.Vendors.Add(entity);
                            existingByBranchAndCode[key] = entity;
                            created++;
                        }
                    }

                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();
                });

                await _audit.LogAsync(new ERP.Application.Models.AuditLogEntry
                {
                    ScreenCode = SC_Code,
                    ScreenName = SC_Name,
                    ModuleName = SC_Module,
                    EntityName = EntityName,
                    Action = AuditAction.Create,
                    Description = $"استيراد بيانات الموردين من Excel: موردون جدد ({created})، موردون محدثون ({updated})"
                });

                string msg = $"تم استيراد بيانات الموردين بنجاح. تم إضافة ({created}) وتحديث ({updated}) مورد.";
                return Json(new
                {
                    success = true,
                    message = msg,
                    data = new { message = msg, createdCount = created, updatedCount = updated }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "حدث خطأ أثناء تنفيذ استيراد الموردين: " + ex.Message });
            }
        }

        private sealed class VendorImportRowDto
        {
            public int RowNumber { get; set; }
            public int BranchId { get; set; }
            public string BranchName { get; set; } = string.Empty;
            public string Code { get; set; } = string.Empty;
            public string NameAr { get; set; } = string.Empty;
            public string? NameEn { get; set; }
            public int CategoryId { get; set; }
            public string CategoryName { get; set; } = string.Empty;
            public int? FinancialAccountId { get; set; }
            public string? FinancialAccountCode { get; set; }
            public int? CurrencyId { get; set; }
            public string? CurrencyCode { get; set; }
            public string? TaxNumber { get; set; }
            public string? CommercialRegister { get; set; }
            public string? TaxPayerType { get; set; }
            public int? GovernorateId { get; set; }
            public string? GovernorateName { get; set; }
            public int? CityId { get; set; }
            public string? CityName { get; set; }
            public string? Address { get; set; }
            public string? Mobile { get; set; }
            public string? Mobile2 { get; set; }
            public string? Phone { get; set; }
            public string? Email { get; set; }
            public string? Notes { get; set; }
            public bool ExistsInDb { get; set; }
            public bool IsValid => Errors.Count == 0;
            public List<string> Errors { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
        }

        private sealed class VendorImportPreviewDto
        {
            public int TotalRows { get; set; }
            public int ValidCount { get; set; }
            public int ErrorCount { get; set; }
            public int WarningCount { get; set; }
            public int NewCount { get; set; }
            public int UpdateCount { get; set; }
            public List<VendorImportRowDto> Rows { get; set; } = new();
        }

        private async Task<VendorImportPreviewDto> ValidateVendorImportAsync(Stream inputStream)
        {
            var preview = new VendorImportPreviewDto();
            var rawRows = ExtractVendorRawRows(inputStream, "الموردين");
            if (rawRows.Count == 0)
            {
                preview.ErrorCount = 1;
                return preview;
            }

            var branches = await _context.Branches.IgnoreQueryFilters().Where(b => b.IsActive).AsNoTracking().ToListAsync();
            var categories = await _context.VendorCategories.IgnoreQueryFilters().Where(c => c.IsActive).AsNoTracking().ToListAsync();
            var accounts = await _context.GLAccounts.IgnoreQueryFilters().Where(a => a.IsActive).AsNoTracking().ToListAsync();
            var currencies = await _context.Currencies.IgnoreQueryFilters().Where(c => c.IsActive).AsNoTracking().ToListAsync();
            var governorates = await _context.Governorates.IgnoreQueryFilters().Where(g => g.IsActive).AsNoTracking().ToListAsync();
            var cities = await _context.Cities.IgnoreQueryFilters().Where(c => c.IsActive).AsNoTracking().ToListAsync();
            var existingVendors = await _context.Vendors.IgnoreQueryFilters().AsNoTracking().Select(v => new { v.BranchId, v.Code, v.NameAr }).ToListAsync();

            var existingSet = existingVendors
                .GroupBy(v => $"{v.BranchId}:{v.Code}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var fileBranchCodeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int fallbackBranchId = _currentUser.BranchId > 0
                ? _currentUser.BranchId
                : (branches.FirstOrDefault(b => _currentUser.CanAccessBranch(b.Id))?.Id ?? branches.FirstOrDefault()?.Id ?? 0);

            foreach (var raw in rawRows)
            {
                string GetCol(int idx) => idx < raw.Cells.Count ? raw.Cells[idx].Trim() : string.Empty;
                string branchStr = GetCol(0);
                string code = GetCol(1);
                string nameAr = GetCol(2);
                string nameEn = GetCol(3);
                string catStr = GetCol(4);
                string accStr = GetCol(5);
                string currStr = GetCol(6);
                string taxNo = GetCol(7);
                string commReg = GetCol(8);
                string taxType = GetCol(9);
                string govStr = GetCol(10);
                string cityStr = GetCol(11);
                string address = GetCol(12);
                string mobile1 = GetCol(13);
                string mobile2 = GetCol(14);
                string phone = GetCol(15);
                string email = GetCol(16);
                string notes = GetCol(17);

                if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(branchStr))
                    continue;

                if (branchStr.StartsWith("دليل وإرشادات") || branchStr.Equals("العمود") || branchStr.Equals("كود الفرع") ||
                    (branchStr.Length == 1 && char.IsLetter(branchStr[0]) && (code.Contains("كود") || code.Contains("اسم") || code.Contains("فئة") || code.Contains("الرقم") || code.Contains("السجل") || code.Contains("نوع") || code.Contains("المحافظة") || code.Contains("المدينة") || code.Contains("العنوان") || code.Contains("الموبايل") || code.Contains("الهاتف") || code.Contains("البريد") || code.Contains("ملاحظات"))))
                    continue;

                var dto = new VendorImportRowDto
                {
                    RowNumber = raw.RowNumber,
                    Code = code,
                    NameAr = nameAr,
                    NameEn = string.IsNullOrWhiteSpace(nameEn) ? nameAr : nameEn,
                    TaxNumber = NullIfEmpty(taxNo),
                    CommercialRegister = NullIfEmpty(commReg),
                    TaxPayerType = NullIfEmpty(taxType),
                    Address = NullIfEmpty(address),
                    Mobile = NullIfEmpty(mobile1),
                    Mobile2 = NullIfEmpty(mobile2),
                    Phone = NullIfEmpty(phone),
                    Email = NullIfEmpty(email),
                    Notes = NullIfEmpty(notes)
                };

                // 1. Resolve Branch
                Branch? branch = null;
                if (!string.IsNullOrWhiteSpace(branchStr))
                {
                    branch = branches.FirstOrDefault(b =>
                        b.Code.Equals(branchStr, StringComparison.OrdinalIgnoreCase) ||
                        b.BranchCode.Equals(branchStr, StringComparison.OrdinalIgnoreCase) ||
                        (b.NameAr != null && b.NameAr.Equals(branchStr, StringComparison.OrdinalIgnoreCase)) ||
                        b.Name.Equals(branchStr, StringComparison.OrdinalIgnoreCase));

                    if (branch == null)
                        dto.Errors.Add($"الفرع '{branchStr}' غير موجود في النظام.");
                }
                else
                {
                    branch = branches.FirstOrDefault(b => b.Id == fallbackBranchId);
                    if (branch == null)
                        dto.Errors.Add("يرجى تحديد كود الفرع.");
                }

                if (branch != null)
                {
                    dto.BranchId = branch.Id;
                    dto.BranchName = branch.NameAr ?? branch.Name;
                    if (!_currentUser.CanAccessBranch(branch.Id))
                        dto.Errors.Add($"لا تملك صلاحية الاستيراد على الفرع '{dto.BranchName}'.");
                }

                // 2. Validate NameAr
                if (string.IsNullOrWhiteSpace(dto.NameAr))
                    dto.Errors.Add("اسم المورد (عربي) مطلوب.");

                // 3. Check Code / Duplicate
                if (!string.IsNullOrWhiteSpace(dto.Code) && dto.BranchId > 0)
                {
                    string key = $"{dto.BranchId}:{dto.Code}";
                    if (!fileBranchCodeSet.Add(key))
                    {
                        dto.Errors.Add($"كود المورد '{dto.Code}' مكرر داخل الملف لنفس الفرع.");
                    }
                    else if (existingSet.ContainsKey(key))
                    {
                        dto.ExistsInDb = true;
                        dto.Warnings.Add("المورد موجود مسبقاً في هذا الفرع وسيتم تحديث بياناته.");
                    }
                }
                else if (string.IsNullOrWhiteSpace(dto.Code))
                {
                    dto.Warnings.Add("سيتم توليد كود المورد تلقائياً عند الحفظ.");
                }

                // 4. Resolve Category
                if (dto.BranchId > 0)
                {
                    VendorCategory? cat = null;
                    if (!string.IsNullOrWhiteSpace(catStr))
                    {
                        cat = categories.FirstOrDefault(c => c.BranchId == dto.BranchId &&
                            (c.Code.Equals(catStr, StringComparison.OrdinalIgnoreCase) ||
                             c.NameAr.Equals(catStr, StringComparison.OrdinalIgnoreCase) ||
                             c.NameEn.Equals(catStr, StringComparison.OrdinalIgnoreCase)));
                    }
                    cat ??= categories.FirstOrDefault(c => c.BranchId == dto.BranchId && c.IsDefault)
                         ?? categories.FirstOrDefault(c => c.BranchId == dto.BranchId);

                    if (cat != null)
                    {
                        dto.CategoryId = cat.Id;
                        dto.CategoryName = cat.NameAr;
                    }
                    else
                    {
                        dto.Errors.Add("لا توجد فئة موردين معرفة لهذا الفرع.");
                    }
                }

                // 5. Resolve Financial Account (or fallback to Category ControlAccountId)
                if (!string.IsNullOrWhiteSpace(accStr))
                {
                    var acc = accounts.FirstOrDefault(a =>
                        a.Code.Equals(accStr, StringComparison.OrdinalIgnoreCase) ||
                        a.NameAr.Equals(accStr, StringComparison.OrdinalIgnoreCase));
                    if (acc == null)
                        dto.Errors.Add($"الحساب المالي '{accStr}' غير موجود في شجرة الحسابات.");
                    else if (acc.IsGroup)
                        dto.Errors.Add($"الحساب '{accStr}' هو مجموعة رئيسية وليس حساب حركة فرعي.");
                    else
                    {
                        dto.FinancialAccountId = acc.Id;
                        dto.FinancialAccountCode = acc.Code;
                    }
                }
                else if (dto.CategoryId > 0)
                {
                    var catForAcc = categories.FirstOrDefault(c => c.Id == dto.CategoryId);
                    if (catForAcc?.ControlAccountId != null)
                    {
                        var catAcc = accounts.FirstOrDefault(a => a.Id == catForAcc.ControlAccountId.Value && !a.IsGroup);
                        if (catAcc != null)
                        {
                            dto.FinancialAccountId = catAcc.Id;
                            dto.FinancialAccountCode = catAcc.Code;
                        }
                    }
                }

                if (!dto.FinancialAccountId.HasValue || dto.FinancialAccountId.Value <= 0)
                {
                    dto.Errors.Add("يرجى تحديد الحساب المالي للمورد أو ربط فئة المورد بحساب رقابي افتراضي.");
                }

                // 6. Resolve Currency
                string effCurr = string.IsNullOrWhiteSpace(currStr) ? "EGP" : currStr.ToUpperInvariant();
                var curr = currencies.FirstOrDefault(c => c.Code.Equals(effCurr, StringComparison.OrdinalIgnoreCase) || c.NameAr.Equals(effCurr, StringComparison.OrdinalIgnoreCase));
                if (curr != null)
                {
                    dto.CurrencyId = curr.Id;
                    dto.CurrencyCode = curr.Code;
                }
                else if (!string.IsNullOrWhiteSpace(currStr))
                {
                    dto.Errors.Add($"العملة '{currStr}' غير معرفة.");
                }

                // 7. Resolve Governorate & City
                if (!string.IsNullOrWhiteSpace(govStr))
                {
                    var gov = governorates.FirstOrDefault(g =>
                        g.Code.Equals(govStr, StringComparison.OrdinalIgnoreCase) ||
                        g.NameAr.Equals(govStr, StringComparison.OrdinalIgnoreCase) ||
                        g.NameEn.Equals(govStr, StringComparison.OrdinalIgnoreCase));
                    if (gov != null)
                    {
                        dto.GovernorateId = gov.Id;
                        dto.GovernorateName = gov.NameAr;
                    }
                }

                if (!string.IsNullOrWhiteSpace(cityStr))
                {
                    var city = cities.FirstOrDefault(c =>
                        (!dto.GovernorateId.HasValue || c.GovernorateId == dto.GovernorateId.Value) &&
                        (c.Code.Equals(cityStr, StringComparison.OrdinalIgnoreCase) ||
                         c.NameAr.Equals(cityStr, StringComparison.OrdinalIgnoreCase) ||
                         c.NameEn.Equals(cityStr, StringComparison.OrdinalIgnoreCase)));
                    if (city != null)
                    {
                        dto.CityId = city.Id;
                        dto.CityName = city.NameAr;
                        dto.GovernorateId ??= city.GovernorateId;
                    }
                }

                preview.Rows.Add(dto);
                if (dto.IsValid)
                {
                    preview.ValidCount++;
                    if (dto.ExistsInDb) preview.UpdateCount++;
                    else preview.NewCount++;
                }
                else
                {
                    preview.ErrorCount++;
                }
                if (dto.Warnings.Count > 0)
                    preview.WarningCount++;
            }

            preview.TotalRows = preview.Rows.Count;
            return preview;
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static List<(int RowNumber, List<string> Cells)> ExtractVendorRawRows(Stream inputStream, string preferredSheetName)
        {
            using var ms = new MemoryStream();
            if (inputStream.CanSeek) inputStream.Position = 0;
            inputStream.CopyTo(ms);
            byte[] bytes = ms.ToArray();
            if (bytes.Length == 0) return new();

            bool isZip = bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B;
            if (isZip)
            {
                using var wbStream = new MemoryStream(bytes);
                using var workbook = new XLWorkbook(wbStream);
                var ws = workbook.Worksheets.FirstOrDefault(w => w.Name == preferredSheetName) ?? workbook.Worksheets.FirstOrDefault();
                if (ws == null) return new();

                var list = new List<(int RowNumber, List<string> Cells)>();
                int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
                for (int r = 2; r <= lastRow; r++)
                {
                    var row = ws.Row(r);
                    var cells = new List<string>();
                    for (int c = 1; c <= 18; c++)
                        cells.Add(row.Cell(c).GetString().Trim());
                    list.Add((r, cells));
                }
                return list;
            }
            else
            {
                string text = Encoding.UTF8.GetString(bytes);
                if (text.Contains('\uFFFD'))
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    text = Encoding.GetEncoding(1256).GetString(bytes);
                }
                var lines = ParseCsvText(text);
                var list = new List<(int RowNumber, List<string> Cells)>();
                for (int i = 1; i < lines.Count; i++)
                {
                    list.Add((i + 1, lines[i]));
                }
                return list;
            }
        }

        private static List<List<string>> ParseCsvText(string csvText)
        {
            var result = new List<List<string>>();
            var currentRow = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < csvText.Length; i++)
            {
                char c = csvText[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csvText.Length && csvText[i + 1] == '"') { currentField.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else currentField.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { currentRow.Add(currentField.ToString()); currentField.Clear(); }
                    else if (c == '\r' || c == '\n')
                    {
                        if (c == '\r' && i + 1 < csvText.Length && csvText[i + 1] == '\n') i++;
                        currentRow.Add(currentField.ToString());
                        currentField.Clear();
                        result.Add(currentRow);
                        currentRow = new List<string>();
                    }
                    else currentField.Append(c);
                }
            }
            if (currentField.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentField.ToString());
                result.Add(currentRow);
            }
            return result;
        }

        // ─── Related Party Group (Multi-Currency & Customer Linking) Endpoints ──────────

        [HttpGet]
        [HasPermission("MasterData.Vendors.View")]
        public async Task<IActionResult> GetRelatedRecords(
            int partyId,
            string partyType = "Vendor",
            int? unifiedCurrencyId = null,
            DateTime? conversionDate = null)
        {
            var details = await _partyGroupService.GetPartyGroupDetailsAsync(
                partyType,
                partyId,
                unifiedCurrencyId,
                conversionDate);
            return Json(new { success = true, data = details });
        }

        [HttpGet]
        [HasPermission("MasterData.Vendors.View")]
        public async Task<IActionResult> SearchPartiesForLink(
            string term = "",
            string searchPartyType = "All",
            int branchId = 0,
            string? excludePartyType = "Vendor",
            int? excludePartyId = null)
        {
            int effectiveBranchId = branchId > 0 ? branchId : _currentUser.BranchId;
            var list = await _partyGroupService.SearchPartiesForLinkingAsync(
                searchPartyType,
                term,
                effectiveBranchId,
                excludePartyType,
                excludePartyId);
            return Json(new { success = true, data = list });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Vendors.Edit")]
        public async Task<IActionResult> LinkRelatedRecord([FromBody] LinkPartyRecordRequestDto request)
        {
            if (request == null)
                return Json(new { success = false, message = "بيانات الربط غير صالحة." });

            var res = await _partyGroupService.LinkRecordsAsync(request);
            return Json(new
            {
                success = res.Success,
                requiresMoveConfirmation = res.RequiresMoveConfirmation,
                message = res.Message,
                partyGroupId = res.PartyGroupId,
                data = res.GroupDetails
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Vendors.Edit")]
        public async Task<IActionResult> UnlinkRelatedRecord(string partyType, int partyId)
        {
            var res = await _partyGroupService.UnlinkRecordAsync(partyType, partyId);
            return Json(new
            {
                success = res.Success,
                message = res.Message,
                partyGroupId = res.PartyGroupId
            });
        }
    }
}
