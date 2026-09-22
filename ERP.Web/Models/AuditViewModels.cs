using System;
using System.Collections.Generic;
using ERP.Domain.Enums;

namespace ERP.Web.Models
{
    // ─── Filter / Search ──────────────────────────────────────────────────────────

    public class AuditFilterViewModel
    {
        /// <summary>General search term across entity, description, user, screen, action, and JSON content.</summary>
        public string? SearchTerm { get; set; }

        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo   { get; set; }

        public string? UserId       { get; set; }
        public string? EmployeeCode { get; set; }
        public string? UserName     { get; set; }

        public int? CompanyId    { get; set; }
        public int? BranchId     { get; set; }
        public int? FiscalYearId { get; set; }

        public string? ModuleName { get; set; }
        public string? ScreenCode { get; set; }
        public string? ScreenName { get; set; }

        public string? AuditAction { get; set; }   // mapped from AuditAction enum string

        /// <summary>
        /// Action filter. Ignores ASP.NET Core route values collision where RouteData["action"] == "Index" or "Details".
        /// </summary>
        public string? Action
        {
            get => AuditAction;
            set
            {
                if (!string.Equals(value, "Index", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(value, "Details", StringComparison.OrdinalIgnoreCase))
                {
                    AuditAction = value;
                }
            }
        }
        public string? EntityName { get; set; }
        public string? EntityId   { get; set; }

        /// <summary>null/empty = all, "true" = success only, "false" = failed only</summary>
        public string? Success { get; set; }

        // Pagination
        public int Page     { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    // ─── Grid Row ─────────────────────────────────────────────────────────────────

    public class AuditListItemViewModel
    {
        public long     Id           { get; set; }
        public DateTime TimestampUtc { get; set; }

        public string? UserName     { get; set; }
        public string? EmployeeCode { get; set; }

        public string? BranchId     { get; set; }
        public string? ModuleName   { get; set; }
        public string? ScreenCode   { get; set; }
        public string? ScreenName   { get; set; }

        public string? Action       { get; set; }
        public string? EntityName   { get; set; }
        public string? EntityId     { get; set; }

        public string? IpAddress    { get; set; }
        public bool    Success      { get; set; }
    }

    // ─── List Page ────────────────────────────────────────────────────────────────

    public class AuditListViewModel
    {
        public AuditFilterViewModel           Filter   { get; set; } = new();
        public List<AuditListItemViewModel>   Items    { get; set; } = new();
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }

        // Dropdown data for filter combos
        public List<string> AvailableModules  { get; set; } = new();
        public List<string> AvailableEntities { get; set; } = new();
        public List<string> AvailableActions  { get; set; } = new();
    }

    // ─── Detail Page ──────────────────────────────────────────────────────────────

    public class AuditDetailViewModel
    {
        public long     Id           { get; set; }
        public DateTime TimestampUtc { get; set; }

        // User
        public string? UserId       { get; set; }
        public string? EmployeeCode { get; set; }
        public string? UserName     { get; set; }

        // Org context
        public int? CompanyId    { get; set; }
        public int? SisterCompanyId { get; set; }
        public int? BranchId     { get; set; }
        public int? FiscalYearId { get; set; }

        // Screen
        public string? ScreenCode { get; set; }
        public string? ScreenName { get; set; }
        public string? ModuleName { get; set; }

        // Entity
        public string? EntityName { get; set; }
        public string? EntityId   { get; set; }

        // Action
        public string? Action      { get; set; }
        public string? Description { get; set; }

        // Request
        public string? IpAddress   { get; set; }
        public string? UserAgent   { get; set; }
        public string? RequestId   { get; set; }
        public string? CorrelationId { get; set; }
        public string? HttpMethod  { get; set; }
        public string? RequestPath { get; set; }

        // Result
        public bool    Success      { get; set; }
        public string? ErrorMessage { get; set; }

        // Raw JSON (for collapsible panels)
        public string? OldValuesJson     { get; set; }
        public string? NewValuesJson     { get; set; }
        public string? ChangedValuesJson { get; set; }

        // Parsed changed fields for the table
        public List<ChangedFieldViewModel> ChangedFields { get; set; } = new();
    }

    // ─── Changed Field Row ────────────────────────────────────────────────────────

    public class ChangedFieldViewModel
    {
        public string FieldName { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }
}
