using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Application.Models;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ERP.Tests.Unit
{
    /// <summary>
    /// Unit tests for the central AuditService.
    /// Uses EF Core InMemory database for storage assertions.
    /// </summary>
    public class AuditServiceTests : IAsyncLifetime
    {
        private ErpDbContext _db = null!;
        private IServiceProvider _serviceProvider = null!;
        private Mock<ICurrentUserContext> _userContextMock = null!;
        private Mock<IHttpContextAccessor> _httpContextMock = null!;
        private AuditService _sut = null!;

        // ─── Setup ────────────────────────────────────────────────────────────────

        public async Task InitializeAsync()
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new ErpDbContext(options);

            // Mock ICurrentUserContext
            _userContextMock = new Mock<ICurrentUserContext>();
            _userContextMock.Setup(x => x.UserId).Returns("user-001");
            _userContextMock.Setup(x => x.Username).Returns("admin");
            _userContextMock.Setup(x => x.EmployeeCode).Returns("105");
            _userContextMock.Setup(x => x.CompanyId).Returns(1);
            _userContextMock.Setup(x => x.BranchId).Returns(10);
            _userContextMock.Setup(x => x.FiscalYearId).Returns(2026);

            // Mock IHttpContextAccessor
            _httpContextMock = new Mock<IHttpContextAccessor>();
            _httpContextMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

            // ServiceProvider for AuditService (uses separate scope)
            var services = new ServiceCollection();
            services.AddDbContext<ErpDbContext>(o => o.UseInMemoryDatabase(
                _db.Database.GetConnectionString() ?? Guid.NewGuid().ToString()));
            services.AddScoped<ICurrentUserContext>(_ => _userContextMock.Object);
            services.AddScoped<IHttpContextAccessor>(_ => _httpContextMock.Object);

            // Use a service provider that returns the SAME db instance
            var sp = new ServiceCollection();
            sp.AddSingleton(_db);

            _serviceProvider = BuildServiceProvider(_db);

            _sut = new AuditService(
                _serviceProvider,
                _userContextMock.Object,
                _httpContextMock.Object,
                NullLogger<AuditService>.Instance);

            await Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _db.DisposeAsync();
        }

        private static IServiceProvider BuildServiceProvider(ErpDbContext db)
        {
            var services = new ServiceCollection();
            services.AddSingleton(db);
            return services.BuildServiceProvider();
        }

        // ─── Test 1: Create action logs NewValues ─────────────────────────────────

        [Fact]
        public async Task LogCreateAsync_ShouldSaveAuditRecord_WithNewValues()
        {
            // Arrange
            var entity = new { Id = 42, Code = "BR001", Name = "Main Branch" };

            // Act
            await _sut.LogCreateAsync("BRN001", "Branches", "Organization",
                "Branch", "42", entity);

            // Assert
            var log = await _db.AuditLogs.FirstOrDefaultAsync();
            Assert.NotNull(log);
            Assert.Equal("Create", log.Action);
            Assert.Equal("Branch", log.EntityName);
            Assert.Equal("42", log.EntityId);
            Assert.NotNull(log.NewValues);
            Assert.Null(log.OldValues);   // Create should have no OldValues
            Assert.True(log.Success);
            Assert.Equal("user-001", log.UserId);
            Assert.Equal("admin", log.UserName);
            Assert.Equal("105", log.EmployeeCode);
            Assert.Equal(1, log.CompanyId);
            Assert.Equal(10, log.BranchId);
        }

        // ─── Test 2: Update action logs ChangedValues ─────────────────────────────

        [Fact]
        public async Task LogUpdateAsync_ShouldSaveChangedValues_WhenFieldsChange()
        {
            // Arrange
            var oldValues = new { Id = 5, Code = "OLD", Name = "Old Name", IsActive = true };
            var newValues = new { Id = 5, Code = "NEW", Name = "New Name", IsActive = true };

            // Act
            await _sut.LogUpdateAsync("BRN001", "Branches", "Organization",
                "Branch", "5", oldValues, newValues);

            // Assert
            var log = await _db.AuditLogs.FirstOrDefaultAsync();
            Assert.NotNull(log);
            Assert.Equal("Update", log.Action);
            Assert.NotNull(log.ChangedValues);
            Assert.NotNull(log.OldValues);
            Assert.NotNull(log.NewValues);

            // ChangedValues should contain only Code and Name (IsActive didn't change)
            using var doc = JsonDocument.Parse(log.ChangedValues!);
            Assert.True(doc.RootElement.TryGetProperty("Code", out _), "Code should be in ChangedValues");
            Assert.True(doc.RootElement.TryGetProperty("Name", out _), "Name should be in ChangedValues");
            Assert.False(doc.RootElement.TryGetProperty("IsActive", out _), "IsActive should NOT be in ChangedValues (unchanged)");
        }

        // ─── Test 3: Update with no changes → no audit record ────────────────────

        [Fact]
        public async Task LogUpdateAsync_ShouldSkipAudit_WhenNoFieldsChanged()
        {
            // Arrange
            var values = new { Id = 5, Code = "BR001", Name = "Same Name", IsActive = true };

            // Act
            await _sut.LogUpdateAsync("BRN001", "Branches", "Organization",
                "Branch", "5", values, values); // identical old and new

            // Assert
            var count = await _db.AuditLogs.CountAsync();
            Assert.Equal(0, count); // should be skipped entirely
        }

        // ─── Test 4: Delete action logs OldValues only ───────────────────────────

        [Fact]
        public async Task LogDeleteAsync_ShouldSaveOldValues_WithNoNewValues()
        {
            // Arrange
            var entity = new { Id = 7, Code = "BR007", Name = "Branch 007" };

            // Act
            await _sut.LogDeleteAsync("BRN001", "Branches", "Organization",
                "Branch", "7", entity);

            // Assert
            var log = await _db.AuditLogs.FirstOrDefaultAsync();
            Assert.NotNull(log);
            Assert.Equal("Delete", log.Action);
            Assert.NotNull(log.OldValues);
            Assert.Null(log.NewValues);  // Delete should have no NewValues
            Assert.True(log.Success);
        }

        // ─── Test 5: Sensitive fields are NEVER stored ───────────────────────────

        [Fact]
        public async Task LogCreateAsync_ShouldExcludeSensitiveFields()
        {
            // Arrange – object with sensitive fields
            var entity = new
            {
                Id = 1,
                UserName = "john",
                PasswordHash = "hash-should-not-appear",
                SecurityStamp = "stamp-should-not-appear",
                Token = "token-should-not-appear",
                Email = "john@example.com"
            };

            // Act
            await _sut.LogCreateAsync("USR001", "Users", "Administration",
                "User", "1", entity);

            // Assert
            var log = await _db.AuditLogs.FirstOrDefaultAsync();
            Assert.NotNull(log);
            Assert.NotNull(log.NewValues);

            var json = log.NewValues!;
            Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SecurityStamp", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Token", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash-should-not-appear", json);
            Assert.DoesNotContain("stamp-should-not-appear", json);
            Assert.DoesNotContain("token-should-not-appear", json);

            // Non-sensitive fields should still be there
            Assert.Contains("Email", json);
        }

        // ─── Test 6: ChangedValues diff contains ONLY changed fields ─────────────

        [Fact]
        public async Task LogUpdateAsync_ChangedValues_ShouldContainCorrectOldAndNew()
        {
            // Arrange
            var oldValues = new { Id = 3, Code = "A", IsActive = false };
            var newValues = new { Id = 3, Code = "B", IsActive = true };

            // Act
            await _sut.LogUpdateAsync("BRN001", "Branches", "Organization",
                "Branch", "3", oldValues, newValues);

            // Assert
            var log = await _db.AuditLogs.FirstOrDefaultAsync();
            Assert.NotNull(log?.ChangedValues);

            using var doc = JsonDocument.Parse(log!.ChangedValues!);

            // Code changed: A → B
            Assert.True(doc.RootElement.TryGetProperty("Code", out var codeEl));
            Assert.Equal("A", codeEl.GetProperty("Old").GetString());
            Assert.Equal("B", codeEl.GetProperty("New").GetString());

            // IsActive changed: false → true
            Assert.True(doc.RootElement.TryGetProperty("IsActive", out var activeEl));
            Assert.Equal("false", activeEl.GetProperty("Old").GetString(), ignoreCase: true);
            Assert.Equal("true",  activeEl.GetProperty("New").GetString(), ignoreCase: true);

            // Id did NOT change
            Assert.False(doc.RootElement.TryGetProperty("Id", out _), "Id should NOT appear in ChangedValues");
        }

        // ─── Test 7: LogFailedAsync marks Success=false ───────────────────────────

        [Fact]
        public async Task LogFailedAsync_ShouldSetSuccessFalse_WithErrorMessage()
        {
            // Act
            await _sut.LogFailedAsync("BRN001", "Branches", "Organization",
                "Branch", "99", AuditAction.Delete, "Access denied: insufficient privileges");

            // Assert
            var log = await _db.AuditLogs.FirstOrDefaultAsync();
            Assert.NotNull(log);
            Assert.False(log.Success);
            Assert.Contains("Access denied", log.ErrorMessage);
            Assert.Equal("Delete", log.Action);
        }

        // ─── Test 8: Context fields are auto-populated from ICurrentUserContext ───

        [Fact]
        public async Task LogAsync_ShouldPopulateUserAndOrgContext_FromCurrentUser()
        {
            // Arrange
            var entry = new AuditLogEntry
            {
                ScreenCode = "USR001",
                ScreenName = "Users",
                ModuleName = "Administration",
                EntityName = "User",
                EntityId   = "u-1",
                Action     = AuditAction.Create,
                NewValues  = new { Id = "u-1", Name = "Test" },
                Success    = true,
            };

            // Act
            await _sut.LogAsync(entry);

            // Assert
            var log = await _db.AuditLogs.FirstOrDefaultAsync();
            Assert.NotNull(log);
            Assert.Equal("user-001", log.UserId);
            Assert.Equal("admin",    log.UserName);
            Assert.Equal("105",      log.EmployeeCode);
            Assert.Equal(1,          log.CompanyId);
            Assert.Equal(10,         log.BranchId);
            Assert.Equal(2026,       log.FiscalYearId);
        }

        [Fact(Skip = "Remote live SQL Server diagnostic only")]
        public async Task Diagnose_Live_Query()
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseSqlServer("Server=SQL1002.site4now.net;Database=db_ab95d6_sewtec2026;User Id=db_ab95d6_sewtec2026_admin;Password=Mostafa@1981$;Encrypt=True;TrustServerCertificate=True;")
                .Options;

            using var db = new ErpDbContext(options);

            // Exactly the parameters from the user's URL:
            // ?SearchTerm=&DateFrom=2026-09-01&DateTo=2026-09-26&EmployeeCode=&UserName=&CompanyId=&BranchId=&FiscalYearId=&ModuleName=&Action=&EntityName=&EntityId=&Success=&PageSize=20
            var query = db.AuditLogs.AsNoTracking();

            var searchTerm = "";
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim();
                query = query.Where(a =>
                    (a.EntityName != null && a.EntityName.Contains(term)) ||
                    (a.EntityId != null && a.EntityId.Contains(term)) ||
                    (a.Description != null && a.Description.Contains(term)) ||
                    (a.ScreenCode != null && a.ScreenCode.Contains(term)) ||
                    (a.ScreenName != null && a.ScreenName.Contains(term)) ||
                    (a.ModuleName != null && a.ModuleName.Contains(term)) ||
                    (a.UserName != null && a.UserName.Contains(term)) ||
                    (a.EmployeeCode != null && a.EmployeeCode.Contains(term)) ||
                    (a.Action != null && a.Action.Contains(term)) ||
                    (a.NewValues != null && a.NewValues.Contains(term)) ||
                    (a.OldValues != null && a.OldValues.Contains(term)));
            }

            DateTime? dateFrom = new DateTime(2026, 9, 1);
            if (dateFrom.HasValue)
            {
                var fromDate = dateFrom.Value.Date;
                query = query.Where(a => a.TimestampUtc >= fromDate);
            }

            DateTime? dateTo = new DateTime(2026, 9, 26);
            if (dateTo.HasValue)
            {
                var toDate = dateTo.Value.Date.AddDays(1);
                query = query.Where(a => a.TimestampUtc < toDate);
            }

            var employeeCode = "";
            if (!string.IsNullOrWhiteSpace(employeeCode))
                query = query.Where(a => a.EmployeeCode != null && a.EmployeeCode.Contains(employeeCode.Trim()));

            var userName = "";
            if (!string.IsNullOrWhiteSpace(userName))
                query = query.Where(a => a.UserName != null && a.UserName.Contains(userName.Trim()));

            int? companyId = null;
            if (companyId.HasValue && companyId > 0)
                query = query.Where(a => a.CompanyId == companyId);

            int? branchId = null;
            if (branchId.HasValue && branchId > 0)
                query = query.Where(a => a.BranchId == branchId);

            int? fiscalYearId = null;
            if (fiscalYearId.HasValue && fiscalYearId > 0)
                query = query.Where(a => a.FiscalYearId == fiscalYearId);

            var moduleName = "";
            if (!string.IsNullOrWhiteSpace(moduleName))
                query = query.Where(a => a.ModuleName == moduleName.Trim());

            var screenCode = "";
            if (!string.IsNullOrWhiteSpace(screenCode))
                query = query.Where(a => a.ScreenCode == screenCode.Trim());

            var action = "";
            if (!string.IsNullOrWhiteSpace(action))
                query = query.Where(a => a.Action == action.Trim());

            var entityName = "";
            if (!string.IsNullOrWhiteSpace(entityName))
                query = query.Where(a => a.EntityName == entityName.Trim());

            var entityId = "";
            if (!string.IsNullOrWhiteSpace(entityId))
                query = query.Where(a => a.EntityId == entityId.Trim());

            // Case A: Success is null
            var countA = await query.CountAsync();

            // Case B: Success is false
            var queryB = query.Where(a => a.Success == false);
            var countB = await queryB.CountAsync();

            // Case C: Success is true
            var queryC = query.Where(a => a.Success == true);
            var countC = await queryC.CountAsync();

            // Test model binding of Success=""
            var valueProvider = new Microsoft.AspNetCore.Mvc.ModelBinding.QueryStringValueProvider(
                Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Query,
                new Microsoft.AspNetCore.Http.QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                {
                    { "Success", new Microsoft.Extensions.Primitives.StringValues("") }
                }),
                System.Globalization.CultureInfo.InvariantCulture);

            var vpResult = valueProvider.GetValue("Success");
            var rawValue = vpResult.FirstValue;
            bool? parsedNullableBool = string.IsNullOrEmpty(rawValue) ? null : bool.Parse(rawValue);

            Assert.Null(parsedNullableBool);
        }

        [Fact(Skip = "Integration test requiring specific cloud database state")]
        public async Task Test_AuditController_Direct()
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseSqlServer("Server=SQL1002.site4now.net;Database=db_ab95d6_sewtec2026;User Id=db_ab95d6_sewtec2026_admin;Password=Mostafa@1981$;Encrypt=True;TrustServerCertificate=True;")
                .Options;

            using var db = new ErpDbContext(options);

            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Branches' AND COLUMN_NAME = 'BranchCode')
                BEGIN
                    ALTER TABLE Branches ADD BranchCode NVARCHAR(20) NOT NULL DEFAULT '';
                    EXEC('UPDATE Branches SET BranchCode = Code WHERE BranchCode = '''' OR BranchCode IS NULL;');
                END
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CustomerCategories' AND COLUMN_NAME = 'BranchId')
                BEGIN
                    ALTER TABLE CustomerCategories ADD BranchId INT NOT NULL DEFAULT 1;
                    ALTER TABLE CustomerCategories ADD IsDefault BIT NOT NULL DEFAULT 0;
                END
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'VendorCategories' AND COLUMN_NAME = 'BranchId')
                BEGIN
                    ALTER TABLE VendorCategories ADD BranchId INT NOT NULL DEFAULT 1;
                    ALTER TABLE VendorCategories ADD IsDefault BIT NOT NULL DEFAULT 0;
                END
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TransactionTypes' AND COLUMN_NAME = 'ShowLineNotes')
                BEGIN
                    ALTER TABLE TransactionTypes ADD ShowLineNotes BIT NOT NULL DEFAULT 1;
                END
            ");

            var mockUser = new Mock<ERP.Application.Interfaces.ICurrentUserContext>();
            mockUser.Setup(u => u.IsAuthenticated).Returns(true);
            mockUser.Setup(u => u.UserId).Returns("admin");

            var controller = new ERP.Web.Controllers.AuditController(db, mockUser.Object);

            var filter = new ERP.Web.Models.AuditFilterViewModel
            {
                SearchTerm = "",
                DateFrom = new DateTime(2026, 9, 1),
                DateTo = new DateTime(2026, 9, 26),
                EmployeeCode = "",
                UserName = "",
                CompanyId = null,
                BranchId = null,
                FiscalYearId = null,
                ModuleName = "",
                Action = "",
                EntityName = "",
                EntityId = "",
                Success = "",
                PageSize = 20,
                Page = 1
            };

            // Test various search terms
            var termsToTest = new[] { "", "2027", "FiscalYear", "Create", "سنة", "السنوات المالية", "فرع", "تعديل" };
            var results = new Dictionary<string, int>();

            foreach (var t in termsToTest)
            {
                var f = new ERP.Web.Models.AuditFilterViewModel
                {
                    SearchTerm = t,
                    PageSize = 20,
                    Page = 1
                };
                var res = await controller.Index(f);
                var vr = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(res);
                var m = Assert.IsType<ERP.Web.Models.AuditListViewModel>(vr.Model);
                results[t] = m.TotalCount;
            }

            // Verify all search queries return expected records
            Assert.True(results[""] >= 7, "Empty search should return all records");
            Assert.True(results["2027"] >= 1);
            Assert.True(results["FiscalYear"] >= 1);
            Assert.True(results["Create"] >= 1);
            Assert.True(results["سنة"] >= 1);
            Assert.True(results["السنوات المالية"] >= 1);
            Assert.True(results["فرع"] >= 1);
            Assert.True(results["تعديل"] >= 1);
        }

        [Fact(Skip = "Remote live SQL Server diagnostic only")]
        public async Task Test_DocumentHeaders_Query_Direct()
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseSqlServer("Server=SQL1002.site4now.net;Database=db_ab95d6_sewtec2026;User Id=db_ab95d6_sewtec2026_admin;Password=Mostafa@1981$;Encrypt=True;TrustServerCertificate=True;")
                .Options;

            using var db = new ErpDbContext(options);

            var query = db.DocumentHeaders
                .AsNoTracking()
                .Include(h => h.TransactionType)
                .Include(h => h.Branch)
                .Include(h => h.JournalEntryHeader);

            var list = await query.OrderByDescending(h => h.DocumentDate).ThenByDescending(h => h.Id).Take(10).ToListAsync();
            Assert.NotNull(list);
        }
    }
}
