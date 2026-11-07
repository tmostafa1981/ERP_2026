namespace ERP.Domain.Enums
{
    public enum InventoryEffect
    {
        None = 0,         // لا أثر مخزني (خدمات أو مستند مالي مسبوق بإذن مخزن)
        ReserveStock = 1, // حجز كمية في المخزن
        IssueStock = 2,   // صرف فوري وخصم من المخزون
        ReceiveStock = 3  // استلام فوري وإضافة للمخزون
    }
}
