namespace ERP.Domain.Enums
{
    public enum AdjustmentType
    {
        Addition = 1,  // إضافة / مصاريف / أعباء (Charges / Freight / Fees)
        Discount = 2   // خصم / تنزيل (Discounts / Allowances)
    }

    public enum AdjustmentCalculationMethod
    {
        Amount = 1,     // مبلغ ثابت
        Percentage = 2  // نسبة مئوية من الإجمالي
    }
}
