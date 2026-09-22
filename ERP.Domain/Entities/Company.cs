using System;
using System.Collections.Generic;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class Company : IAuditableEntity
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public string? NameEn { get; set; }
        public string? AddressAr { get; set; }
        public string? AddressEn { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? TaxNumber { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public string LocalizedName => 
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar") 
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : Name)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : Name);

        public string? LocalizedAddress => 
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar") 
                ? (!string.IsNullOrWhiteSpace(AddressAr) ? AddressAr : AddressEn)
                : (!string.IsNullOrWhiteSpace(AddressEn) ? AddressEn : AddressAr);

        public ICollection<Branch> Branches { get; set; } = new List<Branch>();
    }
}
