namespace ERP.Domain.Entities
{
    public class Permission
    {
        public int Id { get; set; }

        /// <summary>Unique permission key, e.g. "Administration.Users.View"</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Permission category/module e.g. "Administration", "Organization", "Sales", "Purchases", "Inventory", "Accounting", "Cash", "Banks", "Reports", "Settings"</summary>
        public string Module { get; set; } = string.Empty;

        /// <summary>Target screen/feature e.g. "Users", "Roles", "Permissions", "Company", "Branch", "SalesInvoice"</summary>
        public string Screen { get; set; } = string.Empty;

        /// <summary>Action type e.g. "View", "Add", "Edit", "Delete", "Print", "Approve", "Cancel", "Post", "Export"</summary>
        public string Action { get; set; } = string.Empty;

        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int SortOrder { get; set; } = 0;

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : Code)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : Code);
    }
}
