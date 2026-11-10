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
    public class CustomerController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;
        private readonly ICodeSequenceService _sequenceService;
        private readonly IPartyGroupService _partyGroupService;

        private const string SC_Code   = "CUS001";
        private const string SC_Name   = "Customers";
        private const string SC_Module = "MasterData";
        private const string EntityName = "Customer";

        public CustomerController(
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
        [HasPermission("MasterData.Customers.View")]
        public async Task<IActionResult> Index(string? searchString, int? branchId, int? categoryId, int page = 1)
        {
            int pageSize = 15;

            int currentBranchId = _currentUser.BranchId;
            int? effectiveBranchId = currentBranchId > 0 ? currentBranchId : branchId;

            var branchesQuery = _currentUser.HasAllBranches
                ? _context.Branches.Where(b => b.IsActive)
                : _context.Branches.Where(b => b.IsActive && b.CompanyId == _currentUser.CompanyId);

            var allBranches = await branchesQuery.OrderBy(b => b.Code).ToListAsync();
            if (!_currentUser.HasAllBranches)
            {
                allBranches = allBranches.Where(b => _currentUser.CanAccessBranch(b.Id)).ToList();
            }
            ViewBag.Branches = allBranches;

            var catQuery = _context.CustomerCategories
                .Include(c => c.ControlAccount)
                .Where(c => c.IsActive);
            if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                catQuery = catQuery.Where(c => c.BranchId == effectiveBranchId.Value);
            }
            ViewBag.Categories   = await catQuery.OrderBy(c => c.IsDefault ? 0 : 1).ThenBy(c => c.Code).ToListAsync();
            ViewBag.Governorates = await _context.Governorates.Where(g => g.IsActive).OrderBy(g => g.Code).ToListAsync();
            ViewBag.Currencies   = await _context.Currencies.Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();

            var vendCatQuery = _context.VendorCategories
                .Include(vc => vc.ControlAccount)
                .Where(vc => vc.IsActive);
            if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                vendCatQuery = vendCatQuery.Where(vc => vc.BranchId == effectiveBranchId.Value);
            }
            ViewBag.VendorCategories = await vendCatQuery.OrderBy(vc => vc.IsDefault ? 0 : 1).ThenBy(vc => vc.Code).ToListAsync();

            var query = _context.Customers
                .Include(c => c.Branch)
                .Include(c => c.CustomerCategory)
                    .ThenInclude(cc => cc.ControlAccount)
                .Include(c => c.Governorate)
                .Include(c => c.City)
                .Include(c => c.Salesman)
                .Include(c => c.Currency)
                .Include(c => c.PartyGroup)
                .Include(c => c.FinancialAccount)
                .AsNoTracking();

            if (currentBranchId > 0)
            {
                query = query.Where(c => c.BranchId == currentBranchId);
            }
            else if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                if (!_currentUser.CanAccessBranch(effectiveBranchId.Value))
                    return Forbid();

                query = query.Where(c => c.BranchId == effectiveBranchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                query = query.Where(c => c.BranchId == _currentUser.BranchId);
            }

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                query = query.Where(c => c.CustomerCategoryId == categoryId.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();

                // Find PartyGroupIds where the group itself or any linked Customer/Vendor matches the search term
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

                query = query.Where(c =>
                    c.Code.Contains(searchString) ||
                    c.NameAr.Contains(searchString) ||
                    c.NameEn.Contains(searchString) ||
                    (c.TaxNumber != null && c.TaxNumber.Contains(searchString)) ||
                    (c.Mobile != null && c.Mobile.Contains(searchString)) ||
                    (c.Mobile2 != null && c.Mobile2.Contains(searchString)) ||
                    (c.Phone != null && c.Phone.Contains(searchString)) ||
                    (c.Email != null && c.Email.Contains(searchString)) ||
                    (c.PartyGroupId != null && matchingGroupIds.Contains(c.PartyGroupId.Value)));
            }

            int totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.BranchId).ThenBy(c => c.Code)
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
        [HasPermission("MasterData.Customers.Add")]
        public async Task<IActionResult> Create(
            Customer model,
            bool? isManualCode = null,
            List<int>? selectedCustomerCurrencyIds = null,
            bool createLinkedVendor = false,
            List<int>? selectedVendorCurrencyIds = null,
            int? linkedVendorCategoryId = null,
            int? linkedVendorFinancialAccountId = null)
        {
            if (model.BranchId > 0 && !_currentUser.CanAccessBranch(model.BranchId))
            {
                ModelState.AddModelError("BranchId", "يرجى تحديد فرع صحيح تملك صلاحية الوصول إليه.");
            }
            else if (_currentUser.BranchId > 0 && model.BranchId > 0 && model.BranchId != _currentUser.BranchId)
            {
                ModelState.AddModelError("BranchId", "غير مصرح بإنشاء عميل في فرع مختلف عن فرع الجلسة الحالي.");
            }
            else if (_currentUser.BranchId > 0)
            {
                model.BranchId = _currentUser.BranchId;
            }
            else if (model.BranchId <= 0)
            {
                ModelState.AddModelError("BranchId", "يرجى تحديد فرع صحيح تملك صلاحية الوصول إليه.");
            }

            // Use Multi-Currency & Linked Vendor Group Creation if currencies or linked vendor are selected
            var mergedCustCurrencies = new List<int>();
            if (model.CurrencyId.HasValue && model.CurrencyId.Value > 0)
                mergedCustCurrencies.Add(model.CurrencyId.Value);
            if (selectedCustomerCurrencyIds != null && selectedCustomerCurrencyIds.Any(id => id > 0))
            {
                foreach (var cid in selectedCustomerCurrencyIds.Where(id => id > 0))
                {
                    if (!mergedCustCurrencies.Contains(cid))
                        mergedCustCurrencies.Add(cid);
                }
            }

            if ((selectedCustomerCurrencyIds != null && selectedCustomerCurrencyIds.Any(id => id > 0)) || createLinkedVendor)
            {
                var groupReq = new CreateCustomerGroupRequestDto
                {
                    BaseCustomer = model,
                    IsManualCode = isManualCode == true,
                    CustomerCurrencyIds = mergedCustCurrencies,
                    CreateLinkedVendor = createLinkedVendor,
                    VendorCurrencyIds = selectedVendorCurrencyIds ?? new List<int>(),
                    LinkedVendorCategoryId = linkedVendorCategoryId,
                    LinkedVendorFinancialAccountId = linkedVendorFinancialAccountId
                };

                var groupRes = await _partyGroupService.CreateCustomerGroupAsync(groupReq);
                if (groupRes.Success)
                {
                    if (Request.IsAjaxRequest())
                        return this.JsonSuccess(groupRes.Message, Url.Action(nameof(Index), new { branchId = model.BranchId }));

                    TempData["SuccessMessage"] = groupRes.Message;
                    return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
                }
                else
                {
                    if (Request.IsAjaxRequest())
                        return this.JsonFailure(groupRes.Message);

                    TempData["ErrorMessage"] = groupRes.Message;
                    return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
                }
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

            // 2. Code uniqueness in the same branch
            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود العميل مطلوب.");
            else if (await _context.Customers.AnyAsync(c => c.BranchId == model.BranchId && c.Code == model.Code.Trim()))
                ModelState.AddModelError("Code", "كود العميل مسجل مسبقاً في هذا الفرع.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "الاسم العربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (model.CustomerCategoryId <= 0 || !await _context.CustomerCategories.AnyAsync(cc => cc.Id == model.CustomerCategoryId && cc.BranchId == model.BranchId))
                ModelState.AddModelError("CustomerCategoryId", "يرجى اختيار تصنيف عملاء تابع لنفس الفرع.");

            // 3. Salesman branch consistency: Salesman.BranchId == Customer.BranchId
            if (model.SalesmanId.HasValue && model.SalesmanId.Value > 0)
            {
                var salesman = await _context.Salesmen.FindAsync(model.SalesmanId.Value);
                if (salesman == null || salesman.BranchId != model.BranchId)
                    ModelState.AddModelError("SalesmanId", "المندوب المحدد يجب أن يكون تابعاً لنفس الفرع.");
            }

            // 4. City governorate consistency: City.GovernorateId == Customer.GovernorateId
            if (model.CityId.HasValue && model.CityId.Value > 0)
            {
                var city = await _context.Cities.FindAsync(model.CityId.Value);
                if (city == null || (model.GovernorateId.HasValue && city.GovernorateId != model.GovernorateId.Value))
                    ModelState.AddModelError("CityId", "المدينة المحددة لا تنتمي إلى المحافظة المختارة.");
            }

            // 5. Financial Account validation
            if (!model.FinancialAccountId.HasValue || model.FinancialAccountId.Value <= 0)
            {
                var cat = await _context.CustomerCategories.FindAsync(model.CustomerCategoryId);
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
                    ModelState.AddModelError("FinancialAccountId", "لا يمكن ربط العميل بحساب رئيسي (تجميعي)، يرجى اختيار حساب فرعي يقبل الحركات.");
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

                _context.Customers.Add(model);

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    var msg = ex.InnerException?.Message ?? ex.Message;
                    if (Request.IsAjaxRequest())
                        return this.JsonFailure("حدث خطأ أثناء حفظ بيانات العميل: " + msg);

                    TempData["ErrorMessage"] = "حدث خطأ أثناء حفظ بيانات العميل: " + msg;
                    return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
                }

                await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                    entityName: "Customer",
                    entityId:   model.Id.ToString(),
                    newValues: new
                    {
                        model.Id,
                        model.BranchId,
                        model.Code,
                        model.NameAr,
                        model.NameEn,
                        model.CustomerCategoryId,
                        model.Address,
                        model.GovernorateId,
                        model.CityId,
                        model.TaxNumber,
                        model.CommercialRegister,
                        model.TaxPayerType,
                        model.SalesmanId,
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
                    return this.JsonSuccess("تم حفظ بيانات العميل بنجاح.", Url.Action(nameof(Index), new { branchId = model.BranchId }));

                TempData["SuccessMessage"] = "Customer created successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure("لا يمكن حفظ العميل. يرجى مراجعة الأخطاء الموضحة وتصحيحها.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Customers.Edit")]
        public async Task<IActionResult> Edit(Customer model)
        {
            var entity = await _context.Customers.FindAsync(model.Id);
            if (entity == null) return NotFound();

            if (_currentUser.BranchId > 0 && (entity.BranchId != _currentUser.BranchId || model.BranchId != _currentUser.BranchId))
                return Forbid();

            if (!_currentUser.CanAccessBranch(entity.BranchId) || !_currentUser.CanAccessBranch(model.BranchId))
                return Forbid();

            if (string.IsNullOrWhiteSpace(model.Code))
                ModelState.AddModelError("Code", "كود العميل مطلوب.");
            else if (await _context.Customers.AnyAsync(c => c.BranchId == model.BranchId && c.Code == model.Code.Trim() && c.Id != model.Id))
                ModelState.AddModelError("Code", "كود العميل مسجل مسبقاً في هذا الفرع.");

            if (string.IsNullOrWhiteSpace(model.NameAr))
                ModelState.AddModelError("NameAr", "الاسم العربي مطلوب.");

            if (string.IsNullOrWhiteSpace(model.NameEn))
                model.NameEn = model.NameAr?.Trim() ?? string.Empty;

            if (model.CustomerCategoryId <= 0 || !await _context.CustomerCategories.AnyAsync(cc => cc.Id == model.CustomerCategoryId && cc.BranchId == model.BranchId))
                ModelState.AddModelError("CustomerCategoryId", "يرجى اختيار تصنيف عملاء تابع لنفس الفرع.");

            if (model.SalesmanId.HasValue && model.SalesmanId.Value > 0)
            {
                var salesman = await _context.Salesmen.FindAsync(model.SalesmanId.Value);
                if (salesman == null || salesman.BranchId != model.BranchId)
                    ModelState.AddModelError("SalesmanId", "المندوب المحدد يجب أن يكون تابعاً لنفس الفرع.");
            }

            if (model.CityId.HasValue && model.CityId.Value > 0)
            {
                var city = await _context.Cities.FindAsync(model.CityId.Value);
                if (city == null || (model.GovernorateId.HasValue && city.GovernorateId != model.GovernorateId.Value))
                    ModelState.AddModelError("CityId", "المدينة المحددة لا تنتمي إلى المحافظة المختارة.");
            }

            // Validate no duplicate currency within the same PartyGroup
            var (currValid, currErr) = await _partyGroupService.ValidatePartyCurrencyChangeAsync("Customer", entity.Id, entity.PartyGroupId, model.CurrencyId);
            if (!currValid)
            {
                ModelState.AddModelError("CurrencyId", currErr);
            }

            // Financial Account validation
            if (!model.FinancialAccountId.HasValue || model.FinancialAccountId.Value <= 0)
            {
                var cat = await _context.CustomerCategories.FindAsync(model.CustomerCategoryId);
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
                    ModelState.AddModelError("FinancialAccountId", "لا يمكن ربط العميل بحساب رئيسي (تجميعي)، يرجى اختيار حساب فرعي يقبل الحركات.");
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
                    entity.CustomerCategoryId,
                    entity.Address,
                    entity.GovernorateId,
                    entity.CityId,
                    entity.TaxNumber,
                    entity.CommercialRegister,
                    entity.TaxPayerType,
                    entity.SalesmanId,
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
                entity.CustomerCategoryId  = model.CustomerCategoryId;
                entity.Address             = model.Address?.Trim();
                entity.GovernorateId       = model.GovernorateId;
                entity.CityId              = model.CityId;
                entity.TaxNumber           = model.TaxNumber?.Trim();
                entity.CommercialRegister  = model.CommercialRegister?.Trim();
                entity.TaxPayerType        = model.TaxPayerType?.Trim();
                entity.SalesmanId          = model.SalesmanId;
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
                        return this.JsonFailure("حدث خطأ أثناء حفظ تعديلات العميل: " + msg);

                    TempData["ErrorMessage"] = "حدث خطأ أثناء حفظ تعديلات العميل: " + msg;
                    return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
                }

                var newValues = new
                {
                    entity.Id,
                    entity.BranchId,
                    entity.Code,
                    entity.NameAr,
                    entity.NameEn,
                    entity.CustomerCategoryId,
                    entity.Address,
                    entity.GovernorateId,
                    entity.CityId,
                    entity.TaxNumber,
                    entity.CommercialRegister,
                    entity.TaxPayerType,
                    entity.SalesmanId,
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
                    return this.JsonSuccess("تم تحديث بيانات العميل بنجاح.", Url.Action(nameof(Index), new { branchId = model.BranchId }));

                TempData["SuccessMessage"] = "Customer updated successfully.";
                return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
            }

            if (Request.IsAjaxRequest())
                return this.JsonValidationFailure("لا يمكن حفظ التعديلات. يرجى مراجعة الأخطاء الموضحة وتصحيحها.");

            TempData["ErrorMessage"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { branchId = model.BranchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Customers.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Customers.FindAsync(id);
            if (entity == null) return NotFound();

            if (_currentUser.BranchId > 0 && entity.BranchId != _currentUser.BranchId)
                return Forbid();

            if (!_currentUser.CanAccessBranch(entity.BranchId))
                return Forbid();

            // Delete Protection: Check if customer is referenced in any database foreign keys
            if (await IsRecordReferencedAsync("Customers", id))
            {
                TempData["ErrorMessage"] = "لا يمكن حذف العميل لوجود حركات مالية أو فواتير أو سجلات مرتبطة به.";
                return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
            }

            var oldValues = new
            {
                entity.Id,
                entity.BranchId,
                entity.Code,
                entity.NameAr,
                entity.NameEn,
                entity.CustomerCategoryId,
                entity.Address,
                entity.GovernorateId,
                entity.CityId,
                entity.TaxNumber,
                entity.CommercialRegister,
                entity.TaxPayerType,
                entity.SalesmanId,
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
                _context.Customers.Remove(entity);
                await _context.SaveChangesAsync();

                await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                    entityName: EntityName,
                    entityId:   id.ToString(),
                    oldValues:  oldValues);

                TempData["SuccessMessage"] = "Customer deleted successfully.";
            }
            catch (DbUpdateException)
            {
                TempData["ErrorMessage"] = "لا يمكن حذف العميل لوجود حركات مالية أو سجلات مرتبطة به.";
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
        [HasPermission("MasterData.Customers.Edit")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var entity = await _context.Customers.FindAsync(id);
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

            TempData["SuccessMessage"] = $"Customer status changed to {(entity.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index), new { branchId = entity.BranchId });
        }

        // ─── GET: Download Excel Template for Customers ───────────────────────
        [HttpGet]
        [HasPermission("MasterData.Customers.View")]
        public async Task<IActionResult> DownloadTemplate()
        {
            var branches = await _context.Branches.IgnoreQueryFilters().Where(b => b.IsActive).OrderBy(b => b.Code).AsNoTracking().ToListAsync();
            var currencies = await _context.Currencies.IgnoreQueryFilters().Where(c => c.IsActive).OrderBy(c => c.Code).AsNoTracking().ToListAsync();
            var categories = await _context.CustomerCategories.IgnoreQueryFilters().Where(c => c.IsActive).OrderBy(c => c.Code).AsNoTracking().ToListAsync();
            var governorates = await _context.Governorates.IgnoreQueryFilters().Where(g => g.IsActive).OrderBy(g => g.Code).AsNoTracking().ToListAsync();

            using var workbook = new XLWorkbook();

            // Sheet 1: العملاء
            var ws = workbook.Worksheets.Add("العملاء");
            ws.RightToLeft = true;
            ws.ShowGridLines = true;

            string[] headers = {
                "كود الفرع",
                "كود العميل",
                "اسم العميل (عربي)",
                "اسم العميل (إنجليزي)",
                "فئة العميل",
                "كود الحساب المالي",
                "كود العملة",
                "المندوب",
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
                new[] { defaultBranchCode, "", "شركة النور للمنسوجات والملابس", "Al-Nour Textiles & Garments Co.", "", "12020101001", "EGP", "", "100-200-300", "45821", "شركة", "القاهرة", "مدينة نصر", "شارع عباس العقاد - مدينة نصر", "01001234567", "01221234567", "0222745678", "info@alnour-tex.com", "عميل ماكينات جملة" },
                new[] { defaultBranchCode, "", "مصنع التوحيد للملابس الجاهزة", "Al-Tawheed Ready Wear Factory", "", "12020201001", "EGP", "", "200-300-400", "78912", "شركة", "القليوبية", "شبرا الخيمة", "المنطقة الصناعية - شبرا الخيمة", "01112345678", "", "0242211223", "sales@tawheed.com", "عميل قطع غيار" },
                new[] { defaultBranchCode, "", "أحمد محمد عبد الرحمن", "Ahmed Mohamed Abdelrahman", "", "12020101001", "EGP", "", "", "", "فرد", "الجيزة", "السادس من أكتوبر", "المنطقة الصناعية الثالثة - 6 أكتوبر", "01551234567", "", "", "", "عميل تجزئة" },
                new[] { defaultBranchCode, "", "شركة الدلتا للتصدير والتوريدات", "Delta Export & Supplies", "", "12020301001", "USD", "", "300-400-500", "96325", "شركة", "الإسكندرية", "سموحة", "طريق الحرية - الإسكندرية", "01098765432", "", "034256789", "contact@delta-exp.com", "عميل تعامل بالدولار" }
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
            ws.Range(2, 11, 1000, 11).CreateDataValidation().List("\"شركة,فرد,هيئة حكومية,أجنبي\"", true);

            ws.Columns(1, headers.Length).AdjustToContents();
            ws.Column(3).Width = 32;
            ws.Column(4).Width = 30;
            ws.Column(14).Width = 30;
            ws.Column(19).Width = 25;

            // Sheet 2: تعليمات وإرشادات الاستيراد
            var wsHelp = workbook.Worksheets.Add("تعليمات وإرشادات الاستيراد");
            wsHelp.RightToLeft = true;
            wsHelp.ShowGridLines = true;
            wsHelp.Cell(1, 1).Value = "دليل وإرشادات ملء ملف استيراد العملاء (Customers)";
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
                ("B", "كود العميل (اختياري)", "يُترك فارغاً ليقوم النظام بتوليد كود تلقائي متسلسل للفرع، أو يُكتب الكود اليدوي بشرط عدم تكراره داخل نفس الفرع."),
                ("C", "اسم العميل عربي (إلزامي)", "الاسم التجاري أو الرسمي للعميل باللغة العربية."),
                ("D", "اسم العميل إنجليزي (اختياري)", "الاسم باللغة الإنجليزية (إذا تُرك فارغاً يتم نسخه تلقائياً من الاسم العربي)."),
                ("E", "فئة العميل (اختياري)", "كود أو اسم فئة العملاء. إذا تُرك فارغاً يتم ربط العميل تلقائياً بالفئة الافتراضية للفرع."),
                ("F", "كود الحساب المالي (اختياري)", "كود حساب العميل في شجرة الحسابات (مثل: 12020101001 عملاء مصري ماكينات، أو 12020201001 عملاء مصري قطع غيار)."),
                ("G", "كود العملة (اختياري)", "رمز العملة الافتراضية للعميل (EGP, USD, EUR, SAR, AED). افتراضياً EGP."),
                ("H", "المندوب (اختياري)", "كود أو اسم مندوب المبيعات التابع لنفس الفرع."),
                ("I", "الرقم الضريبي (اختياري)", "رقم التسجيل الضريبي للعميل (مثلاً: 100-200-300)."),
                ("J", "السجل التجاري (اختياري)", "رقم السجل التجاري للعميل."),
                ("K", "نوع الممول (اختياري)", "شركة، فرد، هيئة حكومية، أو أجنبي."),
                ("L", "المحافظة (اختياري)", "اسم أو كود المحافظة (مثل: القاهرة، الجيزة، الإسكندرية، القليوبية)."),
                ("M", "المدينة (اختياري)", "اسم أو كود المدينة التابعة للمحافظة."),
                ("N", "العنوان (اختياري)", "العنوان التفصيلي للعميل."),
                ("O", "الموبايل 1 (اختياري)", "رقم الهاتف المحمول الأساسي."),
                ("P", "الموبايل 2 (اختياري)", "رقم الهاتف المحمول الإضافي."),
                ("Q", "الهاتف (اختياري)", "رقم الهاتف الأرضي."),
                ("R", "البريد الإلكتروني (اختياري)", "البريد الإلكتروني للعميل."),
                ("S", "ملاحظات (اختياري)", "أي ملاحظات إضافية خاصة بالعميل.")
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
            wsRef.Cell(1, 7).Value = "كود حساب العملاء";
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
            var customerGlRefs = new (string Code, string Name)[]
            {
                ("12020101001", "عملاء مصري ماكينات"),
                ("12020201001", "عملاء مصري قطع غيار"),
                ("12020301001", "عملاء دولار ماكينات"),
                ("12020401001", "عملاء دولار قطع غيار"),
                ("12020501001", "عملاء يورو ماكينات")
            };
            for (int i = 0; i < customerGlRefs.Length; i++)
            {
                wsRef.Cell(i + 2, 7).SetValue(customerGlRefs[i].Code);
                wsRef.Cell(i + 2, 8).SetValue(customerGlRefs[i].Name);
            }
            wsRef.Columns(1, 8).AdjustToContents();

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"نموذج_استيراد_العملاء_{DateTime.Now:yyyyMMdd}.xlsx");
        }

        // ─── POST: Preview Excel Import for Customers ─────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Customers.Add")]
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
                var preview = await ValidateCustomerImportAsync(stream);
                return Json(new { success = true, data = preview });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "حدث خطأ أثناء قراءة وفحص ملف العملاء: " + ex.Message });
            }
        }

        // ─── POST: Confirm Excel Import for Customers ─────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [HasPermission("MasterData.Customers.Add")]
        public async Task<IActionResult> ConfirmExcelImport(IFormFile? excelFile, bool updateExisting = true)
        {
            if (excelFile == null || excelFile.Length == 0)
                return Json(new { success = false, message = "يرجى تحديد ملف Excel أو CSV للرفع (.xlsx / .csv)." });

            try
            {
                using var stream = excelFile.OpenReadStream();
                var preview = await ValidateCustomerImportAsync(stream);

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

                    var existingCustomers = await _context.Customers
                        .IgnoreQueryFilters()
                        .ToListAsync();

                    var existingByBranchAndCode = existingCustomers
                        .GroupBy(c => $"{c.BranchId}:{c.Code}", StringComparer.OrdinalIgnoreCase)
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
                                existing.CustomerCategoryId = r.CategoryId;
                                existing.FinancialAccountId = r.FinancialAccountId;
                                existing.CurrencyId = r.CurrencyId;
                                existing.SalesmanId = r.SalesmanId;
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
                            var entity = new Customer
                            {
                                BranchId = r.BranchId,
                                Code = code,
                                NameAr = r.NameAr,
                                NameEn = string.IsNullOrWhiteSpace(r.NameEn) ? r.NameAr : r.NameEn,
                                CustomerCategoryId = r.CategoryId,
                                FinancialAccountId = r.FinancialAccountId,
                                CurrencyId = r.CurrencyId,
                                SalesmanId = r.SalesmanId,
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
                            _context.Customers.Add(entity);
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
                    Description = $"استيراد بيانات العملاء من Excel: عملاء جدد ({created})، عملاء محدثون ({updated})"
                });

                string msg = $"تم استيراد بيانات العملاء بنجاح. تم إضافة ({created}) وتحديث ({updated}) عميل.";
                return Json(new
                {
                    success = true,
                    message = msg,
                    data = new { message = msg, createdCount = created, updatedCount = updated }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "حدث خطأ أثناء تنفيذ استيراد العملاء: " + ex.Message });
            }
        }

        private sealed class PartyImportRowDto
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
            public int? SalesmanId { get; set; }
            public string? SalesmanName { get; set; }
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

        private sealed class PartyImportPreviewDto
        {
            public int TotalRows { get; set; }
            public int ValidCount { get; set; }
            public int ErrorCount { get; set; }
            public int WarningCount { get; set; }
            public int NewCount { get; set; }
            public int UpdateCount { get; set; }
            public List<PartyImportRowDto> Rows { get; set; } = new();
        }

        private async Task<PartyImportPreviewDto> ValidateCustomerImportAsync(Stream inputStream)
        {
            var preview = new PartyImportPreviewDto();
            var rawRows = ExtractPartyRawRows(inputStream, "العملاء");
            if (rawRows.Count == 0)
            {
                preview.ErrorCount = 1;
                return preview;
            }

            var branches = await _context.Branches.IgnoreQueryFilters().Where(b => b.IsActive).AsNoTracking().ToListAsync();
            var categories = await _context.CustomerCategories.IgnoreQueryFilters().Where(c => c.IsActive).AsNoTracking().ToListAsync();
            var accounts = await _context.GLAccounts.IgnoreQueryFilters().Where(a => a.IsActive).AsNoTracking().ToListAsync();
            var currencies = await _context.Currencies.IgnoreQueryFilters().Where(c => c.IsActive).AsNoTracking().ToListAsync();
            var salesmen = await _context.Salesmen.IgnoreQueryFilters().Where(s => s.IsActive).AsNoTracking().ToListAsync();
            var governorates = await _context.Governorates.IgnoreQueryFilters().Where(g => g.IsActive).AsNoTracking().ToListAsync();
            var cities = await _context.Cities.IgnoreQueryFilters().Where(c => c.IsActive).AsNoTracking().ToListAsync();
            var existingCustomers = await _context.Customers.IgnoreQueryFilters().AsNoTracking().Select(c => new { c.BranchId, c.Code, c.NameAr }).ToListAsync();

            var existingSet = existingCustomers
                .GroupBy(c => $"{c.BranchId}:{c.Code}", StringComparer.OrdinalIgnoreCase)
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
                string salesmanStr = GetCol(7);
                string taxNo = GetCol(8);
                string commReg = GetCol(9);
                string taxType = GetCol(10);
                string govStr = GetCol(11);
                string cityStr = GetCol(12);
                string address = GetCol(13);
                string mobile1 = GetCol(14);
                string mobile2 = GetCol(15);
                string phone = GetCol(16);
                string email = GetCol(17);
                string notes = GetCol(18);

                if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(branchStr))
                    continue;

                if (branchStr.StartsWith("دليل وإرشادات") || branchStr.Equals("العمود") || branchStr.Equals("كود الفرع") ||
                    (branchStr.Length == 1 && char.IsLetter(branchStr[0]) && (code.Contains("كود") || code.Contains("اسم") || code.Contains("فئة") || code.Contains("المندوب") || code.Contains("الرقم") || code.Contains("السجل") || code.Contains("نوع") || code.Contains("المحافظة") || code.Contains("المدينة") || code.Contains("العنوان") || code.Contains("الموبايل") || code.Contains("الهاتف") || code.Contains("البريد") || code.Contains("ملاحظات"))))
                    continue;

                var dto = new PartyImportRowDto
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
                    dto.Errors.Add("اسم العميل (عربي) مطلوب.");

                // 3. Check Code / Duplicate
                if (!string.IsNullOrWhiteSpace(dto.Code) && dto.BranchId > 0)
                {
                    string key = $"{dto.BranchId}:{dto.Code}";
                    if (!fileBranchCodeSet.Add(key))
                    {
                        dto.Errors.Add($"كود العميل '{dto.Code}' مكرر داخل الملف لنفس الفرع.");
                    }
                    else if (existingSet.ContainsKey(key))
                    {
                        dto.ExistsInDb = true;
                        dto.Warnings.Add("العميل موجود مسبقاً في هذا الفرع وسيتم تحديث بياناته.");
                    }
                }
                else if (string.IsNullOrWhiteSpace(dto.Code))
                {
                    dto.Warnings.Add("سيتم توليد كود العميل تلقائياً عند الحفظ.");
                }

                // 4. Resolve Category
                if (dto.BranchId > 0)
                {
                    CustomerCategory? cat = null;
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
                        dto.Errors.Add("لا توجد فئة عملاء معرفة لهذا الفرع.");
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
                    dto.Errors.Add("يرجى تحديد الحساب المالي للعميل أو ربط فئة العميل بحساب رقابي افتراضي.");
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

                // 7. Resolve Salesman
                if (!string.IsNullOrWhiteSpace(salesmanStr) && dto.BranchId > 0)
                {
                    var sm = salesmen.FirstOrDefault(s => s.BranchId == dto.BranchId &&
                        (s.Code.Equals(salesmanStr, StringComparison.OrdinalIgnoreCase) ||
                         s.NameAr.Equals(salesmanStr, StringComparison.OrdinalIgnoreCase) ||
                         s.NameEn.Equals(salesmanStr, StringComparison.OrdinalIgnoreCase)));
                    if (sm != null)
                    {
                        dto.SalesmanId = sm.Id;
                        dto.SalesmanName = sm.NameAr;
                    }
                    else
                    {
                        dto.Warnings.Add($"لم يتم العثور على المندوب '{salesmanStr}' في هذا الفرع؛ سيتم التجاهل.");
                    }
                }

                // 8. Resolve Governorate & City
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

        private static List<(int RowNumber, List<string> Cells)> ExtractPartyRawRows(Stream inputStream, string preferredSheetName)
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
                    for (int c = 1; c <= 19; c++)
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

        // ─── Related Party Group (Multi-Currency & Vendor Linking) Endpoints ────────────

        [HttpGet]
        [HasPermission("MasterData.Customers.View")]
        public async Task<IActionResult> GetRelatedRecords(
            int partyId,
            string partyType = "Customer",
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
        [HasPermission("MasterData.Customers.View")]
        public async Task<IActionResult> SearchPartiesForLink(
            string term = "",
            string searchPartyType = "All",
            int branchId = 0,
            string? excludePartyType = "Customer",
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
        [HasPermission("MasterData.Customers.Edit")]
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
        [HasPermission("MasterData.Customers.Edit")]
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
