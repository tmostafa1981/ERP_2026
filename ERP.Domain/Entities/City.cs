using System;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class City : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public int GovernorateId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Governorate? Governorate { get; set; }

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar")
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : NameAr);
    }
}
