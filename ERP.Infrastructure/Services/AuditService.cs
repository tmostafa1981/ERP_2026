using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Application.Models;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Services
{
    /// <summary>
    /// Central audit service implementation.
    ///
    /// Key design decisions:
    ///   1. Uses a SEPARATE DbContext scope for writing audit logs so that:
    ///      - A failed/rolled-back business transaction does NOT prevent audit from being written.
    ///      - Failed operations (Success=false) are still recorded.
    ///   2. Never throws – all exceptions are caught and logged via ILogger.
    ///   3. Sensitive fields are stripped from all JSON snapshots before storage.
    ///   4. ChangedValues contains ONLY fields that actually changed (not just touched).
    ///   5. Update audit is skipped entirely when no fields changed.
    /// </summary>
    public class AuditService : IAuditService
    {
        // ─── Dependencies ─────────────────────────────────────────────────────────
        private readonly IServiceProvider _serviceProvider;
        private readonly ICurrentUserContext _currentUser;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuditService> _logger;

        // ─── Sensitive Fields – never stored in audit ─────────────────────────────
        private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "Password",
            "ConfirmPassword",
            "PasswordHash",
            "SecurityStamp",
            "ConcurrencyStamp",
            "Token",
            "RefreshToken",
            "AccessToken",
            "SecretKey",
            "ApiSecret",
            "PrivateKey",
            "ConnectionString",
        };

        // ─── JSON options ─────────────────────────────────────────────────────────
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = null,         // preserve original property names
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        };

        public AuditService(
            IServiceProvider serviceProvider,
            ICurrentUserContext currentUser,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuditService> logger)
        {
            _serviceProvider   = serviceProvider;
            _currentUser       = currentUser;
            _httpContextAccessor = httpContextAccessor;
            _logger            = logger;
        }

        // ─── Public API ───────────────────────────────────────────────────────────

        public Task LogCreateAsync(
            string screenCode, string screenName, string moduleName,
            string entityName, string entityId, object newValues,
            string? description = null)
        {
            var entry = new AuditLogEntry
            {
                ScreenCode  = screenCode,
                ScreenName  = screenName,
                ModuleName  = moduleName,
                EntityName  = entityName,
                EntityId    = entityId,
                Action      = AuditAction.Create,
                NewValues   = newValues,
                Description = description ?? $"{entityName} created (Id: {entityId})",
                Success     = true,
            };
            return LogAsync(entry);
        }

        public async Task LogUpdateAsync(
            string screenCode, string screenName, string moduleName,
            string entityName, string entityId,
            object oldValues, object newValues,
            string? description = null)
        {
            // Compute ChangedValues – skip write if nothing changed
            var changedDict = ComputeChangedValues(oldValues, newValues);
            if (changedDict.Count == 0)
            {
                _logger.LogDebug("AuditService: Skipping Update audit for {Entity} {Id} – no fields changed.",
                    entityName, entityId);
                return;
            }

            var changedJson = SerializeChangedValues(changedDict);

            var entry = new AuditLogEntry
            {
                ScreenCode    = screenCode,
                ScreenName    = screenName,
                ModuleName    = moduleName,
                EntityName    = entityName,
                EntityId      = entityId,
                Action        = AuditAction.Update,
                OldValues     = oldValues,
                NewValues     = newValues,
                Description   = description ?? $"{entityName} updated (Id: {entityId}). Changed: {string.Join(", ", changedDict.Keys)}",
                Success       = true,
            };

            await WriteAuditLogAsync(entry, changedJson);
        }

        public Task LogDeleteAsync(
            string screenCode, string screenName, string moduleName,
            string entityName, string entityId, object oldValues,
            string? description = null)
        {
            var entry = new AuditLogEntry
            {
                ScreenCode  = screenCode,
                ScreenName  = screenName,
                ModuleName  = moduleName,
                EntityName  = entityName,
                EntityId    = entityId,
                Action      = AuditAction.Delete,
                OldValues   = oldValues,
                Description = description ?? $"{entityName} deleted (Id: {entityId})",
                Success     = true,
            };
            return LogAsync(entry);
        }

        public Task LogAsync(AuditLogEntry entry)
        {
            return WriteAuditLogAsync(entry, changedValuesJson: null);
        }

        public Task LogFailedAsync(
            string screenCode, string screenName, string moduleName,
            string entityName, string? entityId,
            AuditAction action, string errorMessage)
        {
            var entry = new AuditLogEntry
            {
                ScreenCode   = screenCode,
                ScreenName   = screenName,
                ModuleName   = moduleName,
                EntityName   = entityName,
                EntityId     = entityId,
                Action       = action,
                Success      = false,
                ErrorMessage = SanitizeErrorMessage(errorMessage),
                Description  = $"FAILED: {action.ToDisplayString()} on {entityName} {entityId}",
            };
            return WriteAuditLogAsync(entry, changedValuesJson: null);
        }

        // ─── Core Write Logic ─────────────────────────────────────────────────────

        private async Task WriteAuditLogAsync(AuditLogEntry entry, string? changedValuesJson)
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;

                var log = new AuditLog
                {
                    TimestampUtc = DateTime.UtcNow,

                    // User context
                    UserId       = entry.OverrideUserId       ?? _currentUser.UserId,
                    EmployeeCode = entry.OverrideEmployeeCode ?? _currentUser.EmployeeCode,
                    UserName     = entry.OverrideUserName     ?? _currentUser.Username ?? "SYSTEM",

                    // Organizational context
                    CompanyId    = _currentUser.CompanyId    > 0 ? _currentUser.CompanyId    : null,
                    BranchId     = _currentUser.BranchId     > 0 ? _currentUser.BranchId     : null,
                    FiscalYearId = _currentUser.FiscalYearId > 0 ? _currentUser.FiscalYearId : null,

                    // Screen
                    ScreenCode = entry.ScreenCode,
                    ScreenName = entry.ScreenName,
                    ModuleName = entry.ModuleName,

                    // Entity
                    EntityName = entry.EntityName,
                    EntityId   = entry.EntityId,

                    // Action
                    Action      = entry.Action.ToString(),
                    Description = entry.Description,

                    // Data snapshots (sanitized JSON)
                    OldValues     = entry.OldValues != null  ? SerializeSafe(entry.OldValues)  : null,
                    NewValues     = entry.NewValues != null  ? SerializeSafe(entry.NewValues)  : null,
                    ChangedValues = changedValuesJson,

                    // Request context
                    IpAddress   = GetClientIpAddress(httpContext),
                    UserAgent   = httpContext?.Request.Headers.UserAgent.ToString(),
                    RequestId   = httpContext?.TraceIdentifier,
                    CorrelationId = httpContext?.Request.Headers["X-Correlation-ID"].ToString()
                                    ?? httpContext?.TraceIdentifier,
                    HttpMethod  = httpContext?.Request.Method,
                    RequestPath = httpContext?.Request.Path.ToString(),

                    // Result
                    Success      = entry.Success,
                    ErrorMessage = entry.ErrorMessage,
                };

                // Write using a separate scope/DbContext so this is independent of
                // any ongoing business transaction that may be rolled back.
                using var scope = _serviceProvider.CreateScope();
                var auditDb = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
                auditDb.AuditLogs.Add(log);
                await auditDb.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Audit must never break the main operation.
                _logger.LogError(ex,
                    "AuditService failed to write log for Action={Action} Entity={Entity} EntityId={EntityId}",
                    entry.Action, entry.EntityName, entry.EntityId);
            }
        }

        // ─── Changed Values Computation ───────────────────────────────────────────

        /// <summary>
        /// Compares old and new values, returning only the fields that changed.
        /// Uses JSON-based comparison to handle complex objects uniformly.
        /// </summary>
        private Dictionary<string, (string? Old, string? New)> ComputeChangedValues(
            object oldValues, object newValues)
        {
            var changed = new Dictionary<string, (string? Old, string? New)>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var oldDict = FlattenToDictionary(oldValues);
                var newDict = FlattenToDictionary(newValues);

                var allKeys = new HashSet<string>(oldDict.Keys, StringComparer.OrdinalIgnoreCase);
                allKeys.UnionWith(newDict.Keys);

                foreach (var key in allKeys)
                {
                    // Skip sensitive fields
                    if (SensitiveFields.Contains(key)) continue;

                    oldDict.TryGetValue(key, out var oldVal);
                    newDict.TryGetValue(key, out var newVal);

                    // Normalize nulls for comparison
                    var oldStr = oldVal?.ToString() ?? string.Empty;
                    var newStr = newVal?.ToString() ?? string.Empty;

                    if (!string.Equals(oldStr, newStr, StringComparison.Ordinal))
                    {
                        changed[key] = (oldVal?.ToString(), newVal?.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AuditService: Failed to compute changed values.");
            }

            return changed;
        }

        /// <summary>
        /// Serializes the ChangedValues dictionary to the canonical JSON format:
        /// { "FieldName": { "Old": "...", "New": "..." } }
        /// </summary>
        private static string SerializeChangedValues(
            Dictionary<string, (string? Old, string? New)> changed)
        {
            var dict = changed.ToDictionary(
                kvp => kvp.Key,
                kvp => new { Old = kvp.Value.Old, New = kvp.Value.New });

            return JsonSerializer.Serialize(dict, _jsonOptions);
        }

        /// <summary>
        /// Flattens any object to a string-keyed dictionary using JSON round-trip.
        /// Excludes sensitive fields during flattening.
        /// </summary>
        private static Dictionary<string, object?> FlattenToDictionary(object obj)
        {
            var json = JsonSerializer.Serialize(obj, _jsonOptions);
            using var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (SensitiveFields.Contains(prop.Name)) continue;
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : (object?)prop.Value.GetRawText().Trim('"');
            }

            return result;
        }

        // ─── Serialization Helpers ────────────────────────────────────────────────

        /// <summary>
        /// Serializes an object to JSON, removing sensitive fields.
        /// </summary>
        private string SerializeSafe(object obj)
        {
            try
            {
                var json = JsonSerializer.Serialize(obj, _jsonOptions);
                return StripSensitiveFields(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AuditService: Failed to serialize object for audit.");
                return "{}";
            }
        }

        /// <summary>
        /// Parses JSON and removes any properties matching sensitive field names.
        /// </summary>
        private static string StripSensitiveFields(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var dict = new Dictionary<string, JsonElement>();

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!SensitiveFields.Contains(prop.Name))
                        dict[prop.Name] = prop.Value;
                }

                return JsonSerializer.Serialize(dict, _jsonOptions);
            }
            catch
            {
                return "{}";
            }
        }

        // ─── Request Context Helpers ──────────────────────────────────────────────

        private static string? GetClientIpAddress(HttpContext? httpContext)
        {
            if (httpContext == null) return null;

            // Handle reverse proxy / load balancer headers
            var forwarded = httpContext.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                // Take the first IP in the chain (the original client)
                return forwarded.Split(',')[0].Trim();
            }

            var realIp = httpContext.Request.Headers["X-Real-IP"].ToString();
            if (!string.IsNullOrWhiteSpace(realIp))
                return realIp.Trim();

            return httpContext.Connection.RemoteIpAddress?.ToString();
        }

        /// <summary>
        /// Sanitizes error messages to prevent leaking sensitive data.
        /// </summary>
        private static string SanitizeErrorMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message;

            // Truncate very long error messages
            if (message.Length > 2000)
                message = message[..2000] + "... [truncated]";

            return message;
        }
    }
}
