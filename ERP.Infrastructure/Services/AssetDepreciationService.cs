using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs.FixedAssets;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class AssetDepreciationService : IAssetDepreciationService
    {
        private readonly ErpDbContext _context;
        private readonly IFiscalPeriodService _fiscalPeriodService;
        private readonly IJournalEntryValidationService _validationService;
        private readonly IAuditService _audit;
        private readonly ICurrentUserContext _currentUser;

        private static readonly SemaphoreSlim _depRunLock = new(1, 1);

        private const string SC_Code = "FA-DEP";
        private const string SC_Name = "تشغيل واحتساب الإهلاكات";
        private const string SC_Module = "FixedAssets";

        public AssetDepreciationService(
            ErpDbContext context,
            IFiscalPeriodService fiscalPeriodService,
            IJournalEntryValidationService validationService,
            IAuditService audit,
            ICurrentUserContext currentUser)
        {
            _context = context;
            _fiscalPeriodService = fiscalPeriodService;
            _validationService = validationService;
            _audit = audit;
            _currentUser = currentUser;
        }

        public decimal CalculatePeriodDepreciation(FixedAsset asset, DateTime runDate, DateTime periodStartDate, DateTime periodEndDate)
        {
            if (asset == null) return 0m;

            // 1. Ineligible asset states
            if (asset.Status != AssetStatus.InService) return 0m;
            if (asset.DepreciationMethod == DepreciationMethod.NoDepreciation) return 0m;
            if (asset.IsFullyDepreciated) return 0m;
            if (asset.CurrentBookValue <= asset.ResidualValue) return 0m;

            // 2. Start date check
            if (asset.DepreciationStartDate.HasValue && asset.DepreciationStartDate.Value.Date > periodEndDate.Date)
            {
                return 0m; // Asset depreciation has not started yet
            }

            // 3. Base monthly calculation
            decimal depreciableBasis = Math.Max(0m, asset.CurrentCostLocal - asset.ResidualValue);
            if (depreciableBasis <= 0m) return 0m;

            int usefulMonths = asset.UsefulLifeMonths > 0 ? asset.UsefulLifeMonths : 60;
            decimal baseMonthly = 0m;

            switch (asset.DepreciationMethod)
            {
                case DepreciationMethod.StraightLine:
                    baseMonthly = depreciableBasis / usefulMonths;
                    break;

                case DepreciationMethod.DecliningBalance:
                    decimal annualRate = asset.DepreciationRate > 0
                        ? asset.DepreciationRate
                        : Math.Round((100m / (usefulMonths / 12.0m)) * 2.0m, 4); // Double declining fallback

                    decimal monthlyRate = (annualRate / 100m) / 12m;
                    baseMonthly = asset.CurrentBookValue * monthlyRate;
                    break;

                case DepreciationMethod.UnitsOfProduction:
                    decimal totalExpected = asset.TotalExpectedUnits > 0 ? asset.TotalExpectedUnits : 1m;
                    decimal periodUnits = asset.TotalProducedUnits; // produced in current run
                    baseMonthly = depreciableBasis * (periodUnits / totalExpected);
                    break;

                case DepreciationMethod.NoDepreciation:
                default:
                    return 0m;
            }

            // 4. First Month Policy
            if (asset.DepreciationStartDate.HasValue &&
                asset.DepreciationStartDate.Value.Date >= periodStartDate.Date &&
                asset.DepreciationStartDate.Value.Date <= periodEndDate.Date)
            {
                if (asset.FirstMonthPolicy == FirstMonthPolicy.NextMonth)
                {
                    return 0m; // Postpone to next month
                }
                else if (asset.FirstMonthPolicy == FirstMonthPolicy.ProRataDays)
                {
                    int totalDaysInMonth = (periodEndDate.Date - periodStartDate.Date).Days + 1;
                    int activeDays = (periodEndDate.Date - asset.DepreciationStartDate.Value.Date).Days + 1;
                    if (totalDaysInMonth > 0 && activeDays > 0)
                    {
                        decimal ratio = Math.Min(1.0m, Math.Max(0.0m, (decimal)activeDays / totalDaysInMonth));
                        baseMonthly *= ratio;
                    }
                }
            }

            // 5. Residual Value & Depreciable Cap
            decimal maxAllowable = Math.Max(0m, asset.CurrentBookValue - asset.ResidualValue);
            decimal finalDepreciation = Math.Min(baseMonthly, maxAllowable);

            return Math.Round(finalDepreciation, 4);
        }

        public async Task<DepreciationCalculationResultDto> PreviewDepreciationRunAsync(RunDepreciationFilterDto filter, string userId)
        {
            var targetDate = filter.RunDate.Date;

            // 1. Fiscal Period Validation
            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(targetDate);
            if (!isPeriodAllowed)
            {
                return new DepreciationCalculationResultDto
                {
                    Success = false,
                    Message = periodError ?? "لا يمكن تشغيل الإهلاك في فترة مالية مغلقة."
                };
            }

            var fy = await _context.FiscalYears
                .AsNoTracking()
                .FirstOrDefaultAsync(y => y.IsActive && targetDate >= y.StartDate && targetDate <= y.EndDate);

            if (fy == null)
            {
                return new DepreciationCalculationResultDto
                {
                    Success = false,
                    Message = $"لا توجد سنة مالية معتمدة تغطي التاريخ ({targetDate:yyyy-MM-dd})."
                };
            }

            var period = await _context.FiscalPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.FiscalYearId == fy.Id && targetDate >= p.StartDate && targetDate <= p.EndDate);

            if (period == null)
            {
                return new DepreciationCalculationResultDto
                {
                    Success = false,
                    Message = $"لا توجد فترة مالية / شهر معرف لهذا التاريخ ({targetDate:yyyy-MM-dd})."
                };
            }

            DateTime periodStart = period.StartDate;
            DateTime periodEnd = period.EndDate;

            // 2. Query Candidate Assets
            var query = _context.FixedAssets
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(a => a.AssetCategory)
                .Include(a => a.Branch)
                .Include(a => a.CostCenter)
                .Include(a => a.AssetCostAccount)
                .Include(a => a.AccumulatedDepreciationAccount)
                .Include(a => a.DepreciationExpenseAccount)
                .AsQueryable();

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
                query = query.Where(a => a.BranchId == filter.BranchId.Value);

            if (filter.AssetCategoryId.HasValue && filter.AssetCategoryId.Value > 0)
                query = query.Where(a => a.AssetCategoryId == filter.AssetCategoryId.Value);

            if (filter.SpecificAssetId.HasValue && filter.SpecificAssetId.Value > 0)
                query = query.Where(a => a.Id == filter.SpecificAssetId.Value);

            var candidateAssets = await query.ToListAsync();

            // 3. Find Assets Already Depreciated in this Exact Period
            var alreadyDepreciatedAssetIds = await _context.AssetDepreciationHistories
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(h => h.FiscalPeriodId == period.Id &&
                            h.AssetDepreciationRun != null &&
                            h.AssetDepreciationRun.Status == DepreciationRunStatus.Approved)
                .Select(h => h.AssetId)
                .Distinct()
                .ToListAsync();

            var previewLines = new List<AssetDepreciationPreviewItemDto>();

            foreach (var asset in candidateAssets)
            {
                var cat = asset.AssetCategory;
                int expAccId = asset.DepreciationExpenseAccountId ?? cat?.DepreciationExpenseAccountId ?? 0;
                int accDepAccId = asset.AccumulatedDepreciationAccountId ?? cat?.AccumulatedDepreciationAccountId ?? 0;

                var expAcc = asset.DepreciationExpenseAccount ?? cat?.DepreciationExpenseAccount;
                var accDepAcc = asset.AccumulatedDepreciationAccount ?? cat?.AccumulatedDepreciationAccount;

                bool isAlreadyDepreciated = alreadyDepreciatedAssetIds.Contains(asset.Id);
                bool isEligible = true;
                string? ineligibilityReason = null;

                if (isAlreadyDepreciated)
                {
                    isEligible = false;
                    ineligibilityReason = "تم تشغيل إهلاك هذا الأصل بالفعل لنفس الفترة المالية.";
                }
                else if (asset.Status != AssetStatus.InService)
                {
                    isEligible = false;
                    ineligibilityReason = $"حالة الأصل ({asset.Status}) غير مؤهلة للإهلاك.";
                }
                else if (asset.DepreciationMethod == DepreciationMethod.NoDepreciation)
                {
                    isEligible = false;
                    ineligibilityReason = "الأصل مصنف بدون إهلاك (مثل الأراضي).";
                }
                else if (asset.IsFullyDepreciated || asset.CurrentBookValue <= asset.ResidualValue)
                {
                    isEligible = false;
                    ineligibilityReason = "الأصل مستهلك دفترياً بالكامل ووصل لقيمته التخريدية.";
                }
                else if (asset.DepreciationStartDate.HasValue && asset.DepreciationStartDate.Value.Date > periodEnd.Date)
                {
                    isEligible = false;
                    ineligibilityReason = $"تاريخ بداية إهلاك الأصل ({asset.DepreciationStartDate:yyyy-MM-dd}) لاحق لتاريخ الفترة.";
                }
                else if (expAccId <= 0 || accDepAccId <= 0)
                {
                    isEligible = false;
                    ineligibilityReason = "حساب مصروف الإهلاك أو مجمع الإهلاك غير محدد للأصل.";
                }

                decimal depAmount = 0m;
                if (isEligible)
                {
                    depAmount = CalculatePeriodDepreciation(asset, targetDate, periodStart, periodEnd);
                    if (depAmount <= 0m)
                    {
                        isEligible = false;
                        ineligibilityReason = "ناتج حساب الإهلاك للفترة يساوي صفراً.";
                    }
                }

                decimal priorAcc = asset.CurrentAccumulatedDepreciation;
                decimal postAcc = priorAcc + depAmount;
                decimal bookBefore = asset.CurrentBookValue;
                decimal bookAfter = Math.Max(asset.ResidualValue, bookBefore - depAmount);

                previewLines.Add(new AssetDepreciationPreviewItemDto
                {
                    AssetId = asset.Id,
                    AssetCode = asset.AssetCode,
                    AssetName = asset.LocalizedName,
                    CategoryName = cat?.LocalizedName ?? "",
                    BranchId = asset.BranchId,
                    BranchName = asset.Branch?.LocalizedName ?? "",
                    CostCenterId = asset.CostCenterId,
                    CostCenterName = asset.CostCenter?.LocalizedName,
                    AssetCost = asset.CurrentCostLocal,
                    ResidualValue = asset.ResidualValue,
                    UsefulLifeMonths = asset.UsefulLifeMonths,
                    DepreciationMethod = asset.DepreciationMethod,
                    DepreciationMethodName = GetMethodName(asset.DepreciationMethod),
                    PriorAccumulatedDepreciation = priorAcc,
                    CurrentPeriodDepreciation = depAmount,
                    PostAccumulatedDepreciation = postAcc,
                    BookValueBefore = bookBefore,
                    BookValueAfter = bookAfter,
                    ExpenseAccountId = expAccId,
                    ExpenseAccountCode = expAcc?.Code ?? "",
                    ExpenseAccountName = expAcc?.LocalizedName ?? "",
                    AccumulatedAccountId = accDepAccId,
                    AccumulatedAccountCode = accDepAcc?.Code ?? "",
                    AccumulatedAccountName = accDepAcc?.LocalizedName ?? "",
                    IsEligible = isEligible,
                    IneligibilityReason = ineligibilityReason
                });
            }

            var eligibleLines = previewLines.Where(l => l.IsEligible && l.CurrentPeriodDepreciation > 0).ToList();

            return new DepreciationCalculationResultDto
            {
                Success = true,
                RunDate = targetDate,
                FiscalYearId = fy.Id,
                FiscalYearName = fy.LocalizedName,
                FiscalPeriodId = period.Id,
                FiscalPeriodName = period.LocalizedName,
                BranchId = filter.BranchId,
                BranchName = filter.BranchId.HasValue ? (candidateAssets.FirstOrDefault()?.Branch?.LocalizedName ?? "") : "كافة الفروع المسموح بها",
                AssetCategoryId = filter.AssetCategoryId,
                CategoryName = filter.AssetCategoryId.HasValue ? (previewLines.FirstOrDefault()?.CategoryName ?? "") : "كافة المجموعات",
                TotalAssetsCount = eligibleLines.Count,
                TotalDepreciationAmount = eligibleLines.Sum(l => l.CurrentPeriodDepreciation),
                Lines = previewLines
            };
        }

        public async Task<(bool Success, string Message, int? RunId, string? RunNumber, int? JournalEntryId)> ExecuteDepreciationRunAsync(ExecuteDepreciationRunDto dto, string userId)
        {
            if (dto == null) return (false, "البيانات المدخلة غير صحيحة.", null, null, null);

            var preview = await PreviewDepreciationRunAsync(new RunDepreciationFilterDto
            {
                RunDate = dto.RunDate,
                CompanyId = dto.CompanyId,
                BranchId = dto.BranchId,
                AssetCategoryId = dto.AssetCategoryId,
                SpecificAssetId = dto.SpecificAssetId
            }, userId);

            if (!preview.Success) return (false, preview.Message ?? "فشل معاينة الإهلاك.", null, null, null);

            var eligibleLines = preview.Lines.Where(l => l.IsEligible && l.CurrentPeriodDepreciation > 0).ToList();
            if (!eligibleLines.Any())
            {
                return (false, "لا توجد أي أصول مؤهلة لاحتساب الإهلاك في هذه الفترة أو تم إهلاكها مسبقاً.", null, null, null);
            }

            var targetDate = dto.RunDate.Date;
            var fy = await _context.FiscalYears.FindAsync(preview.FiscalYearId);
            string fyYear = fy?.Year ?? targetDate.Year.ToString();

            AssetDepreciationRun run;
            await _depRunLock.WaitAsync();
            try
            {
                string runNumber = await GenerateNextRunNumberAsync(dto.BranchId, preview.FiscalYearId, fyYear);

                run = new AssetDepreciationRun
                {
                    RunNumber = runNumber,
                    RunDate = targetDate,
                    CompanyId = dto.CompanyId,
                    BranchId = dto.BranchId,
                    FiscalYearId = preview.FiscalYearId,
                    FiscalPeriodId = preview.FiscalPeriodId,
                    AssetCategoryId = dto.AssetCategoryId,
                    TotalDepreciationAmount = preview.TotalDepreciationAmount,
                    AssetsCount = eligibleLines.Count,
                    Status = DepreciationRunStatus.Draft,
                    Notes = dto.Notes?.Trim(),
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                foreach (var line in eligibleLines)
                {
                    run.Histories.Add(new AssetDepreciationHistory
                    {
                        AssetId = line.AssetId,
                        DepreciationDate = targetDate,
                        FiscalYearId = preview.FiscalYearId,
                        FiscalPeriodId = preview.FiscalPeriodId,
                        AssetCostAtRun = line.AssetCost,
                        PriorAccumulatedDepreciation = line.PriorAccumulatedDepreciation,
                        DepreciationAmount = line.CurrentPeriodDepreciation,
                        PostAccumulatedDepreciation = line.PostAccumulatedDepreciation,
                        BookValueBefore = line.BookValueBefore,
                        BookValueAfter = line.BookValueAfter,
                        BranchId = line.BranchId,
                        CostCenterId = line.CostCenterId,
                        ExpenseAccountId = line.ExpenseAccountId,
                        AccumulatedAccountId = line.AccumulatedAccountId,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                _context.AssetDepreciationRuns.Add(run);
                await _context.SaveChangesAsync();
            }
            finally
            {
                _depRunLock.Release();
            }

            await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "AssetDepreciationRun",
                entityId: run.Id.ToString(),
                newValues: new { run.Id, run.RunNumber, run.RunDate, run.TotalDepreciationAmount, run.AssetsCount });

            if (dto.AutoPost)
            {
                var approveRes = await ApproveDepreciationRunAsync(run.Id, userId);
                if (!approveRes.Success)
                {
                    return (true, $"تم إنشاء مسودة تشغيل الإهلاك ({run.RunNumber})، ولكن تعذر الترحيل التلقائي: {approveRes.Message}", run.Id, run.RunNumber, null);
                }
                return (true, $"تم إنشاء واعتماد تشغيل الإهلاك ({run.RunNumber}) وترحيل القيد المحاسبي بنجاح.", run.Id, run.RunNumber, approveRes.JournalEntryId);
            }

            return (true, $"تم حفظ مسودة تشغيل الإهلاك ({run.RunNumber}) بنجاح.", run.Id, run.RunNumber, null);
        }

        public async Task<(bool Success, string Message, int? JournalEntryId)> ApproveDepreciationRunAsync(int runId, string userId)
        {
            var run = await _context.AssetDepreciationRuns
                .Include(r => r.Histories)
                .FirstOrDefaultAsync(r => r.Id == runId);

            if (run == null) return (false, "عملية تشغيل الإهلاك غير موجودة.", null);
            if (run.Status == DepreciationRunStatus.Approved) return (false, "عملية الإهلاك معتمدة ومرحلة بالفعل.", run.JournalEntryId);
            if (run.Status == DepreciationRunStatus.Reversed) return (false, "لا يمكن اعتماد عملية إهلاك معكوسة.", null);

            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(run.RunDate);
            if (!isPeriodAllowed) return (false, periodError ?? "الفترة المالية للعملية مغلقة.", null);

            if (!run.Histories.Any()) return (false, "لا توجد سطور إهلاك للاعتماد.", null);

            var fy = await _context.FiscalYears.FindAsync(run.FiscalYearId);
            if (fy == null) return (false, "السنة المالية غير موجودة.", null);

            var baseCurr = await _context.Currencies.FirstOrDefaultAsync(c => c.IsBaseCurrency)
                           ?? await _context.Currencies.FirstAsync();

            int targetBranchId = run.BranchId ?? run.Histories.First().BranchId;

            // Generate Journal Entry
            var jv = new JournalEntryHeader
            {
                BranchId = targetBranchId,
                FiscalYearId = run.FiscalYearId,
                EntryNumber = $"JV-{targetBranchId}-{fy.Year}-DEP-{Guid.NewGuid().ToString().Substring(0, 4)}",
                EntryDate = run.RunDate.Date,
                Description = $"قيد إهلاك الأصول الثابتة الدوري - عملية رقم {run.RunNumber}",
                Notes = run.Notes,
                Status = JournalEntryStatus.Posted,
                SourceDocumentType = SourceDocumentType.AssetDepreciation,
                SourceDocumentId = run.Id,
                PostedAt = DateTime.UtcNow,
                PostedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            // Group lines by (CostCenterId, ExpenseAccountId, AccumulatedAccountId) for tidy entries
            var groups = run.Histories
                .GroupBy(h => new { h.CostCenterId, h.ExpenseAccountId, h.AccumulatedAccountId })
                .ToList();

            int lineIndex = 1;
            foreach (var grp in groups)
            {
                decimal grpTotal = Math.Round(grp.Sum(h => h.DepreciationAmount), 4);
                if (grpTotal <= 0) continue;

                // 1. Debit Depreciation Expense
                jv.Lines.Add(new JournalEntryLine
                {
                    LineNumber = lineIndex++,
                    AccountId = grp.Key.ExpenseAccountId,
                    CurrencyId = baseCurr.Id,
                    ExchangeRate = 1.0m,
                    DebitLocal = grpTotal,
                    CreditLocal = 0m,
                    CostCenterId = grp.Key.CostCenterId,
                    Description = $"مصروف إهلاك أصول - تشغيل {run.RunNumber}"
                });

                // 2. Credit Accumulated Depreciation
                jv.Lines.Add(new JournalEntryLine
                {
                    LineNumber = lineIndex++,
                    AccountId = grp.Key.AccumulatedAccountId,
                    CurrencyId = baseCurr.Id,
                    ExchangeRate = 1.0m,
                    DebitLocal = 0m,
                    CreditLocal = grpTotal,
                    CostCenterId = grp.Key.CostCenterId,
                    Description = $"مجمع إهلاك أصول - تشغيل {run.RunNumber}"
                });
            }

            jv.TotalDebitLocal = jv.Lines.Sum(l => l.DebitLocal);
            jv.TotalCreditLocal = jv.Lines.Sum(l => l.CreditLocal);

            if (Math.Abs(jv.TotalDebitLocal - jv.TotalCreditLocal) > 0.01m)
            {
                return (false, $"قيد الإهلاك غير متوازن! المدين: {jv.TotalDebitLocal} والدائن: {jv.TotalCreditLocal}", null);
            }

            // Update All Fixed Assets
            var assetIds = run.Histories.Select(h => h.AssetId).Distinct().ToList();
            var assets = await _context.FixedAssets.Where(a => assetIds.Contains(a.Id)).ToListAsync();
            var assetDict = assets.ToDictionary(a => a.Id);

            foreach (var h in run.Histories)
            {
                if (assetDict.TryGetValue(h.AssetId, out var asset))
                {
                    asset.CurrentAccumulatedDepreciation += h.DepreciationAmount;
                    asset.CurrentBookValue = Math.Max(asset.ResidualValue, asset.CurrentCostLocal - asset.CurrentAccumulatedDepreciation);
                    asset.LastDepreciationDate = run.RunDate.Date;
                    if (asset.CurrentBookValue <= asset.ResidualValue)
                    {
                        asset.IsFullyDepreciated = true;
                    }
                    asset.UpdatedAt = DateTime.UtcNow;
                }
            }

            _context.JournalEntryHeaders.Add(jv);
            await _context.SaveChangesAsync();

            run.Status = DepreciationRunStatus.Approved;
            run.JournalEntryId = jv.Id;
            run.ApprovedAt = DateTime.UtcNow;
            run.ApprovedByUserId = userId;
            run.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _audit.LogAsync(new ERP.Application.Models.AuditLogEntry
            {
                ScreenCode = SC_Code,
                ScreenName = SC_Name,
                ModuleName = SC_Module,
                Action = AuditAction.Approve,
                EntityName = "AssetDepreciationRun",
                EntityId = run.Id.ToString(),
                Description = $"اعتماد عملية الإهلاك {run.RunNumber} وقيد رقم {jv.EntryNumber}"
            });

            return (true, $"تم اعتماد عملية الإهلاك ({run.RunNumber}) وترحيل القيد المحاسبي بنجاح.", jv.Id);
        }

        public async Task<(bool Success, string Message, int? ReversalJournalEntryId)> ReverseDepreciationRunAsync(int runId, DateTime reversalDate, string userId)
        {
            var run = await _context.AssetDepreciationRuns
                .Include(r => r.Histories)
                .Include(r => r.JournalEntry)
                    .ThenInclude(j => j.Lines)
                .FirstOrDefaultAsync(r => r.Id == runId);

            if (run == null) return (false, "عملية الإهلاك غير موجودة.", null);
            if (run.Status != DepreciationRunStatus.Approved) return (false, "يمكن فقط عكس عمليات الإهلاك المعتمدة والمرحلة.", null);
            if (run.JournalEntry == null) return (false, "لا يوجد قيد محاسبي مرتبط بهذه العملية لعكسه.", null);

            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(reversalDate);
            if (!isPeriodAllowed) return (false, periodError ?? "الفترة المالية لتاريخ العكس مغلقة.", null);

            var fy = await _context.FiscalYears.FirstOrDefaultAsync(y => y.IsActive && reversalDate >= y.StartDate && reversalDate <= y.EndDate);
            if (fy == null) return (false, "لا توجد سنة مالية معتمدة لتاريخ العكس.", null);

            var originalJv = run.JournalEntry;

            // Create Reversing Journal Entry (Swapping Debits and Credits)
            var revJv = new JournalEntryHeader
            {
                BranchId = originalJv.BranchId,
                FiscalYearId = fy.Id,
                EntryNumber = $"REV-{originalJv.EntryNumber}",
                EntryDate = reversalDate.Date,
                Description = $"قيد عكس إهلاك الأصول - عملية {run.RunNumber}",
                Notes = $"عكس قيد الإهلاك رقم {originalJv.EntryNumber}",
                Status = JournalEntryStatus.Posted,
                SourceDocumentType = SourceDocumentType.AssetDepreciation,
                SourceDocumentId = run.Id,
                TotalDebitLocal = originalJv.TotalCreditLocal,
                TotalCreditLocal = originalJv.TotalDebitLocal,
                PostedAt = DateTime.UtcNow,
                PostedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            int lineIndex = 1;
            foreach (var origLine in originalJv.Lines)
            {
                revJv.Lines.Add(new JournalEntryLine
                {
                    LineNumber = lineIndex++,
                    AccountId = origLine.AccountId,
                    CurrencyId = origLine.CurrencyId,
                    ExchangeRate = origLine.ExchangeRate,
                    DebitForeign = origLine.CreditForeign,
                    CreditForeign = origLine.DebitForeign,
                    DebitLocal = origLine.CreditLocal,
                    CreditLocal = origLine.DebitLocal,
                    CostCenterId = origLine.CostCenterId,
                    Description = $"عكس: {origLine.Description}"
                });
            }

            // Rollback Asset Balances
            var assetIds = run.Histories.Select(h => h.AssetId).Distinct().ToList();
            var assets = await _context.FixedAssets.Where(a => assetIds.Contains(a.Id)).ToListAsync();
            var assetDict = assets.ToDictionary(a => a.Id);

            foreach (var h in run.Histories)
            {
                if (assetDict.TryGetValue(h.AssetId, out var asset))
                {
                    asset.CurrentAccumulatedDepreciation = Math.Max(0m, asset.CurrentAccumulatedDepreciation - h.DepreciationAmount);
                    asset.CurrentBookValue = asset.CurrentCostLocal - asset.CurrentAccumulatedDepreciation;
                    if (asset.CurrentBookValue > asset.ResidualValue)
                    {
                        asset.IsFullyDepreciated = false;
                    }
                    asset.UpdatedAt = DateTime.UtcNow;
                }
            }

            _context.JournalEntryHeaders.Add(revJv);
            await _context.SaveChangesAsync();

            run.Status = DepreciationRunStatus.Reversed;
            run.ReversalJournalEntryId = revJv.Id;
            run.ReversedAt = DateTime.UtcNow;
            run.ReversedByUserId = userId;
            run.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _audit.LogAsync(new ERP.Application.Models.AuditLogEntry
            {
                ScreenCode = SC_Code,
                ScreenName = SC_Name,
                ModuleName = SC_Module,
                Action = AuditAction.Cancel,
                EntityName = "AssetDepreciationRun",
                EntityId = run.Id.ToString(),
                Description = $"عكس عملية الإهلاك {run.RunNumber} بقيد عكسي رقم {revJv.EntryNumber}"
            });

            return (true, $"تم عكس عملية الإهلاك ({run.RunNumber}) وإنشاء القيد العكسي بنجاح.", revJv.Id);
        }

        public async Task<List<DepreciationRunListDto>> GetDepreciationRunsAsync(int companyId, int? branchId, int? fiscalYearId, string userId)
        {
            var query = _context.AssetDepreciationRuns
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(r => r.FiscalYear)
                .Include(r => r.FiscalPeriod)
                .Include(r => r.Branch)
                .Include(r => r.JournalEntry)
                .Where(r => r.CompanyId == companyId)
                .AsQueryable();

            if (branchId.HasValue && branchId.Value > 0)
                query = query.Where(r => r.BranchId == null || r.BranchId == branchId.Value);

            if (fiscalYearId.HasValue && fiscalYearId.Value > 0)
                query = query.Where(r => r.FiscalYearId == fiscalYearId.Value);

            var list = await query.OrderByDescending(r => r.RunDate).ThenByDescending(r => r.Id).ToListAsync();

            return list.Select(r => new DepreciationRunListDto
            {
                Id = r.Id,
                RunNumber = r.RunNumber,
                RunDate = r.RunDate,
                FiscalYearId = r.FiscalYearId,
                FiscalYearName = r.FiscalYear?.LocalizedName ?? "",
                FiscalPeriodName = r.FiscalPeriod?.LocalizedName ?? "",
                BranchId = r.BranchId,
                BranchName = r.Branch?.LocalizedName ?? "كافة الفروع",
                AssetsCount = r.AssetsCount,
                TotalDepreciationAmount = r.TotalDepreciationAmount,
                Status = r.Status,
                StatusName = GetRunStatusName(r.Status),
                JournalEntryId = r.JournalEntryId,
                JournalEntryNumber = r.JournalEntry?.EntryNumber,
                CreatedAt = r.CreatedAt,
                CreatedByUserName = r.CreatedByUserId
            }).ToList();
        }

        public async Task<DepreciationRunDetailsDto?> GetDepreciationRunByIdAsync(int id, string userId)
        {
            var r = await _context.AssetDepreciationRuns
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.FiscalYear)
                .Include(x => x.FiscalPeriod)
                .Include(x => x.Branch)
                .Include(x => x.JournalEntry)
                .Include(x => x.ReversalJournalEntry)
                .Include(x => x.Histories)
                    .ThenInclude(h => h.Asset)
                .Include(x => x.Histories)
                    .ThenInclude(h => h.CostCenter)
                .Include(x => x.Histories)
                    .ThenInclude(h => h.ExpenseAccount)
                .Include(x => x.Histories)
                    .ThenInclude(h => h.AccumulatedAccount)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (r == null) return null;

            return new DepreciationRunDetailsDto
            {
                Id = r.Id,
                RunNumber = r.RunNumber,
                RunDate = r.RunDate,
                FiscalYearId = r.FiscalYearId,
                FiscalYearName = r.FiscalYear?.LocalizedName ?? "",
                FiscalPeriodName = r.FiscalPeriod?.LocalizedName ?? "",
                BranchId = r.BranchId,
                BranchName = r.Branch?.LocalizedName ?? "كافة الفروع",
                AssetsCount = r.AssetsCount,
                TotalDepreciationAmount = r.TotalDepreciationAmount,
                Status = r.Status,
                StatusName = GetRunStatusName(r.Status),
                JournalEntryId = r.JournalEntryId,
                JournalEntryNumber = r.JournalEntry?.EntryNumber,
                ReversalJournalEntryId = r.ReversalJournalEntryId,
                ReversalJournalEntryNumber = r.ReversalJournalEntry?.EntryNumber,
                ReversedAt = r.ReversedAt,
                ReversedByUserName = r.ReversedByUserId,
                Notes = r.Notes,
                CreatedAt = r.CreatedAt,
                CreatedByUserName = r.CreatedByUserId,
                Lines = r.Histories.Select(h => new AssetDepreciationHistoryItemDto
                {
                    Id = h.Id,
                    AssetDepreciationRunId = h.AssetDepreciationRunId,
                    RunNumber = r.RunNumber,
                    AssetId = h.AssetId,
                    AssetCode = h.Asset?.AssetCode ?? "",
                    AssetName = h.Asset?.LocalizedName ?? "",
                    DepreciationDate = h.DepreciationDate,
                    FiscalPeriodName = r.FiscalPeriod?.LocalizedName ?? "",
                    AssetCostAtRun = h.AssetCostAtRun,
                    PriorAccumulatedDepreciation = h.PriorAccumulatedDepreciation,
                    DepreciationAmount = h.DepreciationAmount,
                    PostAccumulatedDepreciation = h.PostAccumulatedDepreciation,
                    BookValueBefore = h.BookValueBefore,
                    BookValueAfter = h.BookValueAfter,
                    BranchName = h.Branch?.LocalizedName ?? "",
                    CostCenterName = h.CostCenter?.LocalizedName,
                    ExpenseAccountCode = h.ExpenseAccount?.Code ?? "",
                    ExpenseAccountName = h.ExpenseAccount?.LocalizedName ?? "",
                    AccumulatedAccountCode = h.AccumulatedAccount?.Code ?? "",
                    AccumulatedAccountName = h.AccumulatedAccount?.LocalizedName ?? ""
                }).ToList()
            };
        }

        private async Task<string> GenerateNextRunNumberAsync(int? branchId, int fiscalYearId, string fyYear)
        {
            string prefix = branchId.HasValue ? $"DEP-{branchId.Value}-{fyYear}-" : $"DEP-ALL-{fyYear}-";

            var existingNumbers = await _context.AssetDepreciationRuns
                .IgnoreQueryFilters()
                .Where(r => r.FiscalYearId == fiscalYearId && r.RunNumber.StartsWith(prefix))
                .Select(r => r.RunNumber)
                .ToListAsync();

            long maxNumber = 0;
            foreach (var num in existingNumbers)
            {
                if (num.Length > prefix.Length)
                {
                    var sub = num.Substring(prefix.Length);
                    if (long.TryParse(sub, out long n) && n > maxNumber)
                    {
                        maxNumber = n;
                    }
                }
            }

            long nextNumber = maxNumber + 1;
            string candidate = $"{prefix}{nextNumber:D5}";

            while (await _context.AssetDepreciationRuns.IgnoreQueryFilters().AnyAsync(r => r.RunNumber == candidate))
            {
                nextNumber++;
                candidate = $"{prefix}{nextNumber:D5}";
            }

            return candidate;
        }

        private static string GetMethodName(DepreciationMethod method) => method switch
        {
            DepreciationMethod.StraightLine => "قسط ثابت",
            DepreciationMethod.DecliningBalance => "قسط متناقص",
            DepreciationMethod.UnitsOfProduction => "وحدات إنتاج",
            DepreciationMethod.NoDepreciation => "بدون إهلاك",
            _ => method.ToString()
        };

        private static string GetRunStatusName(DepreciationRunStatus status) => status switch
        {
            DepreciationRunStatus.Draft => "مسودة",
            DepreciationRunStatus.Approved => "معتمدة ومرحلة",
            DepreciationRunStatus.Reversed => "معكوسة",
            _ => status.ToString()
        };
    }
}
