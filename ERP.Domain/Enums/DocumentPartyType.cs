namespace ERP.Domain.Enums
{
    public enum DocumentPartyType
    {
        None = 0,         // بدون طرف (تسوية داخلية / حركة مخزن)
        Customer = 1,     // عميل
        Vendor = 2,       // مورد
        Branch = 3        // فرع (تحويل بين الفروع)
    }
}
