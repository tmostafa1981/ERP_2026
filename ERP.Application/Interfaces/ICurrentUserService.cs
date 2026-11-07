namespace ERP.Application.Interfaces
{
    public interface ICurrentUserService
    {
        string? UserId { get; }
        int BranchId { get; }
        bool IsSuperAdmin { get; }
        bool IsAuthenticated { get; }
    }
}
