namespace ERP.Domain.Enums
{
    public enum ItemTrackingType
    {
        None = 0,          // صنف عادي (بدون سيريال أو باتش)
        SerialNumber = 1,  // صنف يتطلب رقم تسلسلي فريد
        BatchNumber = 2    // صنف يتطلب رقم تشغيلة وتاريخ صلاحية (مستقبلاً)
    }
}
