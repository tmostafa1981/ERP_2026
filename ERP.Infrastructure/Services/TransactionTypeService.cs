using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Application.Models;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ERP.Infrastructure.Services
{
    public class TransactionTypeService : ITransactionTypeService
    {
        private readonly ErpDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string CachePrefix = "tt_types_";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

        public TransactionTypeService(
            ErpDbContext context,
            IMemoryCache cache,
            ICurrentUserContext currentUser,
            IAuditService audit)
        {
            _context = context;
            _cache = cache;
            _currentUser = currentUser;
            _audit = audit;
        }

        public async Task<List<TransactionTypeDto>> GetAllowedTypesAsync(string userId, int branchId, TransactionModule? module = null)
        {
            var query = _context.TransactionTypes
                .AsNoTracking()
                .Include(t => t.DefaultWarehouse)
                .Include(t => t.Branch)
                .Include(t => t.DefaultCustomer)
                .Include(t => t.DefaultVendor)
                .Include(t => t.DefaultCashBankAccount)
                .Include(t => t.DefaultDebitAccount)
                .Include(t => t.DefaultCreditAccount)
                .Include(t => t.DefaultTaxAccount)
                .Include(t => t.DefaultDiscountAccount)
                .Include(t => t.DefaultCogsAccount)
                .Include(t => t.DefaultInventoryAccount)
                .Where(t => t.IsActive && (t.BranchId == null || t.BranchId == branchId));

            if (module.HasValue)
            {
                query = query.Where(t => t.Module == module.Value);
            }

            var allTypes = await query.OrderBy(t => t.Code).ToListAsync();

            // SuperAdmin bypass
            if (_currentUser.IsAuthenticated && _currentUser.UserId == userId && _currentUser.HasAllBranches)
            {
                return allTypes.Select(t => MapToDto(t, canAll: true)).ToList();
            }

            // User specific permissions
            var userPerms = await _context.UserTransactionTypePermissions
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .ToDictionaryAsync(p => p.TransactionTypeId);

            var result = new List<TransactionTypeDto>();
            foreach (var tt in allTypes)
            {
                // If user has no record configured in table at all, default to full access
                if (!userPerms.Any())
                {
                    result.Add(MapToDto(tt, canAll: true));
                }
                else if (userPerms.TryGetValue(tt.Id, out var perm) && perm.CanView)
                {
                    result.Add(MapToDto(tt, perm));
                }
            }

            return result;
        }

        public async Task<TransactionTypeDto?> GetByIdAsync(int id, string userId)
        {
            var entity = await _context.TransactionTypes
                .AsNoTracking()
                .Include(t => t.DefaultWarehouse)
                .Include(t => t.Branch)
                .Include(t => t.DefaultCustomer)
                .Include(t => t.DefaultVendor)
                .Include(t => t.DefaultCashBankAccount)
                .Include(t => t.DefaultDebitAccount)
                .Include(t => t.DefaultCreditAccount)
                .Include(t => t.DefaultTaxAccount)
                .Include(t => t.DefaultDiscountAccount)
                .Include(t => t.DefaultCogsAccount)
                .Include(t => t.DefaultInventoryAccount)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (entity == null) return null;

            if (_currentUser.IsAuthenticated && _currentUser.UserId == userId && _currentUser.HasAllBranches)
            {
                return MapToDto(entity, canAll: true);
            }

            var perm = await _context.UserTransactionTypePermissions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId && p.TransactionTypeId == id);

            return perm != null ? MapToDto(entity, perm) : MapToDto(entity, canAll: true);
        }

        public async Task<TransactionTypeDto?> GetByCodeAsync(string code, string userId)
        {
            var entity = await _context.TransactionTypes
                .AsNoTracking()
                .Include(t => t.DefaultWarehouse)
                .Include(t => t.Branch)
                .FirstOrDefaultAsync(t => t.Code == code);

            if (entity == null) return null;
            return await GetByIdAsync(entity.Id, userId);
        }

        public async Task<bool> HasPermissionAsync(string userId, int transactionTypeId, string action)
        {
            if (_currentUser.IsAuthenticated && _currentUser.UserId == userId && _currentUser.HasAllBranches)
            {
                return true;
            }

            var perm = await _context.UserTransactionTypePermissions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId && p.TransactionTypeId == transactionTypeId);

            // Default allow if user has no entries in permissions table
            if (perm == null)
            {
                bool hasAny = await _context.UserTransactionTypePermissions.AnyAsync(p => p.UserId == userId);
                return !hasAny;
            }

            return action.ToLowerInvariant() switch
            {
                "view" => perm.CanView,
                "create" or "add" => perm.CanCreate,
                "edit" or "update" => perm.CanEdit,
                "delete" or "remove" => perm.CanDelete,
                "post" => perm.CanPost,
                "unpost" => perm.CanUnpost,
                "convert" => perm.CanConvert,
                _ => false
            };
        }

        public async Task<List<TransactionTypeDto>> GetAllConfiguredTypesAsync(TransactionModule? module = null)
        {
            var query = _context.TransactionTypes
                .AsNoTracking()
                .Include(t => t.DefaultWarehouse)
                .Include(t => t.Branch)
                .Include(t => t.DefaultCustomer)
                .Include(t => t.DefaultVendor)
                .Include(t => t.DefaultCashBankAccount)
                .Include(t => t.DefaultDebitAccount)
                .Include(t => t.DefaultCreditAccount)
                .Include(t => t.DefaultTaxAccount)
                .Include(t => t.DefaultDiscountAccount)
                .Include(t => t.DefaultCogsAccount)
                .Include(t => t.DefaultInventoryAccount)
                .AsQueryable();

            if (module.HasValue)
            {
                query = query.Where(t => t.Module == module.Value);
            }

            var list = await query.OrderBy(t => t.Module).ThenBy(t => t.Code).ToListAsync();
            return list.Select(t => MapToDto(t, canAll: true)).ToList();
        }

        public async Task<UserTransactionTypePermissionMatrixDto> GetUserPermissionMatrixAsync(string userId, TransactionModule? module = null)
        {
            var allTypes = await GetAllConfiguredTypesAsync(module);
            var perms = await _context.UserTransactionTypePermissions
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .ToDictionaryAsync(p => p.TransactionTypeId);

            var user = await _context.Users.FindAsync(userId);

            var result = new UserTransactionTypePermissionMatrixDto
            {
                UserId = userId,
                UserName = user?.UserName ?? userId,
                Module = module,
                Permissions = allTypes.Select(tt =>
                {
                    if (perms.TryGetValue(tt.Id, out var p))
                    {
                        tt.CanView = p.CanView;
                        tt.CanCreate = p.CanCreate;
                        tt.CanEdit = p.CanEdit;
                        tt.CanDelete = p.CanDelete;
                        tt.CanPost = p.CanPost;
                        tt.CanUnpost = p.CanUnpost;
                        tt.CanConvert = p.CanConvert;
                    }
                    else
                    {
                        tt.CanView = false;
                        tt.CanCreate = false;
                        tt.CanEdit = false;
                        tt.CanDelete = false;
                        tt.CanPost = false;
                        tt.CanUnpost = false;
                        tt.CanConvert = false;
                    }
                    return tt;
                }).ToList()
            };

            return result;
        }

        public async Task SaveUserPermissionsAsync(string userId, List<TransactionTypeDto> permissions)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("UserId is required", nameof(userId));

            var existing = await _context.UserTransactionTypePermissions
                .Where(p => p.UserId == userId)
                .ToListAsync();

            _context.UserTransactionTypePermissions.RemoveRange(existing);

            foreach (var p in permissions)
            {
                _context.UserTransactionTypePermissions.Add(new UserTransactionTypePermission
                {
                    UserId = userId,
                    TransactionTypeId = p.Id,
                    CanView = p.CanView,
                    CanCreate = p.CanCreate,
                    CanEdit = p.CanEdit,
                    CanDelete = p.CanDelete,
                    CanPost = p.CanPost,
                    CanUnpost = p.CanUnpost,
                    CanConvert = p.CanConvert,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(
                screenCode: "SEC_TT_PERM",
                screenName: "صلاحيات أنواع الحركات",
                moduleName: "إدارة النظام",
                entityName: "UserTransactionTypePermission",
                entityId: userId,
                oldValues: existing.Select(e => new { e.TransactionTypeId, e.CanView, e.CanCreate, e.CanEdit, e.CanDelete, e.CanPost, e.CanUnpost, e.CanConvert }).ToList(),
                newValues: permissions.Select(p => new { p.Id, p.CanView, p.CanCreate, p.CanEdit, p.CanDelete, p.CanPost, p.CanUnpost, p.CanConvert }).ToList(),
                description: $"تحديث مصفوفة صلاحيات أنواع الحركات للمستخدم ({userId})"
            );
        }

        public async Task<TransactionType> CreateTransactionTypeAsync(TransactionType entity)
        {
            _context.TransactionTypes.Add(entity);
            await _context.SaveChangesAsync();

            await _audit.LogCreateAsync(
                screenCode: "SEC_TT",
                screenName: "أنواع المستندات",
                moduleName: "الإعدادات",
                entityName: "TransactionType",
                entityId: entity.Id.ToString(),
                newValues: new { entity.Code, entity.NameAr, entity.Module, entity.BaseNature },
                description: $"إضافة نوع مستند جديد ({entity.LocalizedName})"
            );

            return entity;
        }

        public async Task<TransactionType> UpdateTransactionTypeAsync(TransactionType entity)
        {
            _context.TransactionTypes.Update(entity);
            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(
                screenCode: "SEC_TT",
                screenName: "أنواع المستندات",
                moduleName: "الإعدادات",
                entityName: "TransactionType",
                entityId: entity.Id.ToString(),
                oldValues: new { entity.Id, entity.Code },
                newValues: new { entity.Code, entity.NameAr, entity.Module, entity.BaseNature, entity.IsActive },
                description: $"تعديل بيانات نوع المستند ({entity.LocalizedName})"
            );

            return entity;
        }

        private static TransactionTypeDto MapToDto(TransactionType t, bool canAll = false)
        {
            return new TransactionTypeDto
            {
                Id = t.Id,
                Code = t.Code,
                NameAr = t.NameAr,
                NameEn = t.NameEn,
                LocalizedName = t.LocalizedName,
                Module = t.Module,
                BaseNature = t.BaseNature,
                InventoryEffect = t.InventoryEffect,
                AccountingEffect = t.AccountingEffect,
                IsCashAllowed = t.IsCashAllowed,
                IsCreditAllowed = t.IsCreditAllowed,
                AutoPost = t.AutoPost,
                ShowLineNotes = t.ShowLineNotes,
                CodeSequencePrefix = t.CodeSequencePrefix,
                DefaultWarehouseId = t.DefaultWarehouseId,
                DefaultWarehouseName = t.DefaultWarehouse?.LocalizedName,
                BranchId = t.BranchId,
                BranchName = t.Branch?.LocalizedName,
                SerialTrackingPolicy = t.SerialTrackingPolicy,
                DefaultDebitAccountId = t.DefaultDebitAccountId,
                DefaultDebitAccountName = t.DefaultDebitAccount?.LocalizedName,
                DefaultCreditAccountId = t.DefaultCreditAccountId,
                DefaultCreditAccountName = t.DefaultCreditAccount?.LocalizedName,
                DefaultTaxAccountId = t.DefaultTaxAccountId,
                DefaultTaxAccountName = t.DefaultTaxAccount?.LocalizedName,
                DefaultDiscountAccountId = t.DefaultDiscountAccountId,
                DefaultDiscountAccountName = t.DefaultDiscountAccount?.LocalizedName,
                DefaultCogsAccountId = t.DefaultCogsAccountId,
                DefaultCogsAccountName = t.DefaultCogsAccount?.LocalizedName,
                DefaultInventoryAccountId = t.DefaultInventoryAccountId,
                DefaultInventoryAccountName = t.DefaultInventoryAccount?.LocalizedName,
                DefaultCustomerId = t.DefaultCustomerId,
                DefaultCustomerName = t.DefaultCustomer?.LocalizedName,
                DefaultVendorId = t.DefaultVendorId,
                DefaultVendorName = t.DefaultVendor?.LocalizedName,
                DefaultCashBankAccountId = t.DefaultCashBankAccountId,
                DefaultCashBankAccountName = t.DefaultCashBankAccount?.LocalizedName,
                IsActive = t.IsActive,
                CanView = canAll,
                CanCreate = canAll,
                CanEdit = canAll,
                CanDelete = canAll,
                CanPost = canAll,
                CanUnpost = canAll,
                CanConvert = canAll
            };
        }

        private static TransactionTypeDto MapToDto(TransactionType t, UserTransactionTypePermission p)
        {
            var dto = MapToDto(t, false);
            dto.CanView = p.CanView;
            dto.CanCreate = p.CanCreate;
            dto.CanEdit = p.CanEdit;
            dto.CanDelete = p.CanDelete;
            dto.CanPost = p.CanPost;
            dto.CanUnpost = p.CanUnpost;
            dto.CanConvert = p.CanConvert;
            return dto;
        }
    }
}
