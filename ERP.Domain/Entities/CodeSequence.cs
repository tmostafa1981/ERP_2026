using System;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class CodeSequence : IAuditableEntity
    {
        public int Id { get; set; }
        public string EntityName { get; set; } = string.Empty;
        public int? BranchId { get; set; }
        public string Prefix { get; set; } = string.Empty;
        public long LastNumber { get; set; } = 0;
        public int PaddingLength { get; set; } = 6;
        public bool IsAuto { get; set; } = true;
        public bool IsManualAllowed { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Branch? Branch { get; set; }
    }
}
