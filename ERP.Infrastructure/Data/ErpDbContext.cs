using ERP.Domain.Entities;
using ERP.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Data
{
    public class ErpDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        public ErpDbContext(DbContextOptions<ErpDbContext> options) : base(options)
        {
        }

        public DbSet<Company> Companies { get; set; } = null!;
        public DbSet<Branch> Branches { get; set; } = null!;
        public DbSet<FiscalYear> FiscalYears { get; set; } = null!;
        public DbSet<UserBranch> UserBranches { get; set; } = null!;
        public DbSet<Permission> Permissions { get; set; } = null!;
        public DbSet<RolePermission> RolePermissions { get; set; } = null!;

        /// <summary>Central audit trail. BIGINT PK supports tens of millions of records.</summary>
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;

        // ─── Phase 3 – Master Data DbSets ──────────────────────────────────────────
        public DbSet<Governorate> Governorates { get; set; } = null!;
        public DbSet<City> Cities { get; set; } = null!;
        public DbSet<Salesman> Salesmen { get; set; } = null!;
        public DbSet<Currency> Currencies { get; set; } = null!;
        public DbSet<CustomerCategory> CustomerCategories { get; set; } = null!;
        public DbSet<Customer> Customers { get; set; } = null!;
        public DbSet<VendorCategory> VendorCategories { get; set; } = null!;
        public DbSet<Vendor> Vendors { get; set; } = null!;
        public DbSet<Unit> Units { get; set; } = null!;
        public DbSet<ItemGroup> ItemGroups { get; set; } = null!;
        public DbSet<Item> Items { get; set; } = null!;
        public DbSet<ItemUnit> ItemUnits { get; set; } = null!;
        public DbSet<Warehouse> Warehouses { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ─── Existing entity configurations ────────────────────────────────────
            builder.Entity<Branch>()
                .HasOne(b => b.Company)
                .WithMany(c => c.Branches)
                .HasForeignKey(b => b.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<UserBranch>()
                .HasOne(ub => ub.Branch)
                .WithMany()
                .HasForeignKey(ub => ub.BranchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Permission>()
                .HasIndex(p => p.Code)
                .IsUnique();

            builder.Entity<RolePermission>()
                .HasOne(rp => rp.Permission)
                .WithMany()
                .HasForeignKey(rp => rp.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);

            // ─── Phase 3 – Master Data Fluent API Configurations ──────────────────

            // 1. Governorate (Global)
            builder.Entity<Governorate>(b =>
            {
                b.ToTable("Governorates");
                b.HasKey(g => g.Id);
                b.Property(g => g.Code).HasMaxLength(50).IsRequired();
                b.Property(g => g.NameAr).HasMaxLength(200).IsRequired();
                b.Property(g => g.NameEn).HasMaxLength(200).IsRequired();
                b.HasIndex(g => g.Code).IsUnique().HasDatabaseName("IX_Governorates_Code");
            });

            // 2. City (Global)
            builder.Entity<City>(b =>
            {
                b.ToTable("Cities");
                b.HasKey(c => c.Id);
                b.Property(c => c.Code).HasMaxLength(50).IsRequired();
                b.Property(c => c.NameAr).HasMaxLength(200).IsRequired();
                b.Property(c => c.NameEn).HasMaxLength(200).IsRequired();
                b.HasIndex(c => c.Code).IsUnique().HasDatabaseName("IX_Cities_Code");

                b.HasOne(c => c.Governorate)
                    .WithMany(g => g.Cities)
                    .HasForeignKey(c => c.GovernorateId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 3. Salesman (Branch-Based)
            builder.Entity<Salesman>(b =>
            {
                b.ToTable("Salesmen");
                b.HasKey(s => s.Id);
                b.Property(s => s.Code).HasMaxLength(50).IsRequired();
                b.Property(s => s.NameAr).HasMaxLength(200).IsRequired();
                b.Property(s => s.NameEn).HasMaxLength(200).IsRequired();
                b.Property(s => s.Mobile).HasMaxLength(50);
                b.Property(s => s.Phone).HasMaxLength(50);

                b.HasIndex(s => new { s.BranchId, s.Code })
                    .IsUnique()
                    .HasDatabaseName("IX_Salesmen_BranchId_Code");

                b.HasOne(s => s.Branch)
                    .WithMany()
                    .HasForeignKey(s => s.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 4. Currency (Global)
            builder.Entity<Currency>(b =>
            {
                b.ToTable("Currencies");
                b.HasKey(c => c.Id);
                b.Property(c => c.Code).HasMaxLength(10).IsRequired();
                b.Property(c => c.NameAr).HasMaxLength(100).IsRequired();
                b.Property(c => c.NameEn).HasMaxLength(100).IsRequired();
                b.Property(c => c.Symbol).HasMaxLength(10);
                b.Property(c => c.ExchangeRate).HasColumnType("decimal(18, 4)").HasDefaultValue(1.0m);
                b.HasIndex(c => c.Code).IsUnique().HasDatabaseName("IX_Currencies_Code");
            });

            // 5. CustomerCategory (Global)
            builder.Entity<CustomerCategory>(b =>
            {
                b.ToTable("CustomerCategories");
                b.HasKey(cc => cc.Id);
                b.Property(cc => cc.Code).HasMaxLength(50).IsRequired();
                b.Property(cc => cc.NameAr).HasMaxLength(200).IsRequired();
                b.Property(cc => cc.NameEn).HasMaxLength(200).IsRequired();
                b.HasIndex(cc => cc.Code).IsUnique().HasDatabaseName("IX_CustomerCategories_Code");
            });

            // 6. Customer (Branch-Based)
            builder.Entity<Customer>(b =>
            {
                b.ToTable("Customers");
                b.HasKey(c => c.Id);
                b.Property(c => c.Code).HasMaxLength(50).IsRequired();
                b.Property(c => c.NameAr).HasMaxLength(200).IsRequired();
                b.Property(c => c.NameEn).HasMaxLength(200).IsRequired();
                b.Property(c => c.Address).HasMaxLength(500);
                b.Property(c => c.TaxNumber).HasMaxLength(50);
                b.Property(c => c.CommercialRegister).HasMaxLength(50);
                b.Property(c => c.TaxPayerType).HasMaxLength(50);
                b.Property(c => c.Phone).HasMaxLength(50);
                b.Property(c => c.Mobile).HasMaxLength(50);
                b.Property(c => c.Email).HasMaxLength(100);

                b.HasIndex(c => new { c.BranchId, c.Code })
                    .IsUnique()
                    .HasDatabaseName("IX_Customers_BranchId_Code");

                b.HasOne(c => c.Branch)
                    .WithMany()
                    .HasForeignKey(c => c.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.CustomerCategory)
                    .WithMany(cc => cc.Customers)
                    .HasForeignKey(c => c.CustomerCategoryId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.Governorate)
                    .WithMany()
                    .HasForeignKey(c => c.GovernorateId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.City)
                    .WithMany()
                    .HasForeignKey(c => c.CityId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.Salesman)
                    .WithMany()
                    .HasForeignKey(c => c.SalesmanId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.Currency)
                    .WithMany()
                    .HasForeignKey(c => c.CurrencyId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 7. VendorCategory (Global)
            builder.Entity<VendorCategory>(b =>
            {
                b.ToTable("VendorCategories");
                b.HasKey(vc => vc.Id);
                b.Property(vc => vc.Code).HasMaxLength(50).IsRequired();
                b.Property(vc => vc.NameAr).HasMaxLength(200).IsRequired();
                b.Property(vc => vc.NameEn).HasMaxLength(200).IsRequired();
                b.HasIndex(vc => vc.Code).IsUnique().HasDatabaseName("IX_VendorCategories_Code");
            });

            // 8. Vendor (Branch-Based)
            builder.Entity<Vendor>(b =>
            {
                b.ToTable("Vendors");
                b.HasKey(v => v.Id);
                b.Property(v => v.Code).HasMaxLength(50).IsRequired();
                b.Property(v => v.NameAr).HasMaxLength(200).IsRequired();
                b.Property(v => v.NameEn).HasMaxLength(200).IsRequired();
                b.Property(v => v.Address).HasMaxLength(500);
                b.Property(v => v.TaxNumber).HasMaxLength(50);
                b.Property(v => v.CommercialRegister).HasMaxLength(50);
                b.Property(v => v.TaxPayerType).HasMaxLength(50);
                b.Property(v => v.Phone).HasMaxLength(50);
                b.Property(v => v.Mobile).HasMaxLength(50);
                b.Property(v => v.Email).HasMaxLength(100);

                b.HasIndex(v => new { v.BranchId, v.Code })
                    .IsUnique()
                    .HasDatabaseName("IX_Vendors_BranchId_Code");

                b.HasOne(v => v.Branch)
                    .WithMany()
                    .HasForeignKey(v => v.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(v => v.VendorCategory)
                    .WithMany(vc => vc.Vendors)
                    .HasForeignKey(v => v.VendorCategoryId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(v => v.Governorate)
                    .WithMany()
                    .HasForeignKey(v => v.GovernorateId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(v => v.City)
                    .WithMany()
                    .HasForeignKey(v => v.CityId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(v => v.Currency)
                    .WithMany()
                    .HasForeignKey(v => v.CurrencyId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 9. Unit (Global)
            builder.Entity<Unit>(b =>
            {
                b.ToTable("Units");
                b.HasKey(u => u.Id);
                b.Property(u => u.Code).HasMaxLength(50).IsRequired();
                b.Property(u => u.NameAr).HasMaxLength(100).IsRequired();
                b.Property(u => u.NameEn).HasMaxLength(100).IsRequired();
                b.HasIndex(u => u.Code).IsUnique().HasDatabaseName("IX_Units_Code");
            });

            // 10. ItemGroup (Global - Hierarchical)
            builder.Entity<ItemGroup>(b =>
            {
                b.ToTable("ItemGroups");
                b.HasKey(ig => ig.Id);
                b.Property(ig => ig.Code).HasMaxLength(50).IsRequired();
                b.Property(ig => ig.NameAr).HasMaxLength(200).IsRequired();
                b.Property(ig => ig.NameEn).HasMaxLength(200).IsRequired();
                b.HasIndex(ig => ig.Code).IsUnique().HasDatabaseName("IX_ItemGroups_Code");

                b.HasOne(ig => ig.ParentGroup)
                    .WithMany(ig => ig.SubGroups)
                    .HasForeignKey(ig => ig.ParentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 11. Item (Global - StorageLocation field, NO BranchId, NO WarehouseLocation)
            builder.Entity<Item>(b =>
            {
                b.ToTable("Items");
                b.HasKey(i => i.Id);
                b.Property(i => i.Code).HasMaxLength(50).IsRequired();
                b.Property(i => i.Barcode).HasMaxLength(100);
                b.Property(i => i.TaxCode).HasMaxLength(50);
                b.Property(i => i.NameAr).HasMaxLength(200).IsRequired();
                b.Property(i => i.NameEn).HasMaxLength(200).IsRequired();
                b.Property(i => i.StorageLocation).HasMaxLength(200);

                b.HasIndex(i => i.Code).IsUnique().HasDatabaseName("IX_Items_Code");
                b.HasIndex(i => i.Barcode).HasDatabaseName("IX_Items_Barcode");

                b.HasOne(i => i.ItemGroup)
                    .WithMany(ig => ig.Items)
                    .HasForeignKey(i => i.ItemGroupId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 12. ItemUnit (Global - ItemId + UnitId Unique)
            builder.Entity<ItemUnit>(b =>
            {
                b.ToTable("ItemUnits");
                b.HasKey(iu => iu.Id);
                b.Property(iu => iu.ConversionFactor).HasColumnType("decimal(18, 4)").IsRequired();
                b.Property(iu => iu.SalesPrice).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);
                b.Property(iu => iu.PurchasePrice).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);

                b.HasIndex(iu => new { iu.ItemId, iu.UnitId })
                    .IsUnique()
                    .HasDatabaseName("IX_ItemUnits_ItemId_UnitId");

                b.HasOne(iu => iu.Item)
                    .WithMany(i => i.ItemUnits)
                    .HasForeignKey(iu => iu.ItemId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(iu => iu.Unit)
                    .WithMany(u => u.ItemUnits)
                    .HasForeignKey(iu => iu.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 13. Warehouse (Branch-Based)
            builder.Entity<Warehouse>(b =>
            {
                b.ToTable("Warehouses");
                b.HasKey(w => w.Id);
                b.Property(w => w.Code).HasMaxLength(50).IsRequired();
                b.Property(w => w.NameAr).HasMaxLength(200).IsRequired();
                b.Property(w => w.NameEn).HasMaxLength(200).IsRequired();

                b.HasIndex(w => new { w.BranchId, w.Code })
                    .IsUnique()
                    .HasDatabaseName("IX_Warehouses_BranchId_Code");

                b.HasOne(w => w.Branch)
                    .WithMany()
                    .HasForeignKey(w => w.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── AuditLog configuration ────────────────────────────────────────────
            builder.Entity<AuditLog>(b =>
            {
                b.ToTable("AuditLogs");

                // BIGINT IDENTITY primary key
                b.HasKey(a => a.Id);
                b.Property(a => a.Id)
                    .UseIdentityColumn()
                    .ValueGeneratedOnAdd();

                // Column types and max lengths
                b.Property(a => a.TimestampUtc).HasColumnType("datetime2").IsRequired();
                b.Property(a => a.UserId).HasMaxLength(450);
                b.Property(a => a.EmployeeCode).HasMaxLength(50);
                b.Property(a => a.UserName).HasMaxLength(256);
                b.Property(a => a.ScreenCode).HasMaxLength(20);
                b.Property(a => a.ScreenName).HasMaxLength(100);
                b.Property(a => a.ModuleName).HasMaxLength(100);
                b.Property(a => a.EntityName).HasMaxLength(100);
                b.Property(a => a.EntityId).HasMaxLength(100);
                b.Property(a => a.Action).HasMaxLength(50).IsRequired();
                b.Property(a => a.Description).HasMaxLength(500);
                b.Property(a => a.IpAddress).HasMaxLength(50);
                b.Property(a => a.UserAgent).HasMaxLength(500);
                b.Property(a => a.RequestId).HasMaxLength(100);
                b.Property(a => a.CorrelationId).HasMaxLength(100);
                b.Property(a => a.HttpMethod).HasMaxLength(10);
                b.Property(a => a.RequestPath).HasMaxLength(500);
                b.Property(a => a.ErrorMessage).HasMaxLength(2000);

                // JSON columns – nvarchar(max)
                b.Property(a => a.OldValues).HasColumnType("nvarchar(max)");
                b.Property(a => a.NewValues).HasColumnType("nvarchar(max)");
                b.Property(a => a.ChangedValues).HasColumnType("nvarchar(max)");

                // ─── Indexes ──────────────────────────────────────────────────────
                b.HasIndex(a => a.TimestampUtc).HasDatabaseName("IX_AuditLogs_TimestampUtc");
                b.HasIndex(a => a.UserId).HasDatabaseName("IX_AuditLogs_UserId");
                b.HasIndex(a => a.Action).HasDatabaseName("IX_AuditLogs_Action");
                b.HasIndex(a => a.Success).HasDatabaseName("IX_AuditLogs_Success");
                b.HasIndex(a => new { a.BranchId, a.TimestampUtc }).HasDatabaseName("IX_AuditLogs_Branch_Timestamp");
                b.HasIndex(a => new { a.UserId, a.TimestampUtc }).HasDatabaseName("IX_AuditLogs_User_Timestamp");
                b.HasIndex(a => new { a.ScreenCode, a.TimestampUtc }).HasDatabaseName("IX_AuditLogs_Screen_Timestamp");
                b.HasIndex(a => new { a.EntityName, a.EntityId }).HasDatabaseName("IX_AuditLogs_Entity");
                b.HasIndex(a => new { a.CompanyId, a.BranchId, a.TimestampUtc }).HasDatabaseName("IX_AuditLogs_Company_Branch_Timestamp");
                b.HasIndex(a => new { a.Action, a.Success, a.TimestampUtc }).HasDatabaseName("IX_AuditLogs_Action_Success_Timestamp");
            });
        }
    }
}
