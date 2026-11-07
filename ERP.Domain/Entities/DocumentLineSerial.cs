using System;

namespace ERP.Domain.Entities
{
    public class DocumentLineSerial
    {
        public int Id { get; set; }
        public int DocumentLineId { get; set; }
        public int? ItemSerialNumberId { get; set; }
        public string SerialNumber { get; set; } = string.Empty;

        // Navigations
        public DocumentLine? DocumentLine { get; set; }
        public ItemSerialNumber? ItemSerialNumber { get; set; }
    }
}
