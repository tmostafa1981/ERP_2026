using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs.FixedAssets;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class FixedAssetCategoryService : IFixedAssetCategoryService
    {
        private readonly ErpDbContext _context;
        private readonly IAuditService _audit;

        private const string SC_Code = "FA-CAT";
        private const string SC_Name = "تصنيفات ومجموعات الأصول";
        private const string SC_Module = "FixedAssets";

        public FixedAssetCategoryService(ErpDbContext context, IAuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        public async Task<List<AssetCategoryDto>> GetAllAsync(int? branchId = null)
        {
            var query = _context.AssetCategories
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(c => c.AssetCostAccount)
                .Include(c => c.AccumulatedDepreciationAccount)
                .Include(c => c.DepreciationExpenseAccount)
                .Include(c => c.DisposalGainAccount)
                .Include(c => c.DisposalLossAccount)
                .Include(c => c.CipAccount)
                .Include(c => c.Branch)
                .Include(c => c.Assets)
                .AsQueryable();

            if (branchId.HasValue && branchId.Value > 0)
            {
                query = query.Where(c => c.BranchId == null || c.BranchId == branchId.Value);
            }

            var list = await query.OrderBy(c => c.Code).ToListAsync();

            return list.Select(c => new AssetCategoryDto
            {
                Id = c.Id,
                Code = c.Code,
                NameAr = c.NameAr,
                NameEn = c.NameEn,
                LocalizedName = c.LocalizedName,
                Description = c.Description,
                DefaultUsefulLifeMonths = c.DefaultUsefulLifeMonths,
                DefaultDepreciationMethod = c.DefaultDepreciationMethod,
                DefaultResidualValueRate = c.DefaultResidualValueRate,
                AssetCostAccountId = c.AssetCostAccountId,
                AssetCostAccountCode = c.AssetCostAccount?.Code ?? "",
                AssetCostAccountName = c.AssetCostAccount?.LocalizedName ?? "",
                AccumulatedDepreciationAccountId = c.AccumulatedDepreciationAccountId,
                AccumulatedDepreciationAccountCode = c.AccumulatedDepreciationAccount?.Code ?? "",
                AccumulatedDepreciationAccountName = c.AccumulatedDepreciationAccount?.LocalizedName ?? "",
                DepreciationExpenseAccountId = c.DepreciationExpenseAccountId,
                DepreciationExpenseAccountCode = c.DepreciationExpenseAccount?.Code ?? "",
                DepreciationExpenseAccountName = c.DepreciationExpenseAccount?.LocalizedName ?? "",
                DisposalGainAccountId = c.DisposalGainAccountId,
                DisposalGainAccountCode = c.DisposalGainAccount?.Code ?? "",
                DisposalGainAccountName = c.DisposalGainAccount?.LocalizedName ?? "",
                DisposalLossAccountId = c.DisposalLossAccountId,
                DisposalLossAccountCode = c.DisposalLossAccount?.Code ?? "",
                DisposalLossAccountName = c.DisposalLossAccount?.LocalizedName ?? "",
                CipAccountId = c.CipAccountId,
                CipAccountCode = c.CipAccount?.Code ?? "",
                CipAccountName = c.CipAccount?.LocalizedName ?? "",
                BranchId = c.BranchId,
                BranchName = c.Branch?.LocalizedName,
                IsActive = c.IsActive,
                AssetsCount = c.Assets.Count
            }).ToList();
        }

        public async Task<AssetCategoryDto?> GetByIdAsync(int id)
        {
            var c = await _context.AssetCategories
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.AssetCostAccount)
                .Include(x => x.AccumulatedDepreciationAccount)
                .Include(x => x.DepreciationExpenseAccount)
                .Include(x => x.DisposalGainAccount)
                .Include(x => x.DisposalLossAccount)
                .Include(x => x.CipAccount)
                .Include(x => x.Branch)
                .Include(x => x.Assets)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (c == null) return null;

            return new AssetCategoryDto
            {
                Id = c.Id,
                Code = c.Code,
                NameAr = c.NameAr,
                NameEn = c.NameEn,
                LocalizedName = c.LocalizedName,
                Description = c.Description,
                DefaultUsefulLifeMonths = c.DefaultUsefulLifeMonths,
                DefaultDepreciationMethod = c.DefaultDepreciationMethod,
                DefaultResidualValueRate = c.DefaultResidualValueRate,
                AssetCostAccountId = c.AssetCostAccountId,
                AssetCostAccountCode = c.AssetCostAccount?.Code ?? "",
                AssetCostAccountName = c.AssetCostAccount?.LocalizedName ?? "",
                AccumulatedDepreciationAccountId = c.AccumulatedDepreciationAccountId,
                AccumulatedDepreciationAccountCode = c.AccumulatedDepreciationAccount?.Code ?? "",
                AccumulatedDepreciationAccountName = c.AccumulatedDepreciationAccount?.LocalizedName ?? "",
                DepreciationExpenseAccountId = c.DepreciationExpenseAccountId,
                DepreciationExpenseAccountCode = c.DepreciationExpenseAccount?.Code ?? "",
                DepreciationExpenseAccountName = c.DepreciationExpenseAccount?.LocalizedName ?? "",
                DisposalGainAccountId = c.DisposalGainAccountId,
                DisposalGainAccountCode = c.DisposalGainAccount?.Code ?? "",
                DisposalGainAccountName = c.DisposalGainAccount?.LocalizedName ?? "",
                DisposalLossAccountId = c.DisposalLossAccountId,
                DisposalLossAccountCode = c.DisposalLossAccount?.Code ?? "",
                DisposalLossAccountName = c.DisposalLossAccount?.LocalizedName ?? "",
                CipAccountId = c.CipAccountId,
                CipAccountCode = c.CipAccount?.Code ?? "",
                CipAccountName = c.CipAccount?.LocalizedName ?? "",
                BranchId = c.BranchId,
                BranchName = c.Branch?.LocalizedName,
                IsActive = c.IsActive,
                AssetsCount = c.Assets.Count
            };
        }

        public async Task<(bool Success, string Message, int? CategoryId)> CreateAsync(CreateAssetCategoryDto dto, string userId)
        {
            if (dto == null) return (false, "البيانات المدخلة غير صحيحة.", null);
            if (string.IsNullOrWhiteSpace(dto.Code)) return (false, "كود مجموعة الأصل مطلوب.", null);
            if (string.IsNullOrWhiteSpace(dto.NameAr)) return (false, "اسم مجموعة الأصل بالعربي مطلوب.", null);

            var codeExists = await _context.AssetCategories.IgnoreQueryFilters().AnyAsync(c => c.Code == dto.Code.Trim());
            if (codeExists) return (false, $"كود مجموعة الأصل '{dto.Code}' مسجل بالفعل.", null);

            // Validate Accounts
            var costAcc = await _context.GLAccounts.FindAsync(dto.AssetCostAccountId);
            if (costAcc == null || !costAcc.IsActive || costAcc.IsGroup)
                return (false, "حساب تكلفة الأصل غير صالح أو حساب رئيسي أو غير نشط.", null);

            var accDepAcc = await _context.GLAccounts.FindAsync(dto.AccumulatedDepreciationAccountId);
            if (accDepAcc == null || !accDepAcc.IsActive || accDepAcc.IsGroup)
                return (false, "حساب مجمع الإهلاك غير صالح أو حساب رئيسي أو غير نشط.", null);

            var depExpAcc = await _context.GLAccounts.FindAsync(dto.DepreciationExpenseAccountId);
            if (depExpAcc == null || !depExpAcc.IsActive || depExpAcc.IsGroup)
                return (false, "حساب مصروف الإهلاك غير صالح أو حساب رئيسي أو غير نشط.", null);

            var gainAcc = await _context.GLAccounts.FindAsync(dto.DisposalGainAccountId);
            if (gainAcc == null || !gainAcc.IsActive || gainAcc.IsGroup)
                return (false, "حساب أرباح استبعاد الأصول غير صالح أو حساب رئيسي أو غير نشط.", null);

            var lossAcc = await _context.GLAccounts.FindAsync(dto.DisposalLossAccountId);
            if (lossAcc == null || !lossAcc.IsActive || lossAcc.IsGroup)
                return (false, "حساب خسائر استبعاد الأصول غير صالح أو حساب رئيسي أو غير نشط.", null);

            var category = new AssetCategory
            {
                Code = dto.Code.Trim(),
                NameAr = dto.NameAr.Trim(),
                NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? dto.NameAr.Trim() : dto.NameEn.Trim(),
                Description = dto.Description?.Trim(),
                DefaultUsefulLifeMonths = dto.DefaultUsefulLifeMonths > 0 ? dto.DefaultUsefulLifeMonths : 60,
                DefaultDepreciationMethod = dto.DefaultDepreciationMethod,
                DefaultResidualValueRate = Math.Max(0m, dto.DefaultResidualValueRate),
                AssetCostAccountId = dto.AssetCostAccountId,
                AccumulatedDepreciationAccountId = dto.AccumulatedDepreciationAccountId,
                DepreciationExpenseAccountId = dto.DepreciationExpenseAccountId,
                DisposalGainAccountId = dto.DisposalGainAccountId,
                DisposalLossAccountId = dto.DisposalLossAccountId,
                CipAccountId = dto.CipAccountId > 0 ? dto.CipAccountId : null,
                BranchId = dto.BranchId > 0 ? dto.BranchId : null,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.AssetCategories.Add(category);
            await _context.SaveChangesAsync();

            await _audit.LogCreateAsync(SC_Code, SC_Name, SC_Module,
                entityName: "AssetCategory",
                entityId: category.Id.ToString(),
                newValues: new { category.Id, category.Code, category.NameAr, category.DefaultUsefulLifeMonths });

            return (true, "تم إضافة مجموعة الأصول بنجاح.", category.Id);
        }

        public async Task<(bool Success, string Message)> UpdateAsync(int id, CreateAssetCategoryDto dto, string userId)
        {
            var category = await _context.AssetCategories.FirstOrDefaultAsync(c => c.Id == id);
            if (category == null) return (false, "مجموعة الأصل غير موجودة.");

            var codeExists = await _context.AssetCategories.IgnoreQueryFilters().AnyAsync(c => c.Code == dto.Code.Trim() && c.Id != id);
            if (codeExists) return (false, $"كود مجموعة الأصل '{dto.Code}' مسجل بالفعل لمجموعة أخرى.");

            category.Code = dto.Code.Trim();
            category.NameAr = dto.NameAr.Trim();
            category.NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? dto.NameAr.Trim() : dto.NameEn.Trim();
            category.Description = dto.Description?.Trim();
            category.DefaultUsefulLifeMonths = dto.DefaultUsefulLifeMonths > 0 ? dto.DefaultUsefulLifeMonths : 60;
            category.DefaultDepreciationMethod = dto.DefaultDepreciationMethod;
            category.DefaultResidualValueRate = Math.Max(0m, dto.DefaultResidualValueRate);
            category.AssetCostAccountId = dto.AssetCostAccountId;
            category.AccumulatedDepreciationAccountId = dto.AccumulatedDepreciationAccountId;
            category.DepreciationExpenseAccountId = dto.DepreciationExpenseAccountId;
            category.DisposalGainAccountId = dto.DisposalGainAccountId;
            category.DisposalLossAccountId = dto.DisposalLossAccountId;
            category.CipAccountId = dto.CipAccountId > 0 ? dto.CipAccountId : null;
            category.BranchId = dto.BranchId > 0 ? dto.BranchId : null;
            category.IsActive = dto.IsActive;
            category.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _audit.LogAsync(new ERP.Application.Models.AuditLogEntry
            {
                ScreenCode = SC_Code,
                ScreenName = SC_Name,
                ModuleName = SC_Module,
                Action = ERP.Domain.Enums.AuditAction.Update,
                EntityName = "AssetCategory",
                EntityId = category.Id.ToString(),
                Description = $"تعديل تصنيف الأصول {category.Code}"
            });

            return (true, "تم تعديل مجموعة الأصول بنجاح.");
        }

        public async Task<(bool Success, string Message)> DeleteAsync(int id, string userId)
        {
            var category = await _context.AssetCategories
                .Include(c => c.Assets)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null) return (false, "مجموعة الأصل غير موجودة.");

            if (category.Assets.Any())
            {
                return (false, $"لا يمكن حذف مجموعة الأصل ({category.LocalizedName}) لوجود {category.Assets.Count} أصل مرتبط بها. يمكنك إيقاف تفعيلها بدلاً من الحذف.");
            }

            _context.AssetCategories.Remove(category);
            await _context.SaveChangesAsync();

            await _audit.LogDeleteAsync(SC_Code, SC_Name, SC_Module,
                entityName: "AssetCategory",
                entityId: id.ToString(),
                oldValues: new { category.Id, category.Code, category.NameAr });

            return (true, "تم حذف مجموعة الأصول بنجاح.");
        }
    }
}
