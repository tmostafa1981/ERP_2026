using System;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// Central audit log record. One row per auditable operation.
    /// Stored in AuditLogs table with BIGINT primary key to support
    /// tens of millions of records over the system lifetime.
    /// </summary>
    public class AuditLog
    {
        // ─── Primary Key ──────────────────────────────────────────────────────────
        /// <summary>BIGINT IDENTITY – supports millions of audit records.</summary>
        public long Id { get; set; }

        // ─── Timestamp ────────────────────────────────────────────────────────────
        /// <summary>Always stored in UTC. Convert to local timezone on display.</summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        // ─── User Information ─────────────────────────────────────────────────────
        /// <summary>AspNetUsers.Id (GUID string).</summary>
        public string? UserId { get; set; }

        /// <summary>Employee code used as login identifier (e.g., "105").</summary>
        public string? EmployeeCode { get; set; }

        /// <summary>Display name of the user who performed the action. "SYSTEM" for background jobs.</summary>
        public string? UserName { get; set; }

        // ─── Organizational Context ───────────────────────────────────────────────
        /// <summary>The company context at the time of the operation.</summary>
        public int? CompanyId { get; set; }

        /// <summary>The sister/branch company (if applicable).</summary>
        public int? SisterCompanyId { get; set; }

        /// <summary>The branch context at the time of the operation.</summary>
        public int? BranchId { get; set; }

        /// <summary>The fiscal year context at the time of the operation.</summary>
        public int? FiscalYearId { get; set; }

        // ─── Screen / Module Information ──────────────────────────────────────────
        /// <summary>Unique screen identifier, e.g., "BRN001", "CUS001", "AUD001".</summary>
        public string? ScreenCode { get; set; }

        /// <summary>Human-readable screen name, e.g., "Branches", "Customers".</summary>
        public string? ScreenName { get; set; }

        /// <summary>Module name, e.g., "Administration", "Organization", "Sales".</summary>
        public string? ModuleName { get; set; }

        // ─── Entity Information ───────────────────────────────────────────────────
        /// <summary>The type name of the affected entity, e.g., "Branch", "Customer".</summary>
        public string? EntityName { get; set; }

        /// <summary>The primary key value of the affected record as a string.</summary>
        public string? EntityId { get; set; }

        // ─── Action ───────────────────────────────────────────────────────────────
        /// <summary>The action type (e.g., "Create", "Update", "Delete").</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>Optional human-readable description of the operation.</summary>
        public string? Description { get; set; }

        // ─── Data Changes (JSON) ──────────────────────────────────────────────────
        /// <summary>
        /// JSON snapshot of the entity BEFORE the operation.
        /// Null for Create actions. Populated for Update and Delete.
        /// Sensitive fields (passwords, tokens) are excluded.
        /// </summary>
        public string? OldValues { get; set; }

        /// <summary>
        /// JSON snapshot of the entity AFTER the operation.
        /// Null for Delete actions. Populated for Create and Update.
        /// Sensitive fields (passwords, tokens) are excluded.
        /// </summary>
        public string? NewValues { get; set; }

        /// <summary>
        /// JSON object containing ONLY the fields that changed between OldValues and NewValues.
        /// Format: { "FieldName": { "Old": "oldValue", "New": "newValue" } }
        /// Only populated for Update actions where actual changes occurred.
        /// </summary>
        public string? ChangedValues { get; set; }

        // ─── Request Context ──────────────────────────────────────────────────────
        /// <summary>Client IP address. Handles IPv4, IPv6, and reverse proxy headers.</summary>
        public string? IpAddress { get; set; }

        /// <summary>Browser/OS user agent string.</summary>
        public string? UserAgent { get; set; }

        /// <summary>ASP.NET Core request trace identifier.</summary>
        public string? RequestId { get; set; }

        /// <summary>Correlation ID for tracking operations across services.</summary>
        public string? CorrelationId { get; set; }

        /// <summary>HTTP method (GET, POST, PUT, DELETE).</summary>
        public string? HttpMethod { get; set; }

        /// <summary>Request path, e.g., "/Branch/Edit".</summary>
        public string? RequestPath { get; set; }

        // ─── Result ───────────────────────────────────────────────────────────────
        /// <summary>True if the operation completed successfully; false if it failed.</summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Error message if Success=false. Must never contain passwords, tokens, or connection strings.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
