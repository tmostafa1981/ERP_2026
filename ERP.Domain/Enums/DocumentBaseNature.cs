namespace ERP.Domain.Enums
{
    public enum DocumentBaseNature
    {
        Invoice = 1,          // فاتورة
        Return = 2,           // مردودات
        IssueVoucher = 3,     // إذن صرف مخزني / مذكرة تسليم
        ReceiptVoucher = 4,   // إذن استلام مخزني / مذكرة فحص واستلام
        Quotation = 5,        // عرض سعر
        Order = 6,            // أمر بيع / أمر شراء
        Transfer = 7,         // تحويل مخزني
        Adjustment = 8        // تسوية جردية
    }
}
