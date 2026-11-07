using System;
using System.Collections.Generic;
using System.Globalization;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// Represents a logical group linking multiple currency accounts for the same Customer
    /// and/or related Vendor accounts across currencies.
    /// Each linked Customer and Vendor remains an independent record with its own Code, Currency, and Ledger Balance.
    /// </summary>
    public class PartyGroup
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string NameAr { get; set; } = string.Empty;

        public string NameEn { get; set; } = string.Empty;

        public int? BranchId { get; set; }
        public Branch? Branch { get; set; }

        public string? Notes { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public string LocalizedName =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase)
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : NameAr);

        // Navigation Properties
        public ICollection<Customer> Customers { get; set; } = new List<Customer>();
        public ICollection<Vendor> Vendors { get; set; } = new List<Vendor>();
    }
}
