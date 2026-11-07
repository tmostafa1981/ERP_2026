namespace ERP.Domain.Enums
{
    public enum DepreciationMethod
    {
        StraightLine = 1,        // القسط الثابت
        DecliningBalance = 2,    // القسط المتناقص
        UnitsOfProduction = 3,   // وحدات الإنتاج
        NoDepreciation = 4       // بدون إهلاك (مثل الأراضي)
    }

    public enum AssetStatus
    {
        New = 1,                 // جديد / مسجل
        InService = 2,           // في الخدمة / تحت التشغيل
        UnderConstruction = 3,   // أصل تحت الإنشاء / تجهيز (CIP)
        Suspended = 4,           // متوقف عن التشغيل / الإهلاك
        Disposed = 5             // مستبعد / مباع / كهنة
    }

    public enum FirstMonthPolicy
    {
        FullMonth = 1,           // احتساب شهر كامل
        ProRataDays = 2,         // حسب عدد أيام الاستخدام الفعلية
        NextMonth = 3            // بداية من الشهر التالي
    }

    public enum AssetTransactionType
    {
        Transfer = 1,            // نقل بين الفروع / المواقع
        AdditionalCost = 2,      // إضافة تكلفة لاحقة
        LifeAdjustment = 3,      // تعديل العمر الإنتاجي أو القيمة التخريدية
        StatusChange = 4,        // تغيير الحالة
        Disposal = 5,            // استبعاد أو بيع الأصل
        Capitalization = 6       // تحويل من تحت الإنشاء إلى أصل جاهز
    }

    public enum DepreciationRunStatus
    {
        Draft = 1,               // مسودة
        Approved = 2,            // معتمدة ومرحلة
        Reversed = 3             // معكوسة
    }
}
