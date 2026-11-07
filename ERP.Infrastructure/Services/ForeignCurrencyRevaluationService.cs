using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.DTOs.Revaluation;
using ERP.Application.Interfaces;
using ERP.Application.Models;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class ForeignCurrencyRevaluationService : IForeignCurrencyRevaluationService
    {
        private readonly ErpDbContext _context;
        private readonly ICurrencyRateService _currencyRateService;
        private readonly IFiscalPeriodService _fiscalPeriodService;
        private readonly IJournalEntryValidationService _validationService;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string SC_Code = "GL_REVAL";
        private const string SC_Name = "Foreign Currency Revaluation";
        private const string SC_Module = "Accounting";

        private static readonly System.Threading.SemaphoreSlim _revalNumberLock = new System.Threading.SemaphoreSlim(1, 1);
        private static readonly System.Threading.SemaphoreSlim _entryNumberLock = UniversalDocumentService.JournalNumberLock;

        public ForeignCurrencyRevaluationService(
            ErpDbContext context,
            ICurrencyRateService currencyRateService,
            IFiscalPeriodService fiscalPeriodService,
            IJournalEntryValidationService validationService,
            ICurrentUserContext currentUser,
            IAuditService audit)
        {
            _context = context;
            _currencyRateService = currencyRateService;
            _fiscalPeriodService = fiscalPeriodService;
            _validationService = validationService;
            _currentUser = currentUser;
            _audit = audit;
        }

        private static decimal Round4(decimal value) => Math.Round(value, 4);

        public async Task<RevaluationSettingsDto> GetDefaultSettingsAsync(int? branchId = null)
        {
            var baseCurr = await _currencyRateService.GetBaseCurrencyAsync();

            // Look for default Unrealized Gain Account (Revenues - 4)
            var gainAcc = await _context.GLAccounts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(a => a.IsActive && !a.IsGroup &&
                            (a.AccountCategory == AccountCategory.Revenues || a.AccountNature == AccountNature.Credit) &&
                            (a.Code.Contains("420") || a.NameAr.Contains("أرباح فروق") || a.NameAr.Contains("فروق عملة") || a.NameEn.Contains("Exchange Gain")))
                .FirstOrDefaultAsync();

            if (gainAcc == null)
            {
                // Fallback to any active detail revenue account
                gainAcc = await _context.GLAccounts
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(a => a.IsActive && !a.IsGroup && a.AccountCategory == AccountCategory.Revenues)
                    .OrderBy(a => a.Code)
                    .FirstOrDefaultAsync();
            }

            // Look for default Unrealized Loss Account (Expenses - 5)
            var lossAcc = await _context.GLAccounts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(a => a.IsActive && !a.IsGroup &&
                            (a.AccountCategory == AccountCategory.Expenses || a.AccountNature == AccountNature.Debit) &&
                            (a.Code.Contains("520") || a.NameAr.Contains("خسائر فروق") || a.NameAr.Contains("فروق عملة") || a.NameEn.Contains("Exchange Loss")))
                .FirstOrDefaultAsync();

            if (lossAcc == null)
            {
                // Fallback to any active detail expense account
                lossAcc = await _context.GLAccounts
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(a => a.IsActive && !a.IsGroup && a.AccountCategory == AccountCategory.Expenses)
                    .OrderBy(a => a.Code)
                    .FirstOrDefaultAsync();
            }

            return new RevaluationSettingsDto
            {
                DefaultGainAccountId = gainAcc?.Id,
                DefaultGainAccountName = gainAcc != null ? $"{gainAcc.Code} - {gainAcc.LocalizedName}" : null,
                DefaultLossAccountId = lossAcc?.Id,
                DefaultLossAccountName = lossAcc != null ? $"{lossAcc.Code} - {lossAcc.LocalizedName}" : null
            };
        }

        public async Task<RevaluationPreviewResultDto> PreviewBalancesAsync(LoadBalancesFilterDto filter, string userId)
        {
            var targetDate = filter.RevaluationDate.Date;

            // 1. Fiscal Period Validation
            var (isAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(targetDate);
            if (!isAllowed)
            {
                return new RevaluationPreviewResultDto
                {
                    Success = false,
                    Message = periodError ?? "التاريخ يقع داخل فترة مالية مغلقة.",
                    RevaluationDate = targetDate
                };
            }

            var baseCurr = await _currencyRateService.GetBaseCurrencyAsync();

            // 2. Resolve Fiscal Year
            var fy = await _context.FiscalYears
                .AsNoTracking()
                .FirstOrDefaultAsync(y => y.IsActive && targetDate >= y.StartDate && targetDate <= y.EndDate);

            if (fy == null)
            {
                return new RevaluationPreviewResultDto
                {
                    Success = false,
                    Message = $"لا توجد سنة مالية معرّفة أو نشطة تغطي التاريخ ({targetDate:yyyy/MM/dd}).",
                    RevaluationDate = targetDate
                };
            }

            // 3. Resolve Branch Scope & Authorized Branches
            List<int> targetBranchIds;
            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                if (!_currentUser.HasAllBranches && !_currentUser.CanAccessBranch(filter.BranchId.Value))
                {
                    return new RevaluationPreviewResultDto
                    {
                        Success = false,
                        Message = "غير مصرح لك بإجراء إعادة تقييم لفرع لا تملك صلاحية الوصول إليه.",
                        RevaluationDate = targetDate
                    };
                }
                targetBranchIds = new List<int> { filter.BranchId.Value };
            }
            else
            {
                if (_currentUser.HasAllBranches)
                {
                    targetBranchIds = await _context.Branches
                        .Where(b => b.IsActive && (filter.CompanyId <= 0 || b.CompanyId == filter.CompanyId))
                        .Select(b => b.Id)
                        .ToListAsync();
                }
                else
                {
                    targetBranchIds = await _context.UserBranches
                        .Where(ub => ub.UserId == userId && ub.Branch.IsActive)
                        .Select(ub => ub.BranchId)
                        .ToListAsync();

                    if (!targetBranchIds.Any() && _currentUser.BranchId > 0)
                    {
                        targetBranchIds = new List<int> { _currentUser.BranchId };
                    }
                }
            }

            // Branch & Currency Dictionaries
            var branchDict = await _context.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.LocalizedName);
            var currDict = await _context.Currencies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c);

            // 4. Resolve Exchange Rates Cache for target date
            var rateCache = new Dictionary<int, ResolveExchangeRateResultDto>();
            var targetCurrencies = currDict.Values.Where(c => !c.IsBaseCurrency && c.Id != baseCurr.Id).ToList();
            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                targetCurrencies = targetCurrencies.Where(c => c.Id == filter.CurrencyId.Value).ToList();
            }

            foreach (var curr in targetCurrencies)
            {
                var r = await _currencyRateService.ResolveExchangeRateAsync(curr.Id, targetDate);
                rateCache[curr.Id] = r;
            }

            // 5. Identify Control Accounts to Prevent Double Counting
            var customerControlAccIds = await _context.Customers
                .Where(c => c.FinancialAccountId.HasValue)
                .Select(c => c.FinancialAccountId!.Value)
                .Distinct()
                .ToListAsync();

            var customerCategoryControlAccIds = await _context.CustomerCategories
                .Where(cc => cc.ControlAccountId.HasValue)
                .Select(cc => cc.ControlAccountId!.Value)
                .Distinct()
                .ToListAsync();

            var vendorControlAccIds = await _context.Vendors
                .Where(v => v.FinancialAccountId.HasValue)
                .Select(v => v.FinancialAccountId!.Value)
                .Distinct()
                .ToListAsync();

            var vendorCategoryControlAccIds = await _context.VendorCategories
                .Where(vc => vc.ControlAccountId.HasValue)
                .Select(vc => vc.ControlAccountId!.Value)
                .Distinct()
                .ToListAsync();

            var markedControlAccIds = await _context.GLAccounts
                .Where(a => a.IsControlAccount || a.SubLedgerType == SubLedgerType.Customer || a.SubLedgerType == SubLedgerType.Vendor)
                .Select(a => a.Id)
                .ToListAsync();

            var allControlAccountIds = new HashSet<int>(customerControlAccIds
                .Concat(customerCategoryControlAccIds)
                .Concat(vendorControlAccIds)
                .Concat(vendorCategoryControlAccIds)
                .Concat(markedControlAccIds));

            var previewLines = new List<RevaluationLinePreviewDto>();
            int lineNum = 1;

            // ─────────────────────────────────────────────────────────────────
            // 6. CUSTOMERS EVALUATION
            // ─────────────────────────────────────────────────────────────────
            if (filter.Scope == RevaluationScope.All || filter.Scope == RevaluationScope.Customers)
            {
                var custQuery = _context.Customers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(c => c.CustomerCategory)
                    .Include(c => c.FinancialAccount)
                    .Where(c => c.IsActive && targetBranchIds.Contains(c.BranchId));

                if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
                {
                    custQuery = custQuery.Where(c => c.CurrencyId == filter.CurrencyId.Value);
                }
                else
                {
                    custQuery = custQuery.Where(c => c.CurrencyId.HasValue && c.CurrencyId.Value != baseCurr.Id);
                }

                var customers = await custQuery.ToListAsync();
                var custIds = customers.Select(c => c.Id).ToList();

                // Aggregate posted transactions up to targetDate
                var custMovements = await _context.JournalEntryLines
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(l => l.JournalEntryHeader != null &&
                                l.JournalEntryHeader.Status == JournalEntryStatus.Posted &&
                                l.JournalEntryHeader.EntryDate <= targetDate &&
                                l.SubLedgerType == SubLedgerType.Customer &&
                                l.SubLedgerId.HasValue &&
                                custIds.Contains(l.SubLedgerId.Value) &&
                                targetBranchIds.Contains(l.JournalEntryHeader.BranchId))
                    .GroupBy(l => new { CustomerId = l.SubLedgerId!.Value, l.CurrencyId, BranchId = l.JournalEntryHeader!.BranchId })
                    .Select(g => new
                    {
                        g.Key.CustomerId,
                        g.Key.CurrencyId,
                        g.Key.BranchId,
                        DebitForeign = g.Sum(x => x.DebitForeign),
                        CreditForeign = g.Sum(x => x.CreditForeign),
                        DebitLocal = g.Sum(x => x.DebitLocal),
                        CreditLocal = g.Sum(x => x.CreditLocal)
                    })
                    .ToListAsync();

                var custMovementsDict = custMovements.ToDictionary(m => (m.CustomerId, m.CurrencyId, m.BranchId));

                foreach (var cust in customers)
                {
                    int currId = cust.CurrencyId ?? baseCurr.Id;
                    if (currId == baseCurr.Id) continue;

                    var key = (cust.Id, currId, cust.BranchId);
                    if (!custMovementsDict.TryGetValue(key, out var mov)) continue;

                    // Foreign Open Balance for Customer (Normal = Debit: DebitForeign - CreditForeign)
                    decimal foreignBalance = Round4(mov.DebitForeign - mov.CreditForeign);
                    if (Math.Abs(foreignBalance) < 0.0001m)
                    {
                        // Exclude fully settled customers
                        continue;
                    }

                    // Carrying Local Amount
                    decimal carryingLocal = Round4(mov.DebitLocal - mov.CreditLocal);

                    // Rate Resolution
                    if (!rateCache.TryGetValue(currId, out var rateRes))
                    {
                        rateRes = await _currencyRateService.ResolveExchangeRateAsync(currId, targetDate);
                        rateCache[currId] = rateRes;
                    }

                    decimal rate = rateRes.Success ? rateRes.Rate : 0m;
                    decimal revaluedLocal = Round4(foreignBalance * rate);
                    decimal diff = Round4(revaluedLocal - carryingLocal);

                    var diffType = RevaluationDifferenceType.None;
                    if (diff > 0.0001m) diffType = RevaluationDifferenceType.Gain;
                    else if (diff < -0.0001m) diffType = RevaluationDifferenceType.Loss;

                    int targetAccId = cust.FinancialAccountId
                        ?? cust.CustomerCategory?.ControlAccountId
                        ?? (cust.FinancialAccount != null ? cust.FinancialAccount.Id : 0);

                    // If still no account, find default AR control account
                    if (targetAccId <= 0)
                    {
                        var defAR = await _context.GLAccounts.IgnoreQueryFilters().AsNoTracking()
                            .FirstOrDefaultAsync(a => a.Code == "110201" || a.IsControlAccount && a.SubLedgerType == SubLedgerType.Customer);
                        targetAccId = defAR?.Id ?? 0;
                    }

                    var targetAcc = targetAccId > 0 ? await _context.GLAccounts.FindAsync(targetAccId) : null;
                    string currCode = currDict.TryGetValue(currId, out var cObj) ? cObj.Code : "";
                    string currName = cObj?.LocalizedName ?? "";
                    string branchName = branchDict.TryGetValue(cust.BranchId, out var bn) ? bn : "";

                    decimal recordedRate = foreignBalance != 0 ? Round4(carryingLocal / foreignBalance) : rate;

                    previewLines.Add(new RevaluationLinePreviewDto
                    {
                        LineNumber = lineNum++,
                        SourceType = RevaluationSourceType.Customer,
                        SourceTypeName = "عميل",
                        SourceId = cust.Id,
                        SourceCode = cust.Code,
                        SourceName = cust.LocalizedName,
                        AccountId = targetAccId,
                        AccountCode = targetAcc?.Code ?? "",
                        AccountName = targetAcc?.LocalizedName ?? "حساب مراقبة العملاء",
                        CustomerId = cust.Id,
                        VendorId = null,
                        BranchId = cust.BranchId,
                        BranchName = branchName,
                        CurrencyId = currId,
                        CurrencyCode = currCode,
                        CurrencyName = currName,
                        ForeignBalance = foreignBalance,
                        CarryingLocalAmount = carryingLocal,
                        CurrentRecordedRate = recordedRate,
                        ExchangeRate = rate,
                        RevaluedLocalAmount = revaluedLocal,
                        DifferenceAmount = Math.Abs(diff),
                        DifferenceType = diffType,
                        BalanceNature = foreignBalance >= 0 ? AccountNature.Debit : AccountNature.Credit,
                        HasValidRate = rateRes.Success,
                        RateErrorMessage = rateRes.Success ? null : rateRes.Message
                    });
                }
            }

            // ─────────────────────────────────────────────────────────────────
            // 7. VENDORS EVALUATION
            // ─────────────────────────────────────────────────────────────────
            if (filter.Scope == RevaluationScope.All || filter.Scope == RevaluationScope.Vendors)
            {
                var vendQuery = _context.Vendors
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(v => v.VendorCategory)
                    .Include(v => v.FinancialAccount)
                    .Where(v => v.IsActive && targetBranchIds.Contains(v.BranchId));

                if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
                {
                    vendQuery = vendQuery.Where(v => v.CurrencyId == filter.CurrencyId.Value);
                }
                else
                {
                    vendQuery = vendQuery.Where(v => v.CurrencyId.HasValue && v.CurrencyId.Value != baseCurr.Id);
                }

                var vendors = await vendQuery.ToListAsync();
                var vendIds = vendors.Select(v => v.Id).ToList();

                var vendMovements = await _context.JournalEntryLines
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(l => l.JournalEntryHeader != null &&
                                l.JournalEntryHeader.Status == JournalEntryStatus.Posted &&
                                l.JournalEntryHeader.EntryDate <= targetDate &&
                                l.SubLedgerType == SubLedgerType.Vendor &&
                                l.SubLedgerId.HasValue &&
                                vendIds.Contains(l.SubLedgerId.Value) &&
                                targetBranchIds.Contains(l.JournalEntryHeader.BranchId))
                    .GroupBy(l => new { VendorId = l.SubLedgerId!.Value, l.CurrencyId, BranchId = l.JournalEntryHeader!.BranchId })
                    .Select(g => new
                    {
                        g.Key.VendorId,
                        g.Key.CurrencyId,
                        g.Key.BranchId,
                        DebitForeign = g.Sum(x => x.DebitForeign),
                        CreditForeign = g.Sum(x => x.CreditForeign),
                        DebitLocal = g.Sum(x => x.DebitLocal),
                        CreditLocal = g.Sum(x => x.CreditLocal)
                    })
                    .ToListAsync();

                var vendMovementsDict = vendMovements.ToDictionary(m => (m.VendorId, m.CurrencyId, m.BranchId));

                foreach (var vend in vendors)
                {
                    int currId = vend.CurrencyId ?? baseCurr.Id;
                    if (currId == baseCurr.Id) continue;

                    var key = (vend.Id, currId, vend.BranchId);
                    if (!vendMovementsDict.TryGetValue(key, out var mov)) continue;

                    // Foreign Open Balance for Vendor (Normal = Credit: CreditForeign - DebitForeign)
                    decimal foreignBalance = Round4(mov.CreditForeign - mov.DebitForeign);
                    if (Math.Abs(foreignBalance) < 0.0001m)
                    {
                        // Exclude fully settled vendors
                        continue;
                    }

                    // Carrying Local Amount (CreditLocal - DebitLocal)
                    decimal carryingLocal = Round4(mov.CreditLocal - mov.DebitLocal);

                    if (!rateCache.TryGetValue(currId, out var rateRes))
                    {
                        rateRes = await _currencyRateService.ResolveExchangeRateAsync(currId, targetDate);
                        rateCache[currId] = rateRes;
                    }

                    decimal rate = rateRes.Success ? rateRes.Rate : 0m;
                    decimal revaluedLocal = Round4(foreignBalance * rate);
                    decimal diff = Round4(revaluedLocal - carryingLocal);

                    // For Vendor (Credit balance):
                    // If revaluedLocal > carryingLocal -> we owe more in local currency -> LOSS!
                    // If revaluedLocal < carryingLocal -> we owe less in local currency -> GAIN!
                    var diffType = RevaluationDifferenceType.None;
                    if (diff > 0.0001m) diffType = RevaluationDifferenceType.Loss;
                    else if (diff < -0.0001m) diffType = RevaluationDifferenceType.Gain;

                    int targetAccId = vend.FinancialAccountId
                        ?? vend.VendorCategory?.ControlAccountId
                        ?? (vend.FinancialAccount != null ? vend.FinancialAccount.Id : 0);

                    if (targetAccId <= 0)
                    {
                        var defAP = await _context.GLAccounts.IgnoreQueryFilters().AsNoTracking()
                            .FirstOrDefaultAsync(a => a.Code == "210101" || a.IsControlAccount && a.SubLedgerType == SubLedgerType.Vendor);
                        targetAccId = defAP?.Id ?? 0;
                    }

                    var targetAcc = targetAccId > 0 ? await _context.GLAccounts.FindAsync(targetAccId) : null;
                    string currCode = currDict.TryGetValue(currId, out var cObj) ? cObj.Code : "";
                    string currName = cObj?.LocalizedName ?? "";
                    string branchName = branchDict.TryGetValue(vend.BranchId, out var bn) ? bn : "";

                    decimal recordedRate = foreignBalance != 0 ? Round4(carryingLocal / foreignBalance) : rate;

                    previewLines.Add(new RevaluationLinePreviewDto
                    {
                        LineNumber = lineNum++,
                        SourceType = RevaluationSourceType.Vendor,
                        SourceTypeName = "مورد",
                        SourceId = vend.Id,
                        SourceCode = vend.Code,
                        SourceName = vend.LocalizedName,
                        AccountId = targetAccId,
                        AccountCode = targetAcc?.Code ?? "",
                        AccountName = targetAcc?.LocalizedName ?? "حساب مراقبة الموردين",
                        CustomerId = null,
                        VendorId = vend.Id,
                        BranchId = vend.BranchId,
                        BranchName = branchName,
                        CurrencyId = currId,
                        CurrencyCode = currCode,
                        CurrencyName = currName,
                        ForeignBalance = foreignBalance,
                        CarryingLocalAmount = carryingLocal,
                        CurrentRecordedRate = recordedRate,
                        ExchangeRate = rate,
                        RevaluedLocalAmount = revaluedLocal,
                        DifferenceAmount = Math.Abs(diff),
                        DifferenceType = diffType,
                        BalanceNature = foreignBalance >= 0 ? AccountNature.Credit : AccountNature.Debit,
                        HasValidRate = rateRes.Success,
                        RateErrorMessage = rateRes.Success ? null : rateRes.Message
                    });
                }
            }

            // ─────────────────────────────────────────────────────────────────
            // 8. GENERAL GL ACCOUNTS EVALUATION (EXCLUDING CONTROL ACCOUNTS!)
            // ─────────────────────────────────────────────────────────────────
            if (filter.Scope == RevaluationScope.All || filter.Scope == RevaluationScope.GeneralAccounts)
            {
                // Find all GL lines with foreign currency, excluding sub-ledger lines and control accounts
                var glLinesQuery = _context.JournalEntryLines
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(l => l.Account)
                    .Include(l => l.JournalEntryHeader)
                    .Where(l => l.JournalEntryHeader != null &&
                                l.JournalEntryHeader.Status == JournalEntryStatus.Posted &&
                                l.JournalEntryHeader.EntryDate <= targetDate &&
                                l.SubLedgerType == SubLedgerType.None &&
                                l.CurrencyId != baseCurr.Id &&
                                targetBranchIds.Contains(l.JournalEntryHeader.BranchId) &&
                                !allControlAccountIds.Contains(l.AccountId) &&
                                l.Account != null && !l.Account.IsGroup && l.Account.IsActive);

                if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
                {
                    glLinesQuery = glLinesQuery.Where(l => l.CurrencyId == filter.CurrencyId.Value);
                }

                var glGroups = await glLinesQuery
                    .GroupBy(l => new { l.AccountId, l.CurrencyId, BranchId = l.JournalEntryHeader!.BranchId })
                    .Select(g => new
                    {
                        g.Key.AccountId,
                        g.Key.CurrencyId,
                        g.Key.BranchId,
                        DebitForeign = g.Sum(x => x.DebitForeign),
                        CreditForeign = g.Sum(x => x.CreditForeign),
                        DebitLocal = g.Sum(x => x.DebitLocal),
                        CreditLocal = g.Sum(x => x.CreditLocal)
                    })
                    .ToListAsync();

                var accountIds = glGroups.Select(g => g.AccountId).Distinct().ToList();
                var accountDict = await _context.GLAccounts
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(a => accountIds.Contains(a.Id))
                    .ToDictionaryAsync(a => a.Id);

                foreach (var g in glGroups)
                {
                    if (!accountDict.TryGetValue(g.AccountId, out var acc)) continue;

                    int currId = g.CurrencyId;
                    bool isDebitNature = acc.AccountNature == AccountNature.Debit;

                    decimal foreignBalance = isDebitNature
                        ? Round4(g.DebitForeign - g.CreditForeign)
                        : Round4(g.CreditForeign - g.DebitForeign);

                    if (Math.Abs(foreignBalance) < 0.0001m)
                    {
                        continue;
                    }

                    decimal carryingLocal = isDebitNature
                        ? Round4(g.DebitLocal - g.CreditLocal)
                        : Round4(g.CreditLocal - g.DebitLocal);

                    if (!rateCache.TryGetValue(currId, out var rateRes))
                    {
                        rateRes = await _currencyRateService.ResolveExchangeRateAsync(currId, targetDate);
                        rateCache[currId] = rateRes;
                    }

                    decimal rate = rateRes.Success ? rateRes.Rate : 0m;
                    decimal revaluedLocal = Round4(foreignBalance * rate);
                    decimal diff = Round4(revaluedLocal - carryingLocal);

                    var diffType = RevaluationDifferenceType.None;
                    if (isDebitNature)
                    {
                        // Asset / Debit: if revalued > carrying -> Gain
                        if (diff > 0.0001m) diffType = RevaluationDifferenceType.Gain;
                        else if (diff < -0.0001m) diffType = RevaluationDifferenceType.Loss;
                    }
                    else
                    {
                        // Liability / Credit: if revalued > carrying -> Loss
                        if (diff > 0.0001m) diffType = RevaluationDifferenceType.Loss;
                        else if (diff < -0.0001m) diffType = RevaluationDifferenceType.Gain;
                    }

                    string currCode = currDict.TryGetValue(currId, out var cObj) ? cObj.Code : "";
                    string currName = cObj?.LocalizedName ?? "";
                    string branchName = branchDict.TryGetValue(g.BranchId, out var bn) ? bn : "";
                    decimal recordedRate = foreignBalance != 0 ? Round4(carryingLocal / foreignBalance) : rate;

                    previewLines.Add(new RevaluationLinePreviewDto
                    {
                        LineNumber = lineNum++,
                        SourceType = RevaluationSourceType.GeneralAccount,
                        SourceTypeName = "حساب عام",
                        SourceId = acc.Id,
                        SourceCode = acc.Code,
                        SourceName = acc.LocalizedName,
                        AccountId = acc.Id,
                        AccountCode = acc.Code,
                        AccountName = acc.LocalizedName,
                        CustomerId = null,
                        VendorId = null,
                        BranchId = g.BranchId,
                        BranchName = branchName,
                        CurrencyId = currId,
                        CurrencyCode = currCode,
                        CurrencyName = currName,
                        ForeignBalance = foreignBalance,
                        CarryingLocalAmount = carryingLocal,
                        CurrentRecordedRate = recordedRate,
                        ExchangeRate = rate,
                        RevaluedLocalAmount = revaluedLocal,
                        DifferenceAmount = Math.Abs(diff),
                        DifferenceType = diffType,
                        BalanceNature = acc.AccountNature,
                        HasValidRate = rateRes.Success,
                        RateErrorMessage = rateRes.Success ? null : rateRes.Message
                    });
                }
            }

            // 9. Summaries & Gain/Loss Aggregations
            decimal totalCustForeign = previewLines.Where(l => l.SourceType == RevaluationSourceType.Customer).Sum(l => l.ForeignBalance);
            decimal totalVendForeign = previewLines.Where(l => l.SourceType == RevaluationSourceType.Vendor).Sum(l => l.ForeignBalance);
            decimal totalAccForeign = previewLines.Where(l => l.SourceType == RevaluationSourceType.GeneralAccount).Sum(l => l.ForeignBalance);

            decimal totalGain = previewLines.Where(l => l.DifferenceType == RevaluationDifferenceType.Gain).Sum(l => l.DifferenceAmount);
            decimal totalLoss = previewLines.Where(l => l.DifferenceType == RevaluationDifferenceType.Loss).Sum(l => l.DifferenceAmount);
            decimal netDiff = Round4(totalGain - totalLoss);

            // Default Unrealized Gain/Loss Accounts
            var defSettings = await GetDefaultSettingsAsync(filter.BranchId);
            int gainAccId = filter.GainAccountId ?? defSettings.DefaultGainAccountId ?? 0;
            int lossAccId = filter.LossAccountId ?? defSettings.DefaultLossAccountId ?? 0;

            var gainAccount = gainAccId > 0 ? await _context.GLAccounts.FindAsync(gainAccId) : null;
            var lossAccount = lossAccId > 0 ? await _context.GLAccounts.FindAsync(lossAccId) : null;

            string branchLabel = filter.BranchId.HasValue && branchDict.TryGetValue(filter.BranchId.Value, out var singleBName)
                ? singleBName
                : "كافة الفروع المصرح بها";

            string currencyLabel = filter.CurrencyId.HasValue && currDict.TryGetValue(filter.CurrencyId.Value, out var cInfo)
                ? $"{cInfo.LocalizedName} ({cInfo.Code})"
                : "كافة العملات الأجنبية";

            var missingCurrencies = previewLines
                .Where(l => !l.HasValidRate)
                .Select(l => $"{l.CurrencyName} ({l.CurrencyCode})")
                .Distinct()
                .ToList();

            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                if (rateCache.TryGetValue(filter.CurrencyId.Value, out var specificRateRes) && !specificRateRes.Success)
                {
                    if (currDict.TryGetValue(filter.CurrencyId.Value, out var specCurr))
                    {
                        var label = $"{specCurr.LocalizedName} ({specCurr.Code})";
                        if (!missingCurrencies.Contains(label))
                        {
                            missingCurrencies.Add(label);
                        }
                    }
                }
            }

            return new RevaluationPreviewResultDto
            {
                Success = true,
                RevaluationDate = targetDate,
                CompanyId = filter.CompanyId,
                BranchId = filter.BranchId,
                BranchName = branchLabel,
                FiscalYearId = fy.Id,
                FiscalYearName = fy.LocalizedName,
                Scope = filter.Scope,
                CurrencyId = filter.CurrencyId,
                CurrencyName = currencyLabel,
                UnrealizedGainAccountId = gainAccId,
                UnrealizedGainAccountName = gainAccount != null ? $"{gainAccount.Code} - {gainAccount.LocalizedName}" : "",
                UnrealizedLossAccountId = lossAccId,
                UnrealizedLossAccountName = lossAccount != null ? $"{lossAccount.Code} - {lossAccount.LocalizedName}" : "",
                TotalCustomerForeignBalance = totalCustForeign,
                TotalVendorForeignBalance = totalVendForeign,
                TotalAccountForeignBalance = totalAccForeign,
                TotalGain = totalGain,
                TotalLoss = totalLoss,
                NetDifference = netDiff,
                HasMissingRates = missingCurrencies.Any(),
                MissingRateCurrencies = missingCurrencies,
                Lines = previewLines
            };
        }

        private async Task<string> GenerateNextRevaluationNumberAsync(int? branchId, int fiscalYearId, string fyYear)
        {
            string prefix = branchId.HasValue ? $"REV-{branchId.Value}-{fyYear}-" : $"REV-ALL-{fyYear}-";

            var existingNumbers = await _context.RevaluationHeaders
                .IgnoreQueryFilters()
                .Where(h => h.FiscalYearId == fiscalYearId && h.RevaluationNumber.StartsWith(prefix))
                .Select(h => h.RevaluationNumber)
                .ToListAsync();

            long maxNumber = 0;
            foreach (var num in existingNumbers)
            {
                if (num.Length > prefix.Length)
                {
                    var suffix = num.Substring(prefix.Length);
                    if (long.TryParse(suffix, out long n) && n > maxNumber)
                    {
                        maxNumber = n;
                    }
                }
            }

            long candidate = maxNumber + 1;
            string candidateCode = $"{prefix}{candidate:D5}";

            while (await _context.RevaluationHeaders.IgnoreQueryFilters().AnyAsync(h => h.RevaluationNumber == candidateCode))
            {
                candidate++;
                candidateCode = $"{prefix}{candidate:D5}";
            }

            return candidateCode;
        }

        private async Task<string> GenerateNextJournalEntryNumberAsync(int branchId, int fiscalYearId, string fiscalYear)
        {
            string prefix = $"JV-{branchId}-{fiscalYear}-";

            var existingNumbers = await _context.JournalEntryHeaders
                .IgnoreQueryFilters()
                .Where(h => h.BranchId == branchId && h.FiscalYearId == fiscalYearId && h.EntryNumber.StartsWith(prefix))
                .Select(h => h.EntryNumber)
                .ToListAsync();

            long maxNumber = 0;
            foreach (var num in existingNumbers)
            {
                if (num.Length > prefix.Length)
                {
                    var suffix = num.Substring(prefix.Length);
                    if (long.TryParse(suffix, out long n) && n > maxNumber)
                    {
                        maxNumber = n;
                    }
                }
            }

            int totalInFY = await _context.JournalEntryHeaders
                .IgnoreQueryFilters()
                .Where(h => h.BranchId == branchId && h.FiscalYearId == fiscalYearId)
                .CountAsync();

            if (totalInFY > maxNumber)
                maxNumber = totalInFY;

            long candidate = maxNumber + 1;
            string candidateCode = $"{prefix}{candidate:D5}";

            while (await _context.JournalEntryHeaders.IgnoreQueryFilters().AnyAsync(h => h.BranchId == branchId && h.EntryNumber == candidateCode))
            {
                candidate++;
                candidateCode = $"{prefix}{candidate:D5}";
            }

            return candidateCode;
        }

        public async Task<(bool Success, string Message, int? HeaderId, string? RevaluationNumber)> CreateRevaluationAsync(CreateRevaluationDto dto, string userId)
        {
            if (dto == null)
                return (false, "البيانات المدخلة غير صحيحة.", null, null);

            var targetDate = dto.RevaluationDate.Date;

            // 1. Fiscal Period Validation
            var (isAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(targetDate);
            if (!isAllowed)
                return (false, periodError ?? "لا يمكن إنشاء تقييم في فترة مالية مغلقة.", null, null);

            // 2. Validate Unrealized Accounts
            var gainAcc = await _context.GLAccounts.FindAsync(dto.UnrealizedGainAccountId);
            if (gainAcc == null || !gainAcc.IsActive || gainAcc.IsGroup)
                return (false, "حساب أرباح فروق العملة غير المحققة غير صالح أو حساب رئيسي أو غير نشط.", null, null);

            var lossAcc = await _context.GLAccounts.FindAsync(dto.UnrealizedLossAccountId);
            if (lossAcc == null || !lossAcc.IsActive || lossAcc.IsGroup)
                return (false, "حساب خسائر فروق العملة غير المحققة غير صالح أو حساب رئيسي أو غير نشط.", null, null);

            // 3. Prevent duplicate active/approved revaluation for the same date & branch scope
            bool existsDuplicate = await _context.RevaluationHeaders
                .IgnoreQueryFilters()
                .AnyAsync(h => h.CompanyId == dto.CompanyId &&
                               h.BranchId == dto.BranchId &&
                               h.RevaluationDate == targetDate &&
                               h.Scope == dto.Scope &&
                               (h.Status == RevaluationStatus.Draft || h.Status == RevaluationStatus.Approved));

            if (existsDuplicate)
            {
                return (false, $"توجد بالفعل عملية إعادة تقييم مسجلة لنفس التاريخ ({targetDate:yyyy-MM-dd}) وبنفس النطاق. يرجى مراجعة العملية السابقة أو عكسها أولاً.", null, null);
            }

            // 4. Reload authorative balances from DB
            var preview = await PreviewBalancesAsync(new LoadBalancesFilterDto
            {
                RevaluationDate = targetDate,
                CompanyId = dto.CompanyId,
                BranchId = dto.BranchId,
                Scope = dto.Scope,
                CurrencyId = dto.CurrencyId,
                GainAccountId = dto.UnrealizedGainAccountId,
                LossAccountId = dto.UnrealizedLossAccountId
            }, userId);

            if (!preview.Success)
            {
                return (false, preview.Message ?? "فشل تحميل الأرصدة للتقييم.", null, null);
            }

            if (!preview.Lines.Any())
            {
                return (false, "لا توجد أي أرصدة مفتوحة بالعملات الأجنبية تستوفي شروط التقييم حتى هذا التاريخ.", null, null);
            }

            if (preview.HasMissingRates)
            {
                return (false, $"لا يمكن حفظ أو اعتماد عملية التقييم لوجود عملات أجنبية ليس لها سعر صرف صالح: {string.Join(", ", preview.MissingRateCurrencies)}", null, null);
            }

            var fy = await _context.FiscalYears.FindAsync(preview.FiscalYearId);
            string fyYear = fy?.Year ?? targetDate.Year.ToString();

            RevaluationHeader header;
            await _revalNumberLock.WaitAsync();
            try
            {
                string revNumber = await GenerateNextRevaluationNumberAsync(dto.BranchId, preview.FiscalYearId, fyYear);

                header = new RevaluationHeader
                {
                    RevaluationNumber = revNumber,
                    RevaluationDate = targetDate,
                    CompanyId = dto.CompanyId,
                    BranchId = dto.BranchId,
                    FiscalYearId = preview.FiscalYearId,
                    Scope = dto.Scope,
                    CurrencyId = dto.CurrencyId,
                    UnrealizedGainAccountId = dto.UnrealizedGainAccountId,
                    UnrealizedLossAccountId = dto.UnrealizedLossAccountId,
                    TotalGain = preview.TotalGain,
                    TotalLoss = preview.TotalLoss,
                    NetDifference = preview.NetDifference,
                    Status = RevaluationStatus.Draft,
                    Notes = dto.Notes?.Trim(),
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                int lineIndex = 1;
                foreach (var pl in preview.Lines)
                {
                    header.Lines.Add(new RevaluationLine
                    {
                        LineNumber = lineIndex++,
                        SourceType = pl.SourceType,
                        SourceId = pl.SourceId,
                        SourceCode = pl.SourceCode,
                        SourceName = pl.SourceName,
                        AccountId = pl.AccountId,
                        CustomerId = pl.CustomerId,
                        VendorId = pl.VendorId,
                        BranchId = pl.BranchId,
                        CurrencyId = pl.CurrencyId,
                        ForeignBalance = pl.ForeignBalance,
                        CarryingLocalAmount = pl.CarryingLocalAmount,
                        ExchangeRate = pl.ExchangeRate,
                        RevaluedLocalAmount = pl.RevaluedLocalAmount,
                        DifferenceAmount = pl.DifferenceAmount,
                        DifferenceType = pl.DifferenceType,
                        BalanceNature = pl.BalanceNature,
                        Notes = pl.Notes,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                _context.RevaluationHeaders.Add(header);
                await _context.SaveChangesAsync();
            }
            finally
            {
                _revalNumberLock.Release();
            }

            await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "RevaluationHeader",
                entityId: header.Id.ToString(),
                newValues: new
                {
                    header.Id,
                    header.RevaluationNumber,
                    header.RevaluationDate,
                    header.BranchId,
                    header.TotalGain,
                    header.TotalLoss,
                    header.NetDifference,
                    LinesCount = header.Lines.Count
                });

            // If auto-post is requested
            if (dto.AutoPost)
            {
                var postRes = await ApproveAndPostAsync(header.Id, userId);
                if (!postRes.Success)
                {
                    return (true, $"تم إنشاء عملية إعادة التقييم ({header.RevaluationNumber}) كمسودة، ولكن تعذر الترحيل التلقائي: {postRes.Message}", header.Id, header.RevaluationNumber);
                }

                return (true, $"تم إنشاء واعتماد عملية إعادة التقييم ({header.RevaluationNumber}) وترحيل القيد المحاسبي بنجاح.", header.Id, header.RevaluationNumber);
            }

            return (true, $"تم حفظ مسودة عملية إعادة التقييم ({header.RevaluationNumber}) بنجاح.", header.Id, header.RevaluationNumber);
        }

        public async Task<(bool Success, string Message, int? JournalEntryId)> ApproveAndPostAsync(int id, string userId)
        {
            var header = await _context.RevaluationHeaders
                .IgnoreQueryFilters()
                .Include(h => h.Lines)
                .Include(h => h.FiscalYear)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null)
                return (false, "عملية إعادة التقييم غير موجودة.", null);

            if (header.Status == RevaluationStatus.Approved)
                return (false, "عملية إعادة التقييم معتمدة ومرحلة بالفعل.", header.JournalEntryId);

            if (header.Status == RevaluationStatus.Reversed)
                return (false, "لا يمكن اعتماد عملية إعادة تقييم معكوسة.", null);

            if (header.Status == RevaluationStatus.Cancelled)
                return (false, "لا يمكن اعتماد عملية إعادة تقييم ملغاة.", null);

            // Fiscal period check
            var (isAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(header.RevaluationDate);
            if (!isAllowed)
                return (false, periodError ?? "لا يمكن ترحيل تقييم داخل فترة مالية مغلقة.", null);

            if (!header.Lines.Any())
                return (false, "لا توجد سطور لإعادة التقييم.", null);

            // Re-verify that all lines have valid rates
            var baseCurr = await _currencyRateService.GetBaseCurrencyAsync();
            var missingRates = header.Lines.Where(l => l.CurrencyId != baseCurr.Id && l.ExchangeRate <= 0).ToList();
            if (missingRates.Any())
            {
                return (false, "يوجد سطور ليس لها سعر صرف صالح. يرجى تعديل أو إعادة تحميل أسعار الصرف.", null);
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
                try
                {
                    // Group lines by BranchId to generate independent, perfectly balanced journal entries per branch
                    var branchGroups = header.Lines.GroupBy(l => l.BranchId).ToList();
                    string fyYear = header.FiscalYear?.Year ?? header.RevaluationDate.Year.ToString();

                    int? primaryJournalEntryId = null;

                    foreach (var bg in branchGroups)
                    {
                        int branchId = bg.Key;
                        var branchLines = bg.ToList();

                        decimal branchGain = branchLines.Where(l => l.DifferenceType == RevaluationDifferenceType.Gain).Sum(l => l.DifferenceAmount);
                        decimal branchLoss = branchLines.Where(l => l.DifferenceType == RevaluationDifferenceType.Loss).Sum(l => l.DifferenceAmount);

                        if (branchGain == 0 && branchLoss == 0)
                        {
                            continue; // No net adjustment needed for this branch
                        }

                        // Concurrency-safe Journal Entry numbering
                        await _entryNumberLock.WaitAsync();
                        JournalEntryHeader jvHeader;
                        try
                        {
                            string jvNumber = await GenerateNextJournalEntryNumberAsync(branchId, header.FiscalYearId, fyYear);

                            jvHeader = new JournalEntryHeader
                            {
                                BranchId = branchId,
                                FiscalYearId = header.FiscalYearId,
                                EntryNumber = jvNumber,
                                EntryDate = header.RevaluationDate,
                                Description = $"قيد فروق إعادة تقييم العملات الأجنبية ({header.RevaluationNumber}) بتاريخ ({header.RevaluationDate:yyyy-MM-dd})",
                                ReferenceNumber = header.RevaluationNumber,
                                Notes = header.Notes,
                                Status = JournalEntryStatus.Posted,
                                SourceDocumentType = SourceDocumentType.CurrencyRevaluation,
                                SourceDocumentId = header.Id,
                                PostedAt = DateTime.UtcNow,
                                PostedByUserId = userId,
                                CreatedAt = DateTime.UtcNow
                            };

                            int jvLineNum = 1;

                            // 1. Lines for GAINS:
                            // Debit party/account, Credit Unrealized Gain Account
                            foreach (var line in branchLines.Where(l => l.DifferenceType == RevaluationDifferenceType.Gain && l.DifferenceAmount > 0))
                            {
                                var subType = line.SourceType switch
                                {
                                    RevaluationSourceType.Customer => SubLedgerType.Customer,
                                    RevaluationSourceType.Vendor => SubLedgerType.Vendor,
                                    _ => SubLedgerType.None
                                };
                                int? subId = line.SourceType == RevaluationSourceType.Customer ? line.CustomerId
                                    : (line.SourceType == RevaluationSourceType.Vendor ? line.VendorId : null);

                                jvHeader.Lines.Add(new JournalEntryLine
                                {
                                    LineNumber = jvLineNum++,
                                    AccountId = line.AccountId,
                                    SubLedgerType = subType,
                                    SubLedgerId = subId,
                                    CurrencyId = line.CurrencyId,
                                    ExchangeRate = line.ExchangeRate,
                                    DebitForeign = 0m, // Foreign balance remains untouched!
                                    CreditForeign = 0m,
                                    DebitLocal = Round4(line.DifferenceAmount),
                                    CreditLocal = 0m,
                                    Description = $"أرباح تقييم {line.SourceName} ({line.SourceCode}) رصيد {line.ForeignBalance:N2}",
                                    CreatedAt = DateTime.UtcNow
                                });
                            }

                            // 2. Lines for LOSSES:
                            // Credit party/account, Debit Unrealized Loss Account
                            foreach (var line in branchLines.Where(l => l.DifferenceType == RevaluationDifferenceType.Loss && l.DifferenceAmount > 0))
                            {
                                var subType = line.SourceType switch
                                {
                                    RevaluationSourceType.Customer => SubLedgerType.Customer,
                                    RevaluationSourceType.Vendor => SubLedgerType.Vendor,
                                    _ => SubLedgerType.None
                                };
                                int? subId = line.SourceType == RevaluationSourceType.Customer ? line.CustomerId
                                    : (line.SourceType == RevaluationSourceType.Vendor ? line.VendorId : null);

                                jvHeader.Lines.Add(new JournalEntryLine
                                {
                                    LineNumber = jvLineNum++,
                                    AccountId = line.AccountId,
                                    SubLedgerType = subType,
                                    SubLedgerId = subId,
                                    CurrencyId = line.CurrencyId,
                                    ExchangeRate = line.ExchangeRate,
                                    DebitForeign = 0m, // Foreign balance remains untouched!
                                    CreditForeign = 0m,
                                    DebitLocal = 0m,
                                    CreditLocal = Round4(line.DifferenceAmount),
                                    Description = $"خسائر تقييم {line.SourceName} ({line.SourceCode}) رصيد {line.ForeignBalance:N2}",
                                    CreatedAt = DateTime.UtcNow
                                });
                            }

                            // 3. Offsetting entry for Unrealized Gain (Credit)
                            if (branchGain > 0)
                            {
                                jvHeader.Lines.Add(new JournalEntryLine
                                {
                                    LineNumber = jvLineNum++,
                                    AccountId = header.UnrealizedGainAccountId,
                                    SubLedgerType = SubLedgerType.None,
                                    SubLedgerId = null,
                                    CurrencyId = baseCurr.Id,
                                    ExchangeRate = 1.0m,
                                    DebitForeign = 0m,
                                    CreditForeign = 0m,
                                    DebitLocal = 0m,
                                    CreditLocal = Round4(branchGain),
                                    Description = $"إجمالي أرباح فروق تقييم العملات الأجنبية للفرع",
                                    CreatedAt = DateTime.UtcNow
                                });
                            }

                            // 4. Offsetting entry for Unrealized Loss (Debit)
                            if (branchLoss > 0)
                            {
                                jvHeader.Lines.Add(new JournalEntryLine
                                {
                                    LineNumber = jvLineNum++,
                                    AccountId = header.UnrealizedLossAccountId,
                                    SubLedgerType = SubLedgerType.None,
                                    SubLedgerId = null,
                                    CurrencyId = baseCurr.Id,
                                    ExchangeRate = 1.0m,
                                    DebitForeign = 0m,
                                    CreditForeign = 0m,
                                    DebitLocal = Round4(branchLoss),
                                    CreditLocal = 0m,
                                    Description = $"إجمالي خسائر فروق تقييم العملات الأجنبية للفرع",
                                    CreatedAt = DateTime.UtcNow
                                });
                            }

                            jvHeader.TotalDebitLocal = Round4(jvHeader.Lines.Sum(l => l.DebitLocal));
                            jvHeader.TotalCreditLocal = Round4(jvHeader.Lines.Sum(l => l.CreditLocal));

                            // Strict Double-Entry Balance Check
                            if (jvHeader.TotalDebitLocal != jvHeader.TotalCreditLocal)
                            {
                                throw new InvalidOperationException($"القيد غير متوازن: إجمالي المدين ({jvHeader.TotalDebitLocal}) لا يساوي إجمالي الدائن ({jvHeader.TotalCreditLocal})");
                            }

                            // Validate via Accounting Validation Service
                            await _validationService.ValidateAsync(jvHeader, userId, isManualAction: false);

                            _context.JournalEntryHeaders.Add(jvHeader);
                            await _context.SaveChangesAsync();
                        }
                        finally
                        {
                            _entryNumberLock.Release();
                        }

                        if (!primaryJournalEntryId.HasValue)
                        {
                            primaryJournalEntryId = jvHeader.Id;
                        }
                    }

                    header.JournalEntryId = primaryJournalEntryId;
                    header.Status = RevaluationStatus.Approved;
                    header.ApprovedByUserId = userId;
                    header.ApprovedAt = DateTime.UtcNow;
                    header.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                        entityName: "RevaluationHeader",
                        entityId: header.Id.ToString(),
                        oldValues: new { Status = RevaluationStatus.Draft },
                        newValues: new { Status = RevaluationStatus.Approved, header.ApprovedAt, header.JournalEntryId });

                    return (true, $"تم اعتماد عملية التقييم ({header.RevaluationNumber}) وإنشاء قيد اليومية العام بنجاح.", header.JournalEntryId);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return (false, "فشل اعتماد التقييم وإنشاء القيد المحاسبي: " + ex.Message, null);
                }
            });
        }

        public async Task<(bool Success, string Message, int? ReversalJournalEntryId)> ReverseAsync(int id, DateTime? reversalDate, string userId)
        {
            var header = await _context.RevaluationHeaders
                .IgnoreQueryFilters()
                .Include(h => h.FiscalYear)
                .Include(h => h.Lines)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null)
                return (false, "عملية إعادة التقييم غير موجودة.", null);

            if (header.Status != RevaluationStatus.Approved)
                return (false, "لا يمكن عكس عملية تقييم إلا إذا كانت معتمدة.", null);

            if (header.ReversalJournalEntryId.HasValue)
                return (false, "تم عكس هذه العملية مسبقاً بقيد عكسي رقم " + header.ReversalJournalEntryId.Value, header.ReversalJournalEntryId);

            // Default reversal date to the day after revaluation date
            var targetReversalDate = (reversalDate ?? header.RevaluationDate.AddDays(1)).Date;

            // Validate that the reversal period is open
            var (isAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(targetReversalDate);
            if (!isAllowed)
                return (false, $"لا يمكن إجراء العكس في فترة مغلقة ({targetReversalDate:yyyy-MM-dd}): {periodError}", null);

            // Find all posted journal entries generated for this revaluation
            var originalJvs = await _context.JournalEntryHeaders
                .IgnoreQueryFilters()
                .Include(j => j.Lines)
                .Where(j => j.SourceDocumentType == SourceDocumentType.CurrencyRevaluation && j.SourceDocumentId == header.Id)
                .ToListAsync();

            if (!originalJvs.Any() && header.JournalEntryId.HasValue)
            {
                var jv = await _context.JournalEntryHeaders
                    .IgnoreQueryFilters()
                    .Include(j => j.Lines)
                    .FirstOrDefaultAsync(j => j.Id == header.JournalEntryId.Value);
                if (jv != null) originalJvs.Add(jv);
            }

            if (!originalJvs.Any())
                return (false, "لم يتم العثور على القيود المحاسبية الأصلية المراد عكسها.", null);

            var revFy = await _context.FiscalYears
                .AsNoTracking()
                .FirstOrDefaultAsync(y => y.IsActive && targetReversalDate >= y.StartDate && targetReversalDate <= y.EndDate);

            if (revFy == null)
                return (false, $"لا توجد سنة مالية معرّفة أو نشطة تغطي تاريخ العكس ({targetReversalDate:yyyy-MM-dd}).", null);

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
                try
                {
                    int? primaryReversalJvId = null;

                    foreach (var origJv in originalJvs)
                    {
                        await _entryNumberLock.WaitAsync();
                        JournalEntryHeader revJv;
                        try
                        {
                            string revJvNumber = await GenerateNextJournalEntryNumberAsync(origJv.BranchId, revFy.Id, revFy.Year);

                            revJv = new JournalEntryHeader
                            {
                                BranchId = origJv.BranchId,
                                FiscalYearId = revFy.Id,
                                EntryNumber = revJvNumber,
                                EntryDate = targetReversalDate,
                                Description = $"قيد عكسي لإعادة تقييم العملات ({header.RevaluationNumber}) - القيد الأصلي ({origJv.EntryNumber})",
                                ReferenceNumber = origJv.EntryNumber,
                                Notes = $"عكس أرباح وخسائر إعادة التقييم غير المحققة للفترة السابقة.",
                                Status = JournalEntryStatus.Posted,
                                SourceDocumentType = SourceDocumentType.CurrencyRevaluation,
                                SourceDocumentId = header.Id,
                                PostedAt = DateTime.UtcNow,
                                PostedByUserId = userId,
                                CreatedAt = DateTime.UtcNow
                            };

                            int revLineIndex = 1;
                            // Reverse every line: Swap Debit and Credit
                            foreach (var line in origJv.Lines)
                            {
                                revJv.Lines.Add(new JournalEntryLine
                                {
                                    LineNumber = revLineIndex++,
                                    AccountId = line.AccountId,
                                    SubLedgerType = line.SubLedgerType,
                                    SubLedgerId = line.SubLedgerId,
                                    CurrencyId = line.CurrencyId,
                                    ExchangeRate = line.ExchangeRate,
                                    DebitForeign = line.CreditForeign,
                                    CreditForeign = line.DebitForeign,
                                    DebitLocal = line.CreditLocal, // Swapped!
                                    CreditLocal = line.DebitLocal, // Swapped!
                                    Description = $"عكس: {line.Description}",
                                    CreatedAt = DateTime.UtcNow
                                });
                            }

                            revJv.TotalDebitLocal = Round4(revJv.Lines.Sum(l => l.DebitLocal));
                            revJv.TotalCreditLocal = Round4(revJv.Lines.Sum(l => l.CreditLocal));

                            if (revJv.TotalDebitLocal != revJv.TotalCreditLocal)
                            {
                                throw new InvalidOperationException($"القيد العكسي غير متوازن: إجمالي المدين ({revJv.TotalDebitLocal}) لا يساوي إجمالي الدائن ({revJv.TotalCreditLocal})");
                            }

                            await _validationService.ValidateAsync(revJv, userId, isManualAction: false);

                            _context.JournalEntryHeaders.Add(revJv);
                            await _context.SaveChangesAsync();
                        }
                        finally
                        {
                            _entryNumberLock.Release();
                        }

                        if (!primaryReversalJvId.HasValue)
                        {
                            primaryReversalJvId = revJv.Id;
                        }
                    }

                    header.ReversalJournalEntryId = primaryReversalJvId;
                    header.ReversalDate = targetReversalDate;
                    header.Status = RevaluationStatus.Reversed;
                    header.ReversedByUserId = userId;
                    header.ReversedAt = DateTime.UtcNow;
                    header.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await _audit.LogUpdateAsync(SC_Code, SC_Name, SC_Module,
                        entityName: "RevaluationHeader",
                        entityId: header.Id.ToString(),
                        oldValues: new { Status = RevaluationStatus.Approved },
                        newValues: new { Status = RevaluationStatus.Reversed, header.ReversalDate, header.ReversalJournalEntryId });

                    return (true, $"تم عكس قيد إعادة التقييم بنجاح بتاريخ ({targetReversalDate:yyyy-MM-dd}).", primaryReversalJvId);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return (false, "حدث خطأ أثناء إنشاء القيد العكسي: " + ex.Message, null);
                }
            });
        }

        public async Task<(bool Success, string Message)> DeleteAsync(int id, string userId)
        {
            var header = await _context.RevaluationHeaders
                .IgnoreQueryFilters()
                .Include(h => h.Lines)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null)
                return (false, "عملية إعادة التقييم غير موجودة.");

            if (header.Status == RevaluationStatus.Approved)
                return (false, "لا يمكن حذف عملية إعادة تقييم معتمدة ومرحلة. يجب عكس القيد أولاً.");

            var (isAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(header.RevaluationDate);
            if (!isAllowed)
                return (false, periodError ?? "لا يمكن حذف عملية داخل فترة مالية مغلقة.");

            var oldValues = new { header.Id, header.RevaluationNumber, header.Status, header.TotalGain, header.TotalLoss };

            _context.RevaluationHeaders.Remove(header);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "RevaluationHeader",
                entityId: id.ToString(),
                oldValues: oldValues);

            return (true, $"تم حذف عملية إعادة التقييم ({header.RevaluationNumber}) بنجاح.");
        }

        public async Task<RevaluationDetailsDto?> GetRevaluationByIdAsync(int id)
        {
            var h = await _context.RevaluationHeaders
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.Company)
                .Include(x => x.Branch)
                .Include(x => x.FiscalYear)
                .Include(x => x.Currency)
                .Include(x => x.UnrealizedGainAccount)
                .Include(x => x.UnrealizedLossAccount)
                .Include(x => x.JournalEntry)
                .Include(x => x.ReversalJournalEntry)
                .Include(x => x.Lines)
                    .ThenInclude(l => l.Account)
                .Include(x => x.Lines)
                    .ThenInclude(l => l.Branch)
                .Include(x => x.Lines)
                    .ThenInclude(l => l.Currency)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (h == null) return null;

            return new RevaluationDetailsDto
            {
                Id = h.Id,
                RevaluationNumber = h.RevaluationNumber,
                RevaluationDate = h.RevaluationDate,
                CompanyId = h.CompanyId,
                CompanyName = h.Company?.LocalizedName ?? "",
                BranchId = h.BranchId,
                BranchName = h.Branch?.LocalizedName ?? "كافة الفروع",
                FiscalYearId = h.FiscalYearId,
                FiscalYearName = h.FiscalYear?.LocalizedName ?? "",
                Scope = h.Scope,
                ScopeName = h.Scope switch
                {
                    RevaluationScope.Customers => "العملاء",
                    RevaluationScope.Vendors => "الموردون",
                    RevaluationScope.GeneralAccounts => "الحسابات العامة",
                    _ => "الكل"
                },
                CurrencyId = h.CurrencyId,
                CurrencyName = h.Currency?.LocalizedName ?? "كافة العملات الأجنبية",
                UnrealizedGainAccountId = h.UnrealizedGainAccountId,
                UnrealizedGainAccountCode = h.UnrealizedGainAccount?.Code ?? "",
                UnrealizedGainAccountName = h.UnrealizedGainAccount?.LocalizedName ?? "",
                UnrealizedLossAccountId = h.UnrealizedLossAccountId,
                UnrealizedLossAccountCode = h.UnrealizedLossAccount?.Code ?? "",
                UnrealizedLossAccountName = h.UnrealizedLossAccount?.LocalizedName ?? "",
                TotalGain = h.TotalGain,
                TotalLoss = h.TotalLoss,
                NetDifference = h.NetDifference,
                Status = h.Status,
                StatusName = h.Status switch
                {
                    RevaluationStatus.Approved => "معتمدة",
                    RevaluationStatus.Reversed => "معكوسة",
                    RevaluationStatus.Cancelled => "ملغاة",
                    _ => "مسودة"
                },
                JournalEntryId = h.JournalEntryId,
                JournalEntryNumber = h.JournalEntry?.EntryNumber,
                ReversalJournalEntryId = h.ReversalJournalEntryId,
                ReversalJournalEntryNumber = h.ReversalJournalEntry?.EntryNumber,
                ReversalDate = h.ReversalDate,
                Notes = h.Notes,
                CreatedByUserId = h.CreatedByUserId,
                CreatedAt = h.CreatedAt,
                ApprovedByUserId = h.ApprovedByUserId,
                ApprovedAt = h.ApprovedAt,
                ReversedByUserId = h.ReversedByUserId,
                ReversedAt = h.ReversedAt,
                Lines = h.Lines.OrderBy(l => l.LineNumber).Select(l => new RevaluationLinePreviewDto
                {
                    LineNumber = l.LineNumber,
                    SourceType = l.SourceType,
                    SourceTypeName = l.SourceType switch
                    {
                        RevaluationSourceType.Customer => "عميل",
                        RevaluationSourceType.Vendor => "مورد",
                        _ => "حساب عام"
                    },
                    SourceId = l.SourceId,
                    SourceCode = l.SourceCode,
                    SourceName = l.SourceName,
                    AccountId = l.AccountId,
                    AccountCode = l.Account?.Code ?? "",
                    AccountName = l.Account?.LocalizedName ?? "",
                    CustomerId = l.CustomerId,
                    VendorId = l.VendorId,
                    BranchId = l.BranchId,
                    BranchName = l.Branch?.LocalizedName ?? "",
                    CurrencyId = l.CurrencyId,
                    CurrencyCode = l.Currency?.Code ?? "",
                    CurrencyName = l.Currency?.LocalizedName ?? "",
                    ForeignBalance = l.ForeignBalance,
                    CarryingLocalAmount = l.CarryingLocalAmount,
                    CurrentRecordedRate = l.ForeignBalance != 0 ? Round4(l.CarryingLocalAmount / l.ForeignBalance) : l.ExchangeRate,
                    ExchangeRate = l.ExchangeRate,
                    RevaluedLocalAmount = l.RevaluedLocalAmount,
                    DifferenceAmount = l.DifferenceAmount,
                    DifferenceType = l.DifferenceType,
                    BalanceNature = l.BalanceNature,
                    HasValidRate = true,
                    Notes = l.Notes
                }).ToList()
            };
        }

        public async Task<PagedResult<RevaluationHeaderListDto>> GetRevaluationsAsync(RevaluationListFilterDto filter)
        {
            var query = _context.RevaluationHeaders
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(h => h.Company)
                .Include(h => h.Branch)
                .Include(h => h.FiscalYear)
                .Include(h => h.Currency)
                .Include(h => h.JournalEntry)
                .Include(h => h.ReversalJournalEntry)
                .AsQueryable();

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                query = query.Where(h => h.BranchId == filter.BranchId.Value);
            }
            else if (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
            {
                query = query.Where(h => h.BranchId == _currentUser.BranchId || h.BranchId == null);
            }

            if (filter.Status.HasValue)
            {
                query = query.Where(h => h.Status == filter.Status.Value);
            }

            if (filter.FiscalYearId.HasValue && filter.FiscalYearId.Value > 0)
            {
                query = query.Where(h => h.FiscalYearId == filter.FiscalYearId.Value);
            }

            if (filter.FromDate.HasValue)
            {
                query = query.Where(h => h.RevaluationDate >= filter.FromDate.Value.Date);
            }

            if (filter.ToDate.HasValue)
            {
                query = query.Where(h => h.RevaluationDate <= filter.ToDate.Value.Date);
            }

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var s = filter.SearchTerm.Trim().ToLower();
                query = query.Where(h => h.RevaluationNumber.ToLower().Contains(s) || (h.Notes != null && h.Notes.ToLower().Contains(s)));
            }

            int totalCount = await query.CountAsync();

            int page = filter.PageNumber > 0 ? filter.PageNumber : 1;
            int size = filter.PageSize > 0 ? filter.PageSize : 20;

            var items = await query
                .OrderByDescending(h => h.RevaluationDate)
                .ThenByDescending(h => h.Id)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(h => new RevaluationHeaderListDto
                {
                    Id = h.Id,
                    RevaluationNumber = h.RevaluationNumber,
                    RevaluationDate = h.RevaluationDate,
                    CompanyId = h.CompanyId,
                    CompanyName = h.Company != null ? h.Company.LocalizedName : "",
                    BranchId = h.BranchId,
                    BranchName = h.Branch != null ? h.Branch.LocalizedName : "كافة الفروع",
                    FiscalYearName = h.FiscalYear != null ? h.FiscalYear.LocalizedName : "",
                    Scope = h.Scope,
                    ScopeName = h.Scope == RevaluationScope.Customers ? "العملاء"
                        : (h.Scope == RevaluationScope.Vendors ? "الموردون"
                        : (h.Scope == RevaluationScope.GeneralAccounts ? "الحسابات العامة" : "الكل")),
                    CurrencyName = h.Currency != null ? h.Currency.LocalizedName : "كافة العملات",
                    TotalGain = h.TotalGain,
                    TotalLoss = h.TotalLoss,
                    NetDifference = h.NetDifference,
                    Status = h.Status,
                    StatusName = h.Status == RevaluationStatus.Approved ? "معتمدة"
                        : (h.Status == RevaluationStatus.Reversed ? "معكوسة"
                        : (h.Status == RevaluationStatus.Cancelled ? "ملغاة" : "مسودة")),
                    JournalEntryId = h.JournalEntryId,
                    JournalEntryNumber = h.JournalEntry != null ? h.JournalEntry.EntryNumber : null,
                    ReversalJournalEntryId = h.ReversalJournalEntryId,
                    ReversalJournalEntryNumber = h.ReversalJournalEntry != null ? h.ReversalJournalEntry.EntryNumber : null,
                    ReversalDate = h.ReversalDate,
                    CreatedByUserName = h.CreatedByUserId,
                    CreatedAt = h.CreatedAt
                })
                .ToListAsync();

            return new PagedResult<RevaluationHeaderListDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = page,
                PageSize = size
            };
        }
    }
}
