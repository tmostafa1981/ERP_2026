using System.ComponentModel.DataAnnotations;

namespace ERP.Domain.Enums
{
    /// <summary>
    /// نوع السجل / الملاحظة
    /// </summary>
    public enum FeedbackType
    {
        [Display(Name = "مشكلة / خطأ برمجي")]
        Bug = 1,

        [Display(Name = "طلب جديد / تعديل")]
        NewFeature = 2,

        [Display(Name = "استفسار / توضيح")]
        Inquiry = 3,

        [Display(Name = "اختبار وتجربة برنامج")]
        SystemTest = 4
    }

    /// <summary>
    /// حالة الطلب وتتبع العمل
    /// </summary>
    public enum FeedbackStatus
    {
        [Display(Name = "جديد")]
        New = 1,

        [Display(Name = "جاري العمل")]
        InProgress = 2,

        [Display(Name = "تحت التنفيذ / قيد الاختبار")]
        Testing = 3,

        [Display(Name = "تم الانتهاء")]
        Completed = 4,

        [Display(Name = "ملغي / مرفوض")]
        Rejected = 5
    }

    /// <summary>
    /// درجة الأهمية / الأولوية
    /// </summary>
    public enum FeedbackPriority
    {
        [Display(Name = "عادي / منخفض")]
        Low = 1,

        [Display(Name = "متوسط")]
        Medium = 2,

        [Display(Name = "عاجل")]
        High = 3,

        [Display(Name = "حرج جداً")]
        Critical = 4
    }

    /// <summary>
    /// القسم / الموديول الخاص بالملاحظة
    /// </summary>
    public enum FeedbackModule
    {
        [Display(Name = "عام")]
        General = 0,

        [Display(Name = "الحسابات العامة والدفاتر")]
        Accounting = 1,

        [Display(Name = "الصندوق والبنوك والسندات")]
        CashBank = 2,

        [Display(Name = "المبيعات والفواتير")]
        Sales = 3,

        [Display(Name = "المشتريات والموردين")]
        Purchases = 4,

        [Display(Name = "المخازن والأصناف")]
        Inventory = 5,

        [Display(Name = "إدارة النظام والصلاحيات")]
        Administration = 6,

        [Display(Name = "التقارير")]
        Reports = 7,

        [Display(Name = "أخرى")]
        Other = 8
    }
}
