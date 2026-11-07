namespace ERP.Application.DTOs
{
    public class CodeSequenceConfigDto
    {
        public string EntityName { get; set; } = string.Empty;
        public int? BranchId { get; set; }
        public string Prefix { get; set; } = string.Empty;
        public long NextNumber { get; set; }
        public int PaddingLength { get; set; } = 6;
        public bool IsAuto { get; set; } = true;
        public bool IsManualAllowed { get; set; } = true;
        public string PreviewCode { get; set; } = string.Empty;
    }
}
