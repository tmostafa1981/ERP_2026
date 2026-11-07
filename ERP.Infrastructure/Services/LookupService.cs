using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs.Lookups;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class LookupService : ILookupService
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserService? _currentUser;
        private readonly ICurrentUserContext? _currentUserContext;
        private readonly IInventoryAvailabilityService? _availabilityService;

        public LookupService(
            ErpDbContext context, 
            ICurrentUserService? currentUser = null,
            ICurrentUserContext? currentUserContext = null,
            IInventoryAvailabilityService? availabilityService = null)
        {
            _context = context;
            _currentUser = currentUser;
            _currentUserContext = currentUserContext;
            _availabilityService = availabilityService;
        }

        public async Task<LookupPagedResult<LookupResultDto>> SearchAsync(LookupSearchRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            string entityType = request.EntityType?.Trim().ToLowerInvariant() ?? string.Empty;
            int page = Math.Max(1, request.Page);
            int pageSize = Math.Clamp(request.PageSize, 1, 100);
            string? query = request.Query?.Trim();

            int? activeBranchId = (request.BranchId.HasValue && request.BranchId.Value > 0)
                ? request.BranchId
                : (_currentUser?.BranchId > 0 ? _currentUser.BranchId : (_currentUserContext?.BranchId > 0 ? _currentUserContext.BranchId : null));

            return entityType switch
            {
                "account" => await SearchAccountsAsync(query, page, pageSize, request.Filters, activeBranchId, cancellationToken),
                "customer" => await SearchCustomersAsync(query, page, pageSize, request.Filters, activeBranchId, cancellationToken),
                "vendor" => await SearchVendorsAsync(query, page, pageSize, request.Filters, activeBranchId, cancellationToken),
                "costcenter" => await SearchCostCentersAsync(query, page, pageSize, request.Filters, cancellationToken),
                "item" => await SearchItemsAsync(query, page, pageSize, request.Filters, cancellationToken),
                "warehouse" => await SearchWarehousesAsync(query, page, pageSize, request.Filters, activeBranchId, cancellationToken),
                "salesman" => await SearchSalesmenAsync(query, page, pageSize, request.Filters, activeBranchId, cancellationToken),
                "branch" => await SearchBranchesAsync(query, page, pageSize, request.Filters, cancellationToken),
                "currency" => await SearchCurrenciesAsync(query, page, pageSize, request.Filters, cancellationToken),
                "user" => await SearchUsersAsync(query, page, pageSize, request.Filters, cancellationToken),
                "customercategory" => await SearchCustomerCategoriesAsync(query, page, pageSize, request.Filters, activeBranchId, cancellationToken),
                "vendorcategory" => await SearchVendorCategoriesAsync(query, page, pageSize, request.Filters, activeBranchId, cancellationToken),
                "itemgroup" or "itemcategory" => await SearchItemGroupsAsync(query, page, pageSize, request.Filters, cancellationToken),
                "unit" => await SearchUnitsAsync(query, page, pageSize, request.Filters, cancellationToken),
                "bank" => await SearchBanksAsync(query, page, pageSize, request.Filters, cancellationToken),
                _ => new LookupPagedResult<LookupResultDto> { Page = page, PageSize = pageSize }
            };
        }

        public async Task<LookupResultDto?> GetByIdAsync(string entityType, int id, CancellationToken cancellationToken = default)
        {
            string type = entityType?.Trim().ToLowerInvariant() ?? string.Empty;
            return type switch
            {
                "account" => await _context.GLAccounts
                    .AsNoTracking()
                    .Include(a => a.Parent)
                    .Where(a => a.Id == id)
                    .Select(a => new LookupResultDto
                    {
                        Id = a.Id,
                        Code = a.Code,
                        NameAr = a.NameAr,
                        NameEn = a.NameEn,
                        SubTitle = a.Parent != null ? a.Parent.NameAr : null,
                        BadgeText = a.IsControlAccount ? "حساب مراقبة" : (a.IsReconciliationAccount ? "حساب تسوية" : null),
                        BadgeColor = a.IsControlAccount ? "warning" : "info",
                        ExtraData = new
                        {
                            currencyId = a.CurrencyId,
                            requiresCostCenter = a.RequiresCostCenter,
                            isReconciliation = a.IsReconciliationAccount,
                            isTransactional = !a.IsGroup,
                            isGroup = a.IsGroup,
                            isControlAccount = a.IsControlAccount,
                            subLedgerType = (int)a.SubLedgerType,
                            accountUsage = (int)a.AccountUsage
                        }
                    }).FirstOrDefaultAsync(cancellationToken),

                "customer" => await _context.Customers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(c => c.CustomerCategory)
                    .Include(c => c.Branch)
                    .Where(c => c.Id == id)
                    .Select(c => new LookupResultDto
                    {
                        Id = c.Id,
                        Code = c.Code,
                        NameAr = c.NameAr,
                        NameEn = c.NameEn,
                        SubTitle = c.TaxNumber != null ? $"ضريبي: {c.TaxNumber}" : (c.Phone ?? c.Mobile),
                        BadgeText = c.Branch != null ? c.Branch.Name : null,
                        BadgeColor = "primary",
                        ExtraData = new
                        {
                            controlAccountId = c.FinancialAccountId ?? (c.CustomerCategory != null ? c.CustomerCategory.ControlAccountId : null),
                            currencyId = c.CurrencyId,
                            taxNumber = c.TaxNumber,
                            phone = c.Phone ?? c.Mobile
                        }
                    }).FirstOrDefaultAsync(cancellationToken),

                "vendor" => await _context.Vendors
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(v => v.VendorCategory)
                    .Include(v => v.Branch)
                    .Where(v => v.Id == id)
                    .Select(v => new LookupResultDto
                    {
                        Id = v.Id,
                        Code = v.Code,
                        NameAr = v.NameAr,
                        NameEn = v.NameEn,
                        SubTitle = v.TaxNumber != null ? $"ضريبي: {v.TaxNumber}" : (v.Phone ?? v.Mobile),
                        BadgeText = v.Branch != null ? v.Branch.Name : null,
                        BadgeColor = "secondary",
                        ExtraData = new
                        {
                            controlAccountId = v.FinancialAccountId ?? (v.VendorCategory != null ? v.VendorCategory.ControlAccountId : null),
                            currencyId = v.CurrencyId,
                            taxNumber = v.TaxNumber,
                            phone = v.Phone ?? v.Mobile
                        }
                    }).FirstOrDefaultAsync(cancellationToken),

                "costcenter" => await _context.CostCenters
                    .AsNoTracking()
                    .Where(c => c.Id == id)
                    .Select(c => new LookupResultDto
                    {
                        Id = c.Id,
                        Code = c.Code,
                        NameAr = c.NameAr,
                        NameEn = c.NameEn,
                        ExtraData = new { isTransactional = c.IsTransactional }
                    }).FirstOrDefaultAsync(cancellationToken),

                "item" => await _context.Items
                    .AsNoTracking()
                    .Include(i => i.ItemGroup)
                    .Include(i => i.ItemUnits).ThenInclude(u => u.Unit)
                    .Where(i => i.Id == id)
                    .Select(i => new LookupResultDto
                    {
                        Id = i.Id,
                        Code = i.Code,
                        NameAr = i.NameAr,
                        NameEn = i.NameEn,
                        SubTitle = (i.ItemUnits.Where(u => u.IsDefaultSales || u.ConversionFactor == 1.0m).Select(u => u.Unit != null ? "الوحدة: " + (u.Unit.NameAr ?? u.Unit.NameEn) : "").FirstOrDefault() ?? "") +
                                   (!string.IsNullOrWhiteSpace(i.Barcode) ? " | باركود: " + i.Barcode : ""),
                        BadgeText = i.IsService ? "صنف خدمي" : (i.ItemGroup != null ? i.ItemGroup.NameAr : "صنف مخزني"),
                        BadgeColor = i.IsService ? "secondary" : "info",
                        ExtraData = new
                        {
                            isService = i.IsService,
                            itemGroupId = i.ItemGroupId
                        }
                    }).FirstOrDefaultAsync(cancellationToken),

                "warehouse" => await _context.Warehouses
                    .AsNoTracking()
                    .Include(w => w.Branch)
                    .Where(w => w.Id == id)
                    .Select(w => new LookupResultDto
                    {
                        Id = w.Id,
                        Code = w.Code,
                        NameAr = w.NameAr,
                        NameEn = w.NameEn,
                        BadgeText = w.Branch != null ? w.Branch.Name : null,
                        BadgeColor = "primary"
                    }).FirstOrDefaultAsync(cancellationToken),

                "salesman" => await _context.Salesmen
                    .AsNoTracking()
                    .Include(s => s.Branch)
                    .Where(s => s.Id == id)
                    .Select(s => new LookupResultDto
                    {
                        Id = s.Id,
                        Code = s.Code,
                        NameAr = s.NameAr,
                        NameEn = s.NameEn,
                        SubTitle = s.Phone ?? s.Mobile,
                        BadgeText = s.Branch != null ? s.Branch.Name : null,
                        BadgeColor = "secondary"
                    }).FirstOrDefaultAsync(cancellationToken),

                "branch" => await _context.Branches
                    .AsNoTracking()
                    .Where(b => b.Id == id)
                    .Select(b => new LookupResultDto
                    {
                        Id = b.Id,
                        Code = b.Code,
                        NameAr = b.NameAr,
                        NameEn = b.NameEn
                    }).FirstOrDefaultAsync(cancellationToken),

                "currency" => await _context.Currencies
                    .AsNoTracking()
                    .Where(c => c.Id == id)
                    .Select(c => new LookupResultDto
                    {
                        Id = c.Id,
                        Code = c.Code,
                        NameAr = c.NameAr,
                        NameEn = c.NameEn,
                        SubTitle = $"سعر الصرف: {c.ExchangeRate}"
                    }).FirstOrDefaultAsync(cancellationToken),

                "customercategory" => await _context.CustomerCategories
                    .AsNoTracking()
                    .Include(c => c.Branch)
                    .Where(c => c.Id == id)
                    .Select(c => new LookupResultDto
                    {
                        Id = c.Id,
                        Code = c.Code,
                        NameAr = c.NameAr,
                        NameEn = c.NameEn,
                        BadgeText = c.Branch != null ? c.Branch.Name : null,
                        BadgeColor = "primary",
                        ExtraData = new { controlAccountId = c.ControlAccountId, branchId = c.BranchId }
                    }).FirstOrDefaultAsync(cancellationToken),

                "vendorcategory" => await _context.VendorCategories
                    .AsNoTracking()
                    .Include(v => v.Branch)
                    .Where(v => v.Id == id)
                    .Select(v => new LookupResultDto
                    {
                        Id = v.Id,
                        Code = v.Code,
                        NameAr = v.NameAr,
                        NameEn = v.NameEn,
                        BadgeText = v.Branch != null ? v.Branch.Name : null,
                        BadgeColor = "secondary",
                        ExtraData = new { controlAccountId = v.ControlAccountId, branchId = v.BranchId }
                    }).FirstOrDefaultAsync(cancellationToken),

                "itemgroup" or "itemcategory" => await _context.ItemGroups
                    .AsNoTracking()
                    .Where(g => g.Id == id)
                    .Select(g => new LookupResultDto
                    {
                        Id = g.Id,
                        Code = g.Code,
                        NameAr = g.NameAr,
                        NameEn = g.NameEn
                    }).FirstOrDefaultAsync(cancellationToken),

                "unit" => await _context.Units
                    .AsNoTracking()
                    .Where(u => u.Id == id)
                    .Select(u => new LookupResultDto
                    {
                        Id = u.Id,
                        Code = u.Code,
                        NameAr = u.NameAr,
                        NameEn = u.NameEn
                    }).FirstOrDefaultAsync(cancellationToken),

                "bank" => await _context.Banks
                    .AsNoTracking()
                    .Where(b => b.Id == id)
                    .Select(b => new LookupResultDto
                    {
                        Id = b.Id,
                        Code = b.Code,
                        NameAr = b.NameAr,
                        NameEn = b.NameEn,
                        SubTitle = b.SwiftCode != null ? $"سويفت: {b.SwiftCode}" : null,
                        BadgeText = b.SwiftCode,
                        BadgeColor = "primary"
                    }).FirstOrDefaultAsync(cancellationToken),

                _ => null
            };
        }

        public async Task<LookupResultDto?> GetByStringIdAsync(string entityType, string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            string type = entityType?.Trim().ToLowerInvariant() ?? string.Empty;

            if (type == "user")
            {
                return await _context.Users
                    .AsNoTracking()
                    .Where(u => u.Id == id)
                    .Select(u => new LookupResultDto
                    {
                        StringId = u.Id,
                        Code = u.EmployeeCode ?? u.UserName ?? "",
                        NameAr = u.FullNameAr ?? u.FullName ?? "",
                        NameEn = u.FullNameEn ?? u.UserName ?? "",
                        SubTitle = u.Email ?? u.Mobile
                    }).FirstOrDefaultAsync(cancellationToken);
            }

            if (int.TryParse(id, out var intId))
            {
                return await GetByIdAsync(entityType, intId, cancellationToken);
            }

            return null;
        }

        public async Task<LookupResultDto?> GetByCodeAsync(string entityType, string code, int? branchId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            string cleanCode = code.Trim();
            string type = entityType?.Trim().ToLowerInvariant() ?? string.Empty;

            int? effectiveBranchId = (branchId.HasValue && branchId.Value > 0)
                ? branchId
                : (_currentUser?.BranchId > 0 ? _currentUser.BranchId : null);

            return type switch
            {
                "account" => await _context.GLAccounts
                    .AsNoTracking()
                    .Include(a => a.Parent)
                    .Include(a => a.Branch)
                    .Where(a => a.IsActive && a.Code == cleanCode && (!effectiveBranchId.HasValue || a.BranchId == null || a.BranchId == effectiveBranchId.Value))
                    .Select(a => new LookupResultDto
                    {
                        Id = a.Id,
                        Code = a.Code,
                        NameAr = a.NameAr,
                        NameEn = a.NameEn,
                        SubTitle = a.Parent != null ? a.Parent.NameAr : null,
                        BadgeText = a.Branch != null ? a.Branch.Name : (a.IsControlAccount ? "حساب مراقبة" : (a.IsReconciliationAccount ? "حساب تسوية" : null)),
                        BadgeColor = a.Branch != null ? "primary" : (a.IsControlAccount ? "warning" : "info"),
                        ExtraData = new
                        {
                            currencyId = a.CurrencyId,
                            branchId = a.BranchId,
                            branchName = a.Branch != null ? a.Branch.Name : null,
                            requiresCostCenter = a.RequiresCostCenter,
                            isReconciliation = a.IsReconciliationAccount,
                            isTransactional = !a.IsGroup,
                            isGroup = a.IsGroup,
                            isControlAccount = a.IsControlAccount,
                            subLedgerType = (int)a.SubLedgerType,
                            accountUsage = (int)a.AccountUsage
                        }
                    }).FirstOrDefaultAsync(cancellationToken),

                "customer" => await _context.Customers
                    .AsNoTracking()
                    .Include(c => c.CustomerCategory)
                    .Include(c => c.Branch)
                    .Where(c => c.IsActive && c.Code == cleanCode && (!effectiveBranchId.HasValue || c.BranchId == effectiveBranchId.Value))
                    .Select(c => new LookupResultDto
                    {
                        Id = c.Id,
                        Code = c.Code,
                        NameAr = c.NameAr,
                        NameEn = c.NameEn,
                        SubTitle = c.TaxNumber != null ? $"ضريبي: {c.TaxNumber}" : (c.Phone ?? c.Mobile),
                        BadgeText = c.Branch != null ? c.Branch.Name : null,
                        BadgeColor = "primary",
                        ExtraData = new
                        {
                            controlAccountId = c.FinancialAccountId ?? (c.CustomerCategory != null ? c.CustomerCategory.ControlAccountId : null),
                            currencyId = c.CurrencyId,
                            taxNumber = c.TaxNumber,
                            phone = c.Phone ?? c.Mobile
                        }
                    }).FirstOrDefaultAsync(cancellationToken),

                "vendor" => await _context.Vendors
                    .AsNoTracking()
                    .Include(v => v.VendorCategory)
                    .Include(v => v.Branch)
                    .Where(v => v.IsActive && v.Code == cleanCode && (!effectiveBranchId.HasValue || v.BranchId == effectiveBranchId.Value))
                    .Select(v => new LookupResultDto
                    {
                        Id = v.Id,
                        Code = v.Code,
                        NameAr = v.NameAr,
                        NameEn = v.NameEn,
                        SubTitle = v.TaxNumber != null ? $"ضريبي: {v.TaxNumber}" : (v.Phone ?? v.Mobile),
                        BadgeText = v.Branch != null ? v.Branch.Name : null,
                        BadgeColor = "secondary",
                        ExtraData = new
                        {
                            controlAccountId = v.FinancialAccountId ?? (v.VendorCategory != null ? v.VendorCategory.ControlAccountId : null),
                            currencyId = v.CurrencyId,
                            taxNumber = v.TaxNumber,
                            phone = v.Phone ?? v.Mobile
                        }
                    }).FirstOrDefaultAsync(cancellationToken),

                "costcenter" => await _context.CostCenters
                    .AsNoTracking()
                    .Where(c => c.IsActive && c.Code == cleanCode)
                    .Select(c => new LookupResultDto
                    {
                        Id = c.Id,
                        Code = c.Code,
                        NameAr = c.NameAr,
                        NameEn = c.NameEn,
                        ExtraData = new { isTransactional = c.IsTransactional }
                    }).FirstOrDefaultAsync(cancellationToken),

                "item" => await _context.Items
                    .AsNoTracking()
                    .Include(i => i.ItemGroup)
                    .Where(i => i.IsActive && (i.Code == cleanCode || i.Barcode == cleanCode))
                    .Select(i => new LookupResultDto
                    {
                        Id = i.Id,
                        Code = i.Code,
                        NameAr = i.NameAr,
                        NameEn = i.NameEn,
                        SubTitle = i.Barcode != null ? $"باركود: {i.Barcode}" : null,
                        BadgeText = i.ItemGroup != null ? i.ItemGroup.NameAr : null,
                        BadgeColor = "info"
                    }).FirstOrDefaultAsync(cancellationToken),

                "warehouse" => await _context.Warehouses
                    .AsNoTracking()
                    .Include(w => w.Branch)
                    .Where(w => w.IsActive && w.Code == cleanCode && (!effectiveBranchId.HasValue || w.BranchId == effectiveBranchId.Value))
                    .Select(w => new LookupResultDto
                    {
                        Id = w.Id,
                        Code = w.Code,
                        NameAr = w.NameAr,
                        NameEn = w.NameEn,
                        BadgeText = w.Branch != null ? w.Branch.Name : null,
                        BadgeColor = "primary"
                    }).FirstOrDefaultAsync(cancellationToken),

                "salesman" => await _context.Salesmen
                    .AsNoTracking()
                    .Include(s => s.Branch)
                    .Where(s => s.IsActive && s.Code == cleanCode && (!effectiveBranchId.HasValue || s.BranchId == effectiveBranchId.Value))
                    .Select(s => new LookupResultDto
                    {
                        Id = s.Id,
                        Code = s.Code,
                        NameAr = s.NameAr,
                        NameEn = s.NameEn,
                        SubTitle = s.Phone ?? s.Mobile,
                        BadgeText = s.Branch != null ? s.Branch.Name : null,
                        BadgeColor = "secondary"
                    }).FirstOrDefaultAsync(cancellationToken),

                "branch" => await _context.Branches
                    .AsNoTracking()
                    .Where(b => b.IsActive && b.Code == cleanCode)
                    .Select(b => new LookupResultDto
                    {
                        Id = b.Id,
                        Code = b.Code,
                        NameAr = b.NameAr,
                        NameEn = b.NameEn
                    }).FirstOrDefaultAsync(cancellationToken),

                "currency" => await _context.Currencies
                    .AsNoTracking()
                    .Where(c => c.IsActive && c.Code == cleanCode)
                    .Select(c => new LookupResultDto
                    {
                        Id = c.Id,
                        Code = c.Code,
                        NameAr = c.NameAr,
                        NameEn = c.NameEn,
                        SubTitle = $"سعر الصرف: {c.ExchangeRate}"
                    }).FirstOrDefaultAsync(cancellationToken),

                "customercategory" => await _context.CustomerCategories
                    .AsNoTracking()
                    .Include(c => c.Branch)
                    .Where(c => c.IsActive && c.Code == cleanCode && (!effectiveBranchId.HasValue || c.BranchId == effectiveBranchId.Value))
                    .Select(c => new LookupResultDto
                    {
                        Id = c.Id,
                        Code = c.Code,
                        NameAr = c.NameAr,
                        NameEn = c.NameEn,
                        BadgeText = c.Branch != null ? c.Branch.Name : null,
                        BadgeColor = "primary",
                        ExtraData = new { controlAccountId = c.ControlAccountId, branchId = c.BranchId }
                    }).FirstOrDefaultAsync(cancellationToken),

                "vendorcategory" => await _context.VendorCategories
                    .AsNoTracking()
                    .Include(v => v.Branch)
                    .Where(v => v.IsActive && v.Code == cleanCode && (!effectiveBranchId.HasValue || v.BranchId == effectiveBranchId.Value))
                    .Select(v => new LookupResultDto
                    {
                        Id = v.Id,
                        Code = v.Code,
                        NameAr = v.NameAr,
                        NameEn = v.NameEn,
                        BadgeText = v.Branch != null ? v.Branch.Name : null,
                        BadgeColor = "secondary",
                        ExtraData = new { controlAccountId = v.ControlAccountId, branchId = v.BranchId }
                    }).FirstOrDefaultAsync(cancellationToken),

                "itemgroup" or "itemcategory" => await _context.ItemGroups
                    .AsNoTracking()
                    .Where(g => g.IsActive && g.Code == cleanCode)
                    .Select(g => new LookupResultDto
                    {
                        Id = g.Id,
                        Code = g.Code,
                        NameAr = g.NameAr,
                        NameEn = g.NameEn
                    }).FirstOrDefaultAsync(cancellationToken),

                "unit" => await _context.Units
                    .AsNoTracking()
                    .Where(u => u.IsActive && u.Code == cleanCode)
                    .Select(u => new LookupResultDto
                    {
                        Id = u.Id,
                        Code = u.Code,
                        NameAr = u.NameAr,
                        NameEn = u.NameEn
                    }).FirstOrDefaultAsync(cancellationToken),

                "user" => await _context.Users
                    .AsNoTracking()
                    .Where(u => u.IsActive && (u.EmployeeCode == cleanCode || u.UserName == cleanCode))
                    .Select(u => new LookupResultDto
                    {
                        StringId = u.Id,
                        Code = u.EmployeeCode ?? u.UserName ?? "",
                        NameAr = u.FullNameAr ?? u.FullName ?? "",
                        NameEn = u.FullNameEn ?? u.UserName ?? "",
                        SubTitle = u.Email ?? u.Mobile
                    }).FirstOrDefaultAsync(cancellationToken),

                "bank" => await _context.Banks
                    .AsNoTracking()
                    .Where(b => b.IsActive && b.Code == cleanCode)
                    .Select(b => new LookupResultDto
                    {
                        Id = b.Id,
                        Code = b.Code,
                        NameAr = b.NameAr,
                        NameEn = b.NameEn,
                        SubTitle = b.SwiftCode != null ? $"سويفت: {b.SwiftCode}" : null,
                        BadgeText = b.SwiftCode,
                        BadgeColor = "primary"
                    }).FirstOrDefaultAsync(cancellationToken),

                _ => null
            };
        }

        public async Task<BatchResolveResponseDto> ResolveBatchAsync(BatchResolveRequestDto request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            string entityType = request.EntityType?.Trim().ToLowerInvariant() ?? string.Empty;
            var cleanKeys = request.Keys?
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToList() ?? new List<string>();

            var response = new BatchResolveResponseDto
            {
                EntityType = entityType
            };

            if (!cleanKeys.Any())
                return response;

            switch (entityType)
            {
                case "bank":
                {
                    var banks = await _context.Banks
                        .AsNoTracking()
                        .Where(b => b.IsActive && (
                            cleanKeys.Contains(b.Code) ||
                            (b.SwiftCode != null && cleanKeys.Contains(b.SwiftCode)) ||
                            cleanKeys.Contains(b.NameAr) ||
                            cleanKeys.Contains(b.NameEn)))
                        .ToListAsync(cancellationToken);

                    foreach (var key in cleanKeys)
                    {
                        var match = banks.FirstOrDefault(b => b.Code.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? banks.FirstOrDefault(b => b.SwiftCode != null && b.SwiftCode.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? banks.FirstOrDefault(b => b.NameAr.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? banks.FirstOrDefault(b => b.NameEn.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? banks.FirstOrDefault(b => b.NameAr.Contains(key, StringComparison.OrdinalIgnoreCase));

                        response.Results.Add(new BatchResolveItemResultDto
                        {
                            Key = key,
                            Found = match != null,
                            Result = match == null ? null : new LookupResultDto
                            {
                                Id = match.Id,
                                Code = match.Code,
                                NameAr = match.NameAr,
                                NameEn = match.NameEn,
                                SubTitle = match.SwiftCode != null ? $"سويفت: {match.SwiftCode}" : null,
                                BadgeText = match.SwiftCode,
                                BadgeColor = "primary"
                            }
                        });
                    }
                    break;
                }

                case "customer":
                {
                    int? effectiveBranchId = (request.BranchId.HasValue && request.BranchId.Value > 0)
                        ? request.BranchId
                        : (_currentUser?.BranchId > 0 ? _currentUser.BranchId : null);
                    var q = _context.Customers
                        .AsNoTracking()
                        .Include(c => c.CustomerCategory)
                        .Include(c => c.Branch)
                        .Where(c => c.IsActive && (cleanKeys.Contains(c.Code) || cleanKeys.Contains(c.NameAr) || cleanKeys.Contains(c.NameEn)));

                    if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
                    {
                        q = q.Where(c => c.BranchId == effectiveBranchId.Value);
                    }

                    var customers = await q.ToListAsync(cancellationToken);

                    foreach (var key in cleanKeys)
                    {
                        var match = customers.FirstOrDefault(c => c.Code.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? customers.FirstOrDefault(c => c.NameAr.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? customers.FirstOrDefault(c => c.NameEn.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? customers.FirstOrDefault(c => c.NameAr.Contains(key, StringComparison.OrdinalIgnoreCase));

                        response.Results.Add(new BatchResolveItemResultDto
                        {
                            Key = key,
                            Found = match != null,
                            Result = match == null ? null : new LookupResultDto
                            {
                                Id = match.Id,
                                Code = match.Code,
                                NameAr = match.NameAr,
                                NameEn = match.NameEn,
                                SubTitle = match.TaxNumber != null ? $"ضريبي: {match.TaxNumber}" : (match.Phone ?? match.Mobile),
                                BadgeText = match.Branch != null ? match.Branch.Name : null,
                                BadgeColor = "primary",
                                ExtraData = new
                                {
                                    controlAccountId = match.FinancialAccountId ?? (match.CustomerCategory != null ? match.CustomerCategory.ControlAccountId : null),
                                    currencyId = match.CurrencyId,
                                    taxNumber = match.TaxNumber,
                                    phone = match.Phone ?? match.Mobile
                                }
                            }
                        });
                    }
                    break;
                }

                case "vendor":
                {
                    int? effectiveBranchId = (request.BranchId.HasValue && request.BranchId.Value > 0)
                        ? request.BranchId
                        : (_currentUser?.BranchId > 0 ? _currentUser.BranchId : null);
                    var q = _context.Vendors
                        .AsNoTracking()
                        .Include(v => v.VendorCategory)
                        .Include(v => v.Branch)
                        .Where(v => v.IsActive && (cleanKeys.Contains(v.Code) || cleanKeys.Contains(v.NameAr) || cleanKeys.Contains(v.NameEn)));

                    if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
                    {
                        q = q.Where(v => v.BranchId == effectiveBranchId.Value);
                    }

                    var vendors = await q.ToListAsync(cancellationToken);

                    foreach (var key in cleanKeys)
                    {
                        var match = vendors.FirstOrDefault(v => v.Code.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? vendors.FirstOrDefault(v => v.NameAr.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? vendors.FirstOrDefault(v => v.NameEn.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? vendors.FirstOrDefault(v => v.NameAr.Contains(key, StringComparison.OrdinalIgnoreCase));

                        response.Results.Add(new BatchResolveItemResultDto
                        {
                            Key = key,
                            Found = match != null,
                            Result = match == null ? null : new LookupResultDto
                            {
                                Id = match.Id,
                                Code = match.Code,
                                NameAr = match.NameAr,
                                NameEn = match.NameEn,
                                SubTitle = match.TaxNumber != null ? $"ضريبي: {match.TaxNumber}" : (match.Phone ?? match.Mobile),
                                BadgeText = match.Branch != null ? match.Branch.Name : null,
                                BadgeColor = "secondary",
                                ExtraData = new
                                {
                                    controlAccountId = match.FinancialAccountId ?? (match.VendorCategory != null ? match.VendorCategory.ControlAccountId : null),
                                    currencyId = match.CurrencyId,
                                    taxNumber = match.TaxNumber,
                                    phone = match.Phone ?? match.Mobile
                                }
                            }
                        });
                    }
                    break;
                }

                case "account":
                {
                    int? effectiveBranchId = (request.BranchId.HasValue && request.BranchId.Value > 0)
                        ? request.BranchId
                        : (_currentUser?.BranchId > 0 ? _currentUser.BranchId : null);
                    var q = _context.GLAccounts
                        .AsNoTracking()
                        .Include(a => a.Parent)
                        .Include(a => a.Branch)
                        .Where(a => a.IsActive && !a.IsGroup && (cleanKeys.Contains(a.Code) || cleanKeys.Contains(a.NameAr) || cleanKeys.Contains(a.NameEn)));

                    if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
                    {
                        q = q.Where(a => a.BranchId == null || a.BranchId == effectiveBranchId.Value);
                    }

                    var accounts = await q.ToListAsync(cancellationToken);

                    foreach (var key in cleanKeys)
                    {
                        var match = accounts.FirstOrDefault(a => a.Code.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? accounts.FirstOrDefault(a => a.NameAr.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? accounts.FirstOrDefault(a => a.NameEn.Equals(key, StringComparison.OrdinalIgnoreCase))
                                 ?? accounts.FirstOrDefault(a => a.NameAr.Contains(key, StringComparison.OrdinalIgnoreCase));

                        response.Results.Add(new BatchResolveItemResultDto
                        {
                            Key = key,
                            Found = match != null,
                            Result = match == null ? null : new LookupResultDto
                            {
                                Id = match.Id,
                                Code = match.Code,
                                NameAr = match.NameAr,
                                NameEn = match.NameEn,
                                SubTitle = match.Parent != null ? match.Parent.NameAr : null,
                                BadgeText = match.Branch != null ? match.Branch.Name : (match.IsControlAccount ? "حساب مراقبة" : null),
                                BadgeColor = match.Branch != null ? "primary" : "warning",
                                ExtraData = new
                                {
                                    currencyId = match.CurrencyId,
                                    branchId = match.BranchId,
                                    requiresCostCenter = match.RequiresCostCenter,
                                    isControlAccount = match.IsControlAccount,
                                    subLedgerType = (int)match.SubLedgerType
                                }
                            }
                        });
                    }
                    break;
                }
            }

            return response;
        }

        // ─── Private Search Handlers ─────────────────────────────────────────

        private async Task<LookupPagedResult<LookupResultDto>> SearchAccountsAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, int? branchId, CancellationToken cancellationToken)
        {
            var q = _context.GLAccounts
                .AsNoTracking()
                .Include(a => a.Parent)
                .Include(a => a.Branch)
                .Where(a => a.IsActive);

            int? effectiveBranchId = branchId;
            if (filters != null && filters.TryGetValue("branchId", out var bVal) && int.TryParse(bVal, out var parsedBranch) && parsedBranch > 0)
            {
                effectiveBranchId = parsedBranch;
            }

            if (effectiveBranchId.HasValue)
            {
                q = q.Where(a => a.BranchId == null || a.BranchId == effectiveBranchId.Value);
            }

            bool transactionalOnly = true;
            if (filters != null && filters.TryGetValue("transactionalOnly", out var transVal) && bool.TryParse(transVal, out var parsedTrans))
                transactionalOnly = parsedTrans;

            if (transactionalOnly)
                q = q.Where(a => !a.IsGroup);

            if (filters != null && filters.TryGetValue("currencyId", out var curVal) && int.TryParse(curVal, out var curId) && curId > 0)
                q = q.Where(a => a.CurrencyId == null || a.CurrencyId == curId);

            if (filters != null && filters.TryGetValue("accountCategory", out var catVal) && !string.IsNullOrWhiteSpace(catVal))
            {
                if (int.TryParse(catVal, out var catInt) && Enum.IsDefined(typeof(AccountCategory), catInt))
                {
                    var catEnum = (AccountCategory)catInt;
                    q = q.Where(a => a.AccountCategory == catEnum);
                }
                else if (Enum.TryParse<AccountCategory>(catVal, true, out var parsedCat))
                {
                    q = q.Where(a => a.AccountCategory == parsedCat);
                }
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(a => a.Code.Contains(t) || a.NameAr.Contains(t) || a.NameEn.Contains(t));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(a => a.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new LookupResultDto
                {
                    Id = a.Id,
                    Code = a.Code,
                    NameAr = a.NameAr,
                    NameEn = a.NameEn,
                    SubTitle = a.Parent != null ? a.Parent.NameAr : null,
                    BadgeText = a.Branch != null ? a.Branch.Name : (a.IsControlAccount ? "حساب مراقبة" : (a.IsReconciliationAccount ? "حساب تسوية" : (a.RequiresCostCenter ? "يتطلب مركز تكلفة" : null))),
                    BadgeColor = a.Branch != null ? "primary" : (a.IsControlAccount ? "warning" : (a.IsReconciliationAccount ? "info" : "secondary")),
                    ExtraData = new
                    {
                        currencyId = a.CurrencyId,
                        branchId = a.BranchId,
                        branchName = a.Branch != null ? a.Branch.Name : null,
                        requiresCostCenter = a.RequiresCostCenter,
                        isReconciliation = a.IsReconciliationAccount,
                        isTransactional = !a.IsGroup,
                        isGroup = a.IsGroup,
                        isControlAccount = a.IsControlAccount,
                        subLedgerType = (int)a.SubLedgerType,
                        accountUsage = (int)a.AccountUsage
                    }
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchCustomersAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, int? branchId, CancellationToken cancellationToken)
        {
            var q = _context.Customers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(c => c.CustomerCategory)
                .Include(c => c.Branch)
                .Where(c => c.IsActive);

            int? effectiveBranchId = branchId;
            if (filters != null && filters.TryGetValue("branchId", out var bVal) && int.TryParse(bVal, out var pb) && pb > 0)
                effectiveBranchId = pb;

            if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                if (_currentUserContext != null && !_currentUserContext.HasAllBranches && !_currentUserContext.CanAccessBranch(effectiveBranchId.Value))
                {
                    return new LookupPagedResult<LookupResultDto> { Page = page, PageSize = pageSize };
                }
                q = q.Where(c => c.BranchId == effectiveBranchId.Value);
            }
            else if (_currentUserContext != null && !_currentUserContext.HasAllBranches)
            {
                var allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUserContext.UserId && ub.Branch.IsActive)
                    .Select(ub => ub.BranchId)
                    .ToListAsync(cancellationToken);

                if (allowedBranchIds.Any())
                {
                    q = q.Where(c => allowedBranchIds.Contains(c.BranchId));
                }
                else if (_currentUserContext.BranchId > 0)
                {
                    q = q.Where(c => c.BranchId == _currentUserContext.BranchId);
                }
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(c => c.Code.Contains(t) ||
                                     c.NameAr.Contains(t) ||
                                     c.NameEn.Contains(t) ||
                                     (c.TaxNumber != null && c.TaxNumber.Contains(t)) ||
                                     (c.Phone != null && c.Phone.Contains(t)) ||
                                     (c.Mobile != null && c.Mobile.Contains(t)) ||
                                     (c.Mobile2 != null && c.Mobile2.Contains(t)));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(c => c.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new LookupResultDto
                {
                    Id = c.Id,
                    Code = c.Code,
                    NameAr = c.NameAr,
                    NameEn = c.NameEn,
                    SubTitle = c.TaxNumber != null ? $"ضريبي: {c.TaxNumber}" : (c.Phone ?? c.Mobile),
                    BadgeText = c.Branch != null ? c.Branch.Name : null,
                    BadgeColor = "primary",
                    ExtraData = new
                    {
                        branchId = c.BranchId,
                        branchName = c.Branch != null ? c.Branch.Name : null,
                        controlAccountId = c.FinancialAccountId ?? (c.CustomerCategory != null ? c.CustomerCategory.ControlAccountId : null),
                        currencyId = c.CurrencyId,
                        taxNumber = c.TaxNumber,
                        phone = c.Phone ?? c.Mobile
                    }
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchVendorsAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, int? branchId, CancellationToken cancellationToken)
        {
            var q = _context.Vendors
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(v => v.VendorCategory)
                .Include(v => v.Branch)
                .Where(v => v.IsActive);

            int? effectiveBranchId = branchId;
            if (filters != null && filters.TryGetValue("branchId", out var bVal) && int.TryParse(bVal, out var pb) && pb > 0)
                effectiveBranchId = pb;

            if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                if (_currentUserContext != null && !_currentUserContext.HasAllBranches && !_currentUserContext.CanAccessBranch(effectiveBranchId.Value))
                {
                    return new LookupPagedResult<LookupResultDto> { Page = page, PageSize = pageSize };
                }
                q = q.Where(v => v.BranchId == effectiveBranchId.Value);
            }
            else if (_currentUserContext != null && !_currentUserContext.HasAllBranches)
            {
                var allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUserContext.UserId && ub.Branch.IsActive)
                    .Select(ub => ub.BranchId)
                    .ToListAsync(cancellationToken);

                if (allowedBranchIds.Any())
                {
                    q = q.Where(v => allowedBranchIds.Contains(v.BranchId));
                }
                else if (_currentUserContext.BranchId > 0)
                {
                    q = q.Where(v => v.BranchId == _currentUserContext.BranchId);
                }
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(v => v.Code.Contains(t) ||
                                     v.NameAr.Contains(t) ||
                                     v.NameEn.Contains(t) ||
                                     (v.TaxNumber != null && v.TaxNumber.Contains(t)) ||
                                     (v.Phone != null && v.Phone.Contains(t)) ||
                                     (v.Mobile != null && v.Mobile.Contains(t)) ||
                                     (v.Mobile2 != null && v.Mobile2.Contains(t)));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(v => v.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(v => new LookupResultDto
                {
                    Id = v.Id,
                    Code = v.Code,
                    NameAr = v.NameAr,
                    NameEn = v.NameEn,
                    SubTitle = v.TaxNumber != null ? $"ضريبي: {v.TaxNumber}" : (v.Phone ?? v.Mobile),
                    BadgeText = v.Branch != null ? v.Branch.Name : null,
                    BadgeColor = "secondary",
                    ExtraData = new
                    {
                        branchId = v.BranchId,
                        branchName = v.Branch != null ? v.Branch.Name : null,
                        controlAccountId = v.FinancialAccountId ?? (v.VendorCategory != null ? v.VendorCategory.ControlAccountId : null),
                        currencyId = v.CurrencyId,
                        taxNumber = v.TaxNumber,
                        phone = v.Phone ?? v.Mobile
                    }
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchCostCentersAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, CancellationToken cancellationToken)
        {
            var q = _context.CostCenters
                .AsNoTracking()
                .Where(c => c.IsActive);

            bool transactionalOnly = true;
            if (filters != null && filters.TryGetValue("transactionalOnly", out var transVal) && bool.TryParse(transVal, out var parsedTrans))
                transactionalOnly = parsedTrans;

            if (transactionalOnly)
                q = q.Where(c => c.IsTransactional);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(c => c.Code.Contains(t) || c.NameAr.Contains(t) || c.NameEn.Contains(t));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(c => c.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new LookupResultDto
                {
                    Id = c.Id,
                    Code = c.Code,
                    NameAr = c.NameAr,
                    NameEn = c.NameEn,
                    ExtraData = new { isTransactional = c.IsTransactional }
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchItemsAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, CancellationToken cancellationToken)
        {
            var q = _context.Items
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(i => i.ItemGroup)
                .Include(i => i.ItemUnits)
                    .ThenInclude(u => u.Unit)
                .Where(i => i.IsActive);

            // Filter item group
            if (filters != null && filters.TryGetValue("itemGroupId", out var grpVal) && int.TryParse(grpVal, out var gId) && gId > 0)
            {
                q = q.Where(i => i.ItemGroupId == gId);
            }

            // Filter physical inventory items vs services
            if (filters != null && filters.TryGetValue("inventoryOnly", out var invVal) && bool.TryParse(invVal, out var invOnly) && invOnly)
            {
                q = q.Where(i => !i.IsService);
            }
            else if (filters != null && filters.TryGetValue("serviceOnly", out var srvVal) && bool.TryParse(srvVal, out var srvOnly) && srvOnly)
            {
                q = q.Where(i => i.IsService);
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(i => i.Code.Contains(t) ||
                                     i.NameAr.Contains(t) ||
                                     i.NameEn.Contains(t) ||
                                     (i.Barcode != null && i.Barcode.Contains(t)));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var rawItems = await q
                .OrderBy(i => i.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            int? branchId = null;
            int? warehouseId = null;
            if (filters != null && filters.TryGetValue("branchId", out var bStr) && int.TryParse(bStr, out var bValId) && bValId > 0)
                branchId = bValId;
            if (filters != null && filters.TryGetValue("warehouseId", out var wStr) && int.TryParse(wStr, out var wValId) && wValId > 0)
                warehouseId = wValId;

            var resultItems = new List<LookupResultDto>();
            foreach (var i in rawItems)
            {
                var baseUnit = i.ItemUnits?.FirstOrDefault(u => u.IsDefaultSales && u.IsActive)?.Unit
                            ?? i.ItemUnits?.FirstOrDefault(u => u.ConversionFactor == 1.0m && u.IsActive)?.Unit
                            ?? i.ItemUnits?.FirstOrDefault(u => u.IsActive)?.Unit;
                string unitName = baseUnit != null ? (!string.IsNullOrWhiteSpace(baseUnit.NameAr) ? baseUnit.NameAr : baseUnit.NameEn) : "";

                string subTitle = "";
                if (!string.IsNullOrWhiteSpace(unitName))
                    subTitle += $"الوحدة: {unitName}";
                if (!string.IsNullOrWhiteSpace(i.Barcode))
                    subTitle += (string.IsNullOrEmpty(subTitle) ? "" : " | ") + $"باركود: {i.Barcode}";

                decimal? onHandStock = null;
                if (_availabilityService != null && !i.IsService)
                {
                    try
                    {
                        int effectiveBranch = branchId ?? (_currentUserContext?.BranchId > 0 ? _currentUserContext.BranchId : (_currentUser?.BranchId > 0 ? _currentUser.BranchId : 1));
                        var avail = await _availabilityService.GetAvailabilityAsync(i.Id, effectiveBranch, warehouseId, baseUnit?.Id, 0, null, cancellationToken);
                        if (avail != null)
                        {
                            onHandStock = avail.OnHandQuantity;
                            subTitle += $" | الرصيد: {avail.OnHandQuantity:G29} {unitName}";
                        }
                    }
                    catch { }
                }

                resultItems.Add(new LookupResultDto
                {
                    Id = i.Id,
                    Code = i.Code,
                    NameAr = i.NameAr,
                    NameEn = i.NameEn,
                    SubTitle = string.IsNullOrWhiteSpace(subTitle) ? null : subTitle,
                    BadgeText = i.IsService ? "صنف خدمي" : (i.ItemGroup != null ? i.ItemGroup.NameAr : "صنف مخزني"),
                    BadgeColor = i.IsService ? "secondary" : "info",
                    Balance = onHandStock,
                    ExtraData = new
                    {
                        unitName = unitName,
                        unitId = baseUnit?.Id,
                        isService = i.IsService,
                        onHandQuantity = onHandStock,
                        balance = onHandStock
                    }
                });
            }

            return new LookupPagedResult<LookupResultDto>
            {
                Items = resultItems,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchWarehousesAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, int? branchId, CancellationToken cancellationToken)
        {
            var q = _context.Warehouses
                .AsNoTracking()
                .Include(w => w.Branch)
                .Where(w => w.IsActive);

            int? effectiveBranchId = branchId;
            if (filters != null && filters.TryGetValue("branchId", out var bVal) && int.TryParse(bVal, out var pb) && pb > 0)
                effectiveBranchId = pb;

            if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                q = q.Where(w => w.BranchId == effectiveBranchId.Value);
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(w => w.Code.Contains(t) || w.NameAr.Contains(t) || w.NameEn.Contains(t));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(w => w.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(w => new LookupResultDto
                {
                    Id = w.Id,
                    Code = w.Code,
                    NameAr = w.NameAr,
                    NameEn = w.NameEn,
                    BadgeText = w.Branch != null ? w.Branch.Name : null,
                    BadgeColor = "secondary"
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchSalesmenAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, int? branchId, CancellationToken cancellationToken)
        {
            var q = _context.Salesmen
                .AsNoTracking()
                .Where(s => s.IsActive);

            int? effectiveBranchId = branchId;
            if (filters != null && filters.TryGetValue("branchId", out var bVal) && int.TryParse(bVal, out var pb) && pb > 0)
                effectiveBranchId = pb;

            if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                q = q.Where(s => s.BranchId == effectiveBranchId.Value);
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(s => s.Code.Contains(t) || s.NameAr.Contains(t) || s.NameEn.Contains(t));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(s => s.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new LookupResultDto
                {
                    Id = s.Id,
                    Code = s.Code,
                    NameAr = s.NameAr,
                    NameEn = s.NameEn
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchBranchesAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, CancellationToken cancellationToken)
        {
            var q = _context.Branches
                .AsNoTracking()
                .Where(b => b.IsActive);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(b => b.BranchCode.Contains(t) || b.Name.Contains(t) || (b.NameAr != null && b.NameAr.Contains(t)) || (b.NameEn != null && b.NameEn.Contains(t)));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(b => b.BranchCode)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(b => new LookupResultDto
                {
                    Id = b.Id,
                    Code = b.BranchCode,
                    NameAr = b.NameAr ?? b.Name,
                    NameEn = b.NameEn ?? b.Name
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchCurrenciesAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, CancellationToken cancellationToken)
        {
            var q = _context.Currencies
                .AsNoTracking()
                .Where(c => c.IsActive);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(c => c.Code.Contains(t) || c.NameAr.Contains(t) || c.NameEn.Contains(t));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(c => c.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new LookupResultDto
                {
                    Id = c.Id,
                    Code = c.Code,
                    NameAr = c.NameAr,
                    NameEn = c.NameEn,
                    SubTitle = $"سعر الصرف: {c.ExchangeRate}"
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchUsersAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, CancellationToken cancellationToken)
        {
            var q = _context.Users
                .AsNoTracking()
                .Where(u => u.IsActive);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(u => (u.EmployeeCode != null && u.EmployeeCode.Contains(t)) ||
                                     (u.UserName != null && u.UserName.Contains(t)) ||
                                     (u.FullName != null && u.FullName.Contains(t)) ||
                                     (u.FullNameAr != null && u.FullNameAr.Contains(t)) ||
                                     (u.FullNameEn != null && u.FullNameEn.Contains(t)) ||
                                     (u.Email != null && u.Email.Contains(t)) ||
                                     (u.Mobile != null && u.Mobile.Contains(t)));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(u => u.EmployeeCode ?? u.UserName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new LookupResultDto
                {
                    StringId = u.Id,
                    Code = u.EmployeeCode ?? u.UserName ?? "",
                    NameAr = u.FullNameAr ?? u.FullName ?? "",
                    NameEn = u.FullNameEn ?? u.UserName ?? "",
                    SubTitle = u.Email ?? u.Mobile
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchCustomerCategoriesAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, int? branchId, CancellationToken cancellationToken)
        {
            var q = _context.CustomerCategories
                .AsNoTracking()
                .Include(c => c.Branch)
                .Where(c => c.IsActive);

            if (branchId.HasValue && branchId.Value > 0)
                q = q.Where(c => c.BranchId == branchId.Value);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(c => c.Code.Contains(t) || c.NameAr.Contains(t) || c.NameEn.Contains(t));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(c => c.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new LookupResultDto
                {
                    Id = c.Id,
                    Code = c.Code,
                    NameAr = c.NameAr,
                    NameEn = c.NameEn,
                    BadgeText = c.Branch != null ? c.Branch.Name : null,
                    BadgeColor = "primary",
                    ExtraData = new { controlAccountId = c.ControlAccountId, branchId = c.BranchId }
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchVendorCategoriesAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, int? branchId, CancellationToken cancellationToken)
        {
            var q = _context.VendorCategories
                .AsNoTracking()
                .Include(v => v.Branch)
                .Where(v => v.IsActive);

            if (branchId.HasValue && branchId.Value > 0)
                q = q.Where(v => v.BranchId == branchId.Value);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(v => v.Code.Contains(t) || v.NameAr.Contains(t) || v.NameEn.Contains(t));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(v => v.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(v => new LookupResultDto
                {
                    Id = v.Id,
                    Code = v.Code,
                    NameAr = v.NameAr,
                    NameEn = v.NameEn,
                    BadgeText = v.Branch != null ? v.Branch.Name : null,
                    BadgeColor = "secondary",
                    ExtraData = new { controlAccountId = v.ControlAccountId, branchId = v.BranchId }
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchItemGroupsAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, CancellationToken cancellationToken)
        {
            var q = _context.ItemGroups
                .AsNoTracking()
                .Where(g => g.IsActive);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(g => g.Code.Contains(t) || g.NameAr.Contains(t) || g.NameEn.Contains(t));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(g => g.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(g => new LookupResultDto
                {
                    Id = g.Id,
                    Code = g.Code,
                    NameAr = g.NameAr,
                    NameEn = g.NameEn
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchUnitsAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, CancellationToken cancellationToken)
        {
            var q = _context.Units
                .AsNoTracking()
                .Where(u => u.IsActive);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(u => u.Code.Contains(t) || u.NameAr.Contains(t) || u.NameEn.Contains(t));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(u => u.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new LookupResultDto
                {
                    Id = u.Id,
                    Code = u.Code,
                    NameAr = u.NameAr,
                    NameEn = u.NameEn
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<LookupPagedResult<LookupResultDto>> SearchBanksAsync(
            string? query, int page, int pageSize, Dictionary<string, string>? filters, CancellationToken cancellationToken)
        {
            var q = _context.Banks
                .AsNoTracking()
                .Where(b => b.IsActive);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    string t = term.Trim();
                    q = q.Where(b => b.Code.Contains(t) || b.NameAr.Contains(t) || b.NameEn.Contains(t) || (b.SwiftCode != null && b.SwiftCode.Contains(t)));
                }
            }

            int total = await q.CountAsync(cancellationToken);
            var items = await q
                .OrderBy(b => b.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(b => new LookupResultDto
                {
                    Id = b.Id,
                    Code = b.Code,
                    NameAr = b.NameAr,
                    NameEn = b.NameEn,
                    SubTitle = b.SwiftCode != null ? $"SWIFT: {b.SwiftCode}" : null,
                    BadgeText = b.Code,
                    BadgeColor = "primary"
                })
                .ToListAsync(cancellationToken);

            return new LookupPagedResult<LookupResultDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }
    }
}
