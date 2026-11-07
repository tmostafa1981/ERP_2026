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
    public class FixedAssetService : IFixedAssetService
    {
        private readonly ErpDbContext _context;
        private readonly IFiscalPeriodService _fiscalPeriodService;
        private readonly IJournalEntryValidationService _validationService;
        private readonly IAuditService _audit;
        private readonly ICurrentUserContext _currentUser;

        private static readonly SemaphoreSlim _assetCodeLock = new(1, 1);

        private const string SC_Code = "FA-ASSET";
        private const string SC_Name = "سجل الأصول الثابتة";
        private const string SC_Module = "FixedAssets";

        public FixedAssetService(
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

        public async Task<List<FixedAssetListDto>> GetAssetsAsync(FixedAssetFilterDto filter, string userId)
        {
            var query = _context.FixedAssets
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(a => a.AssetCategory)
                .Include(a => a.Branch)
                .Include(a => a.CostCenter)
                .AsQueryable();

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
                query = query.Where(a => a.BranchId == filter.BranchId.Value);

            if (filter.AssetCategoryId.HasValue && filter.AssetCategoryId.Value > 0)
                query = query.Where(a => a.AssetCategoryId == filter.AssetCategoryId.Value);

            if (filter.Status.HasValue)
                query = query.Where(a => a.Status == filter.Status.Value);

            if (filter.CostCenterId.HasValue && filter.CostCenterId.Value > 0)
                query = query.Where(a => a.CostCenterId == filter.CostCenterId.Value);

            if (filter.FullyDepreciated.HasValue)
                query = query.Where(a => a.IsFullyDepreciated == filter.FullyDepreciated.Value);

            if (filter.FromPurchaseDate.HasValue)
                query = query.Where(a => a.PurchaseDate >= filter.FromPurchaseDate.Value.Date);

            if (filter.ToPurchaseDate.HasValue)
                query = query.Where(a => a.PurchaseDate <= filter.ToPurchaseDate.Value.Date);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(a => a.AssetCode.ToLower().Contains(term) ||
                                         a.NameAr.ToLower().Contains(term) ||
                                         a.NameEn.ToLower().Contains(term) ||
                                         (a.SerialNumber != null && a.SerialNumber.ToLower().Contains(term)) ||
                                         (a.Barcode != null && a.Barcode.ToLower().Contains(term)));
            }

            var list = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            return list.Select(a => new FixedAssetListDto
            {
                Id = a.Id,
                AssetCode = a.AssetCode,
                NameAr = a.NameAr,
                NameEn = a.NameEn,
                LocalizedName = a.LocalizedName,
                AssetCategoryId = a.AssetCategoryId,
                CategoryName = a.AssetCategory?.LocalizedName ?? "",
                BranchId = a.BranchId,
                BranchName = a.Branch?.LocalizedName ?? "",
                CostCenterName = a.CostCenter?.LocalizedName,
                Department = a.Department,
                CustodianEmployeeName = a.CustodianEmployeeName,
                Status = a.Status,
                StatusName = GetStatusName(a.Status),
                PurchaseDate = a.PurchaseDate,
                DepreciationStartDate = a.DepreciationStartDate,
                CurrentCostLocal = a.CurrentCostLocal,
                CurrentAccumulatedDepreciation = a.CurrentAccumulatedDepreciation,
                CurrentBookValue = a.CurrentBookValue,
                DepreciationMethod = a.DepreciationMethod,
                DepreciationMethodName = GetMethodName(a.DepreciationMethod),
                IsFullyDepreciated = a.IsFullyDepreciated
            }).ToList();
        }

        public async Task<FixedAssetDetailsDto?> GetAssetByIdAsync(int id, string userId)
        {
            var a = await _context.FixedAssets
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.AssetCategory)
                .Include(x => x.Branch)
                .Include(x => x.Warehouse)
                .Include(x => x.CostCenter)
                .Include(x => x.Vendor)
                .Include(x => x.Currency)
                .Include(x => x.PurchaseDocumentHeader)
                .Include(x => x.DisposalJournalEntry)
                .Include(x => x.AssetCostAccount)
                .Include(x => x.AccumulatedDepreciationAccount)
                .Include(x => x.DepreciationExpenseAccount)
                .Include(x => x.DisposalGainAccount)
                .Include(x => x.DisposalLossAccount)
                .Include(x => x.DepreciationHistories.OrderByDescending(h => h.DepreciationDate).Take(50))
                    .ThenInclude(h => h.AssetDepreciationRun)
                .Include(x => x.Transactions.OrderByDescending(t => t.TransactionDate).Take(50))
                    .ThenInclude(t => t.FromBranch)
                .Include(x => x.Transactions)
                    .ThenInclude(t => t.ToBranch)
                .Include(x => x.Transactions)
                    .ThenInclude(t => t.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (a == null) return null;

            var cat = a.AssetCategory;

            return new FixedAssetDetailsDto
            {
                Id = a.Id,
                RowVersion = a.RowVersion,
                AssetCode = a.AssetCode,
                NameAr = a.NameAr,
                NameEn = a.NameEn,
                Description = a.Description,
                SerialNumber = a.SerialNumber,
                Barcode = a.Barcode,
                ImagePath = a.ImagePath,
                AssetCategoryId = a.AssetCategoryId,
                CategoryCode = cat?.Code ?? "",
                CategoryName = cat?.LocalizedName ?? "",
                BranchId = a.BranchId,
                BranchName = a.Branch?.LocalizedName ?? "",
                WarehouseId = a.WarehouseId,
                WarehouseName = a.Warehouse?.LocalizedName,
                Department = a.Department,
                CostCenterId = a.CostCenterId,
                CostCenterName = a.CostCenter?.LocalizedName,
                CustodianEmployeeName = a.CustodianEmployeeName,
                Status = a.Status,
                StatusName = GetStatusName(a.Status),
                PurchaseDate = a.PurchaseDate,
                PlacedInServiceDate = a.PlacedInServiceDate,
                VendorId = a.VendorId,
                VendorName = a.Vendor?.LocalizedName,
                PurchaseDocumentHeaderId = a.PurchaseDocumentHeaderId,
                PurchaseDocumentNumber = a.PurchaseDocumentHeader?.DocumentNumber,
                PurchaseInvoiceReference = a.PurchaseInvoiceReference,
                CurrencyId = a.CurrencyId,
                CurrencyCode = a.Currency?.Code ?? "",
                CurrencyName = a.Currency?.LocalizedName ?? "",
                PurchasePriceForeign = a.PurchasePriceForeign,
                ExchangeRate = a.ExchangeRate,
                InitialCostLocal = a.InitialCostLocal,
                AdditionalExpensesLocal = a.AdditionalExpensesLocal,
                CurrentCostLocal = a.CurrentCostLocal,
                ResidualValue = a.ResidualValue,
                UsefulLifeMonths = a.UsefulLifeMonths,
                DepreciationStartDate = a.DepreciationStartDate,
                DepreciationMethod = a.DepreciationMethod,
                DepreciationMethodName = GetMethodName(a.DepreciationMethod),
                DepreciationRate = a.DepreciationRate,
                TotalExpectedUnits = a.TotalExpectedUnits,
                TotalProducedUnits = a.TotalProducedUnits,
                FirstMonthPolicy = a.FirstMonthPolicy,
                FirstMonthPolicyName = GetFirstMonthPolicyName(a.FirstMonthPolicy),

                AssetCostAccountId = a.AssetCostAccountId ?? cat?.AssetCostAccountId ?? 0,
                AssetCostAccountName = a.AssetCostAccount?.LocalizedName ?? cat?.AssetCostAccount?.LocalizedName ?? "",
                AccumulatedDepreciationAccountId = a.AccumulatedDepreciationAccountId ?? cat?.AccumulatedDepreciationAccountId ?? 0,
                AccumulatedDepreciationAccountName = a.AccumulatedDepreciationAccount?.LocalizedName ?? cat?.AccumulatedDepreciationAccount?.LocalizedName ?? "",
                DepreciationExpenseAccountId = a.DepreciationExpenseAccountId ?? cat?.DepreciationExpenseAccountId ?? 0,
                DepreciationExpenseAccountName = a.DepreciationExpenseAccount?.LocalizedName ?? cat?.DepreciationExpenseAccount?.LocalizedName ?? "",
                DisposalGainAccountId = a.DisposalGainAccountId ?? cat?.DisposalGainAccountId ?? 0,
                DisposalGainAccountName = a.DisposalGainAccount?.LocalizedName ?? cat?.DisposalGainAccount?.LocalizedName ?? "",
                DisposalLossAccountId = a.DisposalLossAccountId ?? cat?.DisposalLossAccountId ?? 0,
                DisposalLossAccountName = a.DisposalLossAccount?.LocalizedName ?? cat?.DisposalLossAccount?.LocalizedName ?? "",

                IsOpeningAsset = a.IsOpeningAsset,
                OpeningCarryingValue = a.OpeningCarryingValue,
                OpeningAccumulatedDepreciation = a.OpeningAccumulatedDepreciation,
                CurrentAccumulatedDepreciation = a.CurrentAccumulatedDepreciation,
                CurrentBookValue = a.CurrentBookValue,
                LastDepreciationDate = a.LastDepreciationDate,
                IsFullyDepreciated = a.IsFullyDepreciated,

                DisposalDate = a.DisposalDate,
                DisposalProceeds = a.DisposalProceeds,
                DisposalExpenses = a.DisposalExpenses,
                DisposalReason = a.DisposalReason,
                DisposalJournalEntryId = a.DisposalJournalEntryId,
                DisposalJournalEntryNumber = a.DisposalJournalEntry?.EntryNumber,

                Notes = a.Notes,
                CreatedAt = a.CreatedAt,
                CreatedByUserName = a.CreatedByUserId,

                DepreciationHistories = a.DepreciationHistories.Select(h => new AssetDepreciationHistoryItemDto
                {
                    Id = h.Id,
                    AssetDepreciationRunId = h.AssetDepreciationRunId,
                    RunNumber = h.AssetDepreciationRun?.RunNumber ?? "",
                    AssetId = h.AssetId,
                    AssetCode = a.AssetCode,
                    AssetName = a.LocalizedName,
                    DepreciationDate = h.DepreciationDate,
                    AssetCostAtRun = h.AssetCostAtRun,
                    PriorAccumulatedDepreciation = h.PriorAccumulatedDepreciation,
                    DepreciationAmount = h.DepreciationAmount,
                    PostAccumulatedDepreciation = h.PostAccumulatedDepreciation,
                    BookValueBefore = h.BookValueBefore,
                    BookValueAfter = h.BookValueAfter
                }).ToList(),

                Transactions = a.Transactions.Select(t => new AssetTransactionItemDto
                {
                    Id = t.Id,
                    AssetId = t.AssetId,
                    TransactionType = t.TransactionType,
                    TransactionTypeName = GetTransactionTypeName(t.TransactionType),
                    TransactionDate = t.TransactionDate,
                    FromBranchName = t.FromBranch?.LocalizedName,
                    ToBranchName = t.ToBranch?.LocalizedName,
                    FromCustodian = t.FromCustodian,
                    ToCustodian = t.ToCustodian,
                    Amount = t.Amount,
                    IsCapitalized = t.IsCapitalized,
                    JournalEntryId = t.JournalEntryId,
                    JournalEntryNumber = t.JournalEntry?.EntryNumber,
                    Reason = t.Reason,
                    CreatedAt = t.CreatedAt
                }).ToList()
            };
        }

        public async Task<(bool Success, string Message, int? AssetId, string? AssetCode)> CreateAssetAsync(CreateFixedAssetDto dto, string userId)
        {
            if (dto == null) return (false, "البيانات المدخلة غير صحيحة.", null, null);
            if (string.IsNullOrWhiteSpace(dto.NameAr)) return (false, "اسم الأصل بالعربي مطلوب.", null, null);
            if (dto.AssetCategoryId <= 0) return (false, "تصنيف الأصل مطلوب.", null, null);
            if (dto.BranchId <= 0) return (false, "الفرع مطلوب.", null, null);

            var category = await _context.AssetCategories
                .Include(c => c.AssetCostAccount)
                .Include(c => c.AccumulatedDepreciationAccount)
                .Include(c => c.DepreciationExpenseAccount)
                .FirstOrDefaultAsync(c => c.Id == dto.AssetCategoryId);

            if (category == null || !category.IsActive)
                return (false, "تصنيف الأصل غير صالح أو غير نشط.", null, null);

            // Calculate Local Initial Cost
            decimal exchangeRate = dto.ExchangeRate > 0 ? dto.ExchangeRate : 1.0m;
            decimal initialCost = dto.InitialCostLocal;
            if (initialCost <= 0 && dto.PurchasePriceForeign > 0)
            {
                initialCost = Math.Round(dto.PurchasePriceForeign * exchangeRate, 4);
            }

            decimal additionalExp = Math.Max(0m, dto.AdditionalExpensesLocal);
            decimal totalInitialCost = initialCost + additionalExp;

            if (dto.Status != AssetStatus.UnderConstruction && totalInitialCost <= 0)
            {
                return (false, "تكلفة الأصل الإجمالية يجب أن تكون أكبر من الصفر للأصول العادية.", null, null);
            }

            if (dto.ResidualValue >= totalInitialCost && totalInitialCost > 0)
            {
                return (false, "القيمة التخريدية لا يمكن أن تكون مساوية أو أكبر من تكلفة الأصل.", null, null);
            }

            // Opening Balances Verification
            decimal openingAccDep = Math.Max(0m, dto.OpeningAccumulatedDepreciation);
            decimal currentAccDep = 0m;
            decimal currentBookValue = totalInitialCost;
            decimal openingCarrying = totalInitialCost;

            if (dto.IsOpeningAsset)
            {
                if (openingAccDep > totalInitialCost)
                {
                    return (false, "مجمع الإهلاك الافتتاحي لا يمكن أن يتجاوز التكلفة التاريخية للأصل.", null, null);
                }
                openingCarrying = totalInitialCost - openingAccDep;
                currentAccDep = openingAccDep;
                currentBookValue = openingCarrying;
            }

            // Depreciation start date defaults
            DateTime? depStartDate = dto.DepreciationStartDate;
            if (!depStartDate.HasValue && dto.Status == AssetStatus.InService)
            {
                depStartDate = dto.PlacedInServiceDate ?? dto.PurchaseDate;
            }

            // Generate Next Asset Code
            string assetCode;
            await _assetCodeLock.WaitAsync();
            try
            {
                if (!string.IsNullOrWhiteSpace(dto.AssetCode))
                {
                    var exists = await _context.FixedAssets.IgnoreQueryFilters().AnyAsync(a => a.AssetCode == dto.AssetCode.Trim());
                    if (exists) return (false, $"كود الأصل '{dto.AssetCode}' مسجل بالفعل.", null, null);
                    assetCode = dto.AssetCode.Trim();
                }
                else
                {
                    assetCode = await GenerateNextAssetCodeAsync(dto.BranchId, 0, dto.PurchaseDate.Year.ToString());
                }
            }
            finally
            {
                _assetCodeLock.Release();
            }

            var asset = new FixedAsset
            {
                AssetCode = assetCode,
                NameAr = dto.NameAr.Trim(),
                NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? dto.NameAr.Trim() : dto.NameEn.Trim(),
                AssetCategoryId = dto.AssetCategoryId,
                Description = dto.Description?.Trim(),
                SerialNumber = dto.SerialNumber?.Trim(),
                Barcode = dto.Barcode?.Trim(),
                BranchId = dto.BranchId,
                WarehouseId = dto.WarehouseId > 0 ? dto.WarehouseId : null,
                Department = dto.Department?.Trim(),
                CostCenterId = dto.CostCenterId > 0 ? dto.CostCenterId : null,
                CustodianEmployeeName = dto.CustodianEmployeeName?.Trim(),
                Status = dto.Status,
                PurchaseDate = dto.PurchaseDate.Date,
                PlacedInServiceDate = dto.PlacedInServiceDate?.Date,
                VendorId = dto.VendorId > 0 ? dto.VendorId : null,
                PurchaseDocumentHeaderId = dto.PurchaseDocumentHeaderId > 0 ? dto.PurchaseDocumentHeaderId : null,
                PurchaseInvoiceReference = dto.PurchaseInvoiceReference?.Trim(),
                CurrencyId = dto.CurrencyId > 0 ? dto.CurrencyId : 1,
                PurchasePriceForeign = dto.PurchasePriceForeign,
                ExchangeRate = exchangeRate,
                InitialCostLocal = initialCost,
                AdditionalExpensesLocal = additionalExp,
                CurrentCostLocal = totalInitialCost,
                ResidualValue = dto.ResidualValue,
                UsefulLifeMonths = dto.UsefulLifeMonths > 0 ? dto.UsefulLifeMonths : category.DefaultUsefulLifeMonths,
                DepreciationStartDate = depStartDate?.Date,
                DepreciationMethod = dto.DepreciationMethod,
                DepreciationRate = dto.DepreciationRate > 0 ? dto.DepreciationRate : 0m,
                TotalExpectedUnits = dto.TotalExpectedUnits,
                TotalProducedUnits = 0m,
                FirstMonthPolicy = dto.FirstMonthPolicy,
                AssetCostAccountId = dto.AssetCostAccountId > 0 ? dto.AssetCostAccountId : null,
                AccumulatedDepreciationAccountId = dto.AccumulatedDepreciationAccountId > 0 ? dto.AccumulatedDepreciationAccountId : null,
                DepreciationExpenseAccountId = dto.DepreciationExpenseAccountId > 0 ? dto.DepreciationExpenseAccountId : null,
                DisposalGainAccountId = dto.DisposalGainAccountId > 0 ? dto.DisposalGainAccountId : null,
                DisposalLossAccountId = dto.DisposalLossAccountId > 0 ? dto.DisposalLossAccountId : null,
                IsOpeningAsset = dto.IsOpeningAsset,
                OpeningCarryingValue = openingCarrying,
                OpeningAccumulatedDepreciation = openingAccDep,
                CurrentAccumulatedDepreciation = currentAccDep,
                CurrentBookValue = currentBookValue,
                IsFullyDepreciated = (totalInitialCost > 0 && currentBookValue <= dto.ResidualValue),
                Notes = dto.Notes?.Trim(),
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            // Initial movement/registration transaction
            asset.Transactions.Add(new AssetTransaction
            {
                TransactionType = AssetTransactionType.StatusChange,
                TransactionDate = dto.PurchaseDate.Date,
                ToBranchId = dto.BranchId,
                ToWarehouseId = asset.WarehouseId,
                ToCostCenterId = asset.CostCenterId,
                ToCustodian = asset.CustodianEmployeeName,
                ToDepartment = asset.Department,
                Amount = totalInitialCost,
                Reason = dto.IsOpeningAsset ? "تسجيل أصل قديم برصيد افتتاحي" : "شراء وتسجيل أصل ثابت جديد",
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            });

            _context.FixedAssets.Add(asset);
            await _context.SaveChangesAsync();

            await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "FixedAsset",
                entityId: asset.Id.ToString(),
                newValues: new { asset.Id, asset.AssetCode, asset.NameAr, asset.CurrentCostLocal });

            return (true, $"تم تسجيل الأصل الثابت ({asset.AssetCode}) بنجاح.", asset.Id, asset.AssetCode);
        }

        public async Task<(bool Success, string Message)> UpdateAssetAsync(UpdateFixedAssetDto dto, string userId)
        {
            if (dto == null) return (false, "البيانات المدخلة غير صحيحة.");

            var asset = await _context.FixedAssets
                .Include(a => a.DepreciationHistories)
                .FirstOrDefaultAsync(a => a.Id == dto.Id);

            if (asset == null) return (false, "الأصل غير موجود.");

            if (asset.Status == AssetStatus.Disposed)
            {
                return (false, "لا يمكن تعديل بيانات أصل مستبعد أو مباع.");
            }

            bool hasDepreciationRuns = asset.DepreciationHistories.Any();

            // If depreciation runs exist, historical cost and opening values cannot be altered directly
            if (hasDepreciationRuns && (asset.InitialCostLocal != dto.InitialCostLocal || asset.OpeningAccumulatedDepreciation != dto.OpeningAccumulatedDepreciation))
            {
                return (false, "لا يمكن تعديل التكلفة التاريخية أو مجمع الإهلاك الافتتاحي لأصل مسجل له عمليات إهلاك سابقة. استخدم خيار إضافة تكاليف لاحقة.");
            }

            asset.NameAr = dto.NameAr.Trim();
            asset.NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? dto.NameAr.Trim() : dto.NameEn.Trim();
            asset.AssetCategoryId = dto.AssetCategoryId;
            asset.Description = dto.Description?.Trim();
            asset.SerialNumber = dto.SerialNumber?.Trim();
            asset.Barcode = dto.Barcode?.Trim();
            asset.WarehouseId = dto.WarehouseId > 0 ? dto.WarehouseId : null;
            asset.Department = dto.Department?.Trim();
            asset.CostCenterId = dto.CostCenterId > 0 ? dto.CostCenterId : null;
            asset.CustodianEmployeeName = dto.CustodianEmployeeName?.Trim();
            asset.VendorId = dto.VendorId > 0 ? dto.VendorId : null;
            asset.PurchaseInvoiceReference = dto.PurchaseInvoiceReference?.Trim();
            asset.ResidualValue = dto.ResidualValue;
            asset.UsefulLifeMonths = dto.UsefulLifeMonths > 0 ? dto.UsefulLifeMonths : asset.UsefulLifeMonths;
            asset.DepreciationMethod = dto.DepreciationMethod;
            asset.DepreciationRate = dto.DepreciationRate;
            asset.TotalExpectedUnits = dto.TotalExpectedUnits;
            asset.FirstMonthPolicy = dto.FirstMonthPolicy;
            asset.AssetCostAccountId = dto.AssetCostAccountId > 0 ? dto.AssetCostAccountId : null;
            asset.AccumulatedDepreciationAccountId = dto.AccumulatedDepreciationAccountId > 0 ? dto.AccumulatedDepreciationAccountId : null;
            asset.DepreciationExpenseAccountId = dto.DepreciationExpenseAccountId > 0 ? dto.DepreciationExpenseAccountId : null;
            asset.DisposalGainAccountId = dto.DisposalGainAccountId > 0 ? dto.DisposalGainAccountId : null;
            asset.DisposalLossAccountId = dto.DisposalLossAccountId > 0 ? dto.DisposalLossAccountId : null;
            asset.Notes = dto.Notes?.Trim();
            asset.UpdatedAt = DateTime.UtcNow;

            if (!hasDepreciationRuns)
            {
                asset.InitialCostLocal = dto.InitialCostLocal;
                asset.AdditionalExpensesLocal = dto.AdditionalExpensesLocal;
                asset.CurrentCostLocal = dto.InitialCostLocal + dto.AdditionalExpensesLocal;
                asset.OpeningAccumulatedDepreciation = dto.OpeningAccumulatedDepreciation;
                asset.CurrentAccumulatedDepreciation = dto.OpeningAccumulatedDepreciation;
                asset.CurrentBookValue = asset.CurrentCostLocal - asset.CurrentAccumulatedDepreciation;
                asset.DepreciationStartDate = dto.DepreciationStartDate?.Date;
            }

            await _context.SaveChangesAsync();

            await _audit.LogAsync(new ERP.Application.Models.AuditLogEntry
            {
                ScreenCode = SC_Code,
                ScreenName = SC_Name,
                ModuleName = SC_Module,
                Action = AuditAction.Update,
                EntityName = "FixedAsset",
                EntityId = asset.Id.ToString(),
                Description = $"تعديل بيانات الأصل {asset.AssetCode}"
            });

            return (true, "تم تعديل بيانات الأصل الثابت بنجاح.");
        }

        public async Task<(bool Success, string Message)> DeleteAssetAsync(int id, string userId)
        {
            var asset = await _context.FixedAssets
                .Include(a => a.DepreciationHistories)
                .Include(a => a.Transactions)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (asset == null) return (false, "الأصل غير موجود.");

            if (asset.DepreciationHistories.Any())
            {
                return (false, "لا يمكن حذف أصل مسجل له عمليات إهلاك دورية في النظام. يمكنك إيقافه أو استبعاده.");
            }

            if (asset.DisposalJournalEntryId.HasValue)
            {
                return (false, "لا يمكن حذف أصل تم استبعاده وتوليد قيد استبعاد له.");
            }

            _context.FixedAssets.Remove(asset);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "FixedAsset",
                entityId: id.ToString(),
                oldValues: new { asset.Id, asset.AssetCode, asset.NameAr });

            return (true, "تم حذف الأصل الثابت بنجاح.");
        }

        public async Task<(bool Success, string Message)> TransferAssetAsync(AssetTransferDto dto, string userId)
        {
            if (dto == null) return (false, "البيانات المدخلة غير صحيحة.");

            var asset = await _context.FixedAssets.FirstOrDefaultAsync(a => a.Id == dto.AssetId);
            if (asset == null) return (false, "الأصل غير موجود.");

            if (asset.Status == AssetStatus.Disposed)
                return (false, "لا يمكن نقل أصل مستبعد.");

            var targetBranch = await _context.Branches.FindAsync(dto.ToBranchId);
            if (targetBranch == null || !targetBranch.IsActive)
                return (false, "الفرع المحول إليه غير صالح أو غير نشط.");

            var tx = new AssetTransaction
            {
                AssetId = asset.Id,
                TransactionType = AssetTransactionType.Transfer,
                TransactionDate = dto.TransferDate.Date,
                FromBranchId = asset.BranchId,
                ToBranchId = dto.ToBranchId,
                FromWarehouseId = asset.WarehouseId,
                ToWarehouseId = dto.ToWarehouseId > 0 ? dto.ToWarehouseId : null,
                FromCostCenterId = asset.CostCenterId,
                ToCostCenterId = dto.ToCostCenterId > 0 ? dto.ToCostCenterId : null,
                FromDepartment = asset.Department,
                ToDepartment = dto.ToDepartment?.Trim(),
                FromCustodian = asset.CustodianEmployeeName,
                ToCustodian = dto.ToCustodian?.Trim(),
                Reason = dto.Reason?.Trim() ?? "نقل أصل بين الفروع / المواقع",
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            // Update Asset Current Location
            asset.BranchId = dto.ToBranchId;
            if (dto.ToWarehouseId.HasValue && dto.ToWarehouseId.Value > 0) asset.WarehouseId = dto.ToWarehouseId.Value;
            if (dto.ToCostCenterId.HasValue && dto.ToCostCenterId.Value > 0) asset.CostCenterId = dto.ToCostCenterId.Value;
            if (!string.IsNullOrWhiteSpace(dto.ToDepartment)) asset.Department = dto.ToDepartment.Trim();
            if (!string.IsNullOrWhiteSpace(dto.ToCustodian)) asset.CustodianEmployeeName = dto.ToCustodian.Trim();
            asset.UpdatedAt = DateTime.UtcNow;

            _context.AssetTransactions.Add(tx);
            await _context.SaveChangesAsync();

            await _audit.LogAsync(new ERP.Application.Models.AuditLogEntry
            {
                ScreenCode = SC_Code,
                ScreenName = SC_Name,
                ModuleName = SC_Module,
                Action = AuditAction.Update,
                EntityName = "FixedAsset",
                EntityId = asset.Id.ToString(),
                Description = $"نقل الأصل {asset.AssetCode} من فرع {tx.FromBranchId} إلى فرع {tx.ToBranchId}"
            });

            return (true, $"تم نقل الأصل ({asset.AssetCode}) وتحديث بيانات الفرع والعهدة بنجاح.");
        }

        public async Task<(bool Success, string Message, int? JournalEntryId)> AddAdditionalCostAsync(AssetAdditionalCostDto dto, string userId)
        {
            if (dto == null) return (false, "البيانات المدخلة غير صحيحة.", null);
            if (dto.Amount <= 0) return (false, "مبلغ التكلفة الإضافية يجب أن يكون أكبر من الصفر.", null);

            var asset = await _context.FixedAssets
                .Include(a => a.AssetCategory)
                .FirstOrDefaultAsync(a => a.Id == dto.AssetId);

            if (asset == null) return (false, "الأصل غير موجود.", null);
            if (asset.Status == AssetStatus.Disposed) return (false, "لا يمكن إضافة تكاليف على أصل مستبعد.", null);

            var targetDate = dto.TransactionDate.Date;
            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(targetDate);
            if (!isPeriodAllowed) return (false, periodError ?? "الفترة المالية لتاريخ العملية مغلقة.", null);

            int? jvId = null;

            int costAccountId = asset.AssetCostAccountId ?? asset.AssetCategory?.AssetCostAccountId ?? 0;
            if (costAccountId <= 0) return (false, "حساب تكلفة الأصل غير محدد.", null);

            int creditAccountId = dto.CashBankAccountId ?? 0;
            if (creditAccountId <= 0)
            {
                // Fallback default cash/bank or AP
                var defBank = await _context.GLAccounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Code.StartsWith("1101") && !a.IsGroup);
                creditAccountId = defBank?.Id ?? costAccountId;
            }

            int debitAccountId = dto.IsCapitalized ? costAccountId : (dto.ExpenseAccountId ?? 0);
            if (debitAccountId <= 0)
            {
                var defRepair = await _context.GLAccounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Code.StartsWith("52") && !a.IsGroup);
                debitAccountId = defRepair?.Id ?? costAccountId;
            }

            // Create Journal Entry if requested
            if (dto.CreateJournalEntry)
            {
                var fy = await _context.FiscalYears.FirstOrDefaultAsync(y => y.IsActive && targetDate >= y.StartDate && targetDate <= y.EndDate);
                if (fy == null) return (false, "لا توجد سنة مالية معتمدة لهذا التاريخ.", null);

                var baseCurr = await _context.Currencies.FirstOrDefaultAsync(c => c.IsBaseCurrency)
                               ?? await _context.Currencies.FirstAsync();

                string jvNumber = $"JV-{asset.BranchId}-{fy.Year}-{Guid.NewGuid().ToString().Substring(0, 4)}";

                var jv = new JournalEntryHeader
                {
                    BranchId = asset.BranchId,
                    FiscalYearId = fy.Id,
                    EntryNumber = jvNumber,
                    EntryDate = targetDate,
                    Description = dto.IsCapitalized
                        ? $"رسملة تكلفة إضافية على الأصل {asset.AssetCode} - {asset.LocalizedName}"
                        : $"مصروف صيانة/تطوير غير مرسمل للأصل {asset.AssetCode}",
                    Notes = dto.Reason,
                    Status = JournalEntryStatus.Posted,
                    SourceDocumentType = SourceDocumentType.AssetTransaction,
                    TotalDebitLocal = dto.Amount,
                    TotalCreditLocal = dto.Amount,
                    PostedAt = DateTime.UtcNow,
                    PostedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                jv.Lines.Add(new JournalEntryLine
                {
                    LineNumber = 1,
                    AccountId = debitAccountId,
                    CurrencyId = baseCurr.Id,
                    ExchangeRate = 1.0m,
                    DebitLocal = dto.Amount,
                    CreditLocal = 0m,
                    CostCenterId = asset.CostCenterId,
                    Description = dto.IsCapitalized ? $"إضافة تكلفة رأسمالية - {asset.AssetCode}" : $"مصروف تحسينات - {asset.AssetCode}"
                });

                jv.Lines.Add(new JournalEntryLine
                {
                    LineNumber = 2,
                    AccountId = creditAccountId,
                    SubLedgerType = dto.VendorId.HasValue ? SubLedgerType.Vendor : SubLedgerType.None,
                    SubLedgerId = dto.VendorId,
                    CurrencyId = baseCurr.Id,
                    ExchangeRate = 1.0m,
                    DebitLocal = 0m,
                    CreditLocal = dto.Amount,
                    Description = $"سداد تكلفة إضافية للأصل {asset.AssetCode}"
                });

                _context.JournalEntryHeaders.Add(jv);
                await _context.SaveChangesAsync();
                jvId = jv.Id;
            }

            // If Capitalized, increase current cost and book value
            if (dto.IsCapitalized)
            {
                asset.CurrentCostLocal += dto.Amount;
                asset.CurrentBookValue += dto.Amount;
                asset.IsFullyDepreciated = (asset.CurrentBookValue <= asset.ResidualValue);
                asset.UpdatedAt = DateTime.UtcNow;
            }

            var tx = new AssetTransaction
            {
                AssetId = asset.Id,
                TransactionType = AssetTransactionType.AdditionalCost,
                TransactionDate = targetDate,
                ToBranchId = asset.BranchId,
                ToCostCenterId = asset.CostCenterId,
                Amount = dto.Amount,
                IsCapitalized = dto.IsCapitalized,
                ExpenseAccountId = dto.IsCapitalized ? null : debitAccountId,
                JournalEntryId = jvId,
                Reason = dto.Reason ?? (dto.IsCapitalized ? "رسملة تكلفة إضافية" : "مصروف تطوير مباشر"),
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.AssetTransactions.Add(tx);
            await _context.SaveChangesAsync();

            await _audit.LogAsync(new ERP.Application.Models.AuditLogEntry
            {
                ScreenCode = SC_Code,
                ScreenName = SC_Name,
                ModuleName = SC_Module,
                Action = AuditAction.Update,
                EntityName = "FixedAsset",
                EntityId = asset.Id.ToString(),
                Description = $"إضافة تكلفة بمبلغ {dto.Amount} للأصل {asset.AssetCode} (مرسملة: {dto.IsCapitalized})"
            });

            return (true, $"تم تسجيل التكلفة الإضافية للأصل ({asset.AssetCode}) بنجاح.", jvId);
        }

        public async Task<(bool Success, string Message, int? JournalEntryId)> DisposeAssetAsync(AssetDisposalDto dto, string userId)
        {
            if (dto == null) return (false, "البيانات المدخلة غير صحيحة.", null);

            var asset = await _context.FixedAssets
                .Include(a => a.AssetCategory)
                .FirstOrDefaultAsync(a => a.Id == dto.AssetId);

            if (asset == null) return (false, "الأصل غير موجود.", null);
            if (asset.Status == AssetStatus.Disposed) return (false, "الأصل مستبعد ومباع بالفعل سابقاً.", null);

            var targetDate = dto.DisposalDate.Date;
            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(targetDate);
            if (!isPeriodAllowed) return (false, periodError ?? "الفترة المالية لتاريخ الاستبعاد مغلقة.", null);

            var cat = asset.AssetCategory;
            int costAccId = asset.AssetCostAccountId ?? cat?.AssetCostAccountId ?? 0;
            int accDepAccId = asset.AccumulatedDepreciationAccountId ?? cat?.AccumulatedDepreciationAccountId ?? 0;
            int gainAccId = asset.DisposalGainAccountId ?? cat?.DisposalGainAccountId ?? 0;
            int lossAccId = asset.DisposalLossAccountId ?? cat?.DisposalLossAccountId ?? 0;

            if (costAccId <= 0 || accDepAccId <= 0 || gainAccId <= 0 || lossAccId <= 0)
            {
                return (false, "حسابات تكلفة الأصل ومجمع الإهلاك وأرباح/خسائر الاستبعاد يجب أن تكون محددة بالكامل.", null);
            }

            decimal proceeds = Math.Max(0m, dto.ProceedsAmount);
            decimal cost = asset.CurrentCostLocal;
            decimal accDep = asset.CurrentAccumulatedDepreciation;
            decimal netBookValue = Math.Max(0m, cost - accDep);
            decimal netResult = proceeds - netBookValue; // Positive = Gain, Negative = Loss

            int? jvId = null;

            if (dto.AutoPostJournalEntry)
            {
                var fy = await _context.FiscalYears.FirstOrDefaultAsync(y => y.IsActive && targetDate >= y.StartDate && targetDate <= y.EndDate);
                if (fy == null) return (false, "لا توجد سنة مالية معتمدة لهذا التاريخ.", null);

                var baseCurr = await _context.Currencies.FirstOrDefaultAsync(c => c.IsBaseCurrency)
                               ?? await _context.Currencies.FirstAsync();

                int cashAccountId = dto.ProceedsCashBankAccountId ?? 0;
                if (cashAccountId <= 0 && proceeds > 0)
                {
                    var defBank = await _context.GLAccounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Code.StartsWith("1101") && !a.IsGroup);
                    cashAccountId = defBank?.Id ?? costAccId;
                }

                string jvNumber = $"JV-{asset.BranchId}-{fy.Year}-DISP-{Guid.NewGuid().ToString().Substring(0, 4)}";

                var jv = new JournalEntryHeader
                {
                    BranchId = asset.BranchId,
                    FiscalYearId = fy.Id,
                    EntryNumber = jvNumber,
                    EntryDate = targetDate,
                    Description = $"قيد استبعاد/بيع أصل ثابت: {asset.AssetCode} - {asset.LocalizedName}",
                    Notes = dto.Reason,
                    Status = JournalEntryStatus.Posted,
                    SourceDocumentType = SourceDocumentType.AssetDisposal,
                    PostedAt = DateTime.UtcNow,
                    PostedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                int lineNum = 1;

                // 1. Debit Cash/Bank/Customer with proceeds (if proceeds > 0)
                if (proceeds > 0)
                {
                    jv.Lines.Add(new JournalEntryLine
                    {
                        LineNumber = lineNum++,
                        AccountId = cashAccountId,
                        SubLedgerType = dto.CustomerId.HasValue ? SubLedgerType.Customer : SubLedgerType.None,
                        SubLedgerId = dto.CustomerId,
                        CurrencyId = baseCurr.Id,
                        ExchangeRate = 1.0m,
                        DebitLocal = proceeds,
                        CreditLocal = 0m,
                        CostCenterId = asset.CostCenterId,
                        Description = $"متحصلات بيع الأصل {asset.AssetCode}"
                    });
                }

                // 2. Debit Accumulated Depreciation to close it (if accDep > 0)
                if (accDep > 0)
                {
                    jv.Lines.Add(new JournalEntryLine
                    {
                        LineNumber = lineNum++,
                        AccountId = accDepAccId,
                        CurrencyId = baseCurr.Id,
                        ExchangeRate = 1.0m,
                        DebitLocal = accDep,
                        CreditLocal = 0m,
                        CostCenterId = asset.CostCenterId,
                        Description = $"إقفال مجمع إهلاك الأصل {asset.AssetCode}"
                    });
                }

                // 3. Loss or Gain
                if (netResult < -0.0001m)
                {
                    // Loss on disposal: Debit Loss
                    decimal lossAmount = Math.Abs(netResult);
                    jv.Lines.Add(new JournalEntryLine
                    {
                        LineNumber = lineNum++,
                        AccountId = lossAccId,
                        CurrencyId = baseCurr.Id,
                        ExchangeRate = 1.0m,
                        DebitLocal = lossAmount,
                        CreditLocal = 0m,
                        CostCenterId = asset.CostCenterId,
                        Description = $"خسائر استبعاد/بيع الأصل {asset.AssetCode}"
                    });
                }
                else if (netResult > 0.0001m)
                {
                    // Gain on disposal: Credit Gain
                    decimal gainAmount = netResult;
                    jv.Lines.Add(new JournalEntryLine
                    {
                        LineNumber = lineNum++,
                        AccountId = gainAccId,
                        CurrencyId = baseCurr.Id,
                        ExchangeRate = 1.0m,
                        DebitLocal = 0m,
                        CreditLocal = gainAmount,
                        CostCenterId = asset.CostCenterId,
                        Description = $"أرباح استبعاد/بيع الأصل {asset.AssetCode}"
                    });
                }

                // 4. Credit Asset Historical Cost to close the asset (Credit = cost)
                jv.Lines.Add(new JournalEntryLine
                {
                    LineNumber = lineNum++,
                    AccountId = costAccId,
                    CurrencyId = baseCurr.Id,
                    ExchangeRate = 1.0m,
                    DebitLocal = 0m,
                    CreditLocal = cost,
                    CostCenterId = asset.CostCenterId,
                    Description = $"إقفال التكلفة التاريخية للأصل {asset.AssetCode}"
                });

                jv.TotalDebitLocal = jv.Lines.Sum(l => l.DebitLocal);
                jv.TotalCreditLocal = jv.Lines.Sum(l => l.CreditLocal);

                // Strict Balance Check
                if (Math.Abs(jv.TotalDebitLocal - jv.TotalCreditLocal) > 0.01m)
                {
                    return (false, $"قيد استبعاد الأصل غير متوازن! المدين: {jv.TotalDebitLocal} والدائن: {jv.TotalCreditLocal}", null);
                }

                _context.JournalEntryHeaders.Add(jv);
                await _context.SaveChangesAsync();
                jvId = jv.Id;
            }

            // Update Asset State
            asset.Status = AssetStatus.Disposed;
            asset.DisposalDate = targetDate;
            asset.DisposalProceeds = proceeds;
            asset.DisposalExpenses = dto.DisposalExpenses;
            asset.DisposalReason = dto.Reason;
            asset.DisposalJournalEntryId = jvId;
            asset.CurrentBookValue = 0m;
            asset.IsFullyDepreciated = true;
            asset.UpdatedAt = DateTime.UtcNow;

            var tx = new AssetTransaction
            {
                AssetId = asset.Id,
                TransactionType = AssetTransactionType.Disposal,
                TransactionDate = targetDate,
                ToBranchId = asset.BranchId,
                ToCostCenterId = asset.CostCenterId,
                Amount = proceeds,
                JournalEntryId = jvId,
                Reason = dto.Reason ?? $"استبعاد الأصل (صافي الربح/الخسارة: {netResult:N2})",
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.AssetTransactions.Add(tx);
            await _context.SaveChangesAsync();

            await _audit.LogAsync(new ERP.Application.Models.AuditLogEntry
            {
                ScreenCode = SC_Code,
                ScreenName = SC_Name,
                ModuleName = SC_Module,
                Action = AuditAction.Update,
                EntityName = "FixedAsset",
                EntityId = asset.Id.ToString(),
                Description = $"استبعاد الأصل {asset.AssetCode} بقيمة بيع {proceeds} وقيد رقم {jvId}"
            });

            return (true, $"تم استبعاد الأصل ({asset.AssetCode}) بنجاح وتسجيل قيد الاستبعاد المحاسبي.", jvId);
        }

        public async Task<(bool Success, string Message, int? JournalEntryId)> CapitalizeCipAssetAsync(AssetCapitalizeCipDto dto, string userId)
        {
            if (dto == null) return (false, "البيانات المدخلة غير صحيحة.", null);

            var asset = await _context.FixedAssets
                .Include(a => a.AssetCategory)
                .FirstOrDefaultAsync(a => a.Id == dto.AssetId);

            if (asset == null) return (false, "الأصل غير موجود.", null);
            if (asset.Status != AssetStatus.UnderConstruction)
            {
                return (false, "العملية متاحة فقط للأصول التي حالتها 'تحت الإنشاء / تجهيز (CIP)'.", null);
            }

            if (asset.CurrentCostLocal <= 0)
            {
                return (false, "لا يمكن تشغيل وتحويل أصل تكلفته الرأسمالية صفر.", null);
            }

            var targetDate = dto.CapitalizationDate.Date;
            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(targetDate);
            if (!isPeriodAllowed) return (false, periodError ?? "الفترة المالية لتاريخ التحويل مغلقة.", null);

            int? jvId = null;
            var cat = asset.AssetCategory;
            int costAccId = asset.AssetCostAccountId ?? cat?.AssetCostAccountId ?? 0;
            int cipAccId = cat?.CipAccountId ?? 0;

            if (dto.CreateReclassificationEntry && cipAccId > 0 && costAccId > 0 && cipAccId != costAccId)
            {
                var fy = await _context.FiscalYears.FirstOrDefaultAsync(y => y.IsActive && targetDate >= y.StartDate && targetDate <= y.EndDate);
                if (fy != null)
                {
                    var baseCurr = await _context.Currencies.FirstOrDefaultAsync(c => c.IsBaseCurrency)
                                   ?? await _context.Currencies.FirstAsync();

                    var jv = new JournalEntryHeader
                    {
                        BranchId = asset.BranchId,
                        FiscalYearId = fy.Id,
                        EntryNumber = $"JV-{asset.BranchId}-{fy.Year}-CIP-{Guid.NewGuid().ToString().Substring(0, 4)}",
                        EntryDate = targetDate,
                        Description = $"تحويل وتشغيل أصل من مشروعات تحت التنفيذ إلى أصول ثابتة: {asset.AssetCode}",
                        Notes = dto.Notes,
                        Status = JournalEntryStatus.Posted,
                        SourceDocumentType = SourceDocumentType.AssetTransaction,
                        TotalDebitLocal = asset.CurrentCostLocal,
                        TotalCreditLocal = asset.CurrentCostLocal,
                        PostedAt = DateTime.UtcNow,
                        PostedByUserId = userId,
                        CreatedAt = DateTime.UtcNow
                    };

                    jv.Lines.Add(new JournalEntryLine
                    {
                        LineNumber = 1,
                        AccountId = costAccId,
                        CurrencyId = baseCurr.Id,
                        ExchangeRate = 1.0m,
                        DebitLocal = asset.CurrentCostLocal,
                        CreditLocal = 0m,
                        CostCenterId = asset.CostCenterId,
                        Description = $"إثبات الأصل الثابت {asset.AssetCode}"
                    });

                    jv.Lines.Add(new JournalEntryLine
                    {
                        LineNumber = 2,
                        AccountId = cipAccId,
                        CurrencyId = baseCurr.Id,
                        ExchangeRate = 1.0m,
                        DebitLocal = 0m,
                        CreditLocal = asset.CurrentCostLocal,
                        CostCenterId = asset.CostCenterId,
                        Description = $"إقفال مشروعات تحت التنفيذ للأصل {asset.AssetCode}"
                    });

                    _context.JournalEntryHeaders.Add(jv);
                    await _context.SaveChangesAsync();
                    jvId = jv.Id;
                }
            }

            // Transition State
            asset.Status = AssetStatus.InService;
            asset.PlacedInServiceDate = targetDate;
            asset.DepreciationStartDate = dto.DepreciationStartDate.Date;
            asset.UsefulLifeMonths = dto.UsefulLifeMonths > 0 ? dto.UsefulLifeMonths : asset.UsefulLifeMonths;
            asset.ResidualValue = dto.ResidualValue;
            asset.DepreciationMethod = dto.DepreciationMethod;
            asset.DepreciationRate = dto.DepreciationRate;
            asset.CurrentBookValue = asset.CurrentCostLocal - asset.CurrentAccumulatedDepreciation;
            asset.UpdatedAt = DateTime.UtcNow;

            var tx = new AssetTransaction
            {
                AssetId = asset.Id,
                TransactionType = AssetTransactionType.Capitalization,
                TransactionDate = targetDate,
                ToBranchId = asset.BranchId,
                ToCostCenterId = asset.CostCenterId,
                Amount = asset.CurrentCostLocal,
                JournalEntryId = jvId,
                Reason = "اكتمال وتحويل الأصل من تحت الإنشاء إلى أصل جاهز في الخدمة",
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.AssetTransactions.Add(tx);
            await _context.SaveChangesAsync();

            await _audit.LogAsync(new ERP.Application.Models.AuditLogEntry
            {
                ScreenCode = SC_Code,
                ScreenName = SC_Name,
                ModuleName = SC_Module,
                Action = AuditAction.Update,
                EntityName = "FixedAsset",
                EntityId = asset.Id.ToString(),
                Description = $"تحويل الأصل {asset.AssetCode} إلى قيد التشغيل بتكلفة {asset.CurrentCostLocal}"
            });

            return (true, $"تم تحويل الأصل ({asset.AssetCode}) إلى أصل جاهز في الخدمة بنجاح.", jvId);
        }

        public async Task<(bool Success, string Message, int? AssetId)> ConvertPurchaseInvoiceToAssetAsync(int documentHeaderId, int assetCategoryId, int usefulLifeMonths, string userId)
        {
            var doc = await _context.DocumentHeaders
                .Include(d => d.Branch)
                .Include(d => d.Currency)
                .FirstOrDefaultAsync(d => d.Id == documentHeaderId);

            if (doc == null) return (false, "فاتورة الشراء غير موجودة.", null);

            var existingAsset = await _context.FixedAssets.FirstOrDefaultAsync(a => a.PurchaseDocumentHeaderId == documentHeaderId);
            if (existingAsset != null)
            {
                return (false, $"فاتورة الشراء هذه تم تحويلها بالفعل إلى أصل ثابت برقم ({existingAsset.AssetCode}).", existingAsset.Id);
            }

            var category = await _context.AssetCategories.FindAsync(assetCategoryId);
            if (category == null || !category.IsActive)
                return (false, "تصنيف الأصول المحدد غير صالح.", null);

            decimal costLocal = doc.TotalAmountNet * (doc.ExchangeRate > 0 ? doc.ExchangeRate : 1.0m);

            var createDto = new CreateFixedAssetDto
            {
                NameAr = $"أصل من فاتورة مشتريات {doc.DocumentNumber}",
                NameEn = $"Asset from Bill {doc.DocumentNumber}",
                AssetCategoryId = assetCategoryId,
                BranchId = doc.BranchId,
                PurchaseDate = doc.DocumentDate,
                PlacedInServiceDate = doc.DocumentDate,
                DepreciationStartDate = doc.DocumentDate,
                VendorId = doc.PartyType == DocumentPartyType.Vendor ? doc.PartyId : null,
                PurchaseDocumentHeaderId = doc.Id,
                PurchaseInvoiceReference = doc.ReferenceNumber ?? doc.DocumentNumber,
                CurrencyId = doc.CurrencyId,
                PurchasePriceForeign = doc.TotalAmountNet,
                ExchangeRate = doc.ExchangeRate,
                InitialCostLocal = costLocal,
                UsefulLifeMonths = usefulLifeMonths > 0 ? usefulLifeMonths : category.DefaultUsefulLifeMonths,
                DepreciationMethod = category.DefaultDepreciationMethod,
                Status = AssetStatus.InService
            };

            var res = await CreateAssetAsync(createDto, userId);
            return (res.Success, res.Message, res.AssetId);
        }

        public async Task<string> GenerateNextAssetCodeAsync(int branchId, int fiscalYearId, string fyYear)
        {
            string prefix = $"AST-{branchId}-{fyYear}-";

            var existingCodes = await _context.FixedAssets
                .IgnoreQueryFilters()
                .Where(a => a.AssetCode.StartsWith(prefix))
                .Select(a => a.AssetCode)
                .ToListAsync();

            long maxSeq = 0;
            foreach (var code in existingCodes)
            {
                if (code.Length > prefix.Length)
                {
                    var sub = code.Substring(prefix.Length);
                    if (long.TryParse(sub, out long s) && s > maxSeq)
                    {
                        maxSeq = s;
                    }
                }
            }

            long nextSeq = maxSeq + 1;
            string candidate = $"{prefix}{nextSeq:D5}";

            while (await _context.FixedAssets.IgnoreQueryFilters().AnyAsync(a => a.AssetCode == candidate))
            {
                nextSeq++;
                candidate = $"{prefix}{nextSeq:D5}";
            }

            return candidate;
        }

        private static string GetStatusName(AssetStatus status) => status switch
        {
            AssetStatus.New => "جديد",
            AssetStatus.InService => "في الخدمة",
            AssetStatus.UnderConstruction => "تحت الإنشاء (CIP)",
            AssetStatus.Suspended => "متوقف",
            AssetStatus.Disposed => "مستبعد / مباع",
            _ => status.ToString()
        };

        private static string GetMethodName(DepreciationMethod method) => method switch
        {
            DepreciationMethod.StraightLine => "قسط ثابت",
            DepreciationMethod.DecliningBalance => "قسط متناقص",
            DepreciationMethod.UnitsOfProduction => "وحدات إنتاج",
            DepreciationMethod.NoDepreciation => "بدون إهلاك",
            _ => method.ToString()
        };

        private static string GetFirstMonthPolicyName(FirstMonthPolicy policy) => policy switch
        {
            FirstMonthPolicy.FullMonth => "شهر كامل",
            FirstMonthPolicy.ProRataDays => "حسب الأيام الفعلية",
            FirstMonthPolicy.NextMonth => "بداية الشهر التالي",
            _ => policy.ToString()
        };

        private static string GetTransactionTypeName(AssetTransactionType type) => type switch
        {
            AssetTransactionType.Transfer => "نقل موقع / فرع",
            AssetTransactionType.AdditionalCost => "تكلفة إضافية",
            AssetTransactionType.LifeAdjustment => "تعديل عمر إنتاجي",
            AssetTransactionType.StatusChange => "تغيير حالة",
            AssetTransactionType.Disposal => "استبعاد / بيع",
            AssetTransactionType.Capitalization => "تشغيل وتحويل أصل",
            _ => type.ToString()
        };
    }
}
