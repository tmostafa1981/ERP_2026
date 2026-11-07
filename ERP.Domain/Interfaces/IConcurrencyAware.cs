namespace ERP.Domain.Interfaces
{
    /// <summary>
    /// Contract for entities that support optimistic concurrency control via SQL Server RowVersion / timestamp.
    /// </summary>
    public interface IConcurrencyAware
    {
        byte[] RowVersion { get; set; }
    }
}
