namespace ERP.Domain.Enums
{
    public enum DocumentConversionStatus
    {
        Unconverted = 0,         // غير محول (كامل الكمية متاحة للتحويل)
        PartiallyConverted = 1,  // محول جزئياً (جزء من الكمية تم تحويله)
        FullyConverted = 2       // محول بالكامل (استنفدت كل الكميات)
    }
}
