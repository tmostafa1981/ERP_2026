using System;

namespace ERP.Domain.Entities
{
    public class DocumentLineLink
    {
        public int Id { get; set; }
        public int SourceDocumentLineId { get; set; }
        public int TargetDocumentLineId { get; set; }

        public int ItemId { get; set; }
        public decimal ConvertedQuantity { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigations
        public DocumentLine? SourceDocumentLine { get; set; }
        public DocumentLine? TargetDocumentLine { get; set; }
        public Item? Item { get; set; }
    }
}
