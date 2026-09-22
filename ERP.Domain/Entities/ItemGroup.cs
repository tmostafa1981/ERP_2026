using System;
using System.Collections.Generic;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class ItemGroup : IAuditableEntity
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public int? ParentId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public ItemGroup? ParentGroup { get; set; }
        public ICollection<ItemGroup> SubGroups { get; set; } = new List<ItemGroup>();
        public ICollection<Item> Items { get; set; } = new List<Item>();

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar")
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : NameAr);
    }
}
