using System;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class AccountPermission : IAuditableEntity
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public bool CanView { get; set; } = true;
        public bool CanPost { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public GLAccount? Account { get; set; }
    }
}
