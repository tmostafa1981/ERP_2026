using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace ERP.Tests.Unit
{
    public class PaymentMethodPermissionTests
    {
        private ErpDbContext CreateDbContext(string dbName)
        {
            var userMock = new Mock<ICurrentUserService>();
            userMock.Setup(u => u.BranchId).Returns(1);
            userMock.Setup(u => u.IsSuperAdmin).Returns(false);
            userMock.Setup(u => u.IsAuthenticated).Returns(true);

            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new ErpDbContext(options, userMock.Object);
        }

        private PaymentMethodPermissionService CreateService(
            ErpDbContext context,
            IMemoryCache cache,
            string currentUserId = "user-1",
            bool hasAllBranches = false)
        {
            var currentUserMock = new Mock<ICurrentUserContext>();
            currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
            currentUserMock.Setup(c => c.UserId).Returns(currentUserId);
            currentUserMock.Setup(c => c.HasAllBranches).Returns(hasAllBranches);

            var auditMock = new Mock<IAuditService>();

            return new PaymentMethodPermissionService(context, cache, currentUserMock.Object, auditMock.Object);
        }

        [Fact]
        public async Task GetPermissionsAsync_UnconfiguredUser_ReturnsAllAllowed()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var service = CreateService(context, cache, "unconfigured-user");

            var result = await service.GetPermissionsAsync("unconfigured-user", VoucherType.Receipt);

            Assert.Equal(4, result.Count);
            Assert.True(result[VoucherPaymentMethod.Cash].CanCreate);
            Assert.True(result[VoucherPaymentMethod.Cheque].CanCreate);
            Assert.True(result[VoucherPaymentMethod.BankTransfer].CanCreate);
            Assert.True(result[VoucherPaymentMethod.Other].CanCreate);
        }

        [Fact]
        public async Task GetPermissionsAsync_SuperAdmin_ReturnsAllAllowedEvenIfConfigured()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);

            // Configure restriction in database
            context.UserPaymentMethodPermissions.Add(new UserPaymentMethodPermission
            {
                UserId = "admin-1",
                PaymentMethod = VoucherPaymentMethod.Cash,
                CanView = true,
                CanCreate = true
            });
            await context.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var service = CreateService(context, cache, currentUserId: "admin-1", hasAllBranches: true);

            var result = await service.GetPermissionsAsync("admin-1", VoucherType.Receipt);

            Assert.True(result[VoucherPaymentMethod.Cash].CanCreate);
            Assert.True(result[VoucherPaymentMethod.Cheque].CanCreate);
            Assert.True(result[VoucherPaymentMethod.BankTransfer].CanCreate);
            Assert.True(result[VoucherPaymentMethod.Other].CanCreate);
        }

        [Fact]
        public async Task GetPermissionsAsync_CashierUser_OnlyAllowedCash()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);

            // Configure Cashier (Cash only)
            context.UserPaymentMethodPermissions.Add(new UserPaymentMethodPermission
            {
                UserId = "cashier-1",
                PaymentMethod = VoucherPaymentMethod.Cash,
                CanView = true,
                CanCreate = true,
                CanEdit = true,
                CanDelete = true,
                CanPost = true,
                CanUnpost = true
            });
            await context.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var service = CreateService(context, cache, "cashier-1");

            var result = await service.GetPermissionsAsync("cashier-1", VoucherType.Receipt);

            // Cash is allowed
            Assert.True(result[VoucherPaymentMethod.Cash].CanCreate);
            Assert.True(result[VoucherPaymentMethod.Cash].CanPost);

            // Cheque and BankTransfer are denied
            Assert.False(result[VoucherPaymentMethod.Cheque].CanCreate);
            Assert.False(result[VoucherPaymentMethod.BankTransfer].CanCreate);
            Assert.False(result[VoucherPaymentMethod.Other].CanCreate);
        }

        [Fact]
        public async Task ValidateVoucherLinesPermissionAsync_CashierCreatingCheque_ReturnsForbidden()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);

            context.UserPaymentMethodPermissions.Add(new UserPaymentMethodPermission
            {
                UserId = "cashier-1",
                PaymentMethod = VoucherPaymentMethod.Cash,
                CanView = true,
                CanCreate = true
            });
            await context.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var service = CreateService(context, cache, "cashier-1");

            // Validate creating Cheque line
            var check = await service.ValidateVoucherLinesPermissionAsync(
                "cashier-1", VoucherType.Receipt, new[] { VoucherPaymentMethod.Cheque }, "create");

            Assert.False(check.Allowed);
            Assert.Contains("غير مصرح", check.ErrorMessage);
        }

        [Fact]
        public async Task ValidateVoucherLinesPermissionAsync_MultiMethodVoucher_CashierPostingMixedVoucher_Blocked()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);

            // Cashier has Cash post, but not Cheque post
            context.UserPaymentMethodPermissions.Add(new UserPaymentMethodPermission
            {
                UserId = "cashier-1",
                PaymentMethod = VoucherPaymentMethod.Cash,
                CanView = true,
                CanCreate = true,
                CanPost = true
            });
            await context.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var service = CreateService(context, cache, "cashier-1");

            // Voucher contains both Cash and Cheque lines
            var lines = new[] { VoucherPaymentMethod.Cash, VoucherPaymentMethod.Cheque };

            var check = await service.ValidateVoucherLinesPermissionAsync(
                "cashier-1", VoucherType.Receipt, lines, "post");

            Assert.False(check.Allowed);
            Assert.Contains("شيك", check.ErrorMessage);
        }

        [Fact]
        public async Task SavePermissionsAsync_UpdatesDatabaseAndInvalidatesCache()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var service = CreateService(context, cache, "user-save");

            var newPerms = new List<PaymentMethodPermissionDto>
            {
                new PaymentMethodPermissionDto
                {
                    PaymentMethod = VoucherPaymentMethod.Cheque,
                    CanView = true,
                    CanCreate = true,
                    CanEdit = true,
                    CanDelete = false,
                    CanPost = true,
                    CanUnpost = false
                }
            };

            await service.SavePermissionsAsync("user-save", VoucherType.Receipt, newPerms);

            var saved = await context.UserPaymentMethodPermissions
                .Where(p => p.UserId == "user-save" && p.VoucherType == VoucherType.Receipt)
                .ToListAsync();

            Assert.Single(saved);
            Assert.Equal(VoucherPaymentMethod.Cheque, saved[0].PaymentMethod);
            Assert.True(saved[0].CanPost);
            Assert.False(saved[0].CanDelete);

            // Check Matrix retrieval
            var matrix = await service.GetUserMatrixAsync("user-save", VoucherType.Receipt);
            Assert.NotNull(matrix);
            Assert.Equal(4, matrix.Permissions.Count);
            var chequePerm = matrix.Permissions.First(p => p.PaymentMethod == VoucherPaymentMethod.Cheque);
            Assert.True(chequePerm.CanCreate);
            Assert.False(chequePerm.CanDelete);
        }

        [Fact]
        public async Task VoucherTypeScope_ReceiptAllowed_PaymentDenied()
        {
            var dbName = Guid.NewGuid().ToString();
            using var context = CreateDbContext(dbName);

            // User allowed BankTransfer on Receipt, but Cash only on Payment
            context.UserPaymentMethodPermissions.AddRange(
                new UserPaymentMethodPermission
                {
                    UserId = "scoped-user",
                    VoucherType = VoucherType.Receipt,
                    PaymentMethod = VoucherPaymentMethod.BankTransfer,
                    CanView = true,
                    CanCreate = true
                },
                new UserPaymentMethodPermission
                {
                    UserId = "scoped-user",
                    VoucherType = VoucherType.Payment,
                    PaymentMethod = VoucherPaymentMethod.Cash,
                    CanView = true,
                    CanCreate = true
                }
            );
            await context.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var service = CreateService(context, cache, "scoped-user");

            // Receipt: BankTransfer is allowed, Cash is denied
            var receiptBank = await service.HasPermissionAsync("scoped-user", VoucherType.Receipt, VoucherPaymentMethod.BankTransfer, "create");
            var receiptCash = await service.HasPermissionAsync("scoped-user", VoucherType.Receipt, VoucherPaymentMethod.Cash, "create");
            Assert.True(receiptBank);
            Assert.False(receiptCash);

            // Payment: Cash is allowed, BankTransfer is denied
            var paymentBank = await service.HasPermissionAsync("scoped-user", VoucherType.Payment, VoucherPaymentMethod.BankTransfer, "create");
            var paymentCash = await service.HasPermissionAsync("scoped-user", VoucherType.Payment, VoucherPaymentMethod.Cash, "create");
            Assert.False(paymentBank);
            Assert.True(paymentCash);
        }
    }
}
