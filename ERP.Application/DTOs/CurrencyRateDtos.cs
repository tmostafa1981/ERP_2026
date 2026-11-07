using System;
using System.ComponentModel.DataAnnotations;

namespace ERP.Application.DTOs
{
    public class CurrencyDailyRateDto
    {
        public int Id { get; set; }
        public int CurrencyId { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public string CurrencyName { get; set; } = string.Empty;
        public string? CurrencySymbol { get; set; }
        public bool IsBaseCurrency { get; set; }

        public DateTime RateDate { get; set; }
        public decimal ExchangeRate { get; set; }
        public string? Notes { get; set; }

        public string? CreatedByUserId { get; set; }
        public string? CreatedByUserName { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? UpdatedByUserId { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class CreateOrUpdateDailyRateDto
    {
        public int? Id { get; set; }

        [Required(ErrorMessage = "يرجى تحديد العملة.")]
        public int CurrencyId { get; set; }

        [Required(ErrorMessage = "يرجى تحديد تاريخ سعر الصرف.")]
        public DateTime RateDate { get; set; }

        [Range(0.000001, 999999999, ErrorMessage = "يجب أن يكون سعر الصرف أكبر من الصفر.")]
        public decimal ExchangeRate { get; set; }

        [MaxLength(500, ErrorMessage = "الملاحظات يجب ألا تتجاوز 500 حرف.")]
        public string? Notes { get; set; }
    }

    public class ResolveExchangeRateResultDto
    {
        public bool Success { get; set; }
        public decimal Rate { get; set; }
        public DateTime? RateDate { get; set; }
        public int CurrencyId { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public string CurrencyName { get; set; } = string.Empty;
        public bool IsBaseCurrency { get; set; }
        public string? Message { get; set; }
    }

    public class PartyCurrencyInfoDto
    {
        public int PartyId { get; set; }
        public string PartyType { get; set; } = string.Empty; // Customer or Vendor
        public string PartyCode { get; set; } = string.Empty;
        public string PartyName { get; set; } = string.Empty;
        public int? CurrencyId { get; set; }
        public string? CurrencyCode { get; set; }
        public string? CurrencyName { get; set; }
        public bool IsRestricted { get; set; }
    }

    public class AccountCurrencyInfoDto
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public int? CurrencyId { get; set; }
        public string? CurrencyCode { get; set; }
        public string? CurrencyName { get; set; }
        public bool IsRestricted { get; set; }
    }
}
