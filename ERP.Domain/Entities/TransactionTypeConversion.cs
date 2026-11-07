using System;

namespace ERP.Domain.Entities
{
    public class TransactionTypeConversion
    {
        public int Id { get; set; }
        public int SourceTransactionTypeId { get; set; }
        public int TargetTransactionTypeId { get; set; }

        public bool AllowMultipleSources { get; set; } = true;
        public bool AllowPartialQuantity { get; set; } = true;
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigations
        public TransactionType? SourceTransactionType { get; set; }
        public TransactionType? TargetTransactionType { get; set; }
    }
}
