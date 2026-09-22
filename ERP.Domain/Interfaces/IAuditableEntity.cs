namespace ERP.Domain.Interfaces
{
    /// <summary>
    /// Marker interface – any entity that implements this will be tracked
    /// by the central AuditService on Create, Update, and Delete operations.
    ///
    /// Usage:
    ///   public class Customer : IAuditableEntity { ... }
    ///   public class Vendor   : IAuditableEntity { ... }
    ///   public class Branch   : IAuditableEntity { ... }
    ///
    /// The AuditService checks for this interface on EF Core ChangeTracker entries.
    /// </summary>
    public interface IAuditableEntity
    {
        // Intentionally empty – serves as a marker only.
        // All auditing metadata is stored in the central AuditLogs table.
    }
}
