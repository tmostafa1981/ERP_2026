using System.Collections.Generic;
using ERP.Domain.Enums;

namespace ERP.Application.DTOs.Accounts
{
    public class AccountImportRowDto
    {
        public int RowNumber { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public string? ParentCode { get; set; }
        public int Level { get; set; } = 1;
        public bool IsGroup { get; set; } = false;
        public string TypeStr { get; set; } = "حساب حركة (فرعي)";

        public AccountCategory Category { get; set; } = AccountCategory.Assets;
        public string CategoryStr { get; set; } = "أصول";

        public AccountNature Nature { get; set; } = AccountNature.Debit;
        public string NatureStr { get; set; } = "مدين";

        public bool IsControlAccount { get; set; } = false;
        public SubLedgerType SubLedgerType { get; set; } = SubLedgerType.None;
        public string SubLedgerTypeStr { get; set; } = "لا شيء";

        public bool IsReconciliation { get; set; } = false;
        public bool RequiresCostCenter { get; set; } = false;

        public string? CurrencyCode { get; set; }
        public int? CurrencyId { get; set; }

        public string AccountUsageStr { get; set; } = "الكل";
        public AccountUsage AccountUsage { get; set; } = AccountUsage.All;

        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        public bool ExistsInDb { get; set; }
        public string? ExistingType { get; set; } // "مجموعة رئيسية" or "حساب حركة"
    }

    public class AccountImportPreviewDto
    {
        public int TotalRows { get; set; }
        public int ValidCount { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public int GroupCount { get; set; }
        public int AccountCount { get; set; }
        public bool CanExecute => TotalRows > 0 && ErrorCount == 0;
        public List<AccountImportRowDto> Rows { get; set; } = new();
    }

    public class AccountImportResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int GroupsCreated { get; set; }
        public int GroupsUpdated { get; set; }
        public int AccountsCreated { get; set; }
        public int AccountsUpdated { get; set; }
        public int TotalCreated => GroupsCreated + AccountsCreated;
        public int TotalUpdated => GroupsUpdated + AccountsUpdated;
        public List<string> Errors { get; set; } = new();
    }
}
