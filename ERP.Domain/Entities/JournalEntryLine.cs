using System;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class JournalEntryLine : IAuditableEntity
    {
        public int Id { get; set; }
        public int JournalEntryHeaderId { get; set; }
        public int LineNumber { get; set; }
        public int AccountId { get; set; }

        public SubLedgerType SubLedgerType { get; set; } = SubLedgerType.None;
        public int? SubLedgerId { get; set; }

        public int CurrencyId { get; set; }
        public decimal ExchangeRate { get; set; } = 1.000000m;

        public decimal DebitForeign { get; set; }
        public decimal CreditForeign { get; set; }

        public decimal DebitLocal { get; set; }
        public decimal CreditLocal { get; set; }

        public int? CostCenterId { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public JournalEntryHeader? JournalEntryHeader { get; set; }
        public GLAccount? Account { get; set; }
        public Currency? Currency { get; set; }
        public CostCenter? CostCenter { get; set; }
    }
}
