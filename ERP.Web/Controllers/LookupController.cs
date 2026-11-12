using ERP.Application.Interfaces;
using ERP.Infrastructure.Data;
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
    [Route("api/[controller]")]
    [ApiController]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class LookupController : ControllerBase
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IInventoryAvailabilityService? _availabilityService;
        private const int PageSize = 20;

        public LookupController(
            ErpDbContext context, 
            ICurrentUserContext currentUser,
            IInventoryAvailabilityService? availabilityService = null)
        {
            _context = context;
            _currentUser = currentUser;
            _availabilityService = availabilityService;
        }

        [HttpGet("customer")]
        public async Task<IActionResult> Customers([FromQuery] string? q, [FromQuery] int? branchId, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.Customers.AsNoTracking().Where(c => c.IsActive);

            // Single-Active-Branch Session Rule: Always isolate by active branch
            int targetBranchId = (branchId.HasValue && branchId.Value > 0) ? branchId.Value : _currentUser.BranchId;
            if (targetBranchId > 0)
            {
                query = query.Where(c => c.BranchId == targetBranchId);
            }
            else
            {
                return Ok(new { results = Array.Empty<object>(), pagination = new { more = false } });
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(c =>
                    c.Code.Contains(q) ||
                    c.NameAr.Contains(q) ||
                    c.NameEn.Contains(q) ||
                    (c.TaxNumber != null && c.TaxNumber.Contains(q)) ||
                    (c.Mobile != null && c.Mobile.Contains(q)));
            }

            int total = await query.CountAsync();
            var items = await query
                .Include(c => c.Currency)
                .Include(c => c.FinancialAccount)
                .Include(c => c.CustomerCategory)
                    .ThenInclude(cc => cc!.ControlAccount)
                .OrderBy(c => c.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(c => new
                {
                    id = c.Id,
                    text = $"{c.Code} - {c.NameAr} ({c.NameEn})",
                    code = c.Code,
                    name = c.NameAr,
                    nameEn = c.NameEn,
                    currencyId = c.CurrencyId,
                    currencyCode = c.Currency != null ? c.Currency.Code : null,
                    currencyName = c.Currency != null ? c.Currency.NameAr : null,
                    phone = c.Phone ?? c.Mobile,
                    taxNumber = c.TaxNumber,
                    controlAccountId = c.FinancialAccountId ?? (c.CustomerCategory != null ? c.CustomerCategory.ControlAccountId : null),
                    controlAccountCode = c.FinancialAccount != null ? c.FinancialAccount.Code : (c.CustomerCategory != null && c.CustomerCategory.ControlAccount != null ? c.CustomerCategory.ControlAccount.Code : ""),
                    controlAccountName = c.FinancialAccount != null ? c.FinancialAccount.NameAr : (c.CustomerCategory != null && c.CustomerCategory.ControlAccount != null ? c.CustomerCategory.ControlAccount.NameAr : "")
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("vendor")]
        public async Task<IActionResult> Vendors([FromQuery] string? q, [FromQuery] int? branchId, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.Vendors.AsNoTracking().Where(v => v.IsActive);

            // Single-Active-Branch Session Rule: Always isolate by active branch
            int targetBranchId = (branchId.HasValue && branchId.Value > 0) ? branchId.Value : _currentUser.BranchId;
            if (targetBranchId > 0)
            {
                query = query.Where(v => v.BranchId == targetBranchId);
            }
            else
            {
                return Ok(new { results = Array.Empty<object>(), pagination = new { more = false } });
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(v =>
                    v.Code.Contains(q) ||
                    v.NameAr.Contains(q) ||
                    v.NameEn.Contains(q) ||
                    (v.TaxNumber != null && v.TaxNumber.Contains(q)) ||
                    (v.Mobile != null && v.Mobile.Contains(q)));
            }

            int total = await query.CountAsync();
            var items = await query
                .Include(v => v.Currency)
                .Include(v => v.FinancialAccount)
                .Include(v => v.VendorCategory)
                    .ThenInclude(vc => vc!.ControlAccount)
                .OrderBy(v => v.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(v => new
                {
                    id = v.Id,
                    text = $"{v.Code} - {v.NameAr} ({v.NameEn})",
                    code = v.Code,
                    name = v.NameAr,
                    nameEn = v.NameEn,
                    currencyId = v.CurrencyId,
                    currencyCode = v.Currency != null ? v.Currency.Code : null,
                    currencyName = v.Currency != null ? v.Currency.NameAr : null,
                    phone = v.Phone ?? v.Mobile,
                    taxNumber = v.TaxNumber,
                    controlAccountId = v.FinancialAccountId ?? (v.VendorCategory != null ? v.VendorCategory.ControlAccountId : null),
                    controlAccountCode = v.FinancialAccount != null ? v.FinancialAccount.Code : (v.VendorCategory != null && v.VendorCategory.ControlAccount != null ? v.VendorCategory.ControlAccount.Code : ""),
                    controlAccountName = v.FinancialAccount != null ? v.FinancialAccount.NameAr : (v.VendorCategory != null && v.VendorCategory.ControlAccount != null ? v.VendorCategory.ControlAccount.NameAr : "")
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("salesman")]
        public async Task<IActionResult> Salesmen([FromQuery] string? q, [FromQuery] int? branchId, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.Salesmen.AsNoTracking().Where(s => s.IsActive);

            int targetBranchId = _currentUser.BranchId;
            if (targetBranchId > 0)
            {
                query = query.Where(s => s.BranchId == targetBranchId);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(s =>
                    s.Code.Contains(q) ||
                    s.NameAr.Contains(q) ||
                    s.NameEn.Contains(q) ||
                    (s.Mobile != null && s.Mobile.Contains(q)));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(s => s.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(s => new
                {
                    id = s.Id,
                    text = $"{s.Code} - {s.NameAr} ({s.NameEn})",
                    code = s.Code,
                    name = s.NameAr
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("warehouse")]
        public async Task<IActionResult> Warehouses([FromQuery] string? q, [FromQuery] int? branchId, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.Warehouses.AsNoTracking().Where(w => w.IsActive);

            int targetBranchId = _currentUser.BranchId;
            if (targetBranchId > 0)
            {
                query = query.Where(w => w.BranchId == targetBranchId);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(w =>
                    w.Code.Contains(q) ||
                    w.NameAr.Contains(q) ||
                    w.NameEn.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(w => w.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(w => new
                {
                    id = w.Id,
                    text = $"{w.Code} - {w.NameAr} ({w.NameEn})",
                    code = w.Code,
                    name = w.NameAr
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("customerCategory")]
        public async Task<IActionResult> CustomerCategories([FromQuery] string? q, [FromQuery] int? branchId, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.CustomerCategories
                .Include(c => c.ControlAccount)
                .AsNoTracking()
                .Where(c => c.IsActive);

            int targetBranchId = _currentUser.BranchId > 0 ? _currentUser.BranchId : (branchId ?? 0);
            if (targetBranchId > 0)
            {
                if (!_currentUser.CanAccessBranch(targetBranchId))
                    return Forbid();
                query = query.Where(c => c.BranchId == targetBranchId);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(c =>
                    c.Code.Contains(q) ||
                    c.NameAr.Contains(q) ||
                    c.NameEn.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.IsDefault ? 0 : 1).ThenBy(c => c.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(c => new
                {
                    id = c.Id,
                    text = $"{c.Code} - {c.NameAr} {(c.IsDefault ? "(افتراضي)" : "")}",
                    code = c.Code,
                    name = c.NameAr,
                    isDefault = c.IsDefault,
                    controlAccountId = c.ControlAccountId,
                    controlAccountCode = c.ControlAccount != null ? c.ControlAccount.Code : null,
                    controlAccountName = c.ControlAccount != null ? c.ControlAccount.NameAr : null,
                    controlAccountDisplay = c.ControlAccount != null ? $"{c.ControlAccount.Code} - {c.ControlAccount.NameAr}" : null
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("vendorCategory")]
        public async Task<IActionResult> VendorCategories([FromQuery] string? q, [FromQuery] int? branchId, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.VendorCategories
                .Include(c => c.ControlAccount)
                .AsNoTracking()
                .Where(c => c.IsActive);

            int targetBranchId = _currentUser.BranchId > 0 ? _currentUser.BranchId : (branchId ?? 0);
            if (targetBranchId > 0)
            {
                if (!_currentUser.CanAccessBranch(targetBranchId))
                    return Forbid();
                query = query.Where(c => c.BranchId == targetBranchId);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(c =>
                    c.Code.Contains(q) ||
                    c.NameAr.Contains(q) ||
                    c.NameEn.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.IsDefault ? 0 : 1).ThenBy(c => c.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(c => new
                {
                    id = c.Id,
                    text = $"{c.Code} - {c.NameAr} {(c.IsDefault ? "(افتراضي)" : "")}",
                    code = c.Code,
                    name = c.NameAr,
                    isDefault = c.IsDefault,
                    controlAccountId = c.ControlAccountId,
                    controlAccountCode = c.ControlAccount != null ? c.ControlAccount.Code : null,
                    controlAccountName = c.ControlAccount != null ? c.ControlAccount.NameAr : null,
                    controlAccountDisplay = c.ControlAccount != null ? $"{c.ControlAccount.Code} - {c.ControlAccount.NameAr}" : null
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("unit")]
        public async Task<IActionResult> Units([FromQuery] string? q, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.Units.AsNoTracking().Where(u => u.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(u =>
                    u.Code.Contains(q) ||
                    u.NameAr.Contains(q) ||
                    u.NameEn.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(u => u.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(u => new
                {
                    id = u.Id,
                    text = $"{u.Code} - {u.NameAr} ({u.NameEn})",
                    code = u.Code,
                    name = u.NameAr
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("itemGroup")]
        public async Task<IActionResult> ItemGroups([FromQuery] string? q, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.ItemGroups.AsNoTracking().Where(g => g.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(g =>
                    g.Code.Contains(q) ||
                    g.NameAr.Contains(q) ||
                    g.NameEn.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(g => g.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(g => new
                {
                    id = g.Id,
                    text = $"{g.Code} - {g.NameAr} ({g.NameEn})",
                    code = g.Code,
                    name = g.NameAr
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("item")]
        public async Task<IActionResult> Items(
            [FromQuery] string? q, 
            [FromQuery] int? itemGroupId, 
            [FromQuery] int? warehouseId, 
            [FromQuery] int? branchId, 
            [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.Items.AsNoTracking().Where(i => i.IsActive);

            if (itemGroupId.HasValue && itemGroupId.Value > 0)
            {
                query = query.Where(i => i.ItemGroupId == itemGroupId.Value);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(i =>
                    i.Code.Contains(q) ||
                    i.NameAr.Contains(q) ||
                    i.NameEn.Contains(q) ||
                    (i.Barcode != null && i.Barcode.Contains(q)));
            }

            int total = await query.CountAsync();
            var rawItems = await query
                .Include(i => i.ItemUnits).ThenInclude(u => u.Unit)
                .OrderBy(i => i.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            int effectiveBranchId = (branchId.HasValue && branchId.Value > 0) 
                ? branchId.Value 
                : (_currentUser.BranchId > 0 ? _currentUser.BranchId : 1);

            Dictionary<int, decimal> stockBalances = new();
            if (_availabilityService != null && rawItems.Any())
            {
                var physicalItemIds = rawItems.Where(i => !i.IsService).Select(i => i.Id).Distinct().ToList();
                if (physicalItemIds.Any())
                {
                    try
                    {
                        var availMap = await _availabilityService.GetBulkAvailabilityAsync(
                            physicalItemIds, effectiveBranchId, warehouseId);
                        if (availMap != null)
                        {
                            foreach (var kvp in availMap)
                            {
                                stockBalances[kvp.Key] = kvp.Value.OnHandQuantity;
                            }
                        }
                    }
                    catch
                    {
                        // Graceful fallback if availability lookup throws
                    }
                }
            }

            var items = rawItems.Select(i =>
            {
                var defSalesUnit = i.ItemUnits.FirstOrDefault(u => u.IsDefaultSales) ?? i.ItemUnits.FirstOrDefault();
                decimal balance = stockBalances.TryGetValue(i.Id, out var b) ? b : 0m;

                return new
                {
                    id = i.Id,
                    text = $"{i.Code} - {i.NameAr}" + (!string.IsNullOrWhiteSpace(i.NameEn) ? $" ({i.NameEn})" : ""),
                    code = i.Code,
                    name = i.NameAr,
                    nameEn = i.NameEn,
                    barcode = i.Barcode,
                    isService = i.IsService,
                    trackingType = (int)i.TrackingType,
                    isSerial = i.TrackingType == ERP.Domain.Enums.ItemTrackingType.SerialNumber,
                    unitId = defSalesUnit?.UnitId ?? 1,
                    unitName = defSalesUnit?.Unit != null ? defSalesUnit.Unit.NameAr : "قطعة",
                    salesPrice = defSalesUnit?.SalesPrice ?? 0m,
                    purchasePrice = defSalesUnit?.PurchasePrice ?? 0m,
                    balance = balance
                };
            }).ToList();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("currency")]
        public async Task<IActionResult> Currencies([FromQuery] string? q, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.Currencies.AsNoTracking().Where(c => c.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(c =>
                    c.Code.Contains(q) ||
                    c.NameAr.Contains(q) ||
                    c.NameEn.Contains(q) ||
                    (c.Symbol != null && c.Symbol.Contains(q)));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(c => new
                {
                    id = c.Id,
                    text = $"{c.Code} - {c.NameAr} ({c.Symbol ?? c.Code})",
                    code = c.Code,
                    name = c.NameAr
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("governorate")]
        public async Task<IActionResult> Governorates([FromQuery] string? q, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.Governorates.AsNoTracking().Where(g => g.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(g =>
                    g.Code.Contains(q) ||
                    g.NameAr.Contains(q) ||
                    g.NameEn.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(g => g.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(g => new
                {
                    id = g.Id,
                    text = $"{g.Code} - {g.NameAr} ({g.NameEn})",
                    code = g.Code,
                    name = g.NameAr
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("city")]
        public async Task<IActionResult> Cities([FromQuery] string? q, [FromQuery] int? governorateId, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.Cities.AsNoTracking().Where(c => c.IsActive);

            if (governorateId.HasValue && governorateId.Value > 0)
            {
                query = query.Where(c => c.GovernorateId == governorateId.Value);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(c =>
                    c.Code.Contains(q) ||
                    c.NameAr.Contains(q) ||
                    c.NameEn.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(c => new
                {
                    id = c.Id,
                    text = $"{c.Code} - {c.NameAr} ({c.NameEn})",
                    code = c.Code,
                    name = c.NameAr
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("branch")]
        public async Task<IActionResult> Branches([FromQuery] string? q, [FromQuery] int? companyId, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _currentUser.HasAllBranches
                ? _context.Branches.AsNoTracking().Where(b => b.IsActive)
                : _context.Branches.AsNoTracking().Where(b => b.IsActive && b.CompanyId == _currentUser.CompanyId);

            if (companyId.HasValue && companyId.Value > 0)
            {
                query = query.Where(b => b.CompanyId == companyId.Value);
            }

            if (!_currentUser.HasAllBranches)
            {
                var allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUser.UserId && ub.Branch.IsActive)
                    .Select(ub => ub.BranchId)
                    .ToListAsync();
                query = query.Where(b => allowedBranchIds.Contains(b.Id));
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(b =>
                    b.Code.Contains(q) ||
                    b.BranchCode.Contains(q) ||
                    (b.NameAr != null && b.NameAr.Contains(q)) ||
                    (b.NameEn != null && b.NameEn.Contains(q)) ||
                    b.Name.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(b => b.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(b => new
                {
                    id = b.Id,
                    text = $"{b.BranchCode} - {b.NameAr ?? b.Name} ({b.NameEn ?? b.Name})",
                    code = b.BranchCode,
                    name = b.NameAr ?? b.Name
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("user")]
        public async Task<IActionResult> Users([FromQuery] string? q, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.Users.AsNoTracking().Where(u => u.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(u =>
                    (u.UserName != null && u.UserName.Contains(q)) ||
                    u.EmployeeCode.Contains(q) ||
                    (u.FullNameAr != null && u.FullNameAr.Contains(q)) ||
                    (u.FullNameEn != null && u.FullNameEn.Contains(q)));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(u => u.EmployeeCode)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(u => new
                {
                    id = u.Id,
                    text = $"{u.EmployeeCode} - {u.FullNameAr ?? u.FullName} ({u.UserName})",
                    code = u.EmployeeCode,
                    name = u.FullNameAr ?? u.FullName
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("role")]
        public async Task<IActionResult> Roles([FromQuery] string? q, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.Roles.AsNoTracking().Where(r => r.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(r =>
                    (r.Name != null && r.Name.Contains(q)) ||
                    (r.NameAr != null && r.NameAr.Contains(q)) ||
                    (r.NameEn != null && r.NameEn.Contains(q)));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(r => r.Name)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(r => new
                {
                    id = r.Id,
                    text = $"{r.Name} - {r.NameAr ?? r.Name}",
                    code = r.Name,
                    name = r.NameAr ?? r.Name
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("company")]
        public async Task<IActionResult> Companies([FromQuery] string? q, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.Companies.AsNoTracking().Where(c => c.IsActive);

            if (!_currentUser.CanAccessCompany(_currentUser.CompanyId) && !_currentUser.HasAllBranches)
            {
                query = query.Where(c => c.Id == _currentUser.CompanyId);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(c =>
                    c.Code.Contains(q) ||
                    (c.NameAr != null && c.NameAr.Contains(q)) ||
                    (c.NameEn != null && c.NameEn.Contains(q)) ||
                    c.Name.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(c => new
                {
                    id = c.Id,
                    text = $"{c.Code} - {c.NameAr ?? c.Name} ({c.NameEn ?? c.Name})",
                    code = c.Code,
                    name = c.NameAr ?? c.Name
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("fiscalYear")]
        public async Task<IActionResult> FiscalYears([FromQuery] string? q, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.FiscalYears.AsNoTracking().Where(f => f.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(f =>
                    f.Year.Contains(q) ||
                    (f.NameAr != null && f.NameAr.Contains(q)) ||
                    (f.NameEn != null && f.NameEn.Contains(q)) ||
                    f.Name.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderByDescending(f => f.Year)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(f => new
                {
                    id = f.Id,
                    text = $"{f.Year} - {f.NameAr ?? f.Name}",
                    code = f.Year,
                    name = f.NameAr ?? f.Name
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("account")]
        public async Task<IActionResult> Accounts([FromQuery] string? q, [FromQuery] int? branchId, [FromQuery] int page = 1, [FromQuery] bool transactionalOnly = true)
        {
            page = Math.Max(1, page);
            var query = _context.GLAccounts.AsNoTracking().Where(a => a.IsActive);

            if (transactionalOnly)
                query = query.Where(a => !a.IsGroup);

            int targetBranchId = _currentUser.BranchId;
            if (targetBranchId > 0)
            {
                query = query.Where(a => a.BranchId == null || a.BranchId == targetBranchId);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(a =>
                    a.Code.Contains(q) ||
                    a.NameAr.Contains(q) ||
                    a.NameEn.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .Include(a => a.Branch)
                .OrderBy(a => a.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(a => new
                {
                    id = a.Id,
                    text = $"{a.Code} - {a.NameAr}" + (a.Branch != null ? $" [{a.Branch.NameAr ?? a.Branch.Name}]" : "") + (a.IsReconciliationAccount ? " ★" : ""),
                    code = a.Code,
                    name = a.NameAr,
                    branchId = a.BranchId,
                    branchName = a.Branch != null ? (a.Branch.NameAr ?? a.Branch.Name) : null
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("costCenter")]
        public async Task<IActionResult> CostCenters([FromQuery] string? q, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.CostCenters.AsNoTracking().Where(c => c.IsActive && c.IsTransactional);

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(c =>
                    c.Code.Contains(q) ||
                    c.NameAr.Contains(q) ||
                    (c.NameEn != null && c.NameEn.Contains(q)));
            }

            int total = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(c => new
                {
                    id = c.Id,
                    text = $"{c.Code} - {c.NameAr}",
                    code = c.Code,
                    name = c.NameAr
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("cashBankAccount")]
        public async Task<IActionResult> CashBankAccounts([FromQuery] string? q, [FromQuery] int? branchId, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            // Non-group accounts that represent Cash / Bank (under Cash & Banks 1201 or marked as Bank subledger)
            var query = _context.GLAccounts.AsNoTracking().Where(a => a.IsActive && !a.IsGroup && (a.Code.StartsWith("120101") || a.Code.StartsWith("120102") || a.SubLedgerType == ERP.Domain.Enums.SubLedgerType.Bank));

            int currentBranchId = _currentUser.BranchId;
            int? effectiveBranchId = currentBranchId > 0 ? currentBranchId : branchId;

            if (currentBranchId > 0)
            {
                query = query.Where(a => a.BranchId == null || a.BranchId == currentBranchId);
            }
            else if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                if (!_currentUser.CanAccessBranch(effectiveBranchId.Value))
                    return Forbid();
                query = query.Where(a => a.BranchId == null || a.BranchId == effectiveBranchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                query = query.Where(a => a.BranchId == null || a.BranchId == _currentUser.BranchId);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(a =>
                    a.Code.Contains(q) ||
                    a.NameAr.Contains(q) ||
                    a.NameEn.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .Include(a => a.Branch)
                .OrderBy(a => a.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(a => new
                {
                    id = a.Id,
                    text = $"{a.Code} - {a.NameAr}" + (a.Branch != null ? $" [{a.Branch.NameAr ?? a.Branch.Name}]" : ""),
                    code = a.Code,
                    name = a.NameAr,
                    currencyId = a.CurrencyId,
                    branchId = a.BranchId,
                    branchName = a.Branch != null ? (a.Branch.NameAr ?? a.Branch.Name) : null
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }

        [HttpGet("bank")]
        public async Task<IActionResult> Banks([FromQuery] string? q, [FromQuery] int? branchId, [FromQuery] int page = 1)
        {
            page = Math.Max(1, page);
            var query = _context.GLAccounts.AsNoTracking().Where(a => a.IsActive && !a.IsGroup && (a.Code.StartsWith("120101") || a.SubLedgerType == ERP.Domain.Enums.SubLedgerType.Bank));

            int currentBranchId = _currentUser.BranchId;
            int? effectiveBranchId = currentBranchId > 0 ? currentBranchId : branchId;

            if (currentBranchId > 0)
            {
                query = query.Where(a => a.BranchId == null || a.BranchId == currentBranchId);
            }
            else if (effectiveBranchId.HasValue && effectiveBranchId.Value > 0)
            {
                if (!_currentUser.CanAccessBranch(effectiveBranchId.Value))
                    return Forbid();
                query = query.Where(a => a.BranchId == null || a.BranchId == effectiveBranchId.Value);
            }
            else if (!_currentUser.HasAllBranches)
            {
                query = query.Where(a => a.BranchId == null || a.BranchId == _currentUser.BranchId);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(a =>
                    a.Code.Contains(q) ||
                    a.NameAr.Contains(q) ||
                    a.NameEn.Contains(q));
            }

            int total = await query.CountAsync();
            var items = await query
                .Include(a => a.Branch)
                .OrderBy(a => a.Code)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(a => new
                {
                    id = a.Id,
                    text = $"{a.Code} - {a.NameAr}" + (a.Branch != null ? $" [{a.Branch.NameAr ?? a.Branch.Name}]" : ""),
                    code = a.Code,
                    name = a.NameAr,
                    branchId = a.BranchId,
                    branchName = a.Branch != null ? (a.Branch.NameAr ?? a.Branch.Name) : null
                })
                .ToListAsync();

            return Ok(new { results = items, pagination = new { more = (page * PageSize) < total } });
        }
    }
}
