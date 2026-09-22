using System;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class Customer : IAuditableEntity
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public int CustomerCategoryId { get; set; }
        public string? Address { get; set; }
        public int? GovernorateId { get; set; }
        public int? CityId { get; set; }
        public string? TaxNumber { get; set; }
        public string? CommercialRegister { get; set; }
        public string? TaxPayerType { get; set; }
        public int? SalesmanId { get; set; }
        public int? CurrencyId { get; set; }
        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Email { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Branch? Branch { get; set; }
        public CustomerCategory? CustomerCategory { get; set; }
        public Governorate? Governorate { get; set; }
        public City? City { get; set; }
        public Salesman? Salesman { get; set; }
        public Currency? Currency { get; set; }

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar")
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : NameAr);
    }
}
