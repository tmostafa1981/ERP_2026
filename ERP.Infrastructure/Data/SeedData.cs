using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Domain.Entities;
using ERP.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Infrastructure.Data
{
    public static class SeedData
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<ErpDbContext>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            context.Database.EnsureCreated();

            // ─── Auto-migrate schema changes for backward compatibility ───────────────
            try
            {
                // 1. Companies table
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Companies')
                    BEGIN
                        CREATE TABLE Companies (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Code NVARCHAR(50) NOT NULL,
                            Name NVARCHAR(200) NOT NULL,
                            NameAr NVARCHAR(200) NULL,
                            NameEn NVARCHAR(200) NULL,
                            AddressAr NVARCHAR(500) NULL,
                            AddressEn NVARCHAR(500) NULL,
                            Phone NVARCHAR(50) NULL,
                            Email NVARCHAR(100) NULL,
                            TaxNumber NVARCHAR(50) NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL
                        );
                    END
                ");

                // 2. Branches table extra columns
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Branches' AND COLUMN_NAME = 'CompanyId')
                    BEGIN ALTER TABLE Branches ADD CompanyId INT NOT NULL DEFAULT 1; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Branches' AND COLUMN_NAME = 'NameAr')
                    BEGIN ALTER TABLE Branches ADD NameAr NVARCHAR(200) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Branches' AND COLUMN_NAME = 'NameEn')
                    BEGIN ALTER TABLE Branches ADD NameEn NVARCHAR(200) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Branches' AND COLUMN_NAME = 'AddressAr')
                    BEGIN ALTER TABLE Branches ADD AddressAr NVARCHAR(500) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Branches' AND COLUMN_NAME = 'AddressEn')
                    BEGIN ALTER TABLE Branches ADD AddressEn NVARCHAR(500) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Branches' AND COLUMN_NAME = 'Phone')
                    BEGIN ALTER TABLE Branches ADD Phone NVARCHAR(50) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Branches' AND COLUMN_NAME = 'Email')
                    BEGIN ALTER TABLE Branches ADD Email NVARCHAR(100) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Branches' AND COLUMN_NAME = 'CreatedAt')
                    BEGIN ALTER TABLE Branches ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(); END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Branches' AND COLUMN_NAME = 'UpdatedAt')
                    BEGIN ALTER TABLE Branches ADD UpdatedAt DATETIME2 NULL; END
                ");

                // 3. FiscalYears extra columns
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'FiscalYears' AND COLUMN_NAME = 'Year')
                    BEGIN ALTER TABLE FiscalYears ADD Year NVARCHAR(50) NOT NULL DEFAULT '2026'; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'FiscalYears' AND COLUMN_NAME = 'NameAr')
                    BEGIN ALTER TABLE FiscalYears ADD NameAr NVARCHAR(200) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'FiscalYears' AND COLUMN_NAME = 'NameEn')
                    BEGIN ALTER TABLE FiscalYears ADD NameEn NVARCHAR(200) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'FiscalYears' AND COLUMN_NAME = 'CreatedAt')
                    BEGIN ALTER TABLE FiscalYears ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(); END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'FiscalYears' AND COLUMN_NAME = 'UpdatedAt')
                    BEGIN ALTER TABLE FiscalYears ADD UpdatedAt DATETIME2 NULL; END
                ");

                // 4. AspNetUsers: HasAllBranches, HasAllCompanies, EmployeeCode, FullNameAr, FullNameEn, Mobile, IsActive, PasswordResetRequired
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'HasAllBranches')
                    BEGIN ALTER TABLE AspNetUsers ADD HasAllBranches BIT NOT NULL DEFAULT 0; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'HasAllCompanies')
                    BEGIN ALTER TABLE AspNetUsers ADD HasAllCompanies BIT NOT NULL DEFAULT 0; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'EmployeeCode')
                    BEGIN ALTER TABLE AspNetUsers ADD EmployeeCode NVARCHAR(50) NOT NULL DEFAULT ''; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'FullNameAr')
                    BEGIN ALTER TABLE AspNetUsers ADD FullNameAr NVARCHAR(200) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'FullNameEn')
                    BEGIN ALTER TABLE AspNetUsers ADD FullNameEn NVARCHAR(200) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'Mobile')
                    BEGIN ALTER TABLE AspNetUsers ADD Mobile NVARCHAR(50) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'IsActive')
                    BEGIN ALTER TABLE AspNetUsers ADD IsActive BIT NOT NULL DEFAULT 1; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'PasswordResetRequired')
                    BEGIN ALTER TABLE AspNetUsers ADD PasswordResetRequired BIT NOT NULL DEFAULT 0; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'CreatedAt')
                    BEGIN ALTER TABLE AspNetUsers ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(); END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'CreatedBy')
                    BEGIN ALTER TABLE AspNetUsers ADD CreatedBy NVARCHAR(200) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'UpdatedAt')
                    BEGIN ALTER TABLE AspNetUsers ADD UpdatedAt DATETIME2 NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'UpdatedBy')
                    BEGIN ALTER TABLE AspNetUsers ADD UpdatedBy NVARCHAR(200) NULL; END
                ");

                // 5. AspNetRoles: NameAr, NameEn, Description, IsActive, CreatedAt
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetRoles' AND COLUMN_NAME = 'NameAr')
                    BEGIN ALTER TABLE AspNetRoles ADD NameAr NVARCHAR(200) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetRoles' AND COLUMN_NAME = 'NameEn')
                    BEGIN ALTER TABLE AspNetRoles ADD NameEn NVARCHAR(200) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetRoles' AND COLUMN_NAME = 'Description')
                    BEGIN ALTER TABLE AspNetRoles ADD Description NVARCHAR(500) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetRoles' AND COLUMN_NAME = 'IsActive')
                    BEGIN ALTER TABLE AspNetRoles ADD IsActive BIT NOT NULL DEFAULT 1; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetRoles' AND COLUMN_NAME = 'CreatedAt')
                    BEGIN ALTER TABLE AspNetRoles ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(); END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetRoles' AND COLUMN_NAME = 'CreatedBy')
                    BEGIN ALTER TABLE AspNetRoles ADD CreatedBy NVARCHAR(200) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetRoles' AND COLUMN_NAME = 'UpdatedAt')
                    BEGIN ALTER TABLE AspNetRoles ADD UpdatedAt DATETIME2 NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetRoles' AND COLUMN_NAME = 'UpdatedBy')
                    BEGIN ALTER TABLE AspNetRoles ADD UpdatedBy NVARCHAR(200) NULL; END
                ");

                // 7. Permissions table schema check
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Permissions' AND COLUMN_NAME = 'Code')
                    BEGIN
                        DROP TABLE IF EXISTS Permissions;
                        CREATE TABLE Permissions (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Code NVARCHAR(150) NOT NULL UNIQUE,
                            Module NVARCHAR(100) NOT NULL,
                            Screen NVARCHAR(100) NOT NULL,
                            Action NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            Description NVARCHAR(500) NULL,
                            SortOrder INT NOT NULL DEFAULT 0
                        );
                    END
                ");

                // 8. RolePermissions table
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'RolePermissions')
                    BEGIN
                        CREATE TABLE RolePermissions (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            RoleId NVARCHAR(450) NOT NULL,
                            PermissionId INT NOT NULL,
                            IsGranted BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            CONSTRAINT FK_RolePermissions_Permission FOREIGN KEY (PermissionId) REFERENCES Permissions(Id) ON DELETE CASCADE
                        );
                    END
                ");

                // 9. AuditLogs table (BIGINT PK – central audit trail)
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AuditLogs')
                    BEGIN
                        CREATE TABLE AuditLogs (
                            Id             BIGINT        IDENTITY(1,1) PRIMARY KEY,
                            TimestampUtc   DATETIME2     NOT NULL,
                            UserId         NVARCHAR(450) NULL,
                            EmployeeCode   NVARCHAR(50)  NULL,
                            UserName       NVARCHAR(256) NULL,
                            CompanyId      INT           NULL,
                            SisterCompanyId INT          NULL,
                            BranchId       INT           NULL,
                            FiscalYearId   INT           NULL,
                            ScreenCode     NVARCHAR(20)  NULL,
                            ScreenName     NVARCHAR(100) NULL,
                            ModuleName     NVARCHAR(100) NULL,
                            EntityName     NVARCHAR(100) NULL,
                            EntityId       NVARCHAR(100) NULL,
                            Action         NVARCHAR(50)  NOT NULL,
                            Description    NVARCHAR(500) NULL,
                            OldValues      NVARCHAR(MAX) NULL,
                            NewValues      NVARCHAR(MAX) NULL,
                            ChangedValues  NVARCHAR(MAX) NULL,
                            IpAddress      NVARCHAR(50)  NULL,
                            UserAgent      NVARCHAR(500) NULL,
                            RequestId      NVARCHAR(100) NULL,
                            CorrelationId  NVARCHAR(100) NULL,
                            HttpMethod     NVARCHAR(10)  NULL,
                            RequestPath    NVARCHAR(500) NULL,
                            Success        BIT           NOT NULL DEFAULT 1,
                            ErrorMessage   NVARCHAR(2000) NULL
                        );
                        -- Composite Indexes
                        CREATE INDEX IX_AuditLogs_TimestampUtc              ON AuditLogs (TimestampUtc DESC);
                        CREATE INDEX IX_AuditLogs_UserId                    ON AuditLogs (UserId);
                        CREATE INDEX IX_AuditLogs_Action                    ON AuditLogs (Action);
                        CREATE INDEX IX_AuditLogs_Success                   ON AuditLogs (Success);
                        CREATE INDEX IX_AuditLogs_Branch_Timestamp          ON AuditLogs (BranchId, TimestampUtc DESC);
                        CREATE INDEX IX_AuditLogs_User_Timestamp            ON AuditLogs (UserId, TimestampUtc DESC);
                        CREATE INDEX IX_AuditLogs_Screen_Timestamp          ON AuditLogs (ScreenCode, TimestampUtc DESC);
                        CREATE INDEX IX_AuditLogs_Entity                    ON AuditLogs (EntityName, EntityId);
                        CREATE INDEX IX_AuditLogs_Company_Branch_Timestamp  ON AuditLogs (CompanyId, BranchId, TimestampUtc DESC);
                        CREATE INDEX IX_AuditLogs_Action_Success_Timestamp  ON AuditLogs (Action, Success, TimestampUtc DESC);
                    END
                ");

                // 10. Phase 3 Master Data Tables
                await context.Database.ExecuteSqlRawAsync(@"
                    -- Governorates
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Governorates')
                    BEGIN
                        CREATE TABLE Governorates (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL
                        );
                        CREATE UNIQUE INDEX IX_Governorates_Code ON Governorates (Code);
                    END

                    -- Cities
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Cities')
                    BEGIN
                        CREATE TABLE Cities (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            GovernorateId INT NOT NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_Cities_Governorates FOREIGN KEY (GovernorateId) REFERENCES Governorates(Id)
                        );
                        CREATE UNIQUE INDEX IX_Cities_Code ON Cities (Code);
                    END

                    -- Currencies
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Currencies')
                    BEGIN
                        CREATE TABLE Currencies (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Code NVARCHAR(10) NOT NULL,
                            NameAr NVARCHAR(100) NOT NULL,
                            NameEn NVARCHAR(100) NOT NULL,
                            Symbol NVARCHAR(10) NULL,
                            ExchangeRate DECIMAL(18,4) NOT NULL DEFAULT 1.0,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL
                        );
                        CREATE UNIQUE INDEX IX_Currencies_Code ON Currencies (Code);
                    END

                    -- CustomerCategories
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CustomerCategories')
                    BEGIN
                        CREATE TABLE CustomerCategories (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            ControlAccountId INT NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL
                        );
                        CREATE UNIQUE INDEX IX_CustomerCategories_Code ON CustomerCategories (Code);
                    END

                    -- VendorCategories
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'VendorCategories')
                    BEGIN
                        CREATE TABLE VendorCategories (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            ControlAccountId INT NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL
                        );
                        CREATE UNIQUE INDEX IX_VendorCategories_Code ON VendorCategories (Code);
                    END

                    -- Units
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Units')
                    BEGIN
                        CREATE TABLE Units (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(100) NOT NULL,
                            NameEn NVARCHAR(100) NOT NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL
                        );
                        CREATE UNIQUE INDEX IX_Units_Code ON Units (Code);
                    END

                    -- ItemGroups
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ItemGroups')
                    BEGIN
                        CREATE TABLE ItemGroups (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            ParentId INT NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_ItemGroups_Parent FOREIGN KEY (ParentId) REFERENCES ItemGroups(Id)
                        );
                        CREATE UNIQUE INDEX IX_ItemGroups_Code ON ItemGroups (Code);
                    END

                    -- Items
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Items')
                    BEGIN
                        CREATE TABLE Items (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Code NVARCHAR(50) NOT NULL,
                            Barcode NVARCHAR(100) NULL,
                            TaxCode NVARCHAR(50) NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            ItemGroupId INT NOT NULL,
                            IsService BIT NOT NULL DEFAULT 0,
                            StorageLocation NVARCHAR(200) NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_Items_ItemGroups FOREIGN KEY (ItemGroupId) REFERENCES ItemGroups(Id)
                        );
                        CREATE UNIQUE INDEX IX_Items_Code ON Items (Code);
                        CREATE INDEX IX_Items_Barcode ON Items (Barcode);
                    END

                    -- Salesmen (Branch-Based)
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Salesmen')
                    BEGIN
                        CREATE TABLE Salesmen (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            BranchId INT NOT NULL,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            Mobile NVARCHAR(50) NULL,
                            Phone NVARCHAR(50) NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_Salesmen_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id)
                        );
                        CREATE UNIQUE INDEX IX_Salesmen_BranchId_Code ON Salesmen (BranchId, Code);
                    END

                    -- Customers (Branch-Based)
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Customers')
                    BEGIN
                        CREATE TABLE Customers (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            BranchId INT NOT NULL,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            CustomerCategoryId INT NOT NULL,
                            Address NVARCHAR(500) NULL,
                            GovernorateId INT NULL,
                            CityId INT NULL,
                            TaxNumber NVARCHAR(50) NULL,
                            CommercialRegister NVARCHAR(50) NULL,
                            TaxPayerType NVARCHAR(50) NULL,
                            SalesmanId INT NULL,
                            CurrencyId INT NULL,
                            Phone NVARCHAR(50) NULL,
                            Mobile NVARCHAR(50) NULL,
                            Email NVARCHAR(100) NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_Customers_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id),
                            CONSTRAINT FK_Customers_CustomerCategories FOREIGN KEY (CustomerCategoryId) REFERENCES CustomerCategories(Id),
                            CONSTRAINT FK_Customers_Governorates FOREIGN KEY (GovernorateId) REFERENCES Governorates(Id),
                            CONSTRAINT FK_Customers_Cities FOREIGN KEY (CityId) REFERENCES Cities(Id),
                            CONSTRAINT FK_Customers_Salesmen FOREIGN KEY (SalesmanId) REFERENCES Salesmen(Id),
                            CONSTRAINT FK_Customers_Currencies FOREIGN KEY (CurrencyId) REFERENCES Currencies(Id)
                        );
                        CREATE UNIQUE INDEX IX_Customers_BranchId_Code ON Customers (BranchId, Code);
                    END

                    -- Vendors (Branch-Based)
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Vendors')
                    BEGIN
                        CREATE TABLE Vendors (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            BranchId INT NOT NULL,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            VendorCategoryId INT NOT NULL,
                            Address NVARCHAR(500) NULL,
                            GovernorateId INT NULL,
                            CityId INT NULL,
                            TaxNumber NVARCHAR(50) NULL,
                            CommercialRegister NVARCHAR(50) NULL,
                            TaxPayerType NVARCHAR(50) NULL,
                            CurrencyId INT NULL,
                            Phone NVARCHAR(50) NULL,
                            Mobile NVARCHAR(50) NULL,
                            Email NVARCHAR(100) NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_Vendors_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id),
                            CONSTRAINT FK_Vendors_VendorCategories FOREIGN KEY (VendorCategoryId) REFERENCES VendorCategories(Id),
                            CONSTRAINT FK_Vendors_Governorates FOREIGN KEY (GovernorateId) REFERENCES Governorates(Id),
                            CONSTRAINT FK_Vendors_Cities FOREIGN KEY (CityId) REFERENCES Cities(Id),
                            CONSTRAINT FK_Vendors_Currencies FOREIGN KEY (CurrencyId) REFERENCES Currencies(Id)
                        );
                        CREATE UNIQUE INDEX IX_Vendors_BranchId_Code ON Vendors (BranchId, Code);
                    END

                    -- ItemUnits
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ItemUnits')
                    BEGIN
                        CREATE TABLE ItemUnits (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            ItemId INT NOT NULL,
                            UnitId INT NOT NULL,
                            ConversionFactor DECIMAL(18,4) NOT NULL DEFAULT 1.0,
                            SalesPrice DECIMAL(18,4) NOT NULL DEFAULT 0.0,
                            PurchasePrice DECIMAL(18,4) NOT NULL DEFAULT 0.0,
                            IsDefaultSales BIT NOT NULL DEFAULT 0,
                            IsDefaultPurchase BIT NOT NULL DEFAULT 0,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_ItemUnits_Items FOREIGN KEY (ItemId) REFERENCES Items(Id) ON DELETE CASCADE,
                            CONSTRAINT FK_ItemUnits_Units FOREIGN KEY (UnitId) REFERENCES Units(Id)
                        );
                        CREATE UNIQUE INDEX IX_ItemUnits_ItemId_UnitId ON ItemUnits (ItemId, UnitId);
                    END

                    -- Warehouses (Branch-Based)
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Warehouses')
                    BEGIN
                        CREATE TABLE Warehouses (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            BranchId INT NOT NULL,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            InventoryAccountId INT NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_Warehouses_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id)
                        );
                        CREATE UNIQUE INDEX IX_Warehouses_BranchId_Code ON Warehouses (BranchId, Code);
                    END
                ");
            }
            catch
            {
                // Ignore – table/column may already exist via EF Migration
            }

            // ─── 1. Seed Roles ────────────────────────────────────────────────────────
            var defaultRoles = new (string Name, string NameAr, string NameEn, string Desc)[]
            {
                ("Admin", "مدير النظام", "System Administrator", "Full system access & administration"),
                ("Company Manager", "مدير شركة", "Company Manager", "Managerial access for sister companies"),
                ("Branch Manager", "مدير فرع", "Branch Manager", "Operational branch management"),
                ("Account Manager", "مدير الحسابات", "Account Manager", "Financial and accounting control"),
                ("Sales Manager", "مدير المبيعات", "Sales Manager", "Sales module supervision"),
                ("Sales User", "مستخدم مبيعات", "Sales User", "Sales entry & processing"),
                ("Purchase Manager", "مدير المشتريات", "Purchase Manager", "Purchase module supervision"),
                ("Purchase User", "مستخدم مشتريات", "Purchase User", "Purchase entry & orders"),
                ("Inventory Manager", "مدير المخازن", "Inventory Manager", "Stock control & transfer manager"),
                ("Inventory User", "أمين مخزن", "Inventory Storekeeper", "Stock entry & issue"),
                ("Cashier", "أمين صندوق", "Cashier", "POS & daily cash voucher handler"),
                ("Auditor", "مراجع حسابات", "Auditor", "Read-only financial audit & report inspector"),
                ("Viewer", "مستعرض بيانات", "Data Viewer", "General read-only viewing access")
            };

            foreach (var r in defaultRoles)
            {
                var roleExists = await roleManager.FindByNameAsync(r.Name);
                if (roleExists == null)
                {
                    var newRole = new ApplicationRole(r.Name)
                    {
                        NameAr = r.NameAr,
                        NameEn = r.NameEn,
                        Description = r.Desc,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    await roleManager.CreateAsync(newRole);
                }
            }

            // ─── 2. Seed Permissions ──────────────────────────────────────────────────
            var permissionDefinitions = new (string Code, string Module, string Screen, string Action, string NameAr, string NameEn, int Sort)[]
            {
                // Administration
                ("Administration.Users.View", "Administration", "Users", "View", "عرض المستخدمين", "View Users", 1),
                ("Administration.Users.Add", "Administration", "Users", "Add", "إضافة مستخدم", "Add User", 2),
                ("Administration.Users.Edit", "Administration", "Users", "Edit", "تعديل مستخدم", "Edit User", 3),
                ("Administration.Users.Delete", "Administration", "Users", "Delete", "حذف مستخدم", "Delete User", 4),
                ("Administration.Users.Export", "Administration", "Users", "Export", "تصدير المستخدمين", "Export Users", 5),

                ("Administration.Roles.View", "Administration", "Roles", "View", "عرض الأدوار", "View Roles", 6),
                ("Administration.Roles.Add", "Administration", "Roles", "Add", "إضافة دور", "Add Role", 7),
                ("Administration.Roles.Edit", "Administration", "Roles", "Edit", "تعديل دور", "Edit Role", 8),
                ("Administration.Roles.Delete", "Administration", "Roles", "Delete", "حذف دور", "Delete Role", 9),

                ("Administration.Permissions.View", "Administration", "Permissions", "View", "عرض الصلاحيات", "View Permissions", 10),
                ("Administration.Permissions.Edit", "Administration", "Permissions", "Edit", "تخصيص الصلاحيات", "Edit Permissions", 11),

                ("Administration.UserBranch.View", "Administration", "UserBranch", "View", "عرض ربط الفروع", "View User Branches", 14),
                ("Administration.UserBranch.Edit", "Administration", "UserBranch", "Edit", "تعديل ربط الفروع", "Edit User Branches", 15),

                // Organization
                ("Organization.Company.View", "Organization", "Company", "View", "عرض بيانات الشركة", "View Company Info", 20),
                ("Organization.Company.Edit", "Organization", "Company", "Edit", "تعديل بيانات الشركة", "Edit Company Info", 21),

                ("Organization.Branch.View", "Organization", "Branch", "View", "عرض الفروع", "View Branches", 22),
                ("Organization.Branch.Add", "Organization", "Branch", "Add", "إضافة فرع", "Add Branch", 23),
                ("Organization.Branch.Edit", "Organization", "Branch", "Edit", "تعديل فرع", "Edit Branch", 24),
                ("Organization.Branch.Delete", "Organization", "Branch", "Delete", "حذف فرع", "Delete Branch", 25),

                ("Organization.FiscalYear.View", "Organization", "FiscalYear", "View", "عرض السنوات المالية", "View Fiscal Years", 26),
                ("Organization.FiscalYear.Add", "Organization", "FiscalYear", "Add", "إضافة سنة مالية", "Add Fiscal Year", 27),
                ("Organization.FiscalYear.Edit", "Organization", "FiscalYear", "Edit", "تعديل سنة مالية", "Edit Fiscal Year", 28),
                ("Organization.FiscalYear.Close", "Organization", "FiscalYear", "Approve", "إغلاق سنة مالية", "Close Fiscal Year", 29),

                // Sales
                ("Sales.SalesInvoice.View", "Sales", "SalesInvoice", "View", "عرض فواتير المبيعات", "View Sales Invoices", 30),
                ("Sales.SalesInvoice.Add", "Sales", "SalesInvoice", "Add", "إضافة فاتورة مبيعات", "Add Sales Invoice", 31),
                ("Sales.SalesInvoice.Edit", "Sales", "SalesInvoice", "Edit", "تعديل فاتورة مبيعات", "Edit Sales Invoice", 32),
                ("Sales.SalesInvoice.Delete", "Sales", "SalesInvoice", "Delete", "حذف فاتورة مبيعات", "Delete Sales Invoice", 33),
                ("Sales.SalesInvoice.Print", "Sales", "SalesInvoice", "Print", "طباعة فاتورة مبيعات", "Print Sales Invoice", 34),
                ("Sales.SalesInvoice.Approve", "Sales", "SalesInvoice", "Approve", "اعتماد فاتورة مبيعات", "Approve Sales Invoice", 35),
                ("Sales.SalesInvoice.Cancel", "Sales", "SalesInvoice", "Cancel", "إلغاء فاتورة مبيعات", "Cancel Sales Invoice", 36),
                ("Sales.SalesInvoice.Post", "Sales", "SalesInvoice", "Post", "ترحيل فاتورة مبيعات", "Post Sales Invoice", 37),
                ("Sales.SalesInvoice.Export", "Sales", "SalesInvoice", "Export", "تصدير فواتير المبيعات", "Export Sales Invoices", 38),

                // Purchases
                ("Purchases.PurchaseInvoice.View", "Purchases", "PurchaseInvoice", "View", "عرض فواتير المشتريات", "View Purchase Invoices", 40),
                ("Purchases.PurchaseInvoice.Add", "Purchases", "PurchaseInvoice", "Add", "إضافة فاتورة مشتريات", "Add Purchase Invoice", 41),
                ("Purchases.PurchaseInvoice.Edit", "Purchases", "PurchaseInvoice", "Edit", "تعديل فاتورة مشتريات", "Edit Purchase Invoice", 42),
                ("Purchases.PurchaseInvoice.Delete", "Purchases", "PurchaseInvoice", "Delete", "حذف فاتورة مشتريات", "Delete Purchase Invoice", 43),
                ("Purchases.PurchaseInvoice.Print", "Purchases", "PurchaseInvoice", "Print", "طباعة فاتورة مشتريات", "Print Purchase Invoice", 44),
                ("Purchases.PurchaseInvoice.Approve", "Purchases", "PurchaseInvoice", "Approve", "اعتماد فاتورة مشتريات", "Approve Purchase Invoice", 45),
                ("Purchases.PurchaseInvoice.Cancel", "Purchases", "PurchaseInvoice", "Cancel", "إلغاء فاتورة مشتريات", "Cancel Purchase Invoice", 46),
                ("Purchases.PurchaseInvoice.Post", "Purchases", "PurchaseInvoice", "Post", "ترحيل فاتورة مشتريات", "Post Purchase Invoice", 47),

                // Inventory
                ("Inventory.Items.View", "Inventory", "Items", "View", "عرض أصناف المخزون", "View Inventory Items", 50),
                ("Inventory.Items.Add", "Inventory", "Items", "Add", "إضافة صنف", "Add Inventory Item", 51),
                ("Inventory.Items.Edit", "Inventory", "Items", "Edit", "تعديل صنف", "Edit Inventory Item", 52),
                ("Inventory.Items.Delete", "Inventory", "Items", "Delete", "حذف صنف", "Delete Inventory Item", 53),
                ("Inventory.StockTransfer.View", "Inventory", "StockTransfer", "View", "عرض التحويلات المخزنية", "View Stock Transfers", 54),
                ("Inventory.StockTransfer.Add", "Inventory", "StockTransfer", "Add", "إضافة تحويل مخزني", "Add Stock Transfer", 55),
                ("Inventory.StockTransfer.Approve", "Inventory", "StockTransfer", "Approve", "اعتماد تحويل مخزني", "Approve Stock Transfer", 56),

                // Accounting
                ("Accounting.JournalVoucher.View", "Accounting", "JournalVoucher", "View", "عرض قيود اليومية", "View Journal Vouchers", 60),
                ("Accounting.JournalVoucher.Add", "Accounting", "JournalVoucher", "Add", "إضافة قيد يومية", "Add Journal Voucher", 61),
                ("Accounting.JournalVoucher.Edit", "Accounting", "JournalVoucher", "Edit", "تعديل قيد يومية", "Edit Journal Voucher", 62),
                ("Accounting.JournalVoucher.Post", "Accounting", "JournalVoucher", "Post", "ترحيل قيد يومية", "Post Journal Voucher", 63),

                // Cash & Banks
                ("Cash.ReceiptVoucher.View", "Cash", "ReceiptVoucher", "View", "عرض سندات القبط", "View Receipt Vouchers", 70),
                ("Cash.ReceiptVoucher.Add", "Cash", "ReceiptVoucher", "Add", "إضافة سند قبض", "Add Receipt Voucher", 71),
                ("Cash.PaymentVoucher.View", "Cash", "PaymentVoucher", "View", "عرض سندات الصرف", "View Payment Vouchers", 72),
                ("Cash.PaymentVoucher.Add", "Cash", "PaymentVoucher", "Add", "إضافة سند صرف", "Add Payment Voucher", 73),

                // Reports & Settings
                ("Reports.GeneralReports.View", "Reports", "GeneralReports", "View", "عرض التقارير", "View Reports", 80),
                ("Reports.GeneralReports.Print", "Reports", "GeneralReports", "Print", "طباعة التقارير", "Print Reports", 81),
                ("Reports.GeneralReports.Export", "Reports", "GeneralReports", "Export", "تصدير التقارير", "Export Reports", 82),
                ("Settings.SystemSettings.View", "Settings", "SystemSettings", "View", "عرض الإعدادات", "View Settings", 90),
                ("Settings.SystemSettings.Edit", "Settings", "SystemSettings", "Edit", "تعديل الإعدادات", "Edit Settings", 91),

                // Audit History – AUD001
                // No Delete permission is intentional (audit records must not be deletable via UI)
                ("Administration.AuditHistory.View",   "Administration", "AuditHistory", "View",   "عرض سجل المراجعة",   "View Audit History",   95),
                ("Administration.AuditHistory.Export", "Administration", "AuditHistory", "Export", "تصدير سجل المراجعة", "Export Audit History", 96),

                // ─── Phase 3 – Master Data Permissions ──────────────────────────────
                // Governorates
                ("MasterData.Governorates.View", "MasterData", "Governorates", "View", "عرض المحافظات", "View Governorates", 100),
                ("MasterData.Governorates.Add", "MasterData", "Governorates", "Add", "إضافة محافظة", "Add Governorate", 101),
                ("MasterData.Governorates.Edit", "MasterData", "Governorates", "Edit", "تعديل محافظة", "Edit Governorate", 102),
                ("MasterData.Governorates.Delete", "MasterData", "Governorates", "Delete", "حذف محافظة", "Delete Governorate", 103),

                // Cities
                ("MasterData.Cities.View", "MasterData", "Cities", "View", "عرض المدن", "View Cities", 105),
                ("MasterData.Cities.Add", "MasterData", "Cities", "Add", "إضافة مدينة", "Add City", 106),
                ("MasterData.Cities.Edit", "MasterData", "Cities", "Edit", "تعديل مدينة", "Edit City", 107),
                ("MasterData.Cities.Delete", "MasterData", "Cities", "Delete", "حذف مدينة", "Delete City", 108),

                // Salesmen
                ("MasterData.Salesmen.View", "MasterData", "Salesmen", "View", "عرض المناديب", "View Salesmen", 110),
                ("MasterData.Salesmen.Add", "MasterData", "Salesmen", "Add", "إضافة مندوب", "Add Salesman", 111),
                ("MasterData.Salesmen.Edit", "MasterData", "Salesmen", "Edit", "تعديل مندوب", "Edit Salesman", 112),
                ("MasterData.Salesmen.Delete", "MasterData", "Salesmen", "Delete", "حذف مندوب", "Delete Salesman", 113),

                // Currencies
                ("MasterData.Currencies.View", "MasterData", "Currencies", "View", "عرض العملات", "View Currencies", 115),
                ("MasterData.Currencies.Add", "MasterData", "Currencies", "Add", "إضافة عملة", "Add Currency", 116),
                ("MasterData.Currencies.Edit", "MasterData", "Currencies", "Edit", "تعديل عملة", "Edit Currency", 117),
                ("MasterData.Currencies.Delete", "MasterData", "Currencies", "Delete", "حذف عملة", "Delete Currency", 118),

                // Customer Categories
                ("MasterData.CustomerCategories.View", "MasterData", "CustomerCategories", "View", "عرض فئات العملاء", "View Customer Categories", 120),
                ("MasterData.CustomerCategories.Add", "MasterData", "CustomerCategories", "Add", "إضافة فئة عملاء", "Add Customer Category", 121),
                ("MasterData.CustomerCategories.Edit", "MasterData", "CustomerCategories", "Edit", "تعديل فئة عملاء", "Edit Customer Category", 122),
                ("MasterData.CustomerCategories.Delete", "MasterData", "CustomerCategories", "Delete", "حذف فئة عملاء", "Delete Customer Category", 123),

                // Customers
                ("MasterData.Customers.View", "MasterData", "Customers", "View", "عرض العملاء", "View Customers", 125),
                ("MasterData.Customers.Add", "MasterData", "Customers", "Add", "إضافة عميل", "Add Customer", 126),
                ("MasterData.Customers.Edit", "MasterData", "Customers", "Edit", "تعديل عميل", "Edit Customer", 127),
                ("MasterData.Customers.Delete", "MasterData", "Customers", "Delete", "حذف عميل", "Delete Customer", 128),

                // Vendor Categories
                ("MasterData.VendorCategories.View", "MasterData", "VendorCategories", "View", "عرض فئات الموردين", "View Vendor Categories", 130),
                ("MasterData.VendorCategories.Add", "MasterData", "VendorCategories", "Add", "إضافة فئة موردين", "Add Vendor Category", 131),
                ("MasterData.VendorCategories.Edit", "MasterData", "VendorCategories", "Edit", "تعديل فئة موردين", "Edit Vendor Category", 132),
                ("MasterData.VendorCategories.Delete", "MasterData", "VendorCategories", "Delete", "حذف فئة موردين", "Delete Vendor Category", 133),

                // Vendors
                ("MasterData.Vendors.View", "MasterData", "Vendors", "View", "عرض الموردين", "View Vendors", 135),
                ("MasterData.Vendors.Add", "MasterData", "Vendors", "Add", "إضافة مورد", "Add Vendor", 136),
                ("MasterData.Vendors.Edit", "MasterData", "Vendors", "Edit", "تعديل مورد", "Edit Vendor", 137),
                ("MasterData.Vendors.Delete", "MasterData", "Vendors", "Delete", "حذف مورد", "Delete Vendor", 138),

                // Units
                ("MasterData.Units.View", "MasterData", "Units", "View", "عرض الوحدات", "View Units", 140),
                ("MasterData.Units.Add", "MasterData", "Units", "Add", "إضافة وحدة", "Add Unit", 141),
                ("MasterData.Units.Edit", "MasterData", "Units", "Edit", "تعديل وحدة", "Edit Unit", 142),
                ("MasterData.Units.Delete", "MasterData", "Units", "Delete", "حذف وحدة", "Delete Unit", 143),

                // Item Groups
                ("MasterData.ItemGroups.View", "MasterData", "ItemGroups", "View", "عرض مجموعات الأصناف", "View Item Groups", 145),
                ("MasterData.ItemGroups.Add", "MasterData", "ItemGroups", "Add", "إضافة مجموعة أصناف", "Add Item Group", 146),
                ("MasterData.ItemGroups.Edit", "MasterData", "ItemGroups", "Edit", "تعديل مجموعة أصناف", "Edit Item Group", 147),
                ("MasterData.ItemGroups.Delete", "MasterData", "ItemGroups", "Delete", "حذف مجموعة أصناف", "Delete Item Group", 148),

                // Items
                ("MasterData.Items.View", "MasterData", "Items", "View", "عرض الأصناف", "View Items", 150),
                ("MasterData.Items.Add", "MasterData", "Items", "Add", "إضافة صنف", "Add Item", 151),
                ("MasterData.Items.Edit", "MasterData", "Items", "Edit", "تعديل صنف", "Edit Item", 152),
                ("MasterData.Items.Delete", "MasterData", "Items", "Delete", "حذف صنف", "Delete Item", 153),

                // Item Units
                ("MasterData.ItemUnits.View", "MasterData", "ItemUnits", "View", "عرض وحدات الصنف", "View Item Units", 155),
                ("MasterData.ItemUnits.Add", "MasterData", "ItemUnits", "Add", "إضافة وحدة صنف", "Add Item Unit", 156),
                ("MasterData.ItemUnits.Edit", "MasterData", "ItemUnits", "Edit", "تعديل وحدة صنف", "Edit Item Unit", 157),
                ("MasterData.ItemUnits.Delete", "MasterData", "ItemUnits", "Delete", "حذف وحدة صنف", "Delete Item Unit", 158),

                // Warehouses
                ("MasterData.Warehouses.View", "MasterData", "Warehouses", "View", "عرض المخازن", "View Warehouses", 160),
                ("MasterData.Warehouses.Add", "MasterData", "Warehouses", "Add", "إضافة مخزن", "Add Warehouse", 161),
                ("MasterData.Warehouses.Edit", "MasterData", "Warehouses", "Edit", "تعديل مخزن", "Edit Warehouse", 162),
                ("MasterData.Warehouses.Delete", "MasterData", "Warehouses", "Delete", "حذف مخزن", "Delete Warehouse", 163)
            };

            foreach (var p in permissionDefinitions)
            {
                var existing = await context.Permissions.FirstOrDefaultAsync(perm => perm.Code == p.Code);
                if (existing == null)
                {
                    context.Permissions.Add(new Permission
                    {
                        Code = p.Code,
                        Module = p.Module,
                        Screen = p.Screen,
                        Action = p.Action,
                        NameAr = p.NameAr,
                        NameEn = p.NameEn,
                        SortOrder = p.Sort
                    });
                }
            }
            await context.SaveChangesAsync();

            // ─── 3. Assign all permissions to Admin Role ──────────────────────────────
            var adminRole = await roleManager.FindByNameAsync("Admin");
            if (adminRole != null)
            {
                var allPermissions = await context.Permissions.ToListAsync();
                foreach (var perm in allPermissions)
                {
                    if (!await context.RolePermissions.AnyAsync(rp => rp.RoleId == adminRole.Id && rp.PermissionId == perm.Id))
                    {
                        context.RolePermissions.Add(new RolePermission
                        {
                            RoleId = adminRole.Id,
                            PermissionId = perm.Id,
                            IsGranted = true
                        });
                    }
                }
                await context.SaveChangesAsync();
            }

            // ─── 4. Seed 29 Sister Companies & Branches ──────────────────────────────
            var sisterCompaniesSetup = new (string CompCode, string NameAr, string NameEn, (string Code, string NameAr, string NameEn)[] Branches)[]
            {
                ("COMP01", "سوي تك المركز الرئيسي", "Sewtec Main Center", new[]
                {
                    ("B001","المركز الرئيسي ماكينات","Main Center - Machines"),
                    ("B002","المركز الرئيسي قطع غيار","Main Center - Spare Parts"),
                    ("B003","المركز الرئيسي صيانة","Main Center - Maintenance")
                }),
                ("COMP02", "سوي تك فيصل", "Sewtec Faisal", new[]
                {
                    ("B004","فيصل ماكينات","Faisal - Machines"),
                    ("B005","فيصل قطع غيار","Faisal - Spare Parts")
                }),
                ("COMP03", "سوي تك بولاق", "Sewtec Bulaq", new[]
                {
                    ("B006","بولاق ماكينات","Bulaq - Machines"),
                    ("B007","بولاق قطع غيار","Bulaq - Spare Parts")
                }),
                ("COMP04", "سوي تك الخصوص", "Sewtec El Khosous", new[]
                {
                    ("B008","الخصوص ماكينات","El Khosous - Machines"),
                    ("B009","الخصوص قطع غيار","El Khosous - Spare Parts")
                }),
                ("COMP05", "سوي تك عين شمس", "Sewtec Ain Shams", new[]
                {
                    ("B010","عين شمس ماكينات","Ain Shams - Machines"),
                    ("B011","عين شمس قطع غيار","Ain Shams - Spare Parts")
                }),
                ("COMP06", "سوي تك الرويعي القديم", "Sewtec El Rwei Old", new[]
                {
                    ("B012","الرويعي القديم ماكينات","El Rwei Old - Machines"),
                    ("B013","الرويعي القديم قطع غيار","El Rwei Old - Spare Parts")
                }),
                ("COMP07", "سوي تك بورسعيد", "Sewtec Port Said", new[]
                {
                    ("B014","بورسعيد ماكينات","Port Said - Machines"),
                    ("B015","بورسعيد قطع غيار","Port Said - Spare Parts")
                }),
                ("COMP08", "سوي تك المحلة", "Sewtec El Mahalla", new[]
                {
                    ("B016","المحلة ماكينات","El Mahalla - Machines"),
                    ("B017","المحلة قطع غيار","El Mahalla - Spare Parts")
                }),
                ("COMP09", "سوي تك كفر الدوار", "Sewtec Kafr El Dawwar", new[]
                {
                    ("B018","كفر الدوار ماكينات","Kafr El Dawwar - Machines"),
                    ("B019","كفر الدوار قطع غيار","Kafr El Dawwar - Spare Parts")
                }),
                ("COMP10", "سوي تك المنيا", "Sewtec Minya", new[]
                {
                    ("B020","المنيا ماكينات","Minya - Machines"),
                    ("B021","المنيا قطع غيار","Minya - Spare Parts")
                }),
                ("COMP11", "سوي تك دمياط القديمة", "Sewtec Damietta Old", new[]
                {
                    ("B022","دمياط القديمة ماكينات","Damietta Old - Machines"),
                    ("B023","دمياط القديمة قطع غيار","Damietta Old - Spare Parts")
                }),
                ("COMP12", "سوي تك العبور", "Sewtec El Obour", new[]
                {
                    ("B024","العبور ماكينات","El Obour - Machines"),
                    ("B025","العبور قطع غيار","El Obour - Spare Parts")
                }),
                ("COMP13", "سوي تك شبرا الخيمة", "Sewtec Shoubra El Kheima", new[]
                {
                    ("B026","شبرا الخيمة ماكينات","Shoubra El Kheima - Machines"),
                    ("B027","شبرا الخيمة قطع غيار","Shoubra El Kheima - Spare Parts")
                }),
                ("COMP14", "سوي تك الرويعي زايد", "Sewtec El Rwei Zayed", new[]
                {
                    ("B028","الرويعي زايد ماكينات","El Rwei Zayed - Machines"),
                    ("B029","الرويعي زايد قطع غيار","El Rwei Zayed - Spare Parts")
                }),
                ("COMP15", "سوي تك الفيوم", "Sewtec Fayoum", new[]
                {
                    ("B030","الفيوم ماكينات","Fayoum - Machines"),
                    ("B031","الفيوم قطع غيار","Fayoum - Spare Parts")
                }),
                ("COMP16", "سوي تك اسكندرية", "Sewtec Alexandria", new[]
                {
                    ("B032","اسكندرية ماكينات","Alexandria - Machines"),
                    ("B033","اسكندرية قطع غيار","Alexandria - Spare Parts")
                }),
                ("COMP17", "سوي تك بلبيس", "Sewtec Belbeis", new[]
                {
                    ("B034","بلبيس ماكينات","Belbeis - Machines"),
                    ("B035","بلبيس قطع غيار","Belbeis - Spare Parts")
                }),
                ("COMP18", "سوي تك العجمي", "Sewtec El Agami", new[]
                {
                    ("B036","العجمي ماكينات","El Agami - Machines"),
                    ("B037","العجمي قطع غيار","El Agami - Spare Parts")
                }),
                ("COMP19", "سوي تك الـ 27", "Sewtec El 27", new[]
                {
                    ("B038","الـ 27 ماكينات","El 27 - Machines"),
                    ("B039","الـ 27 قطع غيار","El 27 - Spare Parts")
                }),
                ("COMP20", "سوي تك الوراق", "Sewtec El Warraq", new[]
                {
                    ("B040","الوراق ماكينات","El Warraq - Machines"),
                    ("B041","الوراق قطع غيار","El Warraq - Spare Parts")
                }),
                ("COMP21", "سوي تك المنصورة", "Sewtec Mansoura", new[]
                {
                    ("B042","المنصورة ماكينات","Mansoura - Machines"),
                    ("B043","المنصورة قطع غيار","Mansoura - Spare Parts")
                }),
                ("COMP22", "سوي تك العاشر", "Sewtec 10th of Ramadan", new[]
                {
                    ("B044","العاشر ماكينات","10th of Ramadan - Machines"),
                    ("B045","العاشر قطع غيار","10th of Ramadan - Spare Parts")
                }),
                ("COMP23", "سوي تك مشتول", "Sewtec Mashtool", new[]
                {
                    ("B046","مشتول ماكينات","Mashtool - Machines"),
                    ("B047","مشتول قطع غيار","Mashtool - Spare Parts")
                }),
                ("COMP24", "ماك تك المحلة", "Mac Tech El Mahalla", new[]
                {
                    ("B048","ماك تك المحلة ماكينات","Mac Tech Mahalla - Machines"),
                    ("B049","ماك تك المحلة قطع غيار","Mac Tech Mahalla - Spare Parts"),
                    ("B050","ماك تك الزراعة ماكينات","Mac Tech Agriculture - Machines"),
                    ("B051","ماك تك الزراعة قطع غيار","Mac Tech Agriculture - Spare Parts"),
                    ("B052","ماك تك محلة ابو علي ماكينات","Mac Tech Mahalla Abu Ali - Machines"),
                    ("B053","ماك تك محلة ابو علي قطع غيار","Mac Tech Mahalla Abu Ali - Spare Parts")
                }),
                ("COMP25", "ماك تك", "Mac Tech", new[]
                {
                    ("B054","ماك تك الرئيسي ماكينات","Mac Tech Main - Machines"),
                    ("B055","ماك تك شبرا ماكينات","Mac Tech Shoubra - Machines"),
                    ("B056","ماك تك شبرا قطع غيار","Mac Tech Shoubra - Spare Parts"),
                    ("B057","ماك تك كفر الدوار ماكينات","Mac Tech Kafr El Dawwar - Machines"),
                    ("B058","ماك تك كفر الدوار قطع غيار","Mac Tech Kafr El Dawwar - Spare Parts"),
                    ("B059","ماك تك قها ماكينات","Mac Tech Qaha - Machines"),
                    ("B060","ماك تك قها قطع غيار","Mac Tech Qaha - Spare Parts"),
                    ("B061","ماك تك مستعمل ماكينات","Mac Tech Used - Machines")
                }),
                ("COMP26", "جيت تك", "Jet Tech", new[]
                {
                    ("B062","جيت تك ماكينات","Jet Tech - Machines"),
                    ("B063","جيت تك قطع غيار","Jet Tech - Spare Parts")
                }),
                ("COMP27", "كيان", "Kayan", new[]
                {
                    ("B064","كيان ماكينات","Kayan - Machines"),
                    ("B065","كيان قطع غيار","Kayan - Spare Parts")
                }),
                ("COMP28", "تيب ماتريس", "Tip Matris", new[]
                {
                    ("B066","تيب ماتريس ماكينات","Tip Matris - Machines"),
                    ("B067","تيب ماتريس قطع غيار","Tip Matris - Spare Parts")
                }),
                ("COMP29", "الفا تك", "Alpha Tech", new[]
                {
                    ("B068","برشيوس ماكينات","Precious - Machines"),
                    ("B069","العشرين ماكينات","El Eshreen - Machines"),
                    ("B070","العشرين قطع غيار","El Eshreen - Spare Parts"),
                    ("B071","ارض اللواء ماكينات","Ard El Lewa - Machines"),
                    ("B072","ارض اللواء قطع غيار","Ard El Lewa - Spare Parts"),
                    ("B073","شبين الكوم ماكينات","Shebin El Kom - Machines"),
                    ("B074","شبين الكوم قطع غيار","Shebin El Kom - Spare Parts"),
                    ("B075","الاسماعيلية ماكينات","Ismailia - Machines"),
                    ("B076","الاسماعيلية قطع غيار","Ismailia - Spare Parts")
                })
            };

            foreach (var item in sisterCompaniesSetup)
            {
                var company = await context.Companies.FirstOrDefaultAsync(c => c.Code == item.CompCode);
                if (company == null)
                {
                    company = new Company
                    {
                        Code = item.CompCode,
                        Name = item.NameAr,
                        NameAr = item.NameAr,
                        NameEn = item.NameEn,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    context.Companies.Add(company);
                    await context.SaveChangesAsync();
                }
                else
                {
                    company.Name = item.NameAr;
                    company.NameAr = item.NameAr;
                    company.NameEn = item.NameEn;
                    company.IsActive = true;
                    await context.SaveChangesAsync();
                }

                await SeedBranches(context, item.Branches, company.Id);
            }

            // ─── 5. Seed Admin User ───────────────────────────────────────────────────
            ApplicationUser? adminUser = await userManager.FindByNameAsync("admin");
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = "admin",
                    Email = "admin@erp.com",
                    FullName = "System Administrator",
                    FullNameAr = "مدير النظام",
                    FullNameEn = "System Administrator",
                    EmployeeCode = "ADMIN",
                    HasAllBranches = true,
                    IsActive = true,
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(adminUser, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }
            else
            {
                adminUser.HasAllBranches = true;
                adminUser.IsActive = true;
                if (string.IsNullOrEmpty(adminUser.FullNameAr)) adminUser.FullNameAr = "مدير النظام";
                if (string.IsNullOrEmpty(adminUser.FullNameEn)) adminUser.FullNameEn = "System Administrator";
                if (string.IsNullOrEmpty(adminUser.EmployeeCode)) adminUser.EmployeeCode = "ADMIN";
                await userManager.UpdateAsync(adminUser);
            }

            // ─── 7. Seed Fiscal Years ─────────────────────────────────────────────────
            if (!context.FiscalYears.Any())
            {
                context.FiscalYears.AddRange(
                    new FiscalYear
                    {
                        Year = "2025", Name = "2025",
                        NameAr = "السنة المالية 2025", NameEn = "Fiscal Year 2025",
                        StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2025, 12, 31),
                        IsActive = true, IsClosed = true
                    },
                    new FiscalYear
                    {
                        Year = "2026", Name = "2026",
                        NameAr = "السنة المالية 2026", NameEn = "Fiscal Year 2026",
                        StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31),
                        IsActive = true, IsClosed = false
                    }
                );
                await context.SaveChangesAsync();
            }

            // ─── 8. Assign Admin to all companies & main branch ───────────────────────
            if (adminUser != null)
            {
                var mainBranch = context.Branches.FirstOrDefault(b => b.Code == "B001");
                if (mainBranch != null && !context.UserBranches.Any(ub => ub.UserId == adminUser.Id))
                {
                    context.UserBranches.Add(new UserBranch { UserId = adminUser.Id, BranchId = mainBranch.Id });
                }

                await context.SaveChangesAsync();
            }

            // ─── 5. Seed Phase 3 Master Data (Governorates, Cities, Currencies, Units, etc.)
            await SeedMasterDataAsync(context);
        }

        private static async Task SeedMasterDataAsync(ErpDbContext context)
        {
            // Seed Currencies
            if (!await context.Currencies.AnyAsync())
            {
                context.Currencies.AddRange(
                    new Currency { Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", Symbol = "ج.م", ExchangeRate = 1.0m, IsActive = true },
                    new Currency { Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar", Symbol = "$", ExchangeRate = 48.5m, IsActive = true },
                    new Currency { Code = "EUR", NameAr = "يورو", NameEn = "Euro", Symbol = "€", ExchangeRate = 53.0m, IsActive = true },
                    new Currency { Code = "SAR", NameAr = "ريال سعودي", NameEn = "Saudi Riyal", Symbol = "ر.س", ExchangeRate = 12.9m, IsActive = true }
                );
                await context.SaveChangesAsync();
            }

            // Seed Governorates & Cities
            if (!await context.Governorates.AnyAsync())
            {
                var cairo = new Governorate { Code = "GOV-01", NameAr = "القاهرة", NameEn = "Cairo", IsActive = true };
                var giza = new Governorate { Code = "GOV-02", NameAr = "الجيزة", NameEn = "Giza", IsActive = true };
                var alex = new Governorate { Code = "GOV-03", NameAr = "الإسكندرية", NameEn = "Alexandria", IsActive = true };
                var qal = new Governorate { Code = "GOV-04", NameAr = "القليوبية", NameEn = "Qalyubia", IsActive = true };

                context.Governorates.AddRange(cairo, giza, alex, qal);
                await context.SaveChangesAsync();

                context.Cities.AddRange(
                    new City { Code = "CIT-01", GovernorateId = cairo.Id, NameAr = "مدينة نصر", NameEn = "Nasr City", IsActive = true },
                    new City { Code = "CIT-02", GovernorateId = cairo.Id, NameAr = "مصر الجديدة", NameEn = "Heliopolis", IsActive = true },
                    new City { Code = "CIT-03", GovernorateId = cairo.Id, NameAr = "المعادي", NameEn = "Maadi", IsActive = true },
                    new City { Code = "CIT-04", GovernorateId = giza.Id, NameAr = "الدقي", NameEn = "Dokki", IsActive = true },
                    new City { Code = "CIT-05", GovernorateId = giza.Id, NameAr = "المهندسين", NameEn = "Mohandessin", IsActive = true },
                    new City { Code = "CIT-06", GovernorateId = giza.Id, NameAr = "السادس من أكتوبر", NameEn = "6th of October", IsActive = true },
                    new City { Code = "CIT-07", GovernorateId = alex.Id, NameAr = "سموحة", NameEn = "Smouha", IsActive = true },
                    new City { Code = "CIT-08", GovernorateId = qal.Id, NameAr = "شبرا الخيمة", NameEn = "Shubra El Kheima", IsActive = true }
                );
                await context.SaveChangesAsync();
            }

            // Seed Units
            if (!await context.Units.AnyAsync())
            {
                context.Units.AddRange(
                    new Unit { Code = "PCS", NameAr = "قطعة", NameEn = "Piece", IsActive = true },
                    new Unit { Code = "BOX", NameAr = "كرتونة", NameEn = "Box", IsActive = true },
                    new Unit { Code = "SET", NameAr = "طقم", NameEn = "Set", IsActive = true },
                    new Unit { Code = "KG", NameAr = "كيلوجرام", NameEn = "Kilogram", IsActive = true },
                    new Unit { Code = "MTR", NameAr = "متر", NameEn = "Meter", IsActive = true }
                );
                await context.SaveChangesAsync();
            }

            // Seed Item Groups
            if (!await context.ItemGroups.AnyAsync())
            {
                var mainGroup = new ItemGroup { Code = "GRP-01", NameAr = "مجموعة عامة", NameEn = "General Group", IsActive = true };
                context.ItemGroups.Add(mainGroup);
                await context.SaveChangesAsync();

                context.ItemGroups.AddRange(
                    new ItemGroup { Code = "GRP-02", ParentId = mainGroup.Id, NameAr = "ماكينات خياطة", NameEn = "Sewing Machines", IsActive = true },
                    new ItemGroup { Code = "GRP-03", ParentId = mainGroup.Id, NameAr = "قطع غيار", NameEn = "Spare Parts", IsActive = true },
                    new ItemGroup { Code = "GRP-04", ParentId = mainGroup.Id, NameAr = "مستلزمات إنتاج", NameEn = "Production Supplies", IsActive = true }
                );
                await context.SaveChangesAsync();
            }

            // Seed Customer Categories
            if (!await context.CustomerCategories.AnyAsync())
            {
                context.CustomerCategories.AddRange(
                    new CustomerCategory { Code = "CUSCAT-01", NameAr = "عملاء تجزئة", NameEn = "Retail Customers", IsActive = true },
                    new CustomerCategory { Code = "CUSCAT-02", NameAr = "عملاء جملة", NameEn = "Wholesale Customers", IsActive = true },
                    new CustomerCategory { Code = "CUSCAT-03", NameAr = "شركات ومصانع", NameEn = "Corporate & Factories", IsActive = true }
                );
                await context.SaveChangesAsync();
            }

            // Seed Vendor Categories
            if (!await context.VendorCategories.AnyAsync())
            {
                context.VendorCategories.AddRange(
                    new VendorCategory { Code = "VENCAT-01", NameAr = "موردين محليين", NameEn = "Local Vendors", IsActive = true },
                    new VendorCategory { Code = "VENCAT-02", NameAr = "موردين خارجيين (استيراد)", NameEn = "Import Vendors", IsActive = true },
                    new VendorCategory { Code = "VENCAT-03", NameAr = "موردي خدمات وصيانة", NameEn = "Service & Maintenance Vendors", IsActive = true }
                );
                await context.SaveChangesAsync();
            }
        }

        private static async Task SeedBranches(
            ErpDbContext context,
            (string Code, string NameAr, string NameEn)[] list,
            int companyId)
        {
            foreach (var item in list)
            {
                var existing = await context.Branches.FirstOrDefaultAsync(b => b.Code == item.Code);
                if (existing == null)
                {
                    context.Branches.Add(new Branch
                    {
                        CompanyId = companyId,
                        Code = item.Code,
                        Name = item.NameAr,
                        NameAr = item.NameAr,
                        NameEn = item.NameEn,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.CompanyId = companyId;
                    existing.NameAr = item.NameAr;
                    existing.NameEn = item.NameEn;
                    existing.Name = item.NameAr;
                }
            }
            await context.SaveChangesAsync();
        }
    }
}
