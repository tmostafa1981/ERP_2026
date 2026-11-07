namespace ERP.Domain.Enums
{
    public enum RevaluationStatus
    {
        Draft = 1,
        Approved = 2,
        Reversed = 3,
        Cancelled = 4
    }

    public enum RevaluationScope
    {
        All = 1,
        Customers = 2,
        Vendors = 3,
        GeneralAccounts = 4
    }

    public enum RevaluationSourceType
    {
        Customer = 1,
        Vendor = 2,
        GeneralAccount = 3
    }

    public enum RevaluationDifferenceType
    {
        None = 0,
        Gain = 1,
        Loss = 2
    }
}
