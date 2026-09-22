namespace ERP.Application.Interfaces
{
    public interface ICurrentUserContext
    {
        string? UserId { get; }
        string? Username { get; }
        string? EmployeeCode { get; }

        int CompanyId { get; }
        string CompanyName { get; }

        int BranchId { get; }
        string BranchName { get; }

        int FiscalYearId { get; }
        string FiscalYearName { get; }

        bool IsAuthenticated { get; }
        bool HasAllBranches { get; }

        bool HasPermission(string permissionCode);
        bool HasPermission(string screen, string transactionType, string action);
        bool CanAccessCompany(int companyId);
        bool CanAccessBranch(int branchId);
    }
}
