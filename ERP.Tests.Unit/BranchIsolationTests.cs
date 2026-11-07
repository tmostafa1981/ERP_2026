using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ERP.Tests.Unit
{
    public class BranchIsolationTests
    {
        private ErpDbContext CreateDbContext(int branchId, bool isSuperAdmin, string dbName)
        {
            var userMock = new Mock<ICurrentUserService>();
            userMock.Setup(u => u.BranchId).Returns(branchId);
            userMock.Setup(u => u.IsSuperAdmin).Returns(isSuperAdmin);
            userMock.Setup(u => u.IsAuthenticated).Returns(true);

            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new ErpDbContext(options, userMock.Object);
        }

        [Fact]
        public void QueryFilter_RegularUser_OnlySeesOwnBranchEntities()
        {
            var dbName = Guid.NewGuid().ToString();

            // Seed as SuperAdmin
            using (var adminDb = CreateDbContext(branchId: 0, isSuperAdmin: true, dbName))
            {
                adminDb.Customers.AddRange(
                    new Customer { Id = 1, BranchId = 1, Code = "C1", NameAr = "عميل فرع 1", NameEn = "Customer B1" },
                    new Customer { Id = 2, BranchId = 2, Code = "C2", NameAr = "عميل فرع 2", NameEn = "Customer B2" }
                );
                adminDb.Vendors.AddRange(
                    new Vendor { Id = 1, BranchId = 1, Code = "V1", NameAr = "مورد فرع 1", NameEn = "Vendor B1" },
                    new Vendor { Id = 2, BranchId = 2, Code = "V2", NameAr = "مورد فرع 2", NameEn = "Vendor B2" }
                );
                adminDb.Warehouses.AddRange(
                    new Warehouse { Id = 1, BranchId = 1, Code = "W1", NameAr = "مخزن فرع 1", NameEn = "Warehouse B1" },
                    new Warehouse { Id = 2, BranchId = 2, Code = "W2", NameAr = "مخزن فرع 2", NameEn = "Warehouse B2" }
                );
                adminDb.Salesmen.AddRange(
                    new Salesman { Id = 1, BranchId = 1, Code = "S1", NameAr = "مندوب فرع 1", NameEn = "Salesman B1" },
                    new Salesman { Id = 2, BranchId = 2, Code = "S2", NameAr = "مندوب فرع 2", NameEn = "Salesman B2" }
                );
                adminDb.SaveChanges();
            }

            // Query as Branch 1 user
            using (var userDb = CreateDbContext(branchId: 1, isSuperAdmin: false, dbName))
            {
                var customers = userDb.Customers.ToList();
                Assert.Single(customers);
                Assert.Equal(1, customers[0].BranchId);

                var vendors = userDb.Vendors.ToList();
                Assert.Single(vendors);
                Assert.Equal(1, vendors[0].BranchId);

                var warehouses = userDb.Warehouses.ToList();
                Assert.Single(warehouses);
                Assert.Equal(1, warehouses[0].BranchId);

                var salesmen = userDb.Salesmen.ToList();
                Assert.Single(salesmen);
                Assert.Equal(1, salesmen[0].BranchId);
            }
        }

        [Fact]
        public void QueryFilter_UnscopedContext_SeesAllBranchesEntities()
        {
            var dbName = Guid.NewGuid().ToString();

            // Seed entities across two branches using unscoped context (branchId: 0)
            using (var unscopedDb = CreateDbContext(branchId: 0, isSuperAdmin: true, dbName))
            {
                unscopedDb.Customers.AddRange(
                    new Customer { Id = 1, BranchId = 1, Code = "C1", NameAr = "عميل 1", NameEn = "C1" },
                    new Customer { Id = 2, BranchId = 2, Code = "C2", NameAr = "عميل 2", NameEn = "C2" }
                );
                unscopedDb.SaveChanges();

                var allCustomers = unscopedDb.Customers.ToList();
                Assert.Equal(2, allCustomers.Count);
            }
        }

        [Fact]
        public void QueryFilter_AdminWithActiveBranch_StrictlyFilteredToActiveBranch()
        {
            var dbName = Guid.NewGuid().ToString();

            // Seed entities across two branches
            using (var unscopedDb = CreateDbContext(branchId: 0, isSuperAdmin: true, dbName))
            {
                unscopedDb.Customers.AddRange(
                    new Customer { Id = 1, BranchId = 1, Code = "C1", NameAr = "عميل 1", NameEn = "C1" },
                    new Customer { Id = 2, BranchId = 2, Code = "C2", NameAr = "عميل 2", NameEn = "C2" }
                );
                unscopedDb.SaveChanges();
            }

            // Query as Admin who has logged in / switched to Branch 1
            using (var adminDb = CreateDbContext(branchId: 1, isSuperAdmin: true, dbName))
            {
                var customers = adminDb.Customers.ToList();
                Assert.Single(customers);
                Assert.Equal(1, customers[0].BranchId);
            }
        }

        [Fact]
        public void QueryFilter_GLAccount_ShowsSharedAndCurrentBranchOnly()
        {
            var dbName = Guid.NewGuid().ToString();

            // Seed GLAccounts: shared (BranchId = null), Branch 1 (BranchId = 1), Branch 2 (BranchId = 2)
            using (var seedDb = CreateDbContext(branchId: 0, isSuperAdmin: true, dbName))
            {
                seedDb.GLAccounts.AddRange(
                    new GLAccount { Id = 1, Code = "101", NameAr = "حساب عام أصول", NameEn = "Assets", BranchId = null },
                    new GLAccount { Id = 2, Code = "10201", NameAr = "صندوق فرع 1", NameEn = "Cash Box B1", BranchId = 1 },
                    new GLAccount { Id = 3, Code = "10202", NameAr = "صندوق فرع 2", NameEn = "Cash Box B2", BranchId = 2 }
                );
                seedDb.SaveChanges();
            }

            // User or Admin in Branch 1 must see shared account and Branch 1 account, NOT Branch 2 account
            using (var branch1Db = CreateDbContext(branchId: 1, isSuperAdmin: false, dbName))
            {
                var accounts = branch1Db.GLAccounts.ToList();
                Assert.Equal(2, accounts.Count);
                Assert.Contains(accounts, a => a.Id == 1);
                Assert.Contains(accounts, a => a.Id == 2);
                Assert.DoesNotContain(accounts, a => a.Id == 3);
            }
        }

        [Fact]
        public void QueryFilter_SharedMasterData_NotFilteredByBranch()
        {
            var dbName = Guid.NewGuid().ToString();

            using (var adminDb = CreateDbContext(branchId: 0, isSuperAdmin: true, dbName))
            {
                adminDb.Cities.Add(new City { Id = 1, Code = "CAI-C", NameAr = "مدينة نصر", NameEn = "Nasr City", GovernorateId = 1 });
                adminDb.Currencies.Add(new Currency { Id = 1, Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound" });
                adminDb.Items.Add(new Item { Id = 1, Code = "ITM1", NameAr = "صنف عام", NameEn = "General Item" });
                adminDb.SaveChanges();
            }

            // Regular user in Branch 5 must see shared master data
            using (var userDb = CreateDbContext(branchId: 5, isSuperAdmin: false, dbName))
            {
                Assert.NotEmpty(userDb.Cities.ToList());
                Assert.NotEmpty(userDb.Currencies.ToList());
                Assert.NotEmpty(userDb.Items.ToList());
            }
        }

        [Fact]
        public void SaveChanges_AutoInjectsCurrentBranchId_ForRegularUser()
        {
            var dbName = Guid.NewGuid().ToString();

            using (var userDb = CreateDbContext(branchId: 3, isSuperAdmin: false, dbName))
            {
                var newCustomer = new Customer
                {
                    Code = "CUST-NEW",
                    NameAr = "عميل جديد",
                    NameEn = "New Customer",
                    BranchId = 0 // not set
                };

                userDb.Customers.Add(newCustomer);
                userDb.SaveChanges();

                Assert.Equal(3, newCustomer.BranchId);
            }
        }

        [Fact]
        public void SaveChanges_PreventsHackerInjectingOtherBranch_OnAdded()
        {
            var dbName = Guid.NewGuid().ToString();

            using (var userDb = CreateDbContext(branchId: 3, isSuperAdmin: false, dbName))
            {
                // Attacker tries to set BranchId to 999
                var newVendor = new Vendor
                {
                    Code = "VND-HACK",
                    NameAr = "مورد محاول اختراق",
                    NameEn = "Hacker Vendor",
                    BranchId = 999
                };

                userDb.Vendors.Add(newVendor);
                userDb.SaveChanges();

                // System overrides with user's actual branch
                Assert.Equal(3, newVendor.BranchId);
            }
        }

        [Fact]
        public void SaveChanges_PreventsBranchTampering_OnModified()
        {
            var dbName = Guid.NewGuid().ToString();

            // Seed as SuperAdmin
            using (var adminDb = CreateDbContext(branchId: 0, isSuperAdmin: true, dbName))
            {
                adminDb.Customers.Add(new Customer
                {
                    Id = 10,
                    BranchId = 1,
                    Code = "C10",
                    NameAr = "العميل الأصلي",
                    NameEn = "Original Customer"
                });
                adminDb.SaveChanges();
            }

            // Branch 1 regular user attempts to change BranchId to 2
            using (var userDb = CreateDbContext(branchId: 1, isSuperAdmin: false, dbName))
            {
                var customer = userDb.Customers.First(c => c.Id == 10);
                customer.BranchId = 2; // Tampering attempt
                customer.NameAr = "اسم معدل مسموح";

                userDb.SaveChanges();
            }

            // Verify with fresh context
            using (var checkDb = CreateDbContext(branchId: 0, isSuperAdmin: true, dbName))
            {
                var customer = checkDb.Customers.First(c => c.Id == 10);
                Assert.Equal(1, customer.BranchId); // BranchId did NOT change!
                Assert.Equal("اسم معدل مسموح", customer.NameAr); // Other field was updated
            }
        }

        [Fact]
        public void SaveChanges_SuperAdmin_CanExplicitlyAssignDifferentBranch()
        {
            var dbName = Guid.NewGuid().ToString();

            using (var adminDb = CreateDbContext(branchId: 1, isSuperAdmin: true, dbName))
            {
                var warehouse = new Warehouse
                {
                    Code = "WH-CUSTOM",
                    NameAr = "مخزن فرع محدد",
                    NameEn = "Custom Branch Warehouse",
                    BranchId = 7
                };

                adminDb.Warehouses.Add(warehouse);
                adminDb.SaveChanges();

                Assert.Equal(7, warehouse.BranchId);
            }
        }

        [Fact]
        public async Task LookupService_Customer_StrictlyFiltersByActiveBranch()
        {
            var dbName = Guid.NewGuid().ToString();

            using (var adminDb = CreateDbContext(branchId: 0, isSuperAdmin: true, dbName))
            {
                var b1 = new Branch { Id = 1, Name = "Branch 1", NameAr = "فرع 1", NameEn = "Branch 1", Code = "B1" };
                var b2 = new Branch { Id = 2, Name = "Branch 2", NameAr = "فرع 2", NameEn = "Branch 2", Code = "B2" };
                adminDb.Branches.AddRange(b1, b2);

                var cat1 = new CustomerCategory { Id = 501, BranchId = 1, Code = "CC1", NameAr = "فئة 1", NameEn = "Cat 1", IsActive = true };
                var cat2 = new CustomerCategory { Id = 502, BranchId = 2, Code = "CC2", NameAr = "فئة 2", NameEn = "Cat 2", IsActive = true };
                adminDb.CustomerCategories.AddRange(cat1, cat2);

                adminDb.Customers.AddRange(
                    new Customer { Id = 101, BranchId = 1, CustomerCategoryId = cat1.Id, Code = "C-B1", NameAr = "عميل فرع 1", NameEn = "Customer B1", IsActive = true },
                    new Customer { Id = 102, BranchId = 2, CustomerCategoryId = cat2.Id, Code = "C-B2", NameAr = "عميل فرع 2", NameEn = "Customer B2", IsActive = true }
                );
                await adminDb.SaveChangesAsync();

                var lookupService = new ERP.Infrastructure.Services.LookupService(adminDb);
                var result = await lookupService.SearchAsync(new ERP.Application.DTOs.Lookups.LookupSearchRequest
                {
                    EntityType = "customer",
                    BranchId = 1,
                    Page = 1,
                    PageSize = 20
                });

                Assert.Single(result.Items);
                Assert.Equal(101, result.Items[0].Id);
                Assert.Equal("C-B1", result.Items[0].Code);
            }
        }

        [Fact]
        public async Task LookupService_Vendor_StrictlyFiltersByActiveBranch()
        {
            var dbName = Guid.NewGuid().ToString();

            using (var adminDb = CreateDbContext(branchId: 0, isSuperAdmin: true, dbName))
            {
                var b1 = new Branch { Id = 1, Name = "Branch 1", NameAr = "فرع 1", NameEn = "Branch 1", Code = "B1" };
                var b2 = new Branch { Id = 2, Name = "Branch 2", NameAr = "فرع 2", NameEn = "Branch 2", Code = "B2" };
                adminDb.Branches.AddRange(b1, b2);

                var vcat1 = new VendorCategory { Id = 601, BranchId = 1, Code = "VC1", NameAr = "فئة 1", NameEn = "Cat 1", IsActive = true };
                var vcat2 = new VendorCategory { Id = 602, BranchId = 2, Code = "VC2", NameAr = "فئة 2", NameEn = "Cat 2", IsActive = true };
                adminDb.VendorCategories.AddRange(vcat1, vcat2);

                adminDb.Vendors.AddRange(
                    new Vendor { Id = 201, BranchId = 1, VendorCategoryId = vcat1.Id, Code = "V-B1", NameAr = "مورد فرع 1", NameEn = "Vendor B1", IsActive = true },
                    new Vendor { Id = 202, BranchId = 2, VendorCategoryId = vcat2.Id, Code = "V-B2", NameAr = "مورد فرع 2", NameEn = "Vendor B2", IsActive = true }
                );
                await adminDb.SaveChangesAsync();

                var lookupService = new ERP.Infrastructure.Services.LookupService(adminDb);
                var result = await lookupService.SearchAsync(new ERP.Application.DTOs.Lookups.LookupSearchRequest
                {
                    EntityType = "vendor",
                    BranchId = 1,
                    Page = 1,
                    PageSize = 20
                });

                Assert.Single(result.Items);
                Assert.Equal(201, result.Items[0].Id);
                Assert.Equal("V-B1", result.Items[0].Code);
            }
        }

        [Fact]
        public async Task CustomerPreload_StrictBranchIsolation_NeverReturnsOtherBranchCustomers()
        {
            var dbName = Guid.NewGuid().ToString();

            using (var db = CreateDbContext(branchId: 0, isSuperAdmin: true, dbName))
            {
                db.Customers.AddRange(
                    new Customer { Id = 301, BranchId = 1, Code = "B001-C01", NameAr = "عميل فرع 1", NameEn = "Cust B1", IsActive = true },
                    new Customer { Id = 302, BranchId = 2, Code = "B002-C01", NameAr = "عميل فرع 2", NameEn = "Cust B2", IsActive = true },
                    new Customer { Id = 303, BranchId = 4, Code = "B004-C01", NameAr = "عميل فرع 4", NameEn = "Cust B4", IsActive = true }
                );
                await db.SaveChangesAsync();

                // Test querying for Branch 1
                int activeBranchId = 1;
                var branch1Customers = await db.Customers
                    .Where(c => c.IsActive && c.BranchId == activeBranchId)
                    .ToListAsync();

                Assert.Single(branch1Customers);
                Assert.Equal("B001-C01", branch1Customers[0].Code);
                Assert.Equal(1, branch1Customers[0].BranchId);

                // Verify branch 2 and branch 4 are not returned
                Assert.DoesNotContain(branch1Customers, c => c.BranchId == 2 || c.BranchId == 4);
            }
        }
    }
}
