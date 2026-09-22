using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Web.Authorization;
using ERP.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    /// <summary>
    /// Audit History screen (AUD001).
    /// Provides paginated, filterable view of all audit log records.
    /// Read-only – no Create/Edit/Delete actions exposed.
    /// </summary>
    [Authorize]
    public class AuditController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        private static readonly JsonSerializerOptions _prettyJson = new() { WriteIndented = true };

        public AuditController(ErpDbContext context, ICurrentUserContext currentUser)
        {
            _context     = context;
            _currentUser = currentUser;
        }

        // ─── GET: Audit History List ──────────────────────────────────────────────
        [HasPermission("Administration.AuditHistory.View")]
        public async Task<IActionResult> Index([FromQuery] AuditFilterViewModel filter)
        {
            // Ensure defaults
            filter.Page     = Math.Max(1, filter.Page);
            filter.PageSize = filter.PageSize is 10 or 20 or 50 or 100 ? filter.PageSize : 20;

            // Route data collision protection: RouteData["action"] = "Index" must not filter audit logs
            if (string.Equals(filter.AuditAction, "Index", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(filter.Action, "Index", StringComparison.OrdinalIgnoreCase))
            {
                filter.AuditAction = null;
                filter.Action = null;
            }

            var query = _context.AuditLogs.AsNoTracking();

            // ── Apply filters ─────────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                query = ApplySearchTerm(query, filter.SearchTerm);
            }

            if (filter.DateFrom.HasValue)
            {
                var fromDate = filter.DateFrom.Value.Date;
                query = query.Where(a => a.TimestampUtc >= fromDate);
            }

            if (filter.DateTo.HasValue)
            {
                var toDate = filter.DateTo.Value.Date.AddDays(1);
                query = query.Where(a => a.TimestampUtc < toDate);
            }

            if (!string.IsNullOrWhiteSpace(filter.UserId))
                query = query.Where(a => a.UserId == filter.UserId.Trim());

            if (!string.IsNullOrWhiteSpace(filter.EmployeeCode))
                query = query.Where(a => a.EmployeeCode != null && a.EmployeeCode.Contains(filter.EmployeeCode.Trim()));

            if (!string.IsNullOrWhiteSpace(filter.UserName))
                query = query.Where(a => a.UserName != null && a.UserName.Contains(filter.UserName.Trim()));

            if (filter.CompanyId.HasValue && filter.CompanyId > 0)
                query = query.Where(a => a.CompanyId == filter.CompanyId);

            if (filter.BranchId.HasValue && filter.BranchId > 0)
                query = query.Where(a => a.BranchId == filter.BranchId);

            if (filter.FiscalYearId.HasValue && filter.FiscalYearId > 0)
                query = query.Where(a => a.FiscalYearId == filter.FiscalYearId);

            if (!string.IsNullOrWhiteSpace(filter.ModuleName))
                query = query.Where(a => a.ModuleName == filter.ModuleName.Trim());

            if (!string.IsNullOrWhiteSpace(filter.ScreenCode))
                query = query.Where(a => a.ScreenCode == filter.ScreenCode.Trim());

            if (!string.IsNullOrWhiteSpace(filter.AuditAction))
                query = query.Where(a => a.Action == filter.AuditAction.Trim());

            if (!string.IsNullOrWhiteSpace(filter.EntityName))
                query = query.Where(a => a.EntityName == filter.EntityName.Trim());

            if (!string.IsNullOrWhiteSpace(filter.EntityId))
                query = query.Where(a => a.EntityId == filter.EntityId.Trim());

            if (!string.IsNullOrWhiteSpace(filter.Success) && bool.TryParse(filter.Success, out var successVal))
                query = query.Where(a => a.Success == successVal);

            // ── Count & paginate ──────────────────────────────────────────────────
            int totalCount = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalCount / (double)filter.PageSize);

            var logs = await query
                .OrderByDescending(a => a.TimestampUtc)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(a => new AuditListItemViewModel
                {
                    Id           = a.Id,
                    TimestampUtc = a.TimestampUtc,
                    UserName     = a.UserName,
                    EmployeeCode = a.EmployeeCode,
                    BranchId     = a.BranchId.HasValue ? a.BranchId.ToString() : null,
                    ModuleName   = a.ModuleName,
                    ScreenCode   = a.ScreenCode,
                    ScreenName   = a.ScreenName,
                    Action       = a.Action,
                    EntityName   = a.EntityName,
                    EntityId     = a.EntityId,
                    IpAddress    = a.IpAddress,
                    Success      = a.Success
                })
                .ToListAsync();

            // ── Dropdowns for filter combos ───────────────────────────────────────
            // Static/Standard lists to prevent unnecessary WAN scans over remote SQL Server
            var availableModules = new List<string> { "Account", "Organization", "Administration" };
            var availableEntities = new List<string> { "Branch", "Company", "FiscalYear", "Role", "User", "UserBranch" };

            // Load companies, branches, and fiscal years for dropdowns
            ViewBag.Companies   = await _context.Companies.AsNoTracking().OrderBy(c => c.Code).ToListAsync();
            ViewBag.Branches    = await _context.Branches.AsNoTracking().OrderBy(b => b.Code).ToListAsync();
            ViewBag.FiscalYears = await _context.FiscalYears.AsNoTracking().OrderByDescending(f => f.Year).ToListAsync();

            var hasSuccessFilter = !string.IsNullOrWhiteSpace(filter.Success) && bool.TryParse(filter.Success, out _);
            var hasActiveFilter = !string.IsNullOrWhiteSpace(filter.SearchTerm) ||
                                  filter.DateFrom.HasValue ||
                                  filter.DateTo.HasValue ||
                                  !string.IsNullOrWhiteSpace(filter.EmployeeCode) ||
                                  !string.IsNullOrWhiteSpace(filter.UserName) ||
                                  (filter.CompanyId.HasValue && filter.CompanyId > 0) ||
                                  (filter.BranchId.HasValue && filter.BranchId > 0) ||
                                  (filter.FiscalYearId.HasValue && filter.FiscalYearId > 0) ||
                                  !string.IsNullOrWhiteSpace(filter.ModuleName) ||
                                  !string.IsNullOrWhiteSpace(filter.AuditAction) ||
                                  !string.IsNullOrWhiteSpace(filter.EntityName) ||
                                  !string.IsNullOrWhiteSpace(filter.EntityId) ||
                                  hasSuccessFilter;

            ViewBag.HasActiveFilter = hasActiveFilter;
            ViewBag.GrandTotalCount = hasActiveFilter ? await _context.AuditLogs.CountAsync() : totalCount;

            // All AuditAction values for dropdown
            var availableActions = Enum.GetNames(typeof(AuditAction)).ToList();

            var vm = new AuditListViewModel
            {
                Filter           = filter,
                Items            = logs,
                TotalCount       = totalCount,
                TotalPages       = totalPages,
                AvailableModules = availableModules,
                AvailableEntities = availableEntities,
                AvailableActions = availableActions,
            };

            return View(vm);
        }

        // ─── GET: Audit Detail ────────────────────────────────────────────────────
        [HasPermission("Administration.AuditHistory.View")]
        public async Task<IActionResult> Details(long id)
        {
            var log = await _context.AuditLogs.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
            if (log == null) return NotFound();

            var vm = new AuditDetailViewModel
            {
                Id           = log.Id,
                TimestampUtc = log.TimestampUtc,

                UserId       = log.UserId,
                EmployeeCode = log.EmployeeCode,
                UserName     = log.UserName,

                CompanyId    = log.CompanyId,
                SisterCompanyId = log.SisterCompanyId,
                BranchId     = log.BranchId,
                FiscalYearId = log.FiscalYearId,

                ScreenCode   = log.ScreenCode,
                ScreenName   = log.ScreenName,
                ModuleName   = log.ModuleName,

                EntityName   = log.EntityName,
                EntityId     = log.EntityId,

                Action       = log.Action,
                Description  = log.Description,

                IpAddress    = log.IpAddress,
                UserAgent    = log.UserAgent,
                RequestId    = log.RequestId,
                CorrelationId = log.CorrelationId,
                HttpMethod   = log.HttpMethod,
                RequestPath  = log.RequestPath,

                Success      = log.Success,
                ErrorMessage = log.ErrorMessage,

                OldValuesJson     = PrettyPrint(log.OldValues),
                NewValuesJson     = PrettyPrint(log.NewValues),
                ChangedValuesJson = PrettyPrint(log.ChangedValues),

                ChangedFields = ParseChangedFields(log.ChangedValues),
            };

            return View(vm);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static string? PrettyPrint(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc.RootElement, _prettyJson);
            }
            catch { return json; }
        }

        private static List<ChangedFieldViewModel> ParseChangedFields(string? changedValuesJson)
        {
            var result = new List<ChangedFieldViewModel>();
            if (string.IsNullOrWhiteSpace(changedValuesJson)) return result;

            try
            {
                using var doc = JsonDocument.Parse(changedValuesJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string? oldVal = null;
                    string? newVal = null;

                    if (prop.Value.TryGetProperty("Old", out var oldEl))
                        oldVal = oldEl.ValueKind == JsonValueKind.Null ? null : oldEl.ToString();

                    if (prop.Value.TryGetProperty("New", out var newEl))
                        newVal = newEl.ValueKind == JsonValueKind.Null ? null : newEl.ToString();

                    result.Add(new ChangedFieldViewModel
                    {
                        FieldName = prop.Name,
                        OldValue  = oldVal,
                        NewValue  = newVal
                    });
                }
            }
            catch { /* return empty on parse error */ }

            return result;
        }

        /// <summary>
        /// Applies smart, bilingual (Arabic/English) multi-word search to AuditLogs query.
        /// Matches across all audit columns and maps Arabic domain terms to system entities/actions.
        /// </summary>
        private static IQueryable<ERP.Domain.Entities.AuditLog> ApplySearchTerm(
            IQueryable<ERP.Domain.Entities.AuditLog> query,
            string searchTerm)
        {
            var words = searchTerm.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                var w = word.Trim();
                var lower = w.ToLower();

                // Keyword mapping for Arabic & English terms
                bool matchFiscal  = lower.Contains("سن") || lower.Contains("مالي") || lower.Contains("fiscal") || lower.Contains("year");
                bool matchBranch  = lower.Contains("فرع") || lower.Contains("فروع") || lower.Contains("branch");
                bool matchUser    = lower.Contains("مستخدم") || lower.Contains("حساب") || lower.Contains("user");
                bool matchCompany = lower.Contains("شرك") || lower.Contains("company");

                bool matchCreate  = lower.Contains("اضاف") || lower.Contains("إضاف") || lower.Contains("انشا") || lower.Contains("إنشا") || lower.Contains("جديد") || lower.Contains("create") || lower.Contains("add");
                bool matchUpdate  = lower.Contains("تعديل") || lower.Contains("تحديث") || lower.Contains("تغيير") || lower.Contains("update") || lower.Contains("edit");
                bool matchDelete  = lower.Contains("حذف") || lower.Contains("الغاء") || lower.Contains("إلغاء") || lower.Contains("delete");
                bool matchLogin   = lower.Contains("دخول") || lower.Contains("login");
                bool matchLogout  = lower.Contains("خروج") || lower.Contains("logout");

                query = query.Where(a =>
                    (a.EntityName != null && a.EntityName.Contains(w)) ||
                    (a.EntityId != null && a.EntityId.Contains(w)) ||
                    (a.Description != null && a.Description.Contains(w)) ||
                    (a.ScreenCode != null && a.ScreenCode.Contains(w)) ||
                    (a.ScreenName != null && a.ScreenName.Contains(w)) ||
                    (a.ModuleName != null && a.ModuleName.Contains(w)) ||
                    (a.UserName != null && a.UserName.Contains(w)) ||
                    (a.EmployeeCode != null && a.EmployeeCode.Contains(w)) ||
                    (a.Action != null && a.Action.Contains(w)) ||
                    (a.NewValues != null && a.NewValues.Contains(w)) ||
                    (a.OldValues != null && a.OldValues.Contains(w)) ||
                    (matchFiscal && (a.EntityName == "FiscalYear" || a.ScreenCode == "FYR001")) ||
                    (matchBranch && (a.EntityName == "Branch" || a.ScreenCode == "BRN001")) ||
                    (matchUser && (a.EntityName == "User" || a.ScreenCode == "USR001" || a.ScreenCode == "ACC001")) ||
                    (matchCompany && (a.EntityName == "Company" || a.ScreenCode == "CMP001")) ||
                    (matchCreate && a.Action == "Create") ||
                    (matchUpdate && a.Action == "Update") ||
                    (matchDelete && a.Action == "Delete") ||
                    (matchLogin && a.Action == "Login") ||
                    (matchLogout && a.Action == "Logout")
                );
            }

            return query;
        }
    }
}
