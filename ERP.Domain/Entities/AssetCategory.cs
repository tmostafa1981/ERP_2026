using System;
using System.Collections.Generic;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class AssetCategory : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? Description { get; set; }

        public int DefaultUsefulLifeMonths { get; set; } = 60; // 5 years by default
        public DepreciationMethod DefaultDepreciationMethod { get; set; } = DepreciationMethod.StraightLine;
        public decimal DefaultResidualValueRate { get; set; } = 0m; // % e.g. 5%

        // Account Mappings from Chart of Accounts
        public int AssetCostAccountId { get; set; }
        public int AccumulatedDepreciationAccountId { get; set; }
        public int DepreciationExpenseAccountId { get; set; }
        public int DisposalGainAccountId { get; set; }
        public int DisposalLossAccountId { get; set; }
        public int? CipAccountId { get; set; } // Construction in progress account (optional)

        // Branch scope (null = company-wide)
        public int? BranchId { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigations
        public GLAccount? AssetCostAccount { get; set; }
        public GLAccount? AccumulatedDepreciationAccount { get; set; }
        public GLAccount? DepreciationExpenseAccount { get; set; }
        public GLAccount? DisposalGainAccount { get; set; }
        public GLAccount? DisposalLossAccount { get; set; }
        public GLAccount? CipAccount { get; set; }
        public Branch? Branch { get; set; }

        public ICollection<FixedAsset> Assets { get; set; } = new List<FixedAsset>();

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar")
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : NameAr);
    }
}
