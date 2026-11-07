using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Domain.Entities;

namespace ERP.Application.Interfaces
{
    public class CreateCustomerGroupRequestDto
    {
        public Customer BaseCustomer { get; set; } = new();
        public bool IsManualCode { get; set; }

        /// <summary>
        /// List of Currency IDs to create Customer records for.
        /// If empty, uses BaseCustomer.CurrencyId (or creates a single customer record).
        /// </summary>
        public List<int> CustomerCurrencyIds { get; set; } = new();

        /// <summary>
        /// Whether to simultaneously create linked Vendor record(s) in the same group.
        /// </summary>
        public bool CreateLinkedVendor { get; set; }

        /// <summary>
        /// List of Currency IDs to create linked Vendor records for.
        /// If CreateLinkedVendor is true and this list is empty, defaults to CustomerCurrencyIds.
        /// </summary>
        public List<int> VendorCurrencyIds { get; set; } = new();

        public int? LinkedVendorCategoryId { get; set; }
        public int? LinkedVendorFinancialAccountId { get; set; }
    }

    public class CreateCustomerGroupResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int? PartyGroupId { get; set; }
        public string? PartyGroupCode { get; set; }
        public List<Customer> CreatedCustomers { get; set; } = new();
        public List<Vendor> CreatedVendors { get; set; } = new();
    }

    public class RelatedPartyRecordDto
    {
        public string PartyType { get; set; } = string.Empty; // "Customer" or "Vendor"
        public string PartyTypeAr => PartyType == "Vendor" ? "مورد" : "عميل";
        public int PartyId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public int? CurrencyId { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public string CurrencyNameAr { get; set; } = string.Empty;
        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public int? PartyGroupId { get; set; }
        public string? PartyGroupCode { get; set; }
        public bool IsActive { get; set; }

        // Independent ledger balance in the record's own currency
        public decimal DebitInCurrency { get; set; }
        public decimal CreditInCurrency { get; set; }
        public decimal BalanceInCurrency { get; set; }
        public int JournalEntriesCount { get; set; }
        public int DocumentsCount { get; set; }
    }

    public class CurrencyBalanceSummaryDto
    {
        public int? CurrencyId { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public string CurrencyNameAr { get; set; } = string.Empty;

        public decimal TotalCustomerDebit { get; set; }
        public decimal TotalCustomerCredit { get; set; }
        public decimal NetCustomerBalance { get; set; }

        public decimal TotalVendorDebit { get; set; }
        public decimal TotalVendorCredit { get; set; }
        public decimal NetVendorBalance { get; set; }

        /// <summary>
        /// Net balance in this specific currency only (never mixed with other currencies).
        /// </summary>
        public decimal NetGroupBalanceInSameCurrency => NetCustomerBalance + NetVendorBalance;
    }

    public class UnifiedCurrencyEvaluationDto
    {
        public string TargetCurrencyCode { get; set; } = string.Empty;
        public DateTime ConversionDate { get; set; }
        public Dictionary<string, decimal> AppliedExchangeRates { get; set; } = new();
        public decimal TotalConvertedCustomerBalance { get; set; }
        public decimal TotalConvertedVendorBalance { get; set; }
        public decimal TotalConvertedNetBalance { get; set; }
    }

    public class PartyGroupDetailsDto
    {
        public int? PartyGroupId { get; set; }
        public string GroupCode { get; set; } = string.Empty;
        public string GroupNameAr { get; set; } = string.Empty;
        public string GroupNameEn { get; set; } = string.Empty;
        public int? BranchId { get; set; }
        public List<RelatedPartyRecordDto> Records { get; set; } = new();

        /// <summary>
        /// Independent balances per currency (EGP, USD, EUR, CNY, etc.) - never summed across different currencies.
        /// </summary>
        public List<CurrencyBalanceSummaryDto> BalancesByCurrency { get; set; } = new();

        /// <summary>
        /// Optional unified currency evaluation ONLY populated when explicitly requested with exchange rate & conversion date.
        /// </summary>
        public UnifiedCurrencyEvaluationDto? UnifiedEvaluation { get; set; }
    }

    public class LinkPartyRecordRequestDto
    {
        /// <summary>"Customer" or "Vendor"</summary>
        public string SourcePartyType { get; set; } = "Customer";
        public int SourcePartyId { get; set; }

        /// <summary>"Customer" or "Vendor"</summary>
        public string TargetPartyType { get; set; } = "Customer";
        public int TargetPartyId { get; set; }

        /// <summary>
        /// Must be true when moving a record that already belongs to another PartyGroup.
        /// </summary>
        public bool ConfirmMove { get; set; }
    }

    public class LinkPartyRecordResultDto
    {
        public bool Success { get; set; }
        public bool RequiresMoveConfirmation { get; set; }
        public string Message { get; set; } = string.Empty;
        public int? PartyGroupId { get; set; }
        public PartyGroupDetailsDto? GroupDetails { get; set; }
    }

    public interface IPartyGroupService
    {
        /// <summary>
        /// Formats Arabic and English party names with the currency suffix according to business rules,
        /// ensuring NameEn falls back to NameAr + English/Arabic currency suffix if NameEn is blank.
        /// </summary>
        (string NameAr, string NameEn) FormatPartyCurrencyNames(
            string baseNameAr,
            string? baseNameEn,
            Currency? currency,
            bool appendCurrencySuffix = true);

        /// <summary>
        /// Strips any known trailing currency suffix from a party name to obtain the base group name.
        /// </summary>
        string StripCurrencySuffix(string name);

        /// <summary>
        /// Creates one or multiple Customer records (across selected currencies) and optionally
        /// one or multiple linked Vendor records in a single atomic transaction.
        /// </summary>
        Task<CreateCustomerGroupResultDto> CreateCustomerGroupAsync(CreateCustomerGroupRequestDto request);

        /// <summary>
        /// Validates that changing a Customer or Vendor's CurrencyId does not cause a duplicate currency
        /// within its existing PartyGroup.
        /// </summary>
        Task<(bool IsValid, string ErrorMessage)> ValidatePartyCurrencyChangeAsync(
            string partyType,
            int partyId,
            int? partyGroupId,
            int? newCurrencyId);

        /// <summary>
        /// Manually links two existing Customer/Vendor records into the same PartyGroup
        /// (auto-creating a group if neither is linked, or moving between groups with confirmation).
        /// Never alters codes, currencies, balances, or historical transactions.
        /// </summary>
        Task<LinkPartyRecordResultDto> LinkRecordsAsync(LinkPartyRecordRequestDto request);

        /// <summary>
        /// Unlinks a Customer or Vendor record from its PartyGroup without deleting the record itself
        /// or affecting its balances/movements.
        /// </summary>
        Task<LinkPartyRecordResultDto> UnlinkRecordAsync(string partyType, int partyId);

        /// <summary>
        /// Retrieves all linked Customer & Vendor records and their independent per-currency balances
        /// for a given Customer or Vendor (or PartyGroupId).
        /// </summary>
        Task<PartyGroupDetailsDto> GetPartyGroupDetailsAsync(
            string partyType,
            int partyId,
            int? unifiedTargetCurrencyId = null,
            DateTime? conversionDate = null,
            Dictionary<string, decimal>? customExchangeRates = null);

        /// <summary>
        /// Searches Customers or Vendors by Name or Code for manual linking within accessible branches.
        /// </summary>
        Task<List<RelatedPartyRecordDto>> SearchPartiesForLinkingAsync(
            string searchPartyType,
            string searchTerm,
            int branchId,
            string? excludePartyType = null,
            int? excludePartyId = null);
    }
}
