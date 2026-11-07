using System;
using System.Collections.Generic;
using ERP.Domain.Enums;

namespace ERP.Application.DTOs.Revaluation
{
    public class LoadBalancesFilterDto
    {
        public DateTime RevaluationDate { get; set; } = DateTime.Today;
        public int CompanyId { get; set; }
        public int? BranchId { get; set; }
        public RevaluationScope Scope { get; set; } = RevaluationScope.All;
        public int? CurrencyId { get; set; }
        public int? GainAccountId { get; set; }
        public int? LossAccountId { get; set; }
    }

    public class RevaluationLinePreviewDto
    {
        public int LineNumber { get; set; }
        public RevaluationSourceType SourceType { get; set; }
        public string SourceTypeName { get; set; } = string.Empty;
        public int SourceId { get; set; }
        public string SourceCode { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;

        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;

        public int? CustomerId { get; set; }
        public int? VendorId { get; set; }

        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;

        public int CurrencyId { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public string CurrencyName { get; set; } = string.Empty;

        public decimal ForeignBalance { get; set; }
        public decimal CarryingLocalAmount { get; set; }
        public decimal CurrentRecordedRate { get; set; }

        public decimal ExchangeRate { get; set; }
        public decimal RevaluedLocalAmount { get; set; }
        public decimal DifferenceAmount { get; set; }
        public RevaluationDifferenceType DifferenceType { get; set; } = RevaluationDifferenceType.None;
        public AccountNature BalanceNature { get; set; } = AccountNature.Debit;

        public bool HasValidRate { get; set; } = true;
        public string? RateErrorMessage { get; set; }
        public string? Notes { get; set; }
    }

    public class RevaluationPreviewResultDto
    {
        public bool Success { get; set; } = true;
        public string? Message { get; set; }

        public DateTime RevaluationDate { get; set; }
        public int CompanyId { get; set; }
        public int? BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;

        public int FiscalYearId { get; set; }
        public string FiscalYearName { get; set; } = string.Empty;

        public RevaluationScope Scope { get; set; } = RevaluationScope.All;
        public int? CurrencyId { get; set; }
        public string CurrencyName { get; set; } = "كافة العملات الأجنبية";

        public int UnrealizedGainAccountId { get; set; }
        public string UnrealizedGainAccountName { get; set; } = string.Empty;
        public int UnrealizedLossAccountId { get; set; }
        public string UnrealizedLossAccountName { get; set; } = string.Empty;

        public decimal TotalCustomerForeignBalance { get; set; }
        public decimal TotalVendorForeignBalance { get; set; }
        public decimal TotalAccountForeignBalance { get; set; }

        public decimal TotalGain { get; set; }
        public decimal TotalLoss { get; set; }
        public decimal NetDifference { get; set; }

        public bool HasMissingRates { get; set; }
        public List<string> MissingRateCurrencies { get; set; } = new List<string>();

        public List<RevaluationLinePreviewDto> Lines { get; set; } = new List<RevaluationLinePreviewDto>();
    }

    public class CreateRevaluationDto
    {
        public DateTime RevaluationDate { get; set; } = DateTime.Today;
        public int CompanyId { get; set; }
        public int? BranchId { get; set; }
        public int FiscalYearId { get; set; }
        public RevaluationScope Scope { get; set; } = RevaluationScope.All;
        public int? CurrencyId { get; set; }
        public int UnrealizedGainAccountId { get; set; }
        public int UnrealizedLossAccountId { get; set; }
        public string? Notes { get; set; }
        public bool AutoPost { get; set; } = true;
        public List<RevaluationLinePreviewDto> Lines { get; set; } = new List<RevaluationLinePreviewDto>();
    }

    public class RevaluationHeaderListDto
    {
        public int Id { get; set; }
        public string RevaluationNumber { get; set; } = string.Empty;
        public DateTime RevaluationDate { get; set; }
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public int? BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string FiscalYearName { get; set; } = string.Empty;
        public RevaluationScope Scope { get; set; }
        public string ScopeName { get; set; } = string.Empty;
        public string CurrencyName { get; set; } = string.Empty;
        public decimal TotalGain { get; set; }
        public decimal TotalLoss { get; set; }
        public decimal NetDifference { get; set; }
        public RevaluationStatus Status { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public int? JournalEntryId { get; set; }
        public string? JournalEntryNumber { get; set; }
        public int? ReversalJournalEntryId { get; set; }
        public string? ReversalJournalEntryNumber { get; set; }
        public DateTime? ReversalDate { get; set; }
        public string? CreatedByUserName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class RevaluationDetailsDto
    {
        public int Id { get; set; }
        public string RevaluationNumber { get; set; } = string.Empty;
        public DateTime RevaluationDate { get; set; }
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public int? BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public int FiscalYearId { get; set; }
        public string FiscalYearName { get; set; } = string.Empty;
        public RevaluationScope Scope { get; set; }
        public string ScopeName { get; set; } = string.Empty;
        public int? CurrencyId { get; set; }
        public string CurrencyName { get; set; } = string.Empty;

        public int UnrealizedGainAccountId { get; set; }
        public string UnrealizedGainAccountCode { get; set; } = string.Empty;
        public string UnrealizedGainAccountName { get; set; } = string.Empty;

        public int UnrealizedLossAccountId { get; set; }
        public string UnrealizedLossAccountCode { get; set; } = string.Empty;
        public string UnrealizedLossAccountName { get; set; } = string.Empty;

        public decimal TotalGain { get; set; }
        public decimal TotalLoss { get; set; }
        public decimal NetDifference { get; set; }

        public RevaluationStatus Status { get; set; }
        public string StatusName { get; set; } = string.Empty;

        public int? JournalEntryId { get; set; }
        public string? JournalEntryNumber { get; set; }
        public int? ReversalJournalEntryId { get; set; }
        public string? ReversalJournalEntryNumber { get; set; }
        public DateTime? ReversalDate { get; set; }

        public string? Notes { get; set; }
        public string? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? ApprovedByUserId { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? ReversedByUserId { get; set; }
        public DateTime? ReversedAt { get; set; }

        public List<RevaluationLinePreviewDto> Lines { get; set; } = new List<RevaluationLinePreviewDto>();
    }

    public class RevaluationSettingsDto
    {
        public int? DefaultGainAccountId { get; set; }
        public string? DefaultGainAccountName { get; set; }
        public int? DefaultLossAccountId { get; set; }
        public string? DefaultLossAccountName { get; set; }
    }

    public class RevaluationListFilterDto
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int? BranchId { get; set; }
        public RevaluationStatus? Status { get; set; }
        public int? FiscalYearId { get; set; }
        public string? SearchTerm { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / (PageSize > 0 ? PageSize : 10));
    }
}
