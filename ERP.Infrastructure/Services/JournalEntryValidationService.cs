using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class JournalEntryValidationService : IJournalEntryValidationService
    {
        private readonly ErpDbContext _context;

        public JournalEntryValidationService(ErpDbContext context)
        {
            _context = context;
        }

        public async Task ValidateAsync(JournalEntryHeader header, string? currentUserId = null, bool isManualAction = true)
        {
            if (header == null)
                throw new ArgumentNullException(nameof(header));

            // Rule 6: Auto-Generated Entries cannot be modified manually from JV screen
            if (isManualAction && header.Id > 0 && header.SourceDocumentType != SourceDocumentType.ManualJV)
            {
                throw new InvalidOperationException($"لا يمكن تعديل هذا القيد يدوياً لأنه تم إنشاؤه آلياً من مستند ({header.SourceDocumentType}). التعديل يتم فقط من المستند المصدر.");
            }

            // Rule 5: Closed Fiscal Year
            var fiscalYear = await _context.FiscalYears
                .AsNoTracking()
                .FirstOrDefaultAsync(fy => fy.Id == header.FiscalYearId);

            if (fiscalYear == null)
            {
                throw new InvalidOperationException($"السنة المالية المحددة برقم {header.FiscalYearId} غير موجودة.");
            }

            if (fiscalYear.IsClosed)
            {
                throw new InvalidOperationException($"لا يمكن إنشاء أو تعديل قيود في سنة مالية مغلقة ({fiscalYear.LocalizedName}).");
            }

            var period = await _context.FiscalPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.FiscalYearId == header.FiscalYearId && header.EntryDate >= p.StartDate && header.EntryDate <= p.EndDate);
            if (period != null && period.IsClosed)
            {
                throw new InvalidOperationException($"لا يمكن إنشاء أو تعديل قيود في فترة مالية/شهر مغلق ({period.LocalizedName}).");
            }

            if (header.Lines == null || !header.Lines.Any())
            {
                throw new InvalidOperationException("يجب أن يحتوي القيد على أسطر محاسبية (سطرين على الأقل).");
            }

            if (header.Lines.Count < 2)
            {
                throw new InvalidOperationException("قيد اليومية يجب أن يحتوي على سطرين على الأقل لتحقيق القيد المزدوج.");
            }

            // Rule 1: Double-Entry Local Currency Balance
            decimal sumDebitLocal = header.Lines.Sum(l => l.DebitLocal);
            decimal sumCreditLocal = header.Lines.Sum(l => l.CreditLocal);

            if (Math.Round(sumDebitLocal, 4) != Math.Round(sumCreditLocal, 4))
            {
                throw new InvalidOperationException($"القيد غير متوازن محلياً: إجمالي المدين ({sumDebitLocal:N2}) لا يساوي إجمالي الدائن ({sumCreditLocal:N2}).");
            }

            // Load Accounts used in the lines
            var accountIds = header.Lines.Select(l => l.AccountId).Distinct().ToList();
            var accounts = await _context.GLAccounts
                .AsNoTracking()
                .Include(a => a.Permissions)
                .Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id);

            // Fetch user permissions for these accounts if currentUserId is provided
            var userPermissions = !string.IsNullOrEmpty(currentUserId)
                ? await _context.AccountPermissions
                    .AsNoTracking()
                    .Where(p => p.UserId == currentUserId && accountIds.Contains(p.AccountId))
                    .ToDictionaryAsync(p => p.AccountId)
                : new Dictionary<int, AccountPermission>();

            int lineIndex = 1;
            foreach (var line in header.Lines)
            {
                if (!accounts.TryGetValue(line.AccountId, out var account))
                {
                    throw new InvalidOperationException($"السطر {lineIndex}: الحساب المحدد برقم {line.AccountId} غير موجود.");
                }

                if (!account.IsActive)
                {
                    throw new InvalidOperationException($"السطر {lineIndex}: الحساب ({account.Code} - {account.LocalizedName}) غير نشط.");
                }

                if (account.IsGroup)
                {
                    throw new InvalidOperationException($"السطر {lineIndex}: الحساب ({account.Code} - {account.LocalizedName}) حساب رئيسي تجميعي ولا يقبل حركات مباشرة.");
                }

                // Rule: Branch Scoping / Security
                if (account.BranchId.HasValue && account.BranchId.Value != header.BranchId)
                {
                    throw new InvalidOperationException($"السطر {lineIndex}: الحساب ({account.Code} - {account.LocalizedName}) مخصص لفرع آخر ولا يمكن استخدامه في قيد تابع لفرع رقم {header.BranchId}.");
                }

                // Rule 2: Currency Enforcement
                if (account.CurrencyId.HasValue && account.CurrencyId.Value != line.CurrencyId)
                {
                    throw new InvalidOperationException($"السطر {lineIndex}: عملة الحركة لا تطابق العملة المحددة للحساب ({account.Code} - {account.LocalizedName}).");
                }

                // Rule 3: Account Usage Restriction
                if (account.AccountUsage != AccountUsage.All)
                {
                    bool usageAllowed = false;
                    switch (header.SourceDocumentType)
                    {
                        case SourceDocumentType.ManualJV:
                            usageAllowed = account.AccountUsage == AccountUsage.JournalVoucher;
                            break;
                        case SourceDocumentType.ReceiptVoucher:
                            usageAllowed = account.AccountUsage == AccountUsage.Receipt;
                            break;
                        case SourceDocumentType.PaymentVoucher:
                            usageAllowed = account.AccountUsage == AccountUsage.Payment;
                            break;
                        default:
                            usageAllowed = true;
                            break;
                    }

                    if (!usageAllowed)
                    {
                        throw new InvalidOperationException($"السطر {lineIndex}: الحساب ({account.Code} - {account.LocalizedName}) غير مصرح باستخدامه في شاشات {header.SourceDocumentType}. الاستخدام المسموح به: {account.AccountUsage}.");
                    }
                }

                // Requires Cost Center Check
                if (account.RequiresCostCenter && (!line.CostCenterId.HasValue || line.CostCenterId.Value <= 0))
                {
                    throw new InvalidOperationException($"السطر {lineIndex}: الحساب ({account.Code} - {account.LocalizedName}) يتطلب تحديد مركز تكلفة.");
                }

                // Reconciliation Account Check (Sub-Ledger Required)
                if (account.IsReconciliationAccount && line.SubLedgerType == SubLedgerType.None)
                {
                    throw new InvalidOperationException($"السطر {lineIndex}: الحساب ({account.Code} - {account.LocalizedName}) حساب تسوية (Reconciliation Account) ويشترط تحديد حساب مساعد (Sub-ledger).");
                }

                // Rule 4: Security Rule (CanPost)
                if (!string.IsNullOrEmpty(currentUserId) && userPermissions.TryGetValue(line.AccountId, out var perm))
                {
                    if (!perm.CanPost)
                    {
                        throw new UnauthorizedAccessException($"السطر {lineIndex}: المستخدم ليس لديه صلاحية الترحيل والتوجيه (CanPost) على الحساب ({account.Code} - {account.LocalizedName}).");
                    }
                }

                lineIndex++;
            }
        }
    }
}
