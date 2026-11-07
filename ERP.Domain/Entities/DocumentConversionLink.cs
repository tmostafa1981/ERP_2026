using System;

namespace ERP.Domain.Entities
{
    public class DocumentConversionLink
    {
        public int Id { get; set; }
        public int SourceDocumentId { get; set; }
        public int TargetDocumentId { get; set; }

        public DateTime ConversionDate { get; set; } = DateTime.UtcNow;
        public string? ConvertedByUserId { get; set; }
        public string? Notes { get; set; }

        // Navigations
        public DocumentHeader? SourceDocument { get; set; }
        public DocumentHeader? TargetDocument { get; set; }
    }
}
