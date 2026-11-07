namespace ERP.Domain.Enums
{
    /// <summary>
    /// التصنيف الرئيسي لعناصر القوائم المالية
    /// </summary>
    public enum AccountCategory
    {
        Assets = 1,       // الأصول (قائمة المركز المالي)
        Liabilities = 2,  // الخصوم والالتزامات (قائمة المركز المالي)
        Equity = 3,       // حقوق الملكية (قائمة المركز المالي)
        Revenues = 4,     // الإيرادات (قائمة الدخل)
        Expenses = 5      // المصروفات والتكاليف (قائمة الدخل)
    }

    public static class AccountCategoryExtensions
    {
        public static string ToArabicString(this AccountCategory category) => category switch
        {
            AccountCategory.Assets      => "الأصول",
            AccountCategory.Liabilities => "الخصوم والالتزامات",
            AccountCategory.Equity      => "حقوق الملكية",
            AccountCategory.Revenues    => "الإيرادات",
            AccountCategory.Expenses    => "المصروفات والتكاليف",
            _                           => category.ToString()
        };
    }
}
