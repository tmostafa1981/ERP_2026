using System;

namespace ERP.Domain.Entities
{
    public class UserTransactionTypePermission
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public int TransactionTypeId { get; set; }

        public bool CanView { get; set; } = true;
        public bool CanCreate { get; set; } = false;
        public bool CanEdit { get; set; } = false;
        public bool CanDelete { get; set; } = false;
        public bool CanPost { get; set; } = false;
        public bool CanUnpost { get; set; } = false;
        public bool CanConvert { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigations
        public TransactionType? TransactionType { get; set; }
    }
}
