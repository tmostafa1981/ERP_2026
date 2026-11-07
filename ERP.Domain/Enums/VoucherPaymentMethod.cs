namespace ERP.Domain.Enums
{
    public enum VoucherPaymentMethod : byte
    {
        Cash = 1,          // نقداً
        Cheque = 2,        // شيك
        BankTransfer = 3,  // تحويل بنكي
        Other = 4          // أخرى
    }
}
