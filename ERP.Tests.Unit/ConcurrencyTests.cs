using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Services;
using ERP.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ERP.Tests.Unit
{
    public class ConcurrencyTests : IDisposable
    {
        private readonly ErpDbContext _db;
        private readonly Mock<ICurrentUserContext> _userMock;
        private readonly Mock<IAuditService> _auditMock;
        private readonly ICodeSequenceService _sequenceService;

        public ConcurrencyTests()
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new ErpDbContext(options);
            _userMock = new Mock<ICurrentUserContext>();
            _auditMock = new Mock<IAuditService>();
            _sequenceService = new CodeSequenceService(_db);

            _userMock.Setup(u => u.UserId).Returns("user-test-concurrency");
            _userMock.Setup(u => u.Username).Returns("tester");
            _userMock.Setup(u => u.BranchId).Returns(1);
            _userMock.Setup(u => u.CompanyId).Returns(1);
            _userMock.Setup(u => u.CanAccessBranch(It.IsAny<int>())).Returns(true);
            _userMock.Setup(u => u.CanAccessCompany(It.IsAny<int>())).Returns(true);
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        [Fact]
        public void AllCoreEntities_Implement_IConcurrencyAware()
        {
            var types = new[]
            {
                typeof(GLAccount),
                typeof(JournalEntryHeader),
                typeof(CostCenter),
                typeof(FiscalYear),
                typeof(Customer),
                typeof(Vendor),
                typeof(CustomerCategory),
                typeof(VendorCategory),
                typeof(Salesman),
                typeof(Item),
                typeof(ItemUnit),
                typeof(ItemGroup),
                typeof(Warehouse),
                typeof(ERP.Domain.Entities.Unit),
                typeof(Branch),
                typeof(Company),
                typeof(Governorate),
                typeof(City),
                typeof(Currency)
            };

            foreach (var type in types)
            {
                Assert.True(typeof(IConcurrencyAware).IsAssignableFrom(type), $"{type.Name} must implement IConcurrencyAware");
                var prop = type.GetProperty(nameof(IConcurrencyAware.RowVersion));
                Assert.NotNull(prop);
                Assert.Equal(typeof(byte[]), prop.PropertyType);
            }
        }

        [Fact]
        public async Task CostCenter_EditConflict_ReturnsExpectedMessage()
        {
            var controller = new CostCentersController(_db, _userMock.Object, _auditMock.Object);

            var initialCenter = new CostCenter
            {
                Code = "CC-TEST",
                NameAr = "مركز تجريبي",
                NameEn = "Test Center",
                IsTransactional = true,
                IsActive = true,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
            _db.CostCenters.Add(initialCenter);
            await _db.SaveChangesAsync();

            // Simulate concurrent edit by User 2 changing the RowVersion in DB
            initialCenter.RowVersion = new byte[] { 2, 0, 0, 0 };
            await _db.SaveChangesAsync();

            // User 1 tries to submit with old RowVersion
            var user1Model = new CostCenter
            {
                Id = initialCenter.Id,
                Code = "CC-TEST",
                NameAr = "تعديل مستخدم 1",
                NameEn = "User 1 Edit",
                IsTransactional = true,
                IsActive = true,
                RowVersion = new byte[] { 1, 0, 0, 0 } // stale rowversion
            };

            var result = await controller.Edit(user1Model);
            var jsonRes = Assert.IsType<JsonResult>(result);

            // Reflection to check jsonRes.Value properties
            var val = jsonRes.Value;
            var successProp = val.GetType().GetProperty("success")?.GetValue(val);
            var msgProp = val.GetType().GetProperty("message")?.GetValue(val)?.ToString();

            Assert.Equal(false, successProp);
            Assert.Contains("تم تعديل السجل بواسطة مستخدم آخر", msgProp);
        }
    }
}
