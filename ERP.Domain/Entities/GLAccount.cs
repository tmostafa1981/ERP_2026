using System;
using System.Collections.Generic;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// كيان شجرة الحسابات الموحد (General Ledger Account).
    /// يدمج الحسابات الرئيسية (المجموعات التجميعية) وحسابات الحركة الفرعية في جدول واحد.
    /// </summary>
    public class GLAccount : IAuditableEntity, IConcurrencyAware
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        // التكويد والأسماء
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;

        // الهيكل الشجري (Self-Referencing Hierarchy)
        public int? ParentId { get; set; }
        public int Level { get; set; } = 1;

        /// <summary>
        /// true = حساب رئيسي (تجميعي) لا يقبل القيود المباشرة.
        /// false = حساب حركة فرعي يقبل التوجيه والقيود.
        /// </summary>
        public bool IsGroup { get; set; } = false;

        // الخصائص المحاسبية
        public AccountNature AccountNature { get; set; } = AccountNature.Debit;
        public AccountCategory AccountCategory { get; set; } = AccountCategory.Assets;

        // العملة: null = متعدد العملات، أو قيمة لتقييد الحساب بعملة محددة
        public int? CurrencyId { get; set; }

        // حسابات المراقبة ودفاتر الأستاذ المساعدة (Control Accounts & Sub-Ledger)
        public bool IsControlAccount { get; set; } = false;
        public SubLedgerType SubLedgerType { get; set; } = SubLedgerType.None;
        public bool IsReconciliationAccount { get; set; } = false;

        // مراكز التكلفة ونطاق الشاشات
        public bool RequiresCostCenter { get; set; } = false;
        public AccountUsage AccountUsage { get; set; } = AccountUsage.All;

        // نطاق الفرع (Branch Scope): null = عام لكافة الفروع، أو قيمة محددة لحصر ظهور واستخدام الحساب على فرع معين (مثل الصناديق والخزائن)
        public int? BranchId { get; set; }

        // الحالة وتاريخ الإنشاء
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // العلاقات الملاحية
        public GLAccount? Parent { get; set; }
        public ICollection<GLAccount> Children { get; set; } = new List<GLAccount>();
        public Currency? Currency { get; set; }
        public Branch? Branch { get; set; }
        public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();
        public ICollection<AccountPermission> Permissions { get; set; } = new List<AccountPermission>();

        public string LocalizedName =>
            System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("ar")
                ? (!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)
                : (!string.IsNullOrWhiteSpace(NameEn) ? NameEn : NameAr);
    }
}
