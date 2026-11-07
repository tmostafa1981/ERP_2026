using System;
using System.Collections.Generic;
using ERP.Domain.Enums;

namespace ERP.Web.Models
{
    /// <summary>
    /// عقدة شجرة الحسابات لعرض التسلسل الهرمي.
    /// </summary>
    public class AccountTreeNodeDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string DisplayText => $"{Code} - {(System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar") ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn) : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : NameAr))}";

        public int? ParentId { get; set; }
        public int Level { get; set; }
        public bool IsGroup { get; set; }
        public bool IsActive { get; set; }

        public AccountCategory AccountCategory { get; set; }
        public string AccountCategoryText { get; set; } = string.Empty;

        public AccountNature AccountNature { get; set; }
        public string AccountNatureText { get; set; } = string.Empty;

        public bool IsControlAccount { get; set; }
        public SubLedgerType SubLedgerType { get; set; }
        public string? SubLedgerTypeText { get; set; }

        public bool RequiresCostCenter { get; set; }
        public bool IsReconciliationAccount { get; set; }
        public string AccountUsageText { get; set; } = string.Empty;

        public int? CurrencyId { get; set; }
        public string CurrencyCode { get; set; } = "متعدد العملات";

        public int? BranchId { get; set; }
        public string? BranchName { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public bool HasChildren { get; set; }
    }

    /// <summary>
    /// نموذج تفاصيل الحساب الكاملة للوحة الجانبية (Information Panel).
    /// </summary>
    public class AccountDetailsDto : AccountTreeNodeDto
    {
        public string ParentCode { get; set; } = "-";
        public string ParentName { get; set; } = "حساب رئيسي جذري";
        public int ChildrenCount { get; set; }
        public int TransactionsCount { get; set; }
        public bool CanDelete => ChildrenCount == 0 && TransactionsCount == 0;
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal Balance => TotalDebit - TotalCredit;
    }

    /// <summary>
    /// نموذج الإنشاء السريع من الشجرة.
    /// </summary>
    public class QuickAccountCreateDto
    {
        public int? ParentId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public bool IsGroup { get; set; }
        public int? BranchId { get; set; }
        public AccountCategory AccountCategory { get; set; }
        public AccountNature AccountNature { get; set; }
        public int? CurrencyId { get; set; }
        public bool IsControlAccount { get; set; }
        public SubLedgerType SubLedgerType { get; set; }
        public bool RequiresCostCenter { get; set; }
        public bool IsReconciliationAccount { get; set; }
        public AccountUsage AccountUsage { get; set; } = AccountUsage.All;
    }

    /// <summary>
    /// عنصر سجل التدقيق والمراجعة للحساب.
    /// </summary>
    public class AccountAuditHistoryItemDto
    {
        public string Action { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string? Description { get; set; }
        public string? ChangedValues { get; set; }
    }
}
