using System;
using ERP.Domain.Enums;

namespace ERP.Application.Models
{
    /// <summary>
    /// Data transfer object carrying all information needed to write one AuditLog record.
    /// Populated by the Application/Web layer and consumed by IAuditService.
    /// </summary>
    public class AuditLogEntry
    {
        // ─── Screen / Context ─────────────────────────────────────────────────────
        public string? ScreenCode { get; set; }
        public string? ScreenName { get; set; }
        public string? ModuleName { get; set; }

        // ─── Entity ───────────────────────────────────────────────────────────────
        public string? EntityName { get; set; }
        public string? EntityId { get; set; }

        // ─── Action ───────────────────────────────────────────────────────────────
        public AuditAction Action { get; set; }
        public string? Description { get; set; }

        // ─── Data Snapshots ───────────────────────────────────────────────────────
        /// <summary>Pre-operation snapshot (null for Create).</summary>
        public object? OldValues { get; set; }

        /// <summary>Post-operation snapshot (null for Delete).</summary>
        public object? NewValues { get; set; }

        // ─── Result ───────────────────────────────────────────────────────────────
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }

        // ─── Overrides (optional – service auto-reads from HttpContext) ───────────
        public string? OverrideUserId { get; set; }
        public string? OverrideUserName { get; set; }
        public string? OverrideEmployeeCode { get; set; }
    }
}
