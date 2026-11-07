using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs.FixedAssets;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class FixedAssetReportService : IFixedAssetReportService
    {
        private readonly ErpDbContext _context;

        public FixedAssetReportService(ErpDbContext context)
        {
            _context = context;
        }

        public async Task<List<AssetRegisterReportItemDto>> GetAssetRegisterReportAsync(AssetReportFilterDto filter, string userId)
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

            if (filter.CostCenterId.HasValue && filter.CostCenterId.Value > 0)
                query = query.Where(a => a.CostCenterId == filter.CostCenterId.Value);

            if (filter.Status.HasValue)
                query = query.Where(a => a.Status == filter.Status.Value);

            if (filter.AsOfDate.HasValue)
                query = query.Where(a => a.PurchaseDate <= filter.AsOfDate.Value.Date);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(a => a.AssetCode.ToLower().Contains(term) ||
                                         a.NameAr.ToLower().Contains(term) ||
                                         a.NameEn.ToLower().Contains(term) ||
                                         (a.SerialNumber != null && a.SerialNumber.ToLower().Contains(term)));
            }

            var list = await query.OrderBy(a => a.AssetCode).ToListAsync();

            return list.Select(a => new AssetRegisterReportItemDto
            {
                AssetCode = a.AssetCode,
                AssetName = a.LocalizedName,
                CategoryName = a.AssetCategory?.LocalizedName ?? "",
                BranchName = a.Branch?.LocalizedName ?? "",
                CostCenterName = a.CostCenter?.LocalizedName,
                Custodian = a.CustodianEmployeeName,
                StatusName = a.Status.ToString(),
                PurchaseDate = a.PurchaseDate,
                InServiceDate = a.PlacedInServiceDate,
                InitialCost = a.InitialCostLocal,
                AdditionalCost = a.AdditionalExpensesLocal,
                CurrentCost = a.CurrentCostLocal,
                ResidualValue = a.ResidualValue,
                AccumulatedDepreciation = a.CurrentAccumulatedDepreciation,
                BookValue = a.CurrentBookValue,
                DepreciationMethod = a.DepreciationMethod.ToString(),
                UsefulLifeMonths = a.UsefulLifeMonths
            }).ToList();
        }

        public async Task<List<AssetDepreciationHistoryItemDto>> GetDepreciationHistoryReportAsync(AssetReportFilterDto filter, string userId)
        {
            var query = _context.AssetDepreciationHistories
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(h => h.Asset)
                .Include(h => h.Branch)
                .Include(h => h.CostCenter)
                .Include(h => h.ExpenseAccount)
                .Include(h => h.AccumulatedAccount)
                .Include(h => h.AssetDepreciationRun)
                .AsQueryable();

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
                query = query.Where(h => h.BranchId == filter.BranchId.Value);

            if (filter.SpecificAssetId.HasValue && filter.SpecificAssetId.Value > 0)
                query = query.Where(h => h.AssetId == filter.SpecificAssetId.Value);

            if (filter.FromDate.HasValue)
                query = query.Where(h => h.DepreciationDate >= filter.FromDate.Value.Date);

            if (filter.ToDate.HasValue)
                query = query.Where(h => h.DepreciationDate <= filter.ToDate.Value.Date);

            var list = await query.OrderByDescending(h => h.DepreciationDate).ThenBy(h => h.Id).ToListAsync();

            return list.Select(h => new AssetDepreciationHistoryItemDto
            {
                Id = h.Id,
                AssetDepreciationRunId = h.AssetDepreciationRunId,
                RunNumber = h.AssetDepreciationRun?.RunNumber ?? "",
                AssetId = h.AssetId,
                AssetCode = h.Asset?.AssetCode ?? "",
                AssetName = h.Asset?.LocalizedName ?? "",
                DepreciationDate = h.DepreciationDate,
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
            }).ToList();
        }

        public async Task<List<AssetTransactionItemDto>> GetAssetMovementReportAsync(AssetReportFilterDto filter, string userId)
        {
            var query = _context.AssetTransactions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(t => t.Asset)
                .Include(t => t.FromBranch)
                .Include(t => t.ToBranch)
                .Include(t => t.FromCostCenter)
                .Include(t => t.ToCostCenter)
                .Include(t => t.JournalEntry)
                .AsQueryable();

            if (filter.SpecificAssetId.HasValue && filter.SpecificAssetId.Value > 0)
                query = query.Where(t => t.AssetId == filter.SpecificAssetId.Value);

            if (filter.FromDate.HasValue)
                query = query.Where(t => t.TransactionDate >= filter.FromDate.Value.Date);

            if (filter.ToDate.HasValue)
                query = query.Where(t => t.TransactionDate <= filter.ToDate.Value.Date);

            var list = await query.OrderByDescending(t => t.TransactionDate).ThenBy(t => t.Id).ToListAsync();

            return list.Select(t => new AssetTransactionItemDto
            {
                Id = t.Id,
                AssetId = t.AssetId,
                TransactionType = t.TransactionType,
                TransactionTypeName = t.TransactionType.ToString(),
                TransactionDate = t.TransactionDate,
                FromBranchName = t.FromBranch?.LocalizedName,
                ToBranchName = t.ToBranch?.LocalizedName,
                FromCostCenterName = t.FromCostCenter?.LocalizedName,
                ToCostCenterName = t.ToCostCenter?.LocalizedName,
                FromCustodian = t.FromCustodian,
                ToCustodian = t.ToCustodian,
                Amount = t.Amount,
                IsCapitalized = t.IsCapitalized,
                JournalEntryId = t.JournalEntryId,
                JournalEntryNumber = t.JournalEntry?.EntryNumber,
                Reason = t.Reason,
                CreatedAt = t.CreatedAt,
                CreatedByUserName = t.CreatedByUserId
            }).ToList();
        }

        public async Task<List<AssetRegisterReportItemDto>> GetDisposedAssetsReportAsync(AssetReportFilterDto filter, string userId)
        {
            filter.Status = AssetStatus.Disposed;
            return await GetAssetRegisterReportAsync(filter, userId);
        }

        public async Task<List<AssetRegisterReportItemDto>> GetCipAssetsReportAsync(AssetReportFilterDto filter, string userId)
        {
            filter.Status = AssetStatus.UnderConstruction;
            return await GetAssetRegisterReportAsync(filter, userId);
        }

        public async Task<List<AssetRegisterReportItemDto>> GetUnapprovedOrPendingDepreciationReportAsync(AssetReportFilterDto filter, string userId)
        {
            // Assets in service whose last depreciation date is older than 1 month or null
            var query = _context.FixedAssets
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(a => a.AssetCategory)
                .Include(a => a.Branch)
                .Include(a => a.CostCenter)
                .Where(a => a.Status == AssetStatus.InService && !a.IsFullyDepreciated && a.DepreciationMethod != DepreciationMethod.NoDepreciation)
                .AsQueryable();

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
                query = query.Where(a => a.BranchId == filter.BranchId.Value);

            var list = await query.OrderBy(a => a.LastDepreciationDate).ToListAsync();

            return list.Select(a => new AssetRegisterReportItemDto
            {
                AssetCode = a.AssetCode,
                AssetName = a.LocalizedName,
                CategoryName = a.AssetCategory?.LocalizedName ?? "",
                BranchName = a.Branch?.LocalizedName ?? "",
                CostCenterName = a.CostCenter?.LocalizedName,
                Custodian = a.CustodianEmployeeName,
                StatusName = a.Status.ToString(),
                PurchaseDate = a.PurchaseDate,
                InServiceDate = a.PlacedInServiceDate,
                InitialCost = a.InitialCostLocal,
                AdditionalCost = a.AdditionalExpensesLocal,
                CurrentCost = a.CurrentCostLocal,
                ResidualValue = a.ResidualValue,
                AccumulatedDepreciation = a.CurrentAccumulatedDepreciation,
                BookValue = a.CurrentBookValue,
                DepreciationMethod = a.DepreciationMethod.ToString(),
                UsefulLifeMonths = a.UsefulLifeMonths
            }).ToList();
        }
    }
}
