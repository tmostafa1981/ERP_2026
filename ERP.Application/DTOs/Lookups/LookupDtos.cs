using System.Collections.Generic;

namespace ERP.Application.DTOs.Lookups
{
    public class LookupSearchRequest
    {
        public string EntityType { get; set; } = string.Empty;
        public string? Query { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int? BranchId { get; set; }
        public Dictionary<string, string>? Filters { get; set; }
    }

    public class LookupResultDto
    {
        public int Id { get; set; }
        public string? StringId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string DisplayText => $"{Code} - {(!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)}";
        public string? SubTitle { get; set; }
        public string? BadgeText { get; set; }
        public string? BadgeColor { get; set; }
        public decimal? Balance { get; set; }
        public object? ExtraData { get; set; }
    }

    public class LookupPagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public bool HasMore => (Page * PageSize) < TotalCount;
    }

    public class BatchResolveRequestDto
    {
        public string EntityType { get; set; } = string.Empty;
        public List<string> Keys { get; set; } = new();
        public int? BranchId { get; set; }
        public Dictionary<string, string>? Filters { get; set; }
    }

    public class BatchResolveItemResultDto
    {
        public string Key { get; set; } = string.Empty;
        public bool Found { get; set; }
        public LookupResultDto? Result { get; set; }
    }

    public class BatchResolveResponseDto
    {
        public string EntityType { get; set; } = string.Empty;
        public List<BatchResolveItemResultDto> Results { get; set; } = new();
        public int TotalResolved => Results.Count(r => r.Found);
        public int TotalUnresolved => Results.Count(r => !r.Found);
    }
}
