namespace ERP.Domain.Enums
{
    public enum AccountingEffect
    {
        None = 0,                   // لا أثر محاسبي (مثل عروض الأسعار أو الأذون الداخلية المؤجلة)
        FullInvoiceAndCOGS = 1,     // قيد مالي تام للفاتورة + تكلفة البضاعة والمخزون COGS
        COGSAndInventoryOnly = 2,   // قيد تكلفة المخزون فقط (إذن الصرف/الاستلام المستقل)
        InvoiceARAPOnly = 3         // قيد العميل/المورد والإيراد والضريبة فقط بدون مخزون (فاتورة مسبوقة بإذن مخزني)
    }
}
