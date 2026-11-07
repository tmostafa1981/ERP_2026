namespace ERP.Domain.Enums
{
    /// <summary>
    /// دورة حياة وحالة الشيك البنكي في السندات
    /// </summary>
    public enum ChequeStatus : byte
    {
        None = 0,             // لا ينطبق (نقدي / تحويل)
        UnderCollection = 1,  // برسم التحصيل (سند قبض) / قيد الصرف (سند صرف)
        Collected = 2,        // تم التحصيل (سند قبض) / تم الصرف (سند صرف)
        Returned = 3,         // مرتد / مرفوض
        Cancelled = 4         // ملغي
    }
}
