using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ERP.Infrastructure.Services
{
    public class PaymentMethodPermissionService : IPaymentMethodPermissionService
    {
        private readonly ErpDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;

        private const string CachePrefix = "pm_perm_";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

        public PaymentMethodPermissionService(
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

        public async Task<Dictionary<VoucherPaymentMethod, PaymentMethodPermissionDto>> GetPermissionsAsync(string userId, VoucherType? voucherType = null)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return GetAllAllowed();
            }

            // SuperAdmin bypass
            if (_currentUser.IsAuthenticated && _currentUser.UserId == userId && _currentUser.HasAllBranches)
            {
                return GetAllAllowed();
            }

            string cacheKey = $"{CachePrefix}{userId}_{(voucherType.HasValue ? (int)voucherType.Value : 0)}";
            if (_cache.TryGetValue(cacheKey, out Dictionary<VoucherPaymentMethod, PaymentMethodPermissionDto>? cached) && cached != null)
            {
                return cached;
            }

            // Check if user has ANY records in UserPaymentMethodPermissions
            bool hasAnyConfigured = await _context.UserPaymentMethodPermissions
                .AsNoTracking()
                .AnyAsync(p => p.UserId == userId);

            // Backward compatibility: If user has no entries configured yet, grant full access
            if (!hasAnyConfigured)
            {
                var defaultAll = GetAllAllowed();
                _cache.Set(cacheKey, defaultAll, CacheDuration);
                return defaultAll;
            }

            // Query configured permissions for this user (both specific voucherType and universal null)
            var records = await _context.UserPaymentMethodPermissions
                .AsNoTracking()
                .Where(p => p.UserId == userId && (p.VoucherType == null || p.VoucherType == voucherType))
                .ToListAsync();

            var result = new Dictionary<VoucherPaymentMethod, PaymentMethodPermissionDto>();
            foreach (VoucherPaymentMethod method in Enum.GetValues<VoucherPaymentMethod>())
            {
                // Find matching record (specific voucherType takes precedence over null)
                var rec = records.FirstOrDefault(r => r.PaymentMethod == method && r.VoucherType == voucherType)
                       ?? records.FirstOrDefault(r => r.PaymentMethod == method && r.VoucherType == null);

                if (rec != null)
                {
                    result[method] = new PaymentMethodPermissionDto
                    {
                        PaymentMethod = method,
                        CanView = rec.CanView,
                        CanCreate = rec.CanCreate,
                        CanEdit = rec.CanEdit,
                        CanDelete = rec.CanDelete,
                        CanPost = rec.CanPost,
                        CanUnpost = rec.CanUnpost
                    };
                }
                else
                {
                    // If user is configured in table but this method was not granted, it is denied
                    result[method] = new PaymentMethodPermissionDto
                    {
                        PaymentMethod = method,
                        CanView = false,
                        CanCreate = false,
                        CanEdit = false,
                        CanDelete = false,
                        CanPost = false,
                        CanUnpost = false
                    };
                }
            }

            _cache.Set(cacheKey, result, CacheDuration);
            return result;
        }

        public async Task<UserPaymentMethodMatrixDto> GetUserMatrixAsync(string userId, VoucherType? voucherType = null)
        {
            var permissionsDict = await GetPermissionsAsync(userId, voucherType);
            return new UserPaymentMethodMatrixDto
            {
                UserId = userId,
                VoucherType = voucherType,
                Permissions = permissionsDict.Values.ToList()
            };
        }

        public async Task<bool> HasPermissionAsync(string userId, VoucherType voucherType, VoucherPaymentMethod method, string action)
        {
            var perms = await GetPermissionsAsync(userId, voucherType);
            if (!perms.TryGetValue(method, out var p) || p == null)
                return false;

            return action.ToLowerInvariant() switch
            {
                "view" => p.CanView,
                "create" or "add" => p.CanCreate,
                "edit" or "update" => p.CanEdit,
                "delete" or "remove" => p.CanDelete,
                "post" => p.CanPost,
                "unpost" => p.CanUnpost,
                _ => false
            };
        }

        public async Task<(bool Allowed, string? ErrorMessage)> ValidateVoucherLinesPermissionAsync(
            string userId, VoucherType voucherType, IEnumerable<VoucherPaymentMethod> methods, string action)
        {
            var distinctMethods = methods.Distinct().ToList();
            if (!distinctMethods.Any())
                return (true, null);

            var perms = await GetPermissionsAsync(userId, voucherType);
            foreach (var method in distinctMethods)
            {
                if (!perms.TryGetValue(method, out var p) || p == null)
                {
                    return (false, $"غير مصرح لك بالتعامل مع وسيلة الدفع ({GetMethodNameArabic(method)}).");
                }

                bool allowed = action.ToLowerInvariant() switch
                {
                    "view" => p.CanView,
                    "create" or "add" => p.CanCreate,
                    "edit" or "update" => p.CanEdit,
                    "delete" or "remove" => p.CanDelete,
                    "post" => p.CanPost,
                    "unpost" => p.CanUnpost,
                    _ => false
                };

                if (!allowed)
                {
                    string actionAr = action.ToLowerInvariant() switch
                    {
                        "create" or "add" => "إنشاء",
                        "edit" or "update" => "تعديل",
                        "delete" or "remove" => "حذف",
                        "post" => "ترحيل",
                        "unpost" => "إلغاء ترحيل",
                        _ => "عرض"
                    };

                    return (false, $"غير مصرح لك بإجراء ({actionAr}) لسند يحتوي على وسيلة الدفع ({GetMethodNameArabic(method)}).");
                }
            }

            return (true, null);
        }

        public async Task SavePermissionsAsync(string userId, VoucherType? voucherType, List<PaymentMethodPermissionDto> permissions)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("UserId is required", nameof(userId));

            var existing = await _context.UserPaymentMethodPermissions
                .Where(p => p.UserId == userId && p.VoucherType == voucherType)
                .ToListAsync();

            var oldValues = existing.Select(e => new { e.Id, e.PaymentMethod, e.CanView, e.CanCreate, e.CanEdit, e.CanDelete, e.CanPost, e.CanUnpost }).ToList();

            _context.UserPaymentMethodPermissions.RemoveRange(existing);

            foreach (var p in permissions)
            {
                _context.UserPaymentMethodPermissions.Add(new UserPaymentMethodPermission
                {
                    UserId = userId,
                    VoucherType = voucherType,
                    PaymentMethod = p.PaymentMethod,
                    CanView = p.CanView,
                    CanCreate = p.CanCreate,
                    CanEdit = p.CanEdit,
                    CanDelete = p.CanDelete,
                    CanPost = p.CanPost,
                    CanUnpost = p.CanUnpost,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            InvalidateCache(userId);

            await _audit.LogUpdateAsync(
                screenCode: "USER_PM_PERMS",
                screenName: "Payment Method Permissions",
                moduleName: "Administration",
                entityName: nameof(UserPaymentMethodPermission),
                entityId: userId,
                oldValues: oldValues,
                newValues: permissions);
        }

        public void InvalidateCache(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            _cache.Remove($"{CachePrefix}{userId}_0");
            _cache.Remove($"{CachePrefix}{userId}_1");
            _cache.Remove($"{CachePrefix}{userId}_2");
        }

        private static Dictionary<VoucherPaymentMethod, PaymentMethodPermissionDto> GetAllAllowed()
        {
            var dict = new Dictionary<VoucherPaymentMethod, PaymentMethodPermissionDto>();
            foreach (VoucherPaymentMethod method in Enum.GetValues<VoucherPaymentMethod>())
            {
                dict[method] = new PaymentMethodPermissionDto
                {
                    PaymentMethod = method,
                    CanView = true,
                    CanCreate = true,
                    CanEdit = true,
                    CanDelete = true,
                    CanPost = true,
                    CanUnpost = true
                };
            }
            return dict;
        }

        private static string GetMethodNameArabic(VoucherPaymentMethod method)
        {
            return method switch
            {
                VoucherPaymentMethod.Cash => "نقداً (Cash)",
                VoucherPaymentMethod.Cheque => "شيك (Cheque)",
                VoucherPaymentMethod.BankTransfer => "تحويل بنكي (Bank Transfer)",
                VoucherPaymentMethod.Other => "أخرى (Other)",
                _ => method.ToString()
            };
        }
    }
}
