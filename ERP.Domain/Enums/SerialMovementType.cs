namespace ERP.Domain.Enums
{
    public enum SerialMovementType
    {
        PurchaseReceipt = 1,  // استلام مشتريات
        SalesDelivery = 2,    // صرف / تسليم مبيعات
        SalesReturn = 3,      // مردود مبيعات
        PurchaseReturn = 4,   // مردود مشتريات
        TransferOut = 5,      // تحويل صادر (قيد الشحن / In-Transit)
        TransferIn = 6,       // تحويل وارد (استلام المستودع)
        TransferDirect = 7,   // تحويل مباشر بين مستودعين
        AdjustmentIn = 8,     // تسوية جردية - إضافة
        AdjustmentOut = 9,    // تسوية جردية - صرف
        Damaged = 10,         // إثبات تلف
        Scrapped = 11,        // تكهين / استبعاد نهائي
        Reserved = 12,        // حجز لمستند
        Unreserved = 13       // إلغاء حجز
    }
}
