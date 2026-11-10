using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ERP.Infrastructure.Data
{
    public class ErpDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        private readonly ICurrentUserService? _currentUserService;

        public ErpDbContext(
            DbContextOptions<ErpDbContext> options,
            ICurrentUserService? currentUserService = null) : base(options)
        {
            _currentUserService = currentUserService;
        }

        public int CurrentBranchId => _currentUserService?.BranchId ?? 0;
        public bool IsSuperAdmin => _currentUserService?.IsSuperAdmin ?? true;

        public DbSet<Company> Companies { get; set; } = null!;
        public DbSet<Branch> Branches { get; set; } = null!;
        public DbSet<FiscalYear> FiscalYears { get; set; } = null!;
        public DbSet<FiscalPeriod> FiscalPeriods { get; set; } = null!;
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
        public DbSet<CurrencyDailyRate> CurrencyDailyRates { get; set; } = null!;
        public DbSet<CustomerCategory> CustomerCategories { get; set; } = null!;
        public DbSet<Customer> Customers { get; set; } = null!;
        public DbSet<VendorCategory> VendorCategories { get; set; } = null!;
        public DbSet<Vendor> Vendors { get; set; } = null!;
        public DbSet<PartyGroup> PartyGroups { get; set; } = null!;
        public DbSet<Unit> Units { get; set; } = null!;
        public DbSet<ItemGroup> ItemGroups { get; set; } = null!;
        public DbSet<Item> Items { get; set; } = null!;
        public DbSet<ItemUnit> ItemUnits { get; set; } = null!;
        public DbSet<Warehouse> Warehouses { get; set; } = null!;
        public DbSet<CodeSequence> CodeSequences { get; set; } = null!;

        // ─── Phase 4 – Core Accounting DbSets ──────────────────────────────────────
        public DbSet<GLAccount> GLAccounts { get; set; } = null!;
        public DbSet<AccountPermission> AccountPermissions { get; set; } = null!;
        public DbSet<CostCenter> CostCenters { get; set; } = null!;
        public DbSet<JournalEntryHeader> JournalEntryHeaders { get; set; } = null!;
        public DbSet<JournalEntryLine> JournalEntryLines { get; set; } = null!;

        // ─── Phase 5 – Cash & Bank Voucher Module ──────────────────────────────────
        public DbSet<Bank> Banks { get; set; } = null!;
        public DbSet<VoucherHeader> VoucherHeaders { get; set; } = null!;
        public DbSet<VoucherLine> VoucherLines { get; set; } = null!;
        public DbSet<UserPaymentMethodPermission> UserPaymentMethodPermissions { get; set; } = null!;

        // ─── Phase 7 – Universal Transaction Types & Document Engine ───────────────
        public DbSet<TransactionType> TransactionTypes { get; set; } = null!;
        public DbSet<UserTransactionTypePermission> UserTransactionTypePermissions { get; set; } = null!;
        public DbSet<TransactionTypeConversion> TransactionTypeConversions { get; set; } = null!;
        public DbSet<ItemSerialNumber> ItemSerialNumbers { get; set; } = null!;
        public DbSet<ItemSerialMovement> ItemSerialMovements { get; set; } = null!;
        public DbSet<DocumentHeader> DocumentHeaders { get; set; } = null!;
        public DbSet<DocumentLine> DocumentLines { get; set; } = null!;
        public DbSet<DocumentLineSerial> DocumentLineSerials { get; set; } = null!;
        public DbSet<DocumentConversionLink> DocumentConversionLinks { get; set; } = null!;
        public DbSet<DocumentLineLink> DocumentLineLinks { get; set; } = null!;
        public DbSet<DocumentAdjustment> DocumentAdjustments { get; set; } = null!;
        public DbSet<TransactionTypeAdjustment> TransactionTypeAdjustments { get; set; } = null!;
        public DbSet<DocumentAdjustmentLine> DocumentAdjustmentLines { get; set; } = null!;
        public DbSet<SystemFeedback> SystemFeedbacks { get; set; } = null!;

        // ─── Phase 8 – Foreign Currency Revaluation Module ──────────────────────────
        public DbSet<RevaluationHeader> RevaluationHeaders { get; set; } = null!;
        public DbSet<RevaluationLine> RevaluationLines { get; set; } = null!;

        // ─── Phase 9 – Fixed Assets & Depreciation Module ──────────────────────────
        public DbSet<AssetCategory> AssetCategories { get; set; } = null!;
        public DbSet<FixedAsset> FixedAssets { get; set; } = null!;
        public DbSet<AssetDepreciationRun> AssetDepreciationRuns { get; set; } = null!;
        public DbSet<AssetDepreciationHistory> AssetDepreciationHistories { get; set; } = null!;
        public DbSet<AssetTransaction> AssetTransactions { get; set; } = null!;

        // ─── Phase 10 – Inventory Costing & COGS Module ──────────────────────────
        public DbSet<ItemWarehouseCost> ItemWarehouseCosts { get; set; } = null!;
        public DbSet<ItemCostHistory> ItemCostHistories { get; set; } = null!;
        public DbSet<CostAdjustmentRecord> CostAdjustmentRecords { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ─── Existing entity configurations ────────────────────────────────────
            builder.Entity<Branch>(b =>
            {
                b.Property(br => br.BranchCode).HasMaxLength(20).IsRequired();
                b.HasIndex(br => br.BranchCode).IsUnique().HasDatabaseName("IX_Branches_BranchCode");

                b.HasOne(br => br.Company)
                    .WithMany(c => c.Branches)
                    .HasForeignKey(br => br.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

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
                b.Property(c => c.IsBaseCurrency).HasDefaultValue(false);
                b.HasIndex(c => c.Code).IsUnique().HasDatabaseName("IX_Currencies_Code");
            });

            // 4.1 CurrencyDailyRate (Daily Exchange Rates)
            builder.Entity<CurrencyDailyRate>(b =>
            {
                b.ToTable("CurrencyDailyRates");
                b.HasKey(r => r.Id);
                b.Property(r => r.RateDate).HasColumnType("date").IsRequired();
                b.Property(r => r.ExchangeRate).HasColumnType("decimal(18, 6)").IsRequired();
                b.Property(r => r.Notes).HasMaxLength(500);
                b.Property(r => r.CreatedByUserId).HasMaxLength(100);
                b.Property(r => r.UpdatedByUserId).HasMaxLength(100);

                b.HasIndex(r => new { r.CurrencyId, r.RateDate }).IsUnique().HasDatabaseName("IX_CurrencyDailyRates_Currency_Date");
                b.HasIndex(r => r.RateDate).HasDatabaseName("IX_CurrencyDailyRates_RateDate");

                b.HasOne(r => r.Currency)
                    .WithMany()
                    .HasForeignKey(r => r.CurrencyId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 5. CustomerCategory (Branch-Based)
            builder.Entity<CustomerCategory>(b =>
            {
                b.ToTable("CustomerCategories");
                b.HasKey(cc => cc.Id);
                b.Property(cc => cc.Code).HasMaxLength(50).IsRequired();
                b.Property(cc => cc.NameAr).HasMaxLength(200).IsRequired();
                b.Property(cc => cc.NameEn).HasMaxLength(200).IsRequired();
                b.Property(cc => cc.IsDefault).HasDefaultValue(false);

                b.HasIndex(cc => new { cc.BranchId, cc.Code })
                    .IsUnique()
                    .HasDatabaseName("IX_CustomerCategories_BranchId_Code");

                b.HasOne(cc => cc.Branch)
                    .WithMany()
                    .HasForeignKey(cc => cc.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(cc => cc.ControlAccount)
                    .WithMany()
                    .HasForeignKey(cc => cc.ControlAccountId)
                    .OnDelete(DeleteBehavior.Restrict);
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
                b.Ignore(c => c.Mobile1);
                b.Property(c => c.Mobile2).HasMaxLength(50);
                b.Property(c => c.Email).HasMaxLength(100);
                b.Property(c => c.Notes).HasMaxLength(1000);

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

                b.HasOne(c => c.FinancialAccount)
                    .WithMany()
                    .HasForeignKey(c => c.FinancialAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.PartyGroup)
                    .WithMany(pg => pg.Customers)
                    .HasForeignKey(c => c.PartyGroupId)
                    .OnDelete(DeleteBehavior.SetNull);

                b.HasIndex(c => c.PartyGroupId)
                    .HasDatabaseName("IX_Customers_PartyGroupId");
            });

            // 6b. PartyGroup (Multi-Currency Customer & Vendor Group Linking)
            builder.Entity<PartyGroup>(b =>
            {
                b.ToTable("PartyGroups");
                b.HasKey(pg => pg.Id);
                b.Property(pg => pg.Code).HasMaxLength(50).IsRequired();
                b.Property(pg => pg.NameAr).HasMaxLength(200).IsRequired();
                b.Property(pg => pg.NameEn).HasMaxLength(200).IsRequired();
                b.Property(pg => pg.Notes).HasMaxLength(1000);

                b.HasIndex(pg => pg.Code)
                    .IsUnique()
                    .HasDatabaseName("IX_PartyGroups_Code");

                b.HasOne(pg => pg.Branch)
                    .WithMany()
                    .HasForeignKey(pg => pg.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 7. VendorCategory (Branch-Based)
            builder.Entity<VendorCategory>(b =>
            {
                b.ToTable("VendorCategories");
                b.HasKey(vc => vc.Id);
                b.Property(vc => vc.Code).HasMaxLength(50).IsRequired();
                b.Property(vc => vc.NameAr).HasMaxLength(200).IsRequired();
                b.Property(vc => vc.NameEn).HasMaxLength(200).IsRequired();
                b.Property(vc => vc.IsDefault).HasDefaultValue(false);

                b.HasIndex(vc => new { vc.BranchId, vc.Code })
                    .IsUnique()
                    .HasDatabaseName("IX_VendorCategories_BranchId_Code");

                b.HasOne(vc => vc.Branch)
                    .WithMany()
                    .HasForeignKey(vc => vc.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(vc => vc.ControlAccount)
                    .WithMany()
                    .HasForeignKey(vc => vc.ControlAccountId)
                    .OnDelete(DeleteBehavior.Restrict);
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
                b.Ignore(v => v.Mobile1);
                b.Property(v => v.Mobile2).HasMaxLength(50);
                b.Property(v => v.Email).HasMaxLength(100);
                b.Property(v => v.Notes).HasMaxLength(1000);

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

                b.HasOne(v => v.FinancialAccount)
                    .WithMany()
                    .HasForeignKey(v => v.FinancialAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(v => v.PartyGroup)
                    .WithMany(pg => pg.Vendors)
                    .HasForeignKey(v => v.PartyGroupId)
                    .OnDelete(DeleteBehavior.SetNull);

                b.HasIndex(v => v.PartyGroupId)
                    .HasDatabaseName("IX_Vendors_PartyGroupId");
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

            // ─── CodeSequences ──────────────────────────────────────────────────
            builder.Entity<CodeSequence>(b =>
            {
                b.ToTable("CodeSequences");
                b.HasKey(cs => cs.Id);
                b.Property(cs => cs.EntityName).HasMaxLength(50).IsRequired();
                b.Property(cs => cs.Prefix).HasMaxLength(10).IsRequired();
                b.Property(cs => cs.LastNumber).HasDefaultValue(0);
                b.Property(cs => cs.PaddingLength).HasDefaultValue(6);
                b.Property(cs => cs.IsAuto).HasDefaultValue(true);
                b.Property(cs => cs.IsManualAllowed).HasDefaultValue(true);

                b.HasIndex(cs => new { cs.EntityName, cs.BranchId })
                    .IsUnique()
                    .HasDatabaseName("IX_CodeSequences_EntityName_BranchId");

                b.HasOne(cs => cs.Branch)
                    .WithMany()
                    .HasForeignKey(cs => cs.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── Phase 4 – Core Accounting Configurations ─────────────────────────────
            // 1. GLAccount (Unified Chart of Accounts - Hierarchical)
            builder.Entity<GLAccount>(b =>
            {
                b.ToTable("GLAccounts");
                b.HasKey(a => a.Id);
                b.Property(a => a.Code).HasMaxLength(50).IsRequired();
                b.Property(a => a.NameAr).HasMaxLength(200).IsRequired();
                b.Property(a => a.NameEn).HasMaxLength(200).IsRequired();
                b.Property(a => a.Level).HasDefaultValue(1);
                b.Property(a => a.IsGroup).HasDefaultValue(false);
                b.Property(a => a.IsControlAccount).HasDefaultValue(false);
                b.Property(a => a.IsReconciliationAccount).HasDefaultValue(false);
                b.Property(a => a.RequiresCostCenter).HasDefaultValue(false);
                b.Property(a => a.IsActive).HasDefaultValue(true);

                b.HasIndex(a => a.Code).IsUnique().HasDatabaseName("IX_GLAccounts_Code");
                b.HasIndex(a => a.ParentId).HasDatabaseName("IX_GLAccounts_ParentId");
                b.HasIndex(a => a.IsGroup).HasDatabaseName("IX_GLAccounts_IsGroup");
                b.HasIndex(a => a.BranchId).HasDatabaseName("IX_GLAccounts_BranchId");
                b.HasIndex(a => new { a.IsControlAccount, a.SubLedgerType }).HasDatabaseName("IX_GLAccounts_Control_SubLedger");

                b.HasOne(a => a.Parent)
                    .WithMany(a => a.Children)
                    .HasForeignKey(a => a.ParentId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.Currency)
                    .WithMany()
                    .HasForeignKey(a => a.CurrencyId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.Branch)
                    .WithMany()
                    .HasForeignKey(a => a.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasQueryFilter(a => CurrentBranchId <= 0 || a.BranchId == null || a.BranchId == CurrentBranchId);
            });

            // 3. AccountPermission
            builder.Entity<AccountPermission>(b =>
            {
                b.ToTable("AccountPermissions");
                b.HasKey(p => p.Id);
                b.Property(p => p.UserId).HasMaxLength(450).IsRequired();
                b.HasIndex(p => new { p.AccountId, p.UserId }).IsUnique().HasDatabaseName("IX_AccountPermissions_Account_User");

                b.HasOne(p => p.Account)
                    .WithMany(a => a.Permissions)
                    .HasForeignKey(p => p.AccountId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // 4. CostCenter (Hierarchical)
            builder.Entity<CostCenter>(b =>
            {
                b.ToTable("CostCenters");
                b.HasKey(c => c.Id);
                b.Property(c => c.Code).HasMaxLength(50).IsRequired();
                b.Property(c => c.NameAr).HasMaxLength(200).IsRequired();
                b.Property(c => c.NameEn).HasMaxLength(200).IsRequired();
                b.HasIndex(c => c.Code).IsUnique().HasDatabaseName("IX_CostCenters_Code");

                b.HasOne(c => c.Parent)
                    .WithMany(c => c.Children)
                    .HasForeignKey(c => c.ParentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 5. JournalEntryHeader (Branch-Isolated & Auditable)
            builder.Entity<JournalEntryHeader>(b =>
            {
                b.ToTable("JournalEntryHeaders");
                b.HasKey(h => h.Id);
                b.Property(h => h.EntryNumber).HasMaxLength(50).IsRequired();
                b.Property(h => h.ReferenceNumber).HasMaxLength(100);
                b.Property(h => h.Description).HasMaxLength(500).IsRequired();
                b.Property(h => h.Notes).HasMaxLength(2000);
                b.Property(h => h.TotalDebitLocal).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);
                b.Property(h => h.TotalCreditLocal).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);
                b.Property(h => h.PostedByUserId).HasMaxLength(450);

                b.HasIndex(h => new { h.BranchId, h.EntryNumber }).IsUnique().HasDatabaseName("IX_JournalEntryHeaders_Branch_Number");
                b.HasIndex(h => new { h.BranchId, h.FiscalYearId, h.EntryDate }).HasDatabaseName("IX_JournalEntryHeaders_Branch_FY_Date");
                b.HasIndex(h => new { h.BranchId, h.Status, h.EntryDate }).HasDatabaseName("IX_JournalEntryHeaders_Branch_Status_Date");
                b.HasIndex(h => new { h.Status, h.EntryDate }).HasDatabaseName("IX_JournalEntryHeaders_Status_Date");
                b.HasIndex(h => new { h.SourceDocumentType, h.SourceDocumentId }).HasDatabaseName("IX_JournalEntryHeaders_SourceDoc");

                b.HasOne(h => h.Branch)
                    .WithMany()
                    .HasForeignKey(h => h.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.FiscalYear)
                    .WithMany()
                    .HasForeignKey(h => h.FiscalYearId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 6. JournalEntryLine (Multi-Currency & Sub-Ledger)
            builder.Entity<JournalEntryLine>(b =>
            {
                b.ToTable("JournalEntryLines");
                b.HasKey(l => l.Id);
                b.Property(l => l.Description).HasMaxLength(500).IsRequired();
                b.Property(l => l.Notes).HasMaxLength(1000);

                b.Property(l => l.ExchangeRate).HasColumnType("decimal(18, 6)").HasDefaultValue(1.000000m);
                b.Property(l => l.DebitForeign).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);
                b.Property(l => l.CreditForeign).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);
                b.Property(l => l.DebitLocal).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);
                b.Property(l => l.CreditLocal).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);

                b.HasIndex(l => new { l.JournalEntryHeaderId, l.LineNumber }).IsUnique().HasDatabaseName("IX_JournalEntryLines_Header_LineNumber");
                b.HasIndex(l => new { l.AccountId, l.SubLedgerType, l.SubLedgerId }).HasDatabaseName("IX_JournalEntryLines_Account_SubLedger");
                b.HasIndex(l => new { l.AccountId, l.CurrencyId }).HasDatabaseName("IX_JournalEntryLines_Account_Currency");
                b.HasIndex(l => new { l.SubLedgerType, l.SubLedgerId, l.CurrencyId }).HasDatabaseName("IX_JournalEntryLines_SubLedger_Currency");

                b.HasOne(l => l.JournalEntryHeader)
                    .WithMany(h => h.Lines)
                    .HasForeignKey(l => l.JournalEntryHeaderId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(l => l.Account)
                    .WithMany(a => a.JournalEntryLines)
                    .HasForeignKey(l => l.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(l => l.Currency)
                    .WithMany()
                    .HasForeignKey(l => l.CurrencyId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(l => l.CostCenter)
                    .WithMany(c => c.JournalEntryLines)
                    .HasForeignKey(l => l.CostCenterId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasQueryFilter(l => CurrentBranchId <= 0 || (l.JournalEntryHeader != null && l.JournalEntryHeader.BranchId == CurrentBranchId));
            });

            // ─── Phase 5 – Cash & Bank Voucher Module Configurations ────────────────
            builder.Entity<Bank>(b =>
            {
                b.ToTable("Banks");
                b.HasKey(x => x.Id);
                b.Property(x => x.Code).HasMaxLength(20).IsRequired();
                b.Property(x => x.NameAr).HasMaxLength(150).IsRequired();
                b.Property(x => x.NameEn).HasMaxLength(150).IsRequired();
                b.Property(x => x.SwiftCode).HasMaxLength(20);
                b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("IX_Banks_Code");
                b.HasIndex(x => x.IsActive).HasDatabaseName("IX_Banks_IsActive");
            });

            builder.Entity<VoucherHeader>(b =>
            {
                b.ToTable("VoucherHeaders");
                b.HasKey(h => h.Id);

                b.Property(h => h.VoucherNumber).HasMaxLength(50).IsRequired();
                b.Property(h => h.ReferenceNumber).HasMaxLength(100);
                b.Property(h => h.Notes).HasMaxLength(1000);
                b.Property(h => h.ChequeNumber).HasMaxLength(50);
                b.Property(h => h.BankName).HasMaxLength(150);

                b.Property(h => h.ExchangeRate).HasColumnType("decimal(18, 6)").HasDefaultValue(1.000000m);
                b.Property(h => h.TotalAmountForeign).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);
                b.Property(h => h.TotalAmountLocal).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);

                // Uniqueness per Branch + Fiscal Year + Voucher Type + Voucher Number
                b.HasIndex(h => new { h.BranchId, h.FiscalYearId, h.VoucherType, h.VoucherNumber })
                    .IsUnique()
                    .HasDatabaseName("IX_VoucherHeaders_Branch_Fiscal_Type_Num");

                b.HasIndex(h => h.VoucherDate).HasDatabaseName("IX_VoucherHeaders_VoucherDate");
                b.HasIndex(h => h.Status).HasDatabaseName("IX_VoucherHeaders_Status");
                b.HasIndex(h => h.CashBankAccountId).HasDatabaseName("IX_VoucherHeaders_CashBankAccountId");
                b.HasIndex(h => h.JournalEntryHeaderId).HasDatabaseName("IX_VoucherHeaders_JournalEntryHeaderId");

                b.HasOne(h => h.Branch)
                    .WithMany()
                    .HasForeignKey(h => h.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.FiscalYear)
                    .WithMany()
                    .HasForeignKey(h => h.FiscalYearId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.CashBankAccount)
                    .WithMany()
                    .HasForeignKey(h => h.CashBankAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.Currency)
                    .WithMany()
                    .HasForeignKey(h => h.CurrencyId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.JournalEntryHeader)
                    .WithMany()
                    .HasForeignKey(h => h.JournalEntryHeaderId)
                    .OnDelete(DeleteBehavior.SetNull);

                b.HasMany(h => h.Lines)
                    .WithOne(l => l.VoucherHeader)
                    .HasForeignKey(l => l.VoucherHeaderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<VoucherLine>(b =>
            {
                b.ToTable("VoucherLines");
                b.HasKey(l => l.Id);

                b.Property(l => l.Description).HasMaxLength(500);
                b.Property(l => l.Notes).HasMaxLength(500);
                b.Property(l => l.ChequeNumber).HasMaxLength(50);
                b.Property(l => l.BankName).HasMaxLength(150);
                b.Property(l => l.DrawerName).HasMaxLength(200);

                b.Property(l => l.AmountForeign).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);
                b.Property(l => l.AmountLocal).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);

                b.HasIndex(l => new { l.VoucherHeaderId, l.LineNumber })
                    .IsUnique()
                    .HasDatabaseName("IX_VoucherLines_Header_LineNumber");

                b.HasIndex(l => l.AccountId).HasDatabaseName("IX_VoucherLines_AccountId");
                b.HasIndex(l => new { l.LineType, l.SubLedgerId }).HasDatabaseName("IX_VoucherLines_LineType_SubLedgerId");
                b.HasIndex(l => new { l.ChequeNumber, l.BankId }).HasDatabaseName("IX_VoucherLines_ChequeNumber_BankId");
                b.HasIndex(l => new { l.PaymentMethod, l.ChequeStatus, l.DueDate }).HasDatabaseName("IX_VoucherLines_PaymentMethod_Status_DueDate");

                b.HasOne(l => l.VoucherHeader)
                    .WithMany(h => h.Lines)
                    .HasForeignKey(l => l.VoucherHeaderId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(l => l.Account)
                    .WithMany()
                    .HasForeignKey(l => l.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(l => l.CostCenter)
                    .WithMany()
                    .HasForeignKey(l => l.CostCenterId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(l => l.Bank)
                    .WithMany()
                    .HasForeignKey(l => l.BankId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasQueryFilter(l => CurrentBranchId <= 0 || (l.VoucherHeader != null && l.VoucherHeader.BranchId == CurrentBranchId));
            });

            // 4. UserPaymentMethodPermission
            builder.Entity<UserPaymentMethodPermission>(b =>
            {
                b.ToTable("UserPaymentMethodPermissions");
                b.HasKey(p => p.Id);
                b.Property(p => p.UserId).HasMaxLength(450).IsRequired();
                b.HasIndex(p => new { p.UserId, p.VoucherType, p.PaymentMethod })
                    .IsUnique()
                    .HasDatabaseName("IX_UserPaymentMethodPermissions_User_Type_Method");
            });

            // ─── Phase 7 – Universal Transaction Types & Document Engine ───────────────
            builder.Entity<TransactionType>(b =>
            {
                b.ToTable("TransactionTypes");
                b.HasKey(t => t.Id);
                b.Property(t => t.Code).HasMaxLength(50).IsRequired();
                b.Property(t => t.NameAr).HasMaxLength(150).IsRequired();
                b.Property(t => t.NameEn).HasMaxLength(150).IsRequired();
                b.Property(t => t.CodeSequencePrefix).HasMaxLength(20).IsRequired();
                b.Property(t => t.RowVersion).IsRowVersion();

                b.HasIndex(t => t.Code).IsUnique().HasDatabaseName("IX_TransactionTypes_Code");
                b.HasIndex(t => new { t.Module, t.IsActive }).HasDatabaseName("IX_TransactionTypes_Module_IsActive");

                b.HasOne(t => t.DefaultWarehouse).WithMany().HasForeignKey(t => t.DefaultWarehouseId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(t => t.Branch).WithMany().HasForeignKey(t => t.BranchId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(t => t.DefaultCustomer).WithMany().HasForeignKey(t => t.DefaultCustomerId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(t => t.DefaultVendor).WithMany().HasForeignKey(t => t.DefaultVendorId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(t => t.DefaultCashBankAccount).WithMany().HasForeignKey(t => t.DefaultCashBankAccountId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(t => t.DefaultDebitAccount).WithMany().HasForeignKey(t => t.DefaultDebitAccountId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(t => t.DefaultCreditAccount).WithMany().HasForeignKey(t => t.DefaultCreditAccountId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(t => t.DefaultTaxAccount).WithMany().HasForeignKey(t => t.DefaultTaxAccountId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(t => t.DefaultDiscountAccount).WithMany().HasForeignKey(t => t.DefaultDiscountAccountId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(t => t.DefaultCogsAccount).WithMany().HasForeignKey(t => t.DefaultCogsAccountId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(t => t.DefaultInventoryAccount).WithMany().HasForeignKey(t => t.DefaultInventoryAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<UserTransactionTypePermission>(b =>
            {
                b.ToTable("UserTransactionTypePermissions");
                b.HasKey(p => p.Id);
                b.Property(p => p.UserId).HasMaxLength(450).IsRequired();
                b.HasIndex(p => new { p.UserId, p.TransactionTypeId }).IsUnique().HasDatabaseName("IX_UserTransactionTypePermissions_User_Type");

                b.HasOne(p => p.TransactionType)
                    .WithMany(t => t.UserPermissions)
                    .HasForeignKey(p => p.TransactionTypeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<TransactionTypeConversion>(b =>
            {
                b.ToTable("TransactionTypeConversions");
                b.HasKey(c => c.Id);
                b.HasIndex(c => new { c.SourceTransactionTypeId, c.TargetTransactionTypeId })
                    .IsUnique()
                    .HasDatabaseName("IX_TransactionTypeConversions_Source_Target");

                b.HasOne(c => c.SourceTransactionType)
                    .WithMany(t => t.SourceConversions)
                    .HasForeignKey(c => c.SourceTransactionTypeId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.TargetTransactionType)
                    .WithMany(t => t.TargetConversions)
                    .HasForeignKey(c => c.TargetTransactionTypeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ItemSerialNumber>(b =>
            {
                b.ToTable("ItemSerialNumbers");
                b.HasKey(s => s.Id);
                b.Property(s => s.SerialNumber).HasMaxLength(100).IsRequired();
                b.Property(s => s.RowVersion).IsRowVersion();
                b.Property(s => s.CurrentPartyType).HasMaxLength(50);
                b.Property(s => s.CurrentPartyName).HasMaxLength(200);
                b.Property(s => s.AssetTag).HasMaxLength(100);
                b.Property(s => s.SourceDocType).HasMaxLength(50);
                b.Property(s => s.OutDocType).HasMaxLength(50);
                b.Property(s => s.UnitCost).HasPrecision(18, 4);
                b.Property(s => s.Notes).HasMaxLength(500);

                b.HasIndex(s => new { s.ItemId, s.SerialNumber }).IsUnique().HasDatabaseName("IX_ItemSerialNumbers_Item_Serial");
                b.HasIndex(s => new { s.BranchId, s.WarehouseId, s.ItemId, s.Status }).HasDatabaseName("IX_ItemSerialNumbers_Stock_Lookup");
                b.HasIndex(s => s.SerialNumber).HasDatabaseName("IX_ItemSerialNumbers_SerialNumber");

                b.HasOne(s => s.Item).WithMany().HasForeignKey(s => s.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(s => s.Branch).WithMany().HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(s => s.Warehouse).WithMany().HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ItemSerialMovement>(b =>
            {
                b.ToTable("ItemSerialMovements");
                b.HasKey(m => m.Id);
                b.Property(m => m.SerialNumber).HasMaxLength(100).IsRequired();
                b.Property(m => m.DocumentType).HasMaxLength(50);
                b.Property(m => m.DocumentNumber).HasMaxLength(50);
                b.Property(m => m.PartyName).HasMaxLength(200);
                b.Property(m => m.Notes).HasMaxLength(500);
                b.Property(m => m.UserId).HasMaxLength(100);

                b.HasIndex(m => new { m.ItemSerialNumberId, m.MovementDate }).HasDatabaseName("IX_ItemSerialMovements_Serial_Date");
                b.HasIndex(m => new { m.ItemId, m.SerialNumber }).HasDatabaseName("IX_ItemSerialMovements_Item_Serial");
                b.HasIndex(m => m.MovementDate).HasDatabaseName("IX_ItemSerialMovements_Date");

                b.HasOne(m => m.ItemSerialNumber)
                    .WithMany(s => s.Movements)
                    .HasForeignKey(m => m.ItemSerialNumberId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(m => m.Item).WithMany().HasForeignKey(m => m.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(m => m.FromBranch).WithMany().HasForeignKey(m => m.FromBranchId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(m => m.FromWarehouse).WithMany().HasForeignKey(m => m.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(m => m.ToBranch).WithMany().HasForeignKey(m => m.ToBranchId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(m => m.ToWarehouse).WithMany().HasForeignKey(m => m.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<DocumentHeader>(b =>
            {
                b.ToTable("DocumentHeaders");
                b.HasKey(h => h.Id);
                b.Property(h => h.DocumentNumber).HasMaxLength(50).IsRequired();
                b.Property(h => h.RowVersion).IsRowVersion();

                b.Property(h => h.ExchangeRate).HasPrecision(18, 6);
                b.Property(h => h.TotalAmountBeforeTax).HasPrecision(18, 4);
                b.Property(h => h.DiscountAmount).HasPrecision(18, 4);
                b.Property(h => h.TaxAmount).HasPrecision(18, 4);
                b.Property(h => h.TotalAmountNet).HasPrecision(18, 4);
                b.Property(h => h.PaymentMethod).HasColumnType("tinyint");
                b.Property(h => h.TotalAdditions).HasPrecision(18, 4).HasDefaultValue(0m);

                b.HasIndex(h => new { h.BranchId, h.FiscalYearId, h.TransactionTypeId, h.DocumentNumber })
                    .IsUnique()
                    .HasDatabaseName("IX_DocumentHeaders_Branch_FY_Type_Number");
                b.HasIndex(h => new { h.DocumentDate, h.Status }).HasDatabaseName("IX_DocumentHeaders_Date_Status");
                b.HasIndex(h => new { h.PartyType, h.PartyId }).HasDatabaseName("IX_DocumentHeaders_Party");

                b.HasOne(h => h.Branch).WithMany().HasForeignKey(h => h.BranchId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(h => h.FiscalYear).WithMany().HasForeignKey(h => h.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(h => h.TransactionType).WithMany().HasForeignKey(h => h.TransactionTypeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(h => h.Warehouse).WithMany().HasForeignKey(h => h.WarehouseId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(h => h.Currency).WithMany().HasForeignKey(h => h.CurrencyId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(h => h.CashBankAccount).WithMany().HasForeignKey(h => h.CashBankAccountId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(h => h.JournalEntryHeader).WithMany().HasForeignKey(h => h.JournalEntryHeaderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<DocumentLine>(b =>
            {
                b.ToTable("DocumentLines");
                b.HasKey(l => l.Id);
                b.Property(l => l.ConversionRate).HasPrecision(18, 4);
                b.Property(l => l.Quantity).HasPrecision(18, 4);
                b.Property(l => l.ConvertedQuantity).HasPrecision(18, 4);
                b.Property(l => l.UnitPrice).HasPrecision(18, 4);
                b.Property(l => l.DiscountAmount).HasPrecision(18, 4);
                b.Property(l => l.TaxRate).HasPrecision(18, 4);
                b.Property(l => l.TaxAmount).HasPrecision(18, 4);
                b.Property(l => l.LineTotal).HasPrecision(18, 4);
                b.Property(l => l.UnitCost).HasPrecision(18, 4);
                b.Property(l => l.TotalCost).HasPrecision(18, 4);

                b.HasIndex(l => new { l.DocumentHeaderId, l.LineNumber }).HasDatabaseName("IX_DocumentLines_Header_LineNum");
                b.HasIndex(l => l.ItemId).HasDatabaseName("IX_DocumentLines_ItemId");

                b.HasOne(l => l.DocumentHeader).WithMany(h => h.Lines).HasForeignKey(l => l.DocumentHeaderId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(l => l.Item).WithMany().HasForeignKey(l => l.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.Unit).WithMany().HasForeignKey(l => l.UnitId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.Warehouse).WithMany().HasForeignKey(l => l.WarehouseId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.CostCenter).WithMany().HasForeignKey(l => l.CostCenterId).OnDelete(DeleteBehavior.Restrict);

                b.HasQueryFilter(l => CurrentBranchId <= 0 || (l.DocumentHeader != null && l.DocumentHeader.BranchId == CurrentBranchId));
            });

            builder.Entity<DocumentLineSerial>(b =>
            {
                b.ToTable("DocumentLineSerials");
                b.HasKey(s => s.Id);
                b.Property(s => s.SerialNumber).HasMaxLength(100).IsRequired();

                b.HasOne(s => s.DocumentLine).WithMany(l => l.Serials).HasForeignKey(s => s.DocumentLineId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(s => s.ItemSerialNumber).WithMany().HasForeignKey(s => s.ItemSerialNumberId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<DocumentConversionLink>(b =>
            {
                b.ToTable("DocumentConversionLinks");
                b.HasKey(c => c.Id);
                b.HasIndex(c => new { c.SourceDocumentId, c.TargetDocumentId }).HasDatabaseName("IX_DocumentConversionLinks_Source_Target");

                b.HasOne(c => c.SourceDocument).WithMany(h => h.TargetLinks).HasForeignKey(c => c.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(c => c.TargetDocument).WithMany(h => h.SourceLinks).HasForeignKey(c => c.TargetDocumentId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<DocumentLineLink>(b =>
            {
                b.ToTable("DocumentLineLinks");
                b.HasKey(l => l.Id);
                b.Property(l => l.ConvertedQuantity).HasPrecision(18, 4);

                b.HasIndex(l => new { l.SourceDocumentLineId, l.TargetDocumentLineId }).HasDatabaseName("IX_DocumentLineLinks_Source_Target");

                b.HasOne(l => l.SourceDocumentLine).WithMany(sl => sl.TargetLineLinks).HasForeignKey(l => l.SourceDocumentLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.TargetDocumentLine).WithMany(tl => tl.SourceLineLinks).HasForeignKey(l => l.TargetDocumentLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.Item).WithMany().HasForeignKey(l => l.ItemId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<DocumentAdjustment>(b =>
            {
                b.ToTable("DocumentAdjustments");
                b.HasKey(a => a.Id);
                b.Property(a => a.Code).HasMaxLength(50).IsRequired();
                b.Property(a => a.NameAr).HasMaxLength(150).IsRequired();
                b.Property(a => a.NameEn).HasMaxLength(150).IsRequired();
                b.Property(a => a.DefaultValue).HasPrecision(18, 4);
                b.Property(a => a.RowVersion).IsRowVersion();

                b.HasIndex(a => a.Code).IsUnique().HasDatabaseName("IX_DocumentAdjustments_Code");
                b.HasIndex(a => new { a.AdjustmentType, a.IsActive }).HasDatabaseName("IX_DocumentAdjustments_Type_IsActive");

                b.HasOne(a => a.GLAccount).WithMany().HasForeignKey(a => a.GLAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<TransactionTypeAdjustment>(b =>
            {
                b.ToTable("TransactionTypeAdjustments");
                b.HasKey(ta => ta.Id);
                b.Property(ta => ta.RowVersion).IsRowVersion();

                b.HasIndex(ta => new { ta.TransactionTypeId, ta.DocumentAdjustmentId }).IsUnique().HasDatabaseName("IX_TransactionTypeAdjustments_Type_Adj");

                b.HasOne(ta => ta.TransactionType).WithMany(t => t.AllowedAdjustments).HasForeignKey(ta => ta.TransactionTypeId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(ta => ta.DocumentAdjustment).WithMany(a => a.AllowedTransactionTypes).HasForeignKey(ta => ta.DocumentAdjustmentId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<DocumentAdjustmentLine>(b =>
            {
                b.ToTable("DocumentAdjustmentLines");
                b.HasKey(al => al.Id);
                b.Property(al => al.Percentage).HasPrecision(18, 4);
                b.Property(al => al.Amount).HasPrecision(18, 4);
                b.Property(al => al.AmountLocal).HasPrecision(18, 4);
                b.Property(al => al.Notes).HasMaxLength(500);
                b.Property(al => al.RowVersion).IsRowVersion();

                b.HasIndex(al => new { al.DocumentHeaderId, al.LineNumber }).HasDatabaseName("IX_DocumentAdjustmentLines_Header_Line");

                b.HasOne(al => al.DocumentHeader).WithMany(h => h.Adjustments).HasForeignKey(al => al.DocumentHeaderId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(al => al.DocumentAdjustment).WithMany(a => a.DocumentLines).HasForeignKey(al => al.DocumentAdjustmentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(al => al.GLAccount).WithMany().HasForeignKey(al => al.GLAccountId).OnDelete(DeleteBehavior.Restrict);

                b.HasQueryFilter(al => CurrentBranchId <= 0 || (al.DocumentHeader != null && al.DocumentHeader.BranchId == CurrentBranchId));
            });

            builder.Entity<FiscalPeriod>(b =>
            {
                b.ToTable("FiscalPeriods");
                b.HasKey(p => p.Id);
                b.Property(p => p.NameAr).HasMaxLength(100).IsRequired();
                b.Property(p => p.NameEn).HasMaxLength(100).IsRequired();
                b.HasIndex(p => new { p.FiscalYearId, p.PeriodNumber }).IsUnique().HasDatabaseName("IX_FiscalPeriods_FY_PeriodNum");
                b.HasOne(p => p.FiscalYear)
                    .WithMany(y => y.Periods)
                    .HasForeignKey(p => p.FiscalYearId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ─── Global Query Filters for IMustHaveBranch ──────────────────────
            var branchEntityTypes = builder.Model.GetEntityTypes()
                .Where(t => typeof(IMustHaveBranch).IsAssignableFrom(t.ClrType))
                .Select(t => t.ClrType)
                .ToList();

            foreach (var clrType in branchEntityTypes)
            {
                var method = typeof(ErpDbContext)
                    .GetMethod(nameof(ConfigureGlobalBranchFilter), BindingFlags.NonPublic | BindingFlags.Instance)?
                    .MakeGenericMethod(clrType);
                method?.Invoke(this, new object[] { builder });
            }

            // ─── Concurrency Token (RowVersion / Timestamp) for IConcurrencyAware ──
            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                if (typeof(IConcurrencyAware).IsAssignableFrom(entityType.ClrType))
                {
                    builder.Entity(entityType.ClrType)
                        .Property(nameof(IConcurrencyAware.RowVersion))
                        .IsRowVersion();
                }
            }

            // ─── SystemFeedback Configuration ──────────────────────────────────
            builder.Entity<SystemFeedback>(entity =>
            {
                entity.ToTable("SystemFeedbacks");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TicketCode).HasMaxLength(50).IsRequired();
                entity.Property(e => e.Title).HasMaxLength(250).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(4000).IsRequired();
                entity.Property(e => e.AttachmentFileName).HasMaxLength(255);
                entity.Property(e => e.AttachmentPath).HasMaxLength(500);
                entity.Property(e => e.AttachmentContentType).HasMaxLength(100);
                entity.Property(e => e.CreatedByUserId).HasMaxLength(450);
                entity.Property(e => e.CreatedByUserName).HasMaxLength(256);
                entity.Property(e => e.DeveloperResponse).HasMaxLength(4000);
                entity.Property(e => e.RespondedByUserId).HasMaxLength(450);
                entity.Property(e => e.RespondedByUserName).HasMaxLength(256);

                entity.HasIndex(e => e.TicketCode).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.RequestType);
                entity.HasIndex(e => e.BranchId);

                entity.HasOne(e => e.Branch)
                    .WithMany()
                    .HasForeignKey(e => e.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── Phase 8 – Foreign Currency Revaluation Module ──────────────────────────
            builder.Entity<RevaluationHeader>(b =>
            {
                b.ToTable("RevaluationHeaders");
                b.Property(h => h.RowVersion).IsRowVersion();
                b.Property(h => h.RevaluationNumber).HasMaxLength(50).IsRequired();
                b.Property(h => h.Notes).HasMaxLength(1000);
                b.Property(h => h.TotalGain).HasPrecision(18, 4);
                b.Property(h => h.TotalLoss).HasPrecision(18, 4);
                b.Property(h => h.NetDifference).HasPrecision(18, 4);

                b.HasIndex(h => h.RevaluationNumber).IsUnique().HasDatabaseName("IX_RevaluationHeaders_Number");
                b.HasIndex(h => new { h.CompanyId, h.BranchId, h.RevaluationDate }).HasDatabaseName("IX_RevaluationHeaders_Company_Branch_Date");
                b.HasIndex(h => new { h.Status, h.RevaluationDate }).HasDatabaseName("IX_RevaluationHeaders_Status_Date");

                b.HasOne(h => h.Company)
                    .WithMany()
                    .HasForeignKey(h => h.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.Branch)
                    .WithMany()
                    .HasForeignKey(h => h.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.FiscalYear)
                    .WithMany()
                    .HasForeignKey(h => h.FiscalYearId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.Currency)
                    .WithMany()
                    .HasForeignKey(h => h.CurrencyId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.UnrealizedGainAccount)
                    .WithMany()
                    .HasForeignKey(h => h.UnrealizedGainAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.UnrealizedLossAccount)
                    .WithMany()
                    .HasForeignKey(h => h.UnrealizedLossAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.JournalEntry)
                    .WithMany()
                    .HasForeignKey(h => h.JournalEntryId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.ReversalJournalEntry)
                    .WithMany()
                    .HasForeignKey(h => h.ReversalJournalEntryId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasMany(h => h.Lines)
                    .WithOne(l => l.RevaluationHeader)
                    .HasForeignKey(l => l.RevaluationHeaderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<RevaluationLine>(b =>
            {
                b.ToTable("RevaluationLines");
                b.Property(l => l.SourceCode).HasMaxLength(50).IsRequired();
                b.Property(l => l.SourceName).HasMaxLength(250).IsRequired();
                b.Property(l => l.Notes).HasMaxLength(500);

                b.Property(l => l.ForeignBalance).HasPrecision(18, 4);
                b.Property(l => l.CarryingLocalAmount).HasPrecision(18, 4);
                b.Property(l => l.ExchangeRate).HasPrecision(18, 6);
                b.Property(l => l.RevaluedLocalAmount).HasPrecision(18, 4);
                b.Property(l => l.DifferenceAmount).HasPrecision(18, 4);

                b.HasIndex(l => new { l.RevaluationHeaderId, l.LineNumber }).IsUnique().HasDatabaseName("IX_RevaluationLines_Header_LineNumber");
                b.HasIndex(l => new { l.SourceType, l.SourceId }).HasDatabaseName("IX_RevaluationLines_Source");
                b.HasIndex(l => new { l.AccountId, l.CurrencyId }).HasDatabaseName("IX_RevaluationLines_Account_Currency");
                b.HasIndex(l => l.BranchId).HasDatabaseName("IX_RevaluationLines_Branch");

                b.HasOne(l => l.Account)
                    .WithMany()
                    .HasForeignKey(l => l.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(l => l.Customer)
                    .WithMany()
                    .HasForeignKey(l => l.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(l => l.Vendor)
                    .WithMany()
                    .HasForeignKey(l => l.VendorId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(l => l.Branch)
                    .WithMany()
                    .HasForeignKey(l => l.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(l => l.Currency)
                    .WithMany()
                    .HasForeignKey(l => l.CurrencyId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── Phase 9 – Fixed Assets & Depreciation Module ──────────────────────────
            builder.Entity<AssetCategory>(b =>
            {
                b.ToTable("AssetCategories");
                b.HasKey(c => c.Id);
                b.HasIndex(c => c.Code).IsUnique();
                b.Property(c => c.Code).HasMaxLength(50).IsRequired();
                b.Property(c => c.NameAr).HasMaxLength(200).IsRequired();
                b.Property(c => c.NameEn).HasMaxLength(200).IsRequired();
                b.Property(c => c.Description).HasMaxLength(500);
                b.Property(c => c.DefaultResidualValueRate).HasPrecision(18, 4);

                b.HasOne(c => c.AssetCostAccount)
                    .WithMany()
                    .HasForeignKey(c => c.AssetCostAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.AccumulatedDepreciationAccount)
                    .WithMany()
                    .HasForeignKey(c => c.AccumulatedDepreciationAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.DepreciationExpenseAccount)
                    .WithMany()
                    .HasForeignKey(c => c.DepreciationExpenseAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.DisposalGainAccount)
                    .WithMany()
                    .HasForeignKey(c => c.DisposalGainAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.DisposalLossAccount)
                    .WithMany()
                    .HasForeignKey(c => c.DisposalLossAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.CipAccount)
                    .WithMany()
                    .HasForeignKey(c => c.CipAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.Branch)
                    .WithMany()
                    .HasForeignKey(c => c.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<FixedAsset>(b =>
            {
                b.ToTable("FixedAssets");
                b.HasKey(a => a.Id);
                b.HasIndex(a => a.AssetCode).IsUnique();
                b.HasIndex(a => new { a.BranchId, a.AssetCategoryId, a.Status });
                b.HasIndex(a => a.SerialNumber);
                b.HasIndex(a => a.Barcode);

                b.Property(a => a.AssetCode).HasMaxLength(50).IsRequired();
                b.Property(a => a.NameAr).HasMaxLength(200).IsRequired();
                b.Property(a => a.NameEn).HasMaxLength(200).IsRequired();
                b.Property(a => a.SerialNumber).HasMaxLength(100);
                b.Property(a => a.Barcode).HasMaxLength(100);
                b.Property(a => a.ImagePath).HasMaxLength(500);
                b.Property(a => a.Department).HasMaxLength(150);
                b.Property(a => a.CustodianEmployeeName).HasMaxLength(150);
                b.Property(a => a.PurchaseInvoiceReference).HasMaxLength(100);
                b.Property(a => a.DisposalReason).HasMaxLength(500);

                b.Property(a => a.PurchasePriceForeign).HasPrecision(18, 4);
                b.Property(a => a.ExchangeRate).HasPrecision(18, 6);
                b.Property(a => a.InitialCostLocal).HasPrecision(18, 4);
                b.Property(a => a.AdditionalExpensesLocal).HasPrecision(18, 4);
                b.Property(a => a.CurrentCostLocal).HasPrecision(18, 4);
                b.Property(a => a.ResidualValue).HasPrecision(18, 4);
                b.Property(a => a.DepreciationRate).HasPrecision(18, 4);
                b.Property(a => a.TotalExpectedUnits).HasPrecision(18, 4);
                b.Property(a => a.TotalProducedUnits).HasPrecision(18, 4);
                b.Property(a => a.OpeningCarryingValue).HasPrecision(18, 4);
                b.Property(a => a.OpeningAccumulatedDepreciation).HasPrecision(18, 4);
                b.Property(a => a.CurrentAccumulatedDepreciation).HasPrecision(18, 4);
                b.Property(a => a.CurrentBookValue).HasPrecision(18, 4);
                b.Property(a => a.DisposalProceeds).HasPrecision(18, 4);
                b.Property(a => a.DisposalExpenses).HasPrecision(18, 4);

                b.HasOne(a => a.AssetCategory)
                    .WithMany(c => c.Assets)
                    .HasForeignKey(a => a.AssetCategoryId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.Branch)
                    .WithMany()
                    .HasForeignKey(a => a.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.Warehouse)
                    .WithMany()
                    .HasForeignKey(a => a.WarehouseId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.CostCenter)
                    .WithMany()
                    .HasForeignKey(a => a.CostCenterId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.Vendor)
                    .WithMany()
                    .HasForeignKey(a => a.VendorId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.Currency)
                    .WithMany()
                    .HasForeignKey(a => a.CurrencyId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.PurchaseDocumentHeader)
                    .WithMany()
                    .HasForeignKey(a => a.PurchaseDocumentHeaderId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.DisposalJournalEntry)
                    .WithMany()
                    .HasForeignKey(a => a.DisposalJournalEntryId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.AssetCostAccount)
                    .WithMany()
                    .HasForeignKey(a => a.AssetCostAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.AccumulatedDepreciationAccount)
                    .WithMany()
                    .HasForeignKey(a => a.AccumulatedDepreciationAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.DepreciationExpenseAccount)
                    .WithMany()
                    .HasForeignKey(a => a.DepreciationExpenseAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.DisposalGainAccount)
                    .WithMany()
                    .HasForeignKey(a => a.DisposalGainAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(a => a.DisposalLossAccount)
                    .WithMany()
                    .HasForeignKey(a => a.DisposalLossAccountId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<AssetDepreciationRun>(b =>
            {
                b.ToTable("AssetDepreciationRuns");
                b.HasKey(r => r.Id);
                b.HasIndex(r => r.RunNumber).IsUnique();
                b.HasIndex(r => new { r.CompanyId, r.BranchId, r.RunDate, r.FiscalPeriodId });

                b.Property(r => r.RunNumber).HasMaxLength(50).IsRequired();
                b.Property(r => r.TotalDepreciationAmount).HasPrecision(18, 4);
                b.Property(r => r.Notes).HasMaxLength(1000);

                b.HasOne(r => r.Company)
                    .WithMany()
                    .HasForeignKey(r => r.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(r => r.Branch)
                    .WithMany()
                    .HasForeignKey(r => r.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(r => r.FiscalYear)
                    .WithMany()
                    .HasForeignKey(r => r.FiscalYearId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(r => r.FiscalPeriod)
                    .WithMany()
                    .HasForeignKey(r => r.FiscalPeriodId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(r => r.AssetCategory)
                    .WithMany()
                    .HasForeignKey(r => r.AssetCategoryId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(r => r.JournalEntry)
                    .WithMany()
                    .HasForeignKey(r => r.JournalEntryId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(r => r.ReversalJournalEntry)
                    .WithMany()
                    .HasForeignKey(r => r.ReversalJournalEntryId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<AssetDepreciationHistory>(b =>
            {
                b.ToTable("AssetDepreciationHistories");
                b.HasKey(h => h.Id);
                b.HasIndex(h => new { h.AssetDepreciationRunId, h.AssetId, h.DepreciationDate });
                b.HasIndex(h => new { h.AssetId, h.FiscalPeriodId });

                b.Property(h => h.AssetCostAtRun).HasPrecision(18, 4);
                b.Property(h => h.PriorAccumulatedDepreciation).HasPrecision(18, 4);
                b.Property(h => h.DepreciationAmount).HasPrecision(18, 4);
                b.Property(h => h.PostAccumulatedDepreciation).HasPrecision(18, 4);
                b.Property(h => h.BookValueBefore).HasPrecision(18, 4);
                b.Property(h => h.BookValueAfter).HasPrecision(18, 4);

                b.HasOne(h => h.AssetDepreciationRun)
                    .WithMany(r => r.Histories)
                    .HasForeignKey(h => h.AssetDepreciationRunId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(h => h.Asset)
                    .WithMany(a => a.DepreciationHistories)
                    .HasForeignKey(h => h.AssetId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.Branch)
                    .WithMany()
                    .HasForeignKey(h => h.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.CostCenter)
                    .WithMany()
                    .HasForeignKey(h => h.CostCenterId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.ExpenseAccount)
                    .WithMany()
                    .HasForeignKey(h => h.ExpenseAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(h => h.AccumulatedAccount)
                    .WithMany()
                    .HasForeignKey(h => h.AccumulatedAccountId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<AssetTransaction>(b =>
            {
                b.ToTable("AssetTransactions");
                b.HasKey(t => t.Id);
                b.HasIndex(t => new { t.AssetId, t.TransactionDate, t.TransactionType });

                b.Property(t => t.Amount).HasPrecision(18, 4);
                b.Property(t => t.FromDepartment).HasMaxLength(150);
                b.Property(t => t.ToDepartment).HasMaxLength(150);
                b.Property(t => t.FromCustodian).HasMaxLength(150);
                b.Property(t => t.ToCustodian).HasMaxLength(150);
                b.Property(t => t.Reason).HasMaxLength(500);

                b.HasOne(t => t.Asset)
                    .WithMany(a => a.Transactions)
                    .HasForeignKey(t => t.AssetId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(t => t.FromBranch)
                    .WithMany()
                    .HasForeignKey(t => t.FromBranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(t => t.ToBranch)
                    .WithMany()
                    .HasForeignKey(t => t.ToBranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(t => t.FromWarehouse)
                    .WithMany()
                    .HasForeignKey(t => t.FromWarehouseId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(t => t.ToWarehouse)
                    .WithMany()
                    .HasForeignKey(t => t.ToWarehouseId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(t => t.FromCostCenter)
                    .WithMany()
                    .HasForeignKey(t => t.FromCostCenterId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(t => t.ToCostCenter)
                    .WithMany()
                    .HasForeignKey(t => t.ToCostCenterId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(t => t.JournalEntry)
                    .WithMany()
                    .HasForeignKey(t => t.JournalEntryId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ─── Phase 10 – Inventory Costing & COGS Module Configurations ──────────
            builder.Entity<ItemWarehouseCost>(b =>
            {
                b.ToTable("ItemWarehouseCosts");
                b.HasKey(c => c.Id);
                b.HasIndex(c => new { c.ItemId, c.WarehouseId, c.BranchId }).IsUnique().HasDatabaseName("IX_ItemWarehouseCosts_Item_WH_Branch");
                b.Property(c => c.CurrentQuantity).HasPrecision(18, 4);
                b.Property(c => c.TotalCost).HasPrecision(18, 4);
                b.Property(c => c.MovingAverageCost).HasPrecision(18, 4);
                b.Property(c => c.RowVersion).IsRowVersion();

                b.HasOne(c => c.Item).WithMany().HasForeignKey(c => c.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(c => c.Warehouse).WithMany().HasForeignKey(c => c.WarehouseId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(c => c.Branch).WithMany().HasForeignKey(c => c.BranchId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(c => c.LastDocument).WithMany().HasForeignKey(c => c.LastDocumentId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ItemCostHistory>(b =>
            {
                b.ToTable("ItemCostHistories");
                b.HasKey(h => h.Id);
                b.HasIndex(h => new { h.ItemId, h.WarehouseId, h.BranchId, h.MovementDate }).HasDatabaseName("IX_ItemCostHistories_Item_WH_Branch_Date");
                b.HasIndex(h => h.DocumentHeaderId).HasDatabaseName("IX_ItemCostHistories_DocumentHeaderId");

                b.Property(h => h.Quantity).HasPrecision(18, 4);
                b.Property(h => h.UnitCost).HasPrecision(18, 4);
                b.Property(h => h.TotalCost).HasPrecision(18, 4);
                b.Property(h => h.PreQuantity).HasPrecision(18, 4);
                b.Property(h => h.PreTotalCost).HasPrecision(18, 4);
                b.Property(h => h.PreAverageCost).HasPrecision(18, 4);
                b.Property(h => h.PostQuantity).HasPrecision(18, 4);
                b.Property(h => h.PostTotalCost).HasPrecision(18, 4);
                b.Property(h => h.PostAverageCost).HasPrecision(18, 4);
                b.Property(h => h.Notes).HasMaxLength(500);

                b.HasOne(h => h.Item).WithMany().HasForeignKey(h => h.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(h => h.Warehouse).WithMany().HasForeignKey(h => h.WarehouseId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(h => h.Branch).WithMany().HasForeignKey(h => h.BranchId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(h => h.DocumentHeader).WithMany().HasForeignKey(h => h.DocumentHeaderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(h => h.DocumentLine).WithMany().HasForeignKey(h => h.DocumentLineId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<CostAdjustmentRecord>(b =>
            {
                b.ToTable("CostAdjustmentRecords");
                b.HasKey(r => r.Id);
                b.Property(r => r.BatchNumber).HasMaxLength(50).IsRequired();
                b.Property(r => r.Reason).HasMaxLength(500);
                b.Property(r => r.CreatedByUserId).HasMaxLength(450);

                b.HasIndex(r => r.BatchNumber).HasDatabaseName("IX_CostAdjustmentRecords_BatchNumber");
                b.HasIndex(r => new { r.ItemId, r.WarehouseId, r.BranchId }).HasDatabaseName("IX_CostAdjustmentRecords_Item_WH_Branch");
                b.HasIndex(r => r.SourceDocumentHeaderId).HasDatabaseName("IX_CostAdjustmentRecords_SourceDoc");
                b.HasIndex(r => r.AffectedDocumentHeaderId).HasDatabaseName("IX_CostAdjustmentRecords_AffectedDoc");

                b.Property(r => r.OriginalUnitCost).HasPrecision(18, 4);
                b.Property(r => r.RecalculatedUnitCost).HasPrecision(18, 4);
                b.Property(r => r.OriginalTotalCost).HasPrecision(18, 4);
                b.Property(r => r.RecalculatedTotalCost).HasPrecision(18, 4);
                b.Property(r => r.CostDifference).HasPrecision(18, 4);

                b.HasOne(r => r.Item).WithMany().HasForeignKey(r => r.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(r => r.Warehouse).WithMany().HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(r => r.Branch).WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(r => r.SourceDocumentHeader).WithMany().HasForeignKey(r => r.SourceDocumentHeaderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(r => r.AffectedDocumentHeader).WithMany().HasForeignKey(r => r.AffectedDocumentHeaderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(r => r.AffectedDocumentLine).WithMany().HasForeignKey(r => r.AffectedDocumentLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(r => r.AdjustmentJournalEntry).WithMany().HasForeignKey(r => r.AdjustmentJournalEntryId).OnDelete(DeleteBehavior.Restrict);
            });
        }

        private void ConfigureGlobalBranchFilter<T>(ModelBuilder builder) where T : class, IMustHaveBranch
        {
            builder.Entity<T>().HasQueryFilter(e => CurrentBranchId <= 0 || e.BranchId == CurrentBranchId);
        }

        public override int SaveChanges()
        {
            ApplyBranchSecurity();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyBranchSecurity();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void ApplyBranchSecurity()
        {
            var entries = ChangeTracker.Entries<IMustHaveBranch>().ToList();
            if (!entries.Any()) return;

            var isSuper = IsSuperAdmin;
            var currentBranch = CurrentBranchId;

            foreach (var entry in entries)
            {
                if (entry.State == EntityState.Added)
                {
                    // If user is not superadmin, or if BranchId was not explicitly set (> 0)
                    if (!isSuper || entry.Entity.BranchId <= 0)
                    {
                        if (currentBranch > 0)
                        {
                            entry.Entity.BranchId = currentBranch;
                        }
                    }
                }
                else if (entry.State == EntityState.Modified)
                {
                    var branchProp = entry.Property(e => e.BranchId);
                    if (branchProp.IsModified && !isSuper)
                    {
                        // Prevent non-superadmins from tampering with BranchId across requests
                        branchProp.CurrentValue = branchProp.OriginalValue;
                        branchProp.IsModified = false;
                    }
                }
            }
        }
    }
}
