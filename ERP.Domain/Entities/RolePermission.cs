using System;

namespace ERP.Domain.Entities
{
    public class RolePermission
    {
        public int Id { get; set; }
        public string RoleId { get; set; } = string.Empty;
        public int PermissionId { get; set; }
        public bool IsGranted { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual Permission? Permission { get; set; }
    }
}
