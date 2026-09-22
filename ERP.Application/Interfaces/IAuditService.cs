using System.Threading.Tasks;
using ERP.Application.Models;
using ERP.Domain.Enums;

namespace ERP.Application.Interfaces
{
    /// <summary>
    /// Central audit service interface. Implement once in Infrastructure layer.
    /// Inject into Controllers or other services wherever auditing is needed.
    ///
    /// Design rules:
    ///   - Never throws exceptions (audit must not break the main operation).
    ///   - Automatically reads user/branch/company/fiscal year context from ICurrentUserContext.
    ///   - Automatically reads IP, UserAgent, RequestId from IHttpContextAccessor.
    ///   - Sensitive fields (Password, PasswordHash, Token, etc.) are always excluded.
    ///   - Update audit is skipped if no fields actually changed.
    /// </summary>
    public interface IAuditService
    {
        /// <summary>
        /// Log a Create operation. Records NewValues only.
        /// </summary>
        Task LogCreateAsync(
            string screenCode,
            string screenName,
            string moduleName,
            string entityName,
            string entityId,
            object newValues,
            string? description = null);

        /// <summary>
        /// Log an Update operation. Records OldValues, NewValues, and ChangedValues.
        /// Skips writing if no fields actually changed.
        /// </summary>
        Task LogUpdateAsync(
            string screenCode,
            string screenName,
            string moduleName,
            string entityName,
            string entityId,
            object oldValues,
            object newValues,
            string? description = null);

        /// <summary>
        /// Log a Delete operation. Records OldValues only (entity no longer exists).
        /// </summary>
        Task LogDeleteAsync(
            string screenCode,
            string screenName,
            string moduleName,
            string entityName,
            string entityId,
            object oldValues,
            string? description = null);

        /// <summary>
        /// Generic log method for any action (Login, Logout, Export, Approve, etc.).
        /// </summary>
        Task LogAsync(AuditLogEntry entry);

        /// <summary>
        /// Log a failed operation (permission denied, validation error, exception).
        /// </summary>
        Task LogFailedAsync(
            string screenCode,
            string screenName,
            string moduleName,
            string entityName,
            string? entityId,
            AuditAction action,
            string errorMessage);
    }
}
