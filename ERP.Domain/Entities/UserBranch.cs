namespace ERP.Domain.Entities
{
    public class UserBranch
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public int BranchId { get; set; }
        
        public Branch Branch { get; set; } = null!;
    }
}
