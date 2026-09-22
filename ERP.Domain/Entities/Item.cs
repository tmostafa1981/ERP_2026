using System;
using System.Collections.Generic;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class Item : IAuditableEntity
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public string? TaxCode { get; set; }
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public int ItemGroupId { get; set; }
        public bool IsService { get; set; } = false;
        public string? StorageLocation { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public ItemGroup? ItemGroup { get; set; }
        public ICollection<ItemUnit> ItemUnits { get; set; } = new List<ItemUnit>();

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar")
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : NameAr);
    }
}
