namespace ERP.Domain.Enums
{
    public enum SerialTrackingPolicy
    {
        Disabled = 0,             // معطل تماماً لهذه الحركة
        AsPerItemDefinition = 1,  // تلقائي بحسب تعريف كارت الصنف (Normal / Serial / Batch)
        MandatoryForAll = 2       // إجباري لجميع الأصناف
    }
}
