namespace ERP.Domain.Enums
{
    public enum ItemSerialStatus
    {
        Available = 1,   // متوفر بالمخزن وجاهز للصرف
        InStock = 1,     // Alias for Available
        Reserved = 2,    // محجوز لأمر بيع أو مستند معلق
        Sold = 3,        // تم بيعه / صرفه
        Issued = 3,      // Alias for Sold
        Returned = 4,    // مرتجع للمخزن
        Transferred = 5, // قيد النقل بين الفروع أو المستودعات (In-Transit)
        Damaged = 6,     // تالف
        Scrapped = 7     // مستبعد / تكهين
    }
}
