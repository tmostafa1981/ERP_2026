using System;
using System.Collections.Generic;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    public class FixedAsset : IAuditableEntity, IMustHaveBranch, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        // 1. Basic Identification
        public string AssetCode { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public int AssetCategoryId { get; set; }
        public string? Description { get; set; }
        public string? SerialNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ImagePath { get; set; }

        // 2. Location & Responsibility
        public int BranchId { get; set; }
        public int? WarehouseId { get; set; } // Location / Warehouse
        public string? Department { get; set; }
        public int? CostCenterId { get; set; }
        public string? CustodianEmployeeName { get; set; }

        // 3. Status
        public AssetStatus Status { get; set; } = AssetStatus.New;

        // 4. Procurement & Invoicing
        public DateTime PurchaseDate { get; set; } = DateTime.Today;
        public DateTime? PlacedInServiceDate { get; set; } // تاريخ بدء الاستخدام / التشغيل
        public int? VendorId { get; set; }
        public int? PurchaseDocumentHeaderId { get; set; } // ربط بفاتورة المشتريات إن وجدت
        public string? PurchaseInvoiceReference { get; set; }

        // 5. Valuation & Currency
        public int CurrencyId { get; set; } = 1;
        public decimal PurchasePriceForeign { get; set; }
        public decimal ExchangeRate { get; set; } = 1.000000m;
        public decimal InitialCostLocal { get; set; }
        public decimal AdditionalExpensesLocal { get; set; }
        public decimal CurrentCostLocal { get; set; } // التكلفة التاريخية الرأسمالية الحالية (Initial + Additions)
        public decimal ResidualValue { get; set; } = 0m; // القيمة التخريدية

        // 6. Depreciation Parameters
        public int UsefulLifeMonths { get; set; } = 60;
        public DateTime? DepreciationStartDate { get; set; } // تاريخ بداية الإهلاك
        public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;
        public decimal DepreciationRate { get; set; } = 0m; // نسبة الإهلاك السنوية % (للقسط المتناقص)
        public decimal TotalExpectedUnits { get; set; } = 0m; // لوحدات الإنتاج
        public decimal TotalProducedUnits { get; set; } = 0m; // لوحدات الإنتاج
        public FirstMonthPolicy FirstMonthPolicy { get; set; } = FirstMonthPolicy.FullMonth;

        // 7. GL Account Overrides (Optional - If null, inherits from Category)
        public int? AssetCostAccountId { get; set; }
        public int? AccumulatedDepreciationAccountId { get; set; }
        public int? DepreciationExpenseAccountId { get; set; }
        public int? DisposalGainAccountId { get; set; }
        public int? DisposalLossAccountId { get; set; }

        // 8. Opening & Accumulated Tracking
        public bool IsOpeningAsset { get; set; } = false; // هل تم إدخاله كأصل قديم برصيد افتتاحي
        public decimal OpeningCarryingValue { get; set; } = 0m;
        public decimal OpeningAccumulatedDepreciation { get; set; } = 0m;
        public decimal CurrentAccumulatedDepreciation { get; set; } = 0m;
        public decimal CurrentBookValue { get; set; } = 0m; // CurrentCostLocal - CurrentAccumulatedDepreciation
        public DateTime? LastDepreciationDate { get; set; }
        public bool IsFullyDepreciated { get; set; } = false;
        public int? OpeningJournalEntryId { get; set; }

        // 9. Disposal Tracking
        public DateTime? DisposalDate { get; set; }
        public decimal? DisposalProceeds { get; set; } // قيمة البيع / الخردة
        public decimal? DisposalExpenses { get; set; } // مصاريف البيع
        public string? DisposalReason { get; set; }
        public int? DisposalJournalEntryId { get; set; }

        // 10. Audit & Metadata
        public string? Notes { get; set; }
        public string? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigations
        public AssetCategory? AssetCategory { get; set; }
        public Branch? Branch { get; set; }
        public Warehouse? Warehouse { get; set; }
        public CostCenter? CostCenter { get; set; }
        public Vendor? Vendor { get; set; }
        public Currency? Currency { get; set; }
        public DocumentHeader? PurchaseDocumentHeader { get; set; }
        public JournalEntryHeader? DisposalJournalEntry { get; set; }

        public GLAccount? AssetCostAccount { get; set; }
        public GLAccount? AccumulatedDepreciationAccount { get; set; }
        public GLAccount? DepreciationExpenseAccount { get; set; }
        public GLAccount? DisposalGainAccount { get; set; }
        public GLAccount? DisposalLossAccount { get; set; }

        public ICollection<AssetDepreciationHistory> DepreciationHistories { get; set; } = new List<AssetDepreciationHistory>();
        public ICollection<AssetTransaction> Transactions { get; set; } = new List<AssetTransaction>();

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar")
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : NameAr);
    }
}
