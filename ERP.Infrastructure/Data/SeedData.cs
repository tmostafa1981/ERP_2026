using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Infrastructure.Data
{
    public static partial class SeedData
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<ErpDbContext>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            if (context.Database.IsRelational())
            {
                var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                if (pendingMigrations.Any())
                {
                    await context.Database.MigrateAsync();
                }
            }
            else
            {
                context.Database.EnsureCreated();
            }

            // ─── 0. ALWAYS Ensure Permissions and Roles are Synced (even on existing databases) ─────
            await EnsurePermissionsAndRolesAsync(context, roleManager);
            await EnsurePartyGroupsSchemaAndCurrenciesAsync(context);
            await EnsureCurrencyDailyRatesSchemaAsync(context);

            if (context.Database.IsRelational() && await context.FiscalYears.AnyAsync())
            {
                return; // Schema and seed data already established in database
            }

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
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Branches' AND COLUMN_NAME = 'BranchCode')
                    BEGIN
                        ALTER TABLE Branches ADD BranchCode NVARCHAR(20) NOT NULL DEFAULT '';
                        EXEC('UPDATE Branches SET BranchCode = Code WHERE BranchCode = '''' OR BranchCode IS NULL;');
                    END
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
                            BranchId INT NOT NULL DEFAULT 1,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            IsDefault BIT NOT NULL DEFAULT 0,
                            ControlAccountId INT NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_CustomerCategories_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id)
                        );
                        CREATE UNIQUE INDEX IX_CustomerCategories_BranchId_Code ON CustomerCategories (BranchId, Code);
                    END
                    ELSE
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CustomerCategories' AND COLUMN_NAME = 'BranchId')
                        BEGIN
                            ALTER TABLE CustomerCategories ADD BranchId INT NOT NULL DEFAULT 1;
                            ALTER TABLE CustomerCategories ADD IsDefault BIT NOT NULL DEFAULT 0;
                            DROP INDEX IF EXISTS IX_CustomerCategories_Code ON CustomerCategories;
                            CREATE UNIQUE INDEX IX_CustomerCategories_BranchId_Code ON CustomerCategories (BranchId, Code);
                        END
                    END

                    -- VendorCategories
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'VendorCategories')
                    BEGIN
                        CREATE TABLE VendorCategories (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            BranchId INT NOT NULL DEFAULT 1,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            IsDefault BIT NOT NULL DEFAULT 0,
                            ControlAccountId INT NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_VendorCategories_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id)
                        );
                        CREATE UNIQUE INDEX IX_VendorCategories_BranchId_Code ON VendorCategories (BranchId, Code);
                    END
                    ELSE
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'VendorCategories' AND COLUMN_NAME = 'BranchId')
                        BEGIN
                            ALTER TABLE VendorCategories ADD BranchId INT NOT NULL DEFAULT 1;
                            ALTER TABLE VendorCategories ADD IsDefault BIT NOT NULL DEFAULT 0;
                            DROP INDEX IF EXISTS IX_VendorCategories_Code ON VendorCategories;
                            CREATE UNIQUE INDEX IX_VendorCategories_BranchId_Code ON VendorCategories (BranchId, Code);
                        END
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
                            Mobile2 NVARCHAR(50) NULL,
                            Email NVARCHAR(100) NULL,
                            Notes NVARCHAR(1000) NULL,
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

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Customers' AND COLUMN_NAME = 'Mobile2')
                    BEGIN ALTER TABLE Customers ADD Mobile2 NVARCHAR(50) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Customers' AND COLUMN_NAME = 'Notes')
                    BEGIN ALTER TABLE Customers ADD Notes NVARCHAR(1000) NULL; END

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
                            Mobile2 NVARCHAR(50) NULL,
                            Email NVARCHAR(100) NULL,
                            Notes NVARCHAR(1000) NULL,
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

                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Vendors' AND COLUMN_NAME = 'Mobile2')
                    BEGIN ALTER TABLE Vendors ADD Mobile2 NVARCHAR(50) NULL; END
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Vendors' AND COLUMN_NAME = 'Notes')
                    BEGIN ALTER TABLE Vendors ADD Notes NVARCHAR(1000) NULL; END

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

                    -- CodeSequences
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CodeSequences')
                    BEGIN
                        CREATE TABLE CodeSequences (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            EntityName NVARCHAR(50) NOT NULL,
                            BranchId INT NULL,
                            Prefix NVARCHAR(10) NOT NULL,
                            LastNumber BIGINT NOT NULL DEFAULT 0,
                            PaddingLength INT NOT NULL DEFAULT 6,
                            IsAuto BIT NOT NULL DEFAULT 1,
                            IsManualAllowed BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_CodeSequences_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id)
                        );
                        CREATE UNIQUE INDEX IX_CodeSequences_EntityName_BranchId ON CodeSequences (EntityName, BranchId);
                    END

                    -- GLAccounts (Unified Chart of Accounts)
                    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'GLAccounts')
                    BEGIN
                        CREATE TABLE GLAccounts (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Code NVARCHAR(50) NOT NULL,
                            NameAr NVARCHAR(200) NOT NULL,
                            NameEn NVARCHAR(200) NOT NULL,
                            ParentId INT NULL,
                            Level INT NOT NULL DEFAULT 1,
                            IsGroup BIT NOT NULL DEFAULT 0,
                            AccountNature INT NOT NULL DEFAULT 1,
                            AccountCategory INT NOT NULL DEFAULT 1,
                            CurrencyId INT NULL,
                            BranchId INT NULL,
                            IsControlAccount BIT NOT NULL DEFAULT 0,
                            SubLedgerType INT NOT NULL DEFAULT 0,
                            IsReconciliationAccount BIT NOT NULL DEFAULT 0,
                            RequiresCostCenter BIT NOT NULL DEFAULT 0,
                            AccountUsage INT NOT NULL DEFAULT 1,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_GLAccounts_Parent FOREIGN KEY (ParentId) REFERENCES GLAccounts(Id),
                            CONSTRAINT FK_GLAccounts_Currencies FOREIGN KEY (CurrencyId) REFERENCES Currencies(Id),
                            CONSTRAINT FK_GLAccounts_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id)
                        );
                        CREATE UNIQUE INDEX IX_GLAccounts_Code ON GLAccounts (Code);
                        CREATE INDEX IX_GLAccounts_ParentId ON GLAccounts (ParentId);
                        CREATE INDEX IX_GLAccounts_IsGroup ON GLAccounts (IsGroup);
                        CREATE INDEX IX_GLAccounts_BranchId ON GLAccounts (BranchId);
                        CREATE INDEX IX_GLAccounts_Control_SubLedger ON GLAccounts (IsControlAccount, SubLedgerType);
                    END
                    ELSE
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'GLAccounts' AND COLUMN_NAME = 'BranchId')
                        BEGIN
                            ALTER TABLE GLAccounts ADD BranchId INT NULL;
                            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_GLAccounts_Branches_BranchId')
                            BEGIN
                                ALTER TABLE GLAccounts WITH CHECK ADD CONSTRAINT FK_GLAccounts_Branches_BranchId FOREIGN KEY(BranchId) REFERENCES Branches(Id);
                            END
                            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GLAccounts_BranchId' AND object_id = OBJECT_ID('GLAccounts'))
                            BEGIN
                                CREATE NONCLUSTERED INDEX IX_GLAccounts_BranchId ON GLAccounts(BranchId);
                            END
                        END
                    END

                    -- Map existing seed branch cash boxes to their respective branches safely via dynamic SQL
                    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'GLAccounts' AND COLUMN_NAME = 'BranchId')
                    BEGIN
                        EXEC('
                            UPDATE a
                            SET a.BranchId = b.Id
                            FROM GLAccounts a
                            CROSS JOIN (SELECT TOP 1 Id FROM Branches WHERE Code = ''BR-CAI'' OR BranchCode = ''BR-CAI'' OR NameAr LIKE ''%القاهرة%'') b
                            WHERE a.Code = ''11010102'' AND a.BranchId IS NULL;

                            UPDATE a
                            SET a.BranchId = b.Id
                            FROM GLAccounts a
                            CROSS JOIN (SELECT TOP 1 Id FROM Branches WHERE Code = ''BR-ALX'' OR BranchCode = ''BR-ALX'' OR NameAr LIKE ''%الإسكندرية%'') b
                            WHERE a.Code = ''11010103'' AND a.BranchId IS NULL;
                        ');
                    END

                    -- Remap FKs to GLAccounts table
                    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Customers_Accounts_FinancialAccountId')
                    BEGIN
                        ALTER TABLE Customers DROP CONSTRAINT FK_Customers_Accounts_FinancialAccountId;
                    END
                    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Customers_GLAccounts_FinancialAccountId')
                    BEGIN
                        ALTER TABLE Customers WITH CHECK ADD CONSTRAINT FK_Customers_GLAccounts_FinancialAccountId 
                        FOREIGN KEY (FinancialAccountId) REFERENCES GLAccounts(Id);
                    END

                    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Vendors_Accounts_FinancialAccountId')
                    BEGIN
                        ALTER TABLE Vendors DROP CONSTRAINT FK_Vendors_Accounts_FinancialAccountId;
                    END
                    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Vendors_GLAccounts_FinancialAccountId')
                    BEGIN
                        ALTER TABLE Vendors WITH CHECK ADD CONSTRAINT FK_Vendors_GLAccounts_FinancialAccountId 
                        FOREIGN KEY (FinancialAccountId) REFERENCES GLAccounts(Id);
                    END

                    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AccountPermissions_Accounts_AccountId')
                    BEGIN
                        ALTER TABLE AccountPermissions DROP CONSTRAINT FK_AccountPermissions_Accounts_AccountId;
                    END
                    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AccountPermissions_GLAccounts_AccountId')
                    BEGIN
                        ALTER TABLE AccountPermissions WITH CHECK ADD CONSTRAINT FK_AccountPermissions_GLAccounts_AccountId 
                        FOREIGN KEY (AccountId) REFERENCES GLAccounts(Id) ON DELETE CASCADE;
                    END

                    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_JournalEntryLines_Accounts_AccountId')
                    BEGIN
                        ALTER TABLE JournalEntryLines DROP CONSTRAINT FK_JournalEntryLines_Accounts_AccountId;
                    END
                    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_JournalEntryLines_GLAccounts_AccountId')
                    BEGIN
                        ALTER TABLE JournalEntryLines WITH CHECK ADD CONSTRAINT FK_JournalEntryLines_GLAccounts_AccountId 
                        FOREIGN KEY (AccountId) REFERENCES GLAccounts(Id);
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CustomerCategories_GLAccounts_ControlAccountId')
                    BEGIN
                        ALTER TABLE CustomerCategories WITH CHECK ADD CONSTRAINT FK_CustomerCategories_GLAccounts_ControlAccountId 
                        FOREIGN KEY (ControlAccountId) REFERENCES GLAccounts(Id);
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_VendorCategories_GLAccounts_ControlAccountId')
                    BEGIN
                        ALTER TABLE VendorCategories WITH CHECK ADD CONSTRAINT FK_VendorCategories_GLAccounts_ControlAccountId 
                        FOREIGN KEY (ControlAccountId) REFERENCES GLAccounts(Id);
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

                ("Administration.PaymentMethodPermissions.View", "Administration", "PaymentMethodPermissions", "View", "عرض صلاحيات وسائل الدفع", "View Payment Method Permissions", 16),
                ("Administration.PaymentMethodPermissions.Edit", "Administration", "PaymentMethodPermissions", "Edit", "تعديل صلاحيات وسائل الدفع", "Edit Payment Method Permissions", 17),

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
                ("Accounting.ChartOfAccounts.View", "Accounting", "ChartOfAccounts", "View", "عرض شجرة الحسابات", "View Chart of Accounts", 55),
                ("Accounting.ChartOfAccounts.Add", "Accounting", "ChartOfAccounts", "Add", "إضافة حساب/مجموعة", "Add Account/Group", 56),
                ("Accounting.ChartOfAccounts.Edit", "Accounting", "ChartOfAccounts", "Edit", "تعديل حساب/مجموعة", "Edit Account/Group", 57),
                ("Accounting.ChartOfAccounts.Delete", "Accounting", "ChartOfAccounts", "Delete", "حذف حساب/مجموعة", "Delete Account/Group", 58),

                ("Accounting.JournalVoucher.View", "Accounting", "JournalVoucher", "View", "عرض قيود اليومية", "View Journal Vouchers", 60),
                ("Accounting.JournalVoucher.Add", "Accounting", "JournalVoucher", "Add", "إضافة قيد يومية", "Add Journal Voucher", 61),
                ("Accounting.JournalVoucher.Edit", "Accounting", "JournalVoucher", "Edit", "تعديل قيد يومية", "Edit Journal Voucher", 62),
                ("Accounting.JournalVoucher.Post", "Accounting", "JournalVoucher", "Post", "ترحيل قيد يومية", "Post Journal Voucher", 63),
                ("Accounting.JournalVoucher.Delete", "Accounting", "JournalVoucher", "Delete", "إلغاء وحذف قيد يومية", "Delete Journal Voucher", 64),

                // Cost Centers
                ("Accounting.CostCenters.View", "Accounting", "CostCenters", "View", "عرض مراكز التكلفة", "View Cost Centers", 65),
                ("Accounting.CostCenters.Add", "Accounting", "CostCenters", "Add", "إضافة مركز تكلفة", "Add Cost Center", 66),
                ("Accounting.CostCenters.Edit", "Accounting", "CostCenters", "Edit", "تعديل مركز تكلفة", "Edit Cost Center", 67),
                ("Accounting.CostCenters.Delete", "Accounting", "CostCenters", "Delete", "حذف مركز تكلفة", "Delete Cost Center", 68),

                // Foreign Currency Revaluation
                ("Accounting.CurrencyRevaluation.View", "Accounting", "CurrencyRevaluation", "View", "عرض عمليات إعادة تقييم العملات", "View Foreign Currency Revaluations", 69),
                ("Accounting.CurrencyRevaluation.Add", "Accounting", "CurrencyRevaluation", "Add", "إنشاء عملية إعادة تقييم", "Create Foreign Currency Revaluation", 70),
                ("Accounting.CurrencyRevaluation.Approve", "Accounting", "CurrencyRevaluation", "Approve", "اعتماد وترحيل إعادة التقييم", "Approve & Post Foreign Currency Revaluation", 71),
                ("Accounting.CurrencyRevaluation.Reverse", "Accounting", "CurrencyRevaluation", "Reverse", "عكس قيد إعادة التقييم", "Reverse Foreign Currency Revaluation", 72),
                ("Accounting.CurrencyRevaluation.Delete", "Accounting", "CurrencyRevaluation", "Delete", "حذف عملية إعادة التقييم", "Delete Foreign Currency Revaluation", 73),
                ("Accounting.CurrencyRevaluation.Print", "Accounting", "CurrencyRevaluation", "Print", "طباعة تقرير إعادة التقييم", "Print Foreign Currency Revaluation", 74),
                ("Accounting.CurrencyRevaluation.Export", "Accounting", "CurrencyRevaluation", "Export", "تصدير إعادة التقييم إلى Excel", "Export Foreign Currency Revaluation", 75),

                // Fixed Assets & Depreciation
                ("FixedAssets.Category.View", "FixedAssets", "Category", "View", "عرض مجموعات وتصنيفات الأصول", "View Asset Categories", 76),
                ("FixedAssets.Category.Manage", "FixedAssets", "Category", "Manage", "إدارة وتعديل مجموعات الأصول", "Manage Asset Categories", 77),
                ("FixedAssets.Asset.View", "FixedAssets", "Asset", "View", "عرض سجل الأصول الثابتة", "View Fixed Assets Register", 78),
                ("FixedAssets.Asset.Add", "FixedAssets", "Asset", "Add", "إضافة أصل ثابت جديد", "Add Fixed Asset", 79),
                ("FixedAssets.Asset.Edit", "FixedAssets", "Asset", "Edit", "تعديل بيانات أصل ثابت", "Edit Fixed Asset", 80),
                ("FixedAssets.Asset.Delete", "FixedAssets", "Asset", "Delete", "حذف مسودة أصل ثابت", "Delete Fixed Asset", 81),
                ("FixedAssets.Asset.Transfer", "FixedAssets", "Asset", "Transfer", "نقل الأصل وتغيير العهدة والموقع", "Transfer Fixed Asset", 82),
                ("FixedAssets.Asset.AdditionalCost", "FixedAssets", "Asset", "AdditionalCost", "إضافة تكاليف لاحقة للأصل", "Add Asset Improvement Cost", 83),
                ("FixedAssets.Asset.Disposal", "FixedAssets", "Asset", "Disposal", "استبعاد أو بيع الأصل الثابت", "Dispose or Sell Fixed Asset", 84),
                ("FixedAssets.Depreciation.View", "FixedAssets", "Depreciation", "View", "عرض عمليات وسجل الإهلاك", "View Depreciation Runs", 85),
                ("FixedAssets.Depreciation.Run", "FixedAssets", "Depreciation", "Run", "حساب وتشغيل الإهلاك الدوري", "Run Depreciation Calculation", 86),
                ("FixedAssets.Depreciation.Approve", "FixedAssets", "Depreciation", "Approve", "اعتماد وترحيل قيد الإهلاك", "Approve & Post Depreciation", 87),
                ("FixedAssets.Depreciation.Reverse", "FixedAssets", "Depreciation", "Reverse", "عكس قيد الإهلاك", "Reverse Depreciation Run", 88),
                ("FixedAssets.Reports.View", "FixedAssets", "Reports", "View", "عرض تقارير الأصول الثابتة", "View Fixed Assets Reports", 89),
                ("FixedAssets.Reports.Export", "FixedAssets", "Reports", "Export", "تصدير تقارير الأصول الثابتة", "Export Fixed Assets Reports", 90),

                // Cash & Banks (سندات القبض والصرف)
                ("Cash.ReceiptVoucher.View", "Cash", "ReceiptVoucher", "View", "عرض سندات القبض", "View Receipt Vouchers", 70),
                ("Cash.ReceiptVoucher.Add", "Cash", "ReceiptVoucher", "Add", "إضافة سند قبض", "Add Receipt Voucher", 71),
                ("Cash.ReceiptVoucher.Edit", "Cash", "ReceiptVoucher", "Edit", "تعديل سند قبض", "Edit Receipt Voucher", 72),
                ("Cash.ReceiptVoucher.Delete", "Cash", "ReceiptVoucher", "Delete", "حذف سند قبض", "Delete Receipt Voucher", 73),
                ("Cash.ReceiptVoucher.Post", "Cash", "ReceiptVoucher", "Post", "ترحيل سند قبض", "Post Receipt Voucher", 74),
                ("Cash.ReceiptVoucher.Unpost", "Cash", "ReceiptVoucher", "Unpost", "إلغاء ترحيل سند قبض", "Unpost Receipt Voucher", 75),
                ("Cash.ReceiptVoucher.Cancel", "Cash", "ReceiptVoucher", "Cancel", "إلغاء سند قبض", "Cancel Receipt Voucher", 76),
                ("Cash.ReceiptVoucher.Print", "Cash", "ReceiptVoucher", "Print", "طباعة سند قبض", "Print Receipt Voucher", 77),

                ("Cash.PaymentVoucher.View", "Cash", "PaymentVoucher", "View", "عرض سندات الصرف", "View Payment Vouchers", 80),
                ("Cash.PaymentVoucher.Add", "Cash", "PaymentVoucher", "Add", "إضافة سند صرف", "Add Payment Voucher", 81),
                ("Cash.PaymentVoucher.Edit", "Cash", "PaymentVoucher", "Edit", "تعديل سند صرف", "Edit Payment Voucher", 82),
                ("Cash.PaymentVoucher.Delete", "Cash", "PaymentVoucher", "Delete", "حذف سند صرف", "Delete Payment Voucher", 83),
                ("Cash.PaymentVoucher.Post", "Cash", "PaymentVoucher", "Post", "ترحيل سند صرف", "Post Payment Voucher", 84),
                ("Cash.PaymentVoucher.Unpost", "Cash", "PaymentVoucher", "Unpost", "إلغاء ترحيل سند صرف", "Unpost Payment Voucher", 85),
                ("Cash.PaymentVoucher.Cancel", "Cash", "PaymentVoucher", "Cancel", "إلغاء سند صرف", "Cancel Payment Voucher", 86),
                ("Cash.PaymentVoucher.Print", "Cash", "PaymentVoucher", "Print", "طباعة سند صرف", "Print Payment Voucher", 87),

                // Reports (التقارير المحاسبية)
                ("Reports.GeneralReports.View", "Reports", "GeneralReports", "View", "عرض فهرس التقارير", "View Reports Catalog", 90),
                ("Reports.GeneralReports.Print", "Reports", "GeneralReports", "Print", "طباعة التقارير العامة", "Print General Reports", 91),
                ("Reports.GeneralReports.Export", "Reports", "GeneralReports", "Export", "تصدير التقارير العامة", "Export General Reports", 92),

                ("Reports.AccountStatement.View", "Reports", "AccountStatement", "View", "عرض كشف حساب مالي", "View Account Statement", 93),
                ("Reports.AccountStatement.Print", "Reports", "AccountStatement", "Print", "طباعة كشف حساب مالي", "Print Account Statement", 94),
                ("Reports.AccountStatement.Export", "Reports", "AccountStatement", "Export", "تصدير كشف حساب مالي", "Export Account Statement", 95),

                ("Reports.CustomerStatement.View", "Reports", "CustomerStatement", "View", "عرض كشف حساب عميل", "View Customer Statement", 96),
                ("Reports.CustomerStatement.Print", "Reports", "CustomerStatement", "Print", "طباعة كشف حساب عميل", "Print Customer Statement", 97),
                ("Reports.CustomerStatement.Export", "Reports", "CustomerStatement", "Export", "تصدير كشف حساب عميل", "Export Customer Statement", 98),

                ("Reports.VendorStatement.View", "Reports", "VendorStatement", "View", "عرض كشف حساب مورد", "View Vendor Statement", 99),
                ("Reports.VendorStatement.Print", "Reports", "VendorStatement", "Print", "طباعة كشف حساب مورد", "Print Vendor Statement", 100),
                ("Reports.VendorStatement.Export", "Reports", "VendorStatement", "Export", "تصدير كشف حساب مورد", "Export Vendor Statement", 101),

                ("Reports.TrialBalance.View", "Reports", "TrialBalance", "View", "عرض ميزان المراجعة", "View Trial Balance", 102),
                ("Reports.TrialBalance.Print", "Reports", "TrialBalance", "Print", "طباعة ميزان المراجعة", "Print Trial Balance", 103),
                ("Reports.TrialBalance.Export", "Reports", "TrialBalance", "Export", "تصدير ميزان المراجعة", "Export Trial Balance", 104),

                ("Reports.GeneralLedger.View", "Reports", "GeneralLedger", "View", "عرض دفتر الأستاذ العام", "View General Ledger", 105),
                ("Reports.GeneralLedger.Print", "Reports", "GeneralLedger", "Print", "طباعة دفتر الأستاذ العام", "Print General Ledger", 106),
                ("Reports.GeneralLedger.Export", "Reports", "GeneralLedger", "Export", "تصدير دفتر الأستاذ العام", "Export General Ledger", 107),

                // Sales Reports (تقارير المبيعات)
                ("Reports.SalesReports.View", "Reports", "SalesReports", "View", "عرض تقارير المبيعات", "View Sales Reports", 108),
                ("Reports.SalesReports.Print", "Reports", "SalesReports", "Print", "طباعة تقارير المبيعات", "Print Sales Reports", 109),
                ("Reports.SalesReports.Export", "Reports", "SalesReports", "Export", "تصدير تقارير المبيعات", "Export Sales Reports", 110),

                // Purchase Reports (تقارير المشتريات)
                ("Reports.PurchaseReports.View", "Reports", "PurchaseReports", "View", "عرض تقارير المشتريات", "View Purchases Reports", 111),
                ("Reports.PurchaseReports.Print", "Reports", "PurchaseReports", "Print", "طباعة تقارير المشتريات", "Print Purchases Reports", 112),
                ("Reports.PurchaseReports.Export", "Reports", "PurchaseReports", "Export", "تصدير تقارير المشتريات", "Export Purchases Reports", 113),

                // Inventory Reports (تقارير المخازن)
                ("Reports.InventoryReports.View", "Reports", "InventoryReports", "View", "عرض تقارير المخازن", "View Inventory Reports", 114),
                ("Reports.InventoryReports.Print", "Reports", "InventoryReports", "Print", "طباعة تقارير المخازن", "Print Inventory Reports", 115),
                ("Reports.InventoryReports.Export", "Reports", "InventoryReports", "Export", "تصدير تقارير المخازن", "Export Inventory Reports", 116),

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
                ("MasterData.CurrencyRates.View", "MasterData", "CurrencyRates", "View", "عرض أسعار صرف العملات اليومية", "View Daily Exchange Rates", 119),
                ("MasterData.CurrencyRates.Add", "MasterData", "CurrencyRates", "Add", "إضافة سعر صرف يومي", "Add Daily Exchange Rate", 120),
                ("MasterData.CurrencyRates.Edit", "MasterData", "CurrencyRates", "Edit", "تعديل سعر صرف يومي", "Edit Daily Exchange Rate", 121),
                ("MasterData.CurrencyRates.Delete", "MasterData", "CurrencyRates", "Delete", "حذف سعر صرف يومي", "Delete Daily Exchange Rate", 122),

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

            var advancedReportAndScreenPermissions = new (string Code, string Module, string Screen, string Action, string NameAr, string NameEn, string Description, int Sort)[]
            {
                // ─── Sales Reports & Operations ──────────────────────────────────────────
                ("Sales.ReportsCatalog.View", "Sales", "SalesReportsCatalog", "View", "عرض فهرس تقارير المبيعات", "View Sales Reports Catalog", "فهرس تقارير المبيعات", 170),
                ("Sales.SalesSummary.View", "Sales", "SalesSummary", "View", "عرض ملخص المبيعات", "View Sales Summary", "ملخص المبيعات", 171),
                ("Sales.SalesSummary.Print", "Sales", "SalesSummary", "Print", "طباعة ملخص المبيعات", "Print Sales Summary", "ملخص المبيعات", 172),
                ("Sales.SalesSummary.Export", "Sales", "SalesSummary", "Export", "تصدير ملخص المبيعات", "Export Sales Summary", "ملخص المبيعات", 173),

                ("Sales.InvoiceDetails.View", "Sales", "InvoiceDetails", "View", "عرض تفاصيل فواتير البيع", "View Sales Invoice Details", "تفاصيل فواتير البيع", 174),
                ("Sales.InvoiceDetails.Print", "Sales", "InvoiceDetails", "Print", "طباعة تفاصيل فواتير البيع", "Print Sales Invoice Details", "تفاصيل فواتير البيع", 175),
                ("Sales.InvoiceDetails.Export", "Sales", "InvoiceDetails", "Export", "تصدير تفاصيل فواتير البيع", "Export Sales Invoice Details", "تفاصيل فواتير البيع", 176),

                ("Sales.ByItem.View", "Sales", "ByItem", "View", "عرض المبيعات حسب الصنف", "View Sales by Item", "المبيعات حسب الصنف", 177),
                ("Sales.ByItem.Print", "Sales", "ByItem", "Print", "طباعة المبيعات حسب الصنف", "Print Sales by Item", "المبيعات حسب الصنف", 178),
                ("Sales.ByItem.Export", "Sales", "ByItem", "Export", "تصدير المبيعات حسب الصنف", "Export Sales by Item", "المبيعات حسب الصنف", 179),

                ("Sales.ByCustomer.View", "Sales", "ByCustomer", "View", "عرض المبيعات حسب العميل", "View Sales by Customer", "المبيعات حسب العميل", 180),
                ("Sales.ByCustomer.Print", "Sales", "ByCustomer", "Print", "طباعة المبيعات حسب العميل", "Print Sales by Customer", "المبيعات حسب العميل", 181),
                ("Sales.ByCustomer.Export", "Sales", "ByCustomer", "Export", "تصدير المبيعات حسب العميل", "Export Sales by Customer", "المبيعات حسب العميل", 182),

                ("Sales.BySalesman.View", "Sales", "BySalesman", "View", "عرض المبيعات حسب المندوب", "View Sales by Salesman", "المبيعات حسب المندوب", 183),
                ("Sales.BySalesman.Print", "Sales", "BySalesman", "Print", "طباعة المبيعات حسب المندوب", "Print Sales by Salesman", "المبيعات حسب المندوب", 184),
                ("Sales.BySalesman.Export", "Sales", "BySalesman", "Export", "تصدير المبيعات حسب المندوب", "Export Sales by Salesman", "المبيعات حسب المندوب", 185),

                ("Sales.ReturnsAndNet.View", "Sales", "ReturnsAndNet", "View", "عرض مرتجعات المبيعات وصافي المبيعات", "View Sales Returns and Net", "مرتجعات وصافي المبيعات", 186),
                ("Sales.ReturnsAndNet.Print", "Sales", "ReturnsAndNet", "Print", "طباعة مرتجعات المبيعات وصافي المبيعات", "Print Sales Returns and Net", "مرتجعات وصافي المبيعات", 187),
                ("Sales.ReturnsAndNet.Export", "Sales", "ReturnsAndNet", "Export", "تصدير مرتجعات المبيعات وصافي المبيعات", "Export Sales Returns and Net", "مرتجعات وصافي المبيعات", 188),

                ("Sales.BranchComparison.View", "Sales", "BranchComparison", "View", "عرض مقارنة المبيعات بين الفروع والفترات", "View Sales Comparison", "مقارنة المبيعات بين الفروع", 189),
                ("Sales.BranchComparison.Print", "Sales", "BranchComparison", "Print", "طباعة مقارنة المبيعات بين الفروع والفترات", "Print Sales Comparison", "مقارنة المبيعات بين الفروع", 190),
                ("Sales.BranchComparison.Export", "Sales", "BranchComparison", "Export", "تصدير مقارنة المبيعات بين الفروع والفترات", "Export Sales Comparison", "مقارنة المبيعات بين الفروع", 191),

                ("Sales.TopSellingAndStagnant.View", "Sales", "TopSellingAndStagnant", "View", "عرض الأصناف الأكثر مبيعاً والراكدة", "View Top Selling and Stagnant Items", "الأصناف الأكثر مبيعاً والراكدة", 192),
                ("Sales.TopSellingAndStagnant.Print", "Sales", "TopSellingAndStagnant", "Print", "طباعة الأصناف الأكثر مبيعاً والراكدة", "Print Top Selling and Stagnant Items", "الأصناف الأكثر مبيعاً والراكدة", 193),
                ("Sales.TopSellingAndStagnant.Export", "Sales", "TopSellingAndStagnant", "Export", "تصدير الأصناف الأكثر مبيعاً والراكدة", "Export Top Selling and Stagnant Items", "الأصناف الأكثر مبيعاً والراكدة", 194),

                ("Sales.FulfillmentTracking.View", "Sales", "FulfillmentTracking", "View", "عرض متابعة استكمال فواتير البيع", "View Sales Fulfillment Tracking", "متابعة استكمال فواتير البيع", 195),
                ("Sales.FulfillmentTracking.Print", "Sales", "FulfillmentTracking", "Print", "طباعة متابعة استكمال فواتير البيع", "Print Sales Fulfillment Tracking", "متابعة استكمال فواتير البيع", 196),
                ("Sales.FulfillmentTracking.Export", "Sales", "FulfillmentTracking", "Export", "تصدير متابعة استكمال فواتير البيع", "Export Sales Fulfillment Tracking", "متابعة استكمال فواتير البيع", 197),

                // ─── Purchase Reports & Operations ───────────────────────────────────────
                ("Purchases.ReportsCatalog.View", "Purchases", "PurchaseReportsCatalog", "View", "عرض فهرس تقارير المشتريات", "View Purchase Reports Catalog", "فهرس تقارير المشتريات", 200),
                ("Purchases.PurchaseSummary.View", "Purchases", "PurchaseSummary", "View", "عرض ملخص المشتريات", "View Purchase Summary", "ملخص المشتريات", 201),
                ("Purchases.PurchaseSummary.Print", "Purchases", "PurchaseSummary", "Print", "طباعة ملخص المشتريات", "Print Purchase Summary", "ملخص المشتريات", 202),
                ("Purchases.PurchaseSummary.Export", "Purchases", "PurchaseSummary", "Export", "تصدير ملخص المشتريات", "Export Purchase Summary", "ملخص المشتريات", 203),

                ("Purchases.InvoiceDetails.View", "Purchases", "InvoiceDetails", "View", "عرض تفاصيل فواتير الشراء", "View Purchase Invoice Details", "تفاصيل فواتير الشراء", 204),
                ("Purchases.InvoiceDetails.Print", "Purchases", "InvoiceDetails", "Print", "طباعة تفاصيل فواتير الشراء", "Print Purchase Invoice Details", "تفاصيل فواتير الشراء", 205),
                ("Purchases.InvoiceDetails.Export", "Purchases", "InvoiceDetails", "Export", "تصدير تفاصيل فواتير الشراء", "Export Purchase Invoice Details", "تفاصيل فواتير الشراء", 206),

                ("Purchases.ByItem.View", "Purchases", "ByItem", "View", "عرض المشتريات حسب الصنف", "View Purchases by Item", "المشتريات حسب الصنف", 207),
                ("Purchases.ByItem.Print", "Purchases", "ByItem", "Print", "طباعة المشتريات حسب الصنف", "Print Purchases by Item", "المشتريات حسب الصنف", 208),
                ("Purchases.ByItem.Export", "Purchases", "ByItem", "Export", "تصدير المشتريات حسب الصنف", "Export Purchases by Item", "المشتريات حسب الصنف", 209),

                ("Purchases.ByVendor.View", "Purchases", "ByVendor", "View", "عرض المشتريات حسب المورد", "View Purchases by Vendor", "المشتريات حسب المورد", 210),
                ("Purchases.ByVendor.Print", "Purchases", "ByVendor", "Print", "طباعة المشتريات حسب المورد", "Print Purchases by Vendor", "المشتريات حسب المورد", 211),
                ("Purchases.ByVendor.Export", "Purchases", "ByVendor", "Export", "تصدير المشتريات حسب المورد", "Export Purchases by Vendor", "المشتريات حسب المورد", 212),

                ("Purchases.ReturnsAndNet.View", "Purchases", "ReturnsAndNet", "View", "عرض مرتجعات المشتريات وصافي المشتريات", "View Purchase Returns and Net", "مرتجعات وصافي المشتريات", 213),
                ("Purchases.ReturnsAndNet.Print", "Purchases", "ReturnsAndNet", "Print", "طباعة مرتجعات المشتريات وصافي المشتريات", "Print Purchase Returns and Net", "مرتجعات وصافي المشتريات", 214),
                ("Purchases.ReturnsAndNet.Export", "Purchases", "ReturnsAndNet", "Export", "تصدير مرتجعات المشتريات وصافي المشتريات", "Export Purchase Returns and Net", "مرتجعات وصافي المشتريات", 215),

                ("Purchases.PriceHistory.View", "Purchases", "PriceHistory", "View", "عرض مقارنة أسعار شراء الأصناف", "View Purchase Price History", "مقارنة أسعار شراء الأصناف", 216),
                ("Purchases.PriceHistory.Print", "Purchases", "PriceHistory", "Print", "طباعة مقارنة أسعار شراء الأصناف", "Print Purchase Price History", "مقارنة أسعار شراء الأصناف", 217),
                ("Purchases.PriceHistory.Export", "Purchases", "PriceHistory", "Export", "تصدير مقارنة أسعار شراء الأصناف", "Export Purchase Price History", "مقارنة أسعار شراء الأصناف", 218),

                ("Purchases.PendingStock.View", "Purchases", "PendingStock", "View", "عرض فواتير الشراء بدون حركة مخزنية", "View Purchase Invoices Pending Stock", "فواتير بدون حركة مخزنية", 219),
                ("Purchases.PendingStock.Print", "Purchases", "PendingStock", "Print", "طباعة فواتير الشراء بدون حركة مخزنية", "Print Purchase Invoices Pending Stock", "فواتير بدون حركة مخزنية", 220),
                ("Purchases.PendingStock.Export", "Purchases", "PendingStock", "Export", "تصدير فواتير الشراء بدون حركة مخزنية", "Export Purchase Invoices Pending Stock", "فواتير بدون حركة مخزنية", 221),

                ("Purchases.ReceiptTracking.View", "Purchases", "ReceiptTracking", "View", "عرض متابعة الكميات المستلمة للمشتريات", "View Purchase Receipt Tracking", "متابعة استلام كميات الشراء", 222),
                ("Purchases.ReceiptTracking.Print", "Purchases", "ReceiptTracking", "Print", "طباعة متابعة الكميات المستلمة للمشتريات", "Print Purchase Receipt Tracking", "متابعة استلام كميات الشراء", 223),
                ("Purchases.ReceiptTracking.Export", "Purchases", "ReceiptTracking", "Export", "تصدير متابعة الكميات المستلمة للمشتريات", "Export Purchase Receipt Tracking", "متابعة استلام كميات الشراء", 224),

                // ─── Inventory Reports & Operations ──────────────────────────────────────
                ("Inventory.ReportsCatalog.View", "Inventory", "InventoryReportsCatalog", "View", "عرض فهرس تقارير المخازن", "View Inventory Reports Catalog", "فهرس تقارير المخازن", 230),
                ("Inventory.StockBalances.View", "Inventory", "StockBalances", "View", "عرض أرصدة الأصناف بالمخازن", "View Stock Balances", "أرصدة الأصناف بالمخازن", 231),
                ("Inventory.StockBalances.Print", "Inventory", "StockBalances", "Print", "طباعة أرصدة الأصناف بالمخازن", "Print Stock Balances", "أرصدة الأصناف بالمخازن", 232),
                ("Inventory.StockBalances.Export", "Inventory", "StockBalances", "Export", "تصدير أرصدة الأصناف بالمخازن", "Export Stock Balances", "أرصدة الأصناف بالمخازن", 233),

                ("Inventory.ItemStockCard.View", "Inventory", "ItemStockCard", "View", "عرض كارت الصنف التفصيلي", "View Item Stock Card", "كارت الصنف التفصيلي", 234),
                ("Inventory.ItemStockCard.Print", "Inventory", "ItemStockCard", "Print", "طباعة كارت الصنف التفصيلي", "Print Item Stock Card", "كارت الصنف التفصيلي", 235),
                ("Inventory.ItemStockCard.Export", "Inventory", "ItemStockCard", "Export", "تصدير كارت الصنف التفصيلي", "Export Item Stock Card", "كارت الصنف التفصيلي", 236),

                ("Inventory.InwardOutward.View", "Inventory", "InwardOutward", "View", "عرض حركة الوارد والمنصرف خلال فترة", "View Inward and Outward Stock", "الوارد والمنصرف خلال فترة", 237),
                ("Inventory.InwardOutward.Print", "Inventory", "InwardOutward", "Print", "طباعة حركة الوارد والمنصرف خلال فترة", "Print Inward and Outward Stock", "الوارد والمنصرف خلال فترة", 238),
                ("Inventory.InwardOutward.Export", "Inventory", "InwardOutward", "Export", "تصدير حركة الوارد والمنصرف خلال فترة", "Export Inward and Outward Stock", "الوارد والمنصرف خلال فترة", 239),

                ("Inventory.WarehouseTransfers.View", "Inventory", "WarehouseTransfers", "View", "عرض تقرير التحويلات بين المخازن", "View Warehouse Transfers Report", "تقرير التحويلات بين المخازن", 240),
                ("Inventory.WarehouseTransfers.Print", "Inventory", "WarehouseTransfers", "Print", "طباعة تقرير التحويلات بين المخازن", "Print Warehouse Transfers Report", "تقرير التحويلات بين المخازن", 241),
                ("Inventory.WarehouseTransfers.Export", "Inventory", "WarehouseTransfers", "Export", "تصدير تقرير التحويلات بين المخازن", "Export Warehouse Transfers Report", "تقرير التحويلات بين المخازن", 242),

                ("Inventory.ReturnsAndAdjustments.View", "Inventory", "ReturnsAndAdjustments", "View", "عرض تقرير المرتجعات والتسويات المخزنية", "View Returns and Adjustments Report", "المرتجعات والتسويات المخزنية", 243),
                ("Inventory.ReturnsAndAdjustments.Print", "Inventory", "ReturnsAndAdjustments", "Print", "طباعة تقرير المرتجعات والتسويات المخزنية", "Print Returns and Adjustments Report", "المرتجعات والتسويات المخزنية", 244),
                ("Inventory.ReturnsAndAdjustments.Export", "Inventory", "ReturnsAndAdjustments", "Export", "تصدير تقرير المرتجعات والتسويات المخزنية", "Export Returns and Adjustments Report", "المرتجعات والتسويات المخزنية", 245),

                ("Inventory.ReservedAndAvailable.View", "Inventory", "ReservedAndAvailable", "View", "عرض الكميات المحجوزة والمتاحة للبيع", "View Reserved and Available Stock", "الكميات المحجوزة والمتاحة", 246),
                ("Inventory.ReservedAndAvailable.Print", "Inventory", "ReservedAndAvailable", "Print", "طباعة الكميات المحجوزة والمتاحة للبيع", "Print Reserved and Available Stock", "الكميات المحجوزة والمتاحة", 247),
                ("Inventory.ReservedAndAvailable.Export", "Inventory", "ReservedAndAvailable", "Export", "تصدير الكميات المحجوزة والمتاحة للبيع", "Export Reserved and Available Stock", "الكميات المحجوزة والمتاحة", 248),

                ("Inventory.ReorderLevelAlert.View", "Inventory", "ReorderLevelAlert", "View", "عرض تقرير حد إعادة الطلب والنواقص", "View Reorder Level Alert Report", "حد إعادة الطلب والنواقص", 249),
                ("Inventory.ReorderLevelAlert.Print", "Inventory", "ReorderLevelAlert", "Print", "طباعة تقرير حد إعادة الطلب والنواقص", "Print Reorder Level Alert Report", "حد إعادة الطلب والنواقص", 250),
                ("Inventory.ReorderLevelAlert.Export", "Inventory", "ReorderLevelAlert", "Export", "تصدير تقرير حد إعادة الطلب والنواقص", "Export Reorder Level Alert Report", "حد إعادة الطلب والنواقص", 251),

                ("Inventory.DeadStock.View", "Inventory", "DeadStock", "View", "عرض تقرير الأصناف الراكدة", "View Dead Stock Report", "تقرير الأصناف الراكدة", 252),
                ("Inventory.DeadStock.Print", "Inventory", "DeadStock", "Print", "طباعة تقرير الأصناف الراكدة", "Print Dead Stock Report", "تقرير الأصناف الراكدة", 253),
                ("Inventory.DeadStock.Export", "Inventory", "DeadStock", "Export", "تصدير تقرير الأصناف الراكدة", "Export Dead Stock Report", "تقرير الأصناف الراكدة", 254),

                ("Inventory.SerialNumbersReport.View", "Inventory", "SerialNumbersReport", "View", "عرض تقرير الأرقام المسلسلة وسجل حركتها", "View Serial Numbers Movement Report", "تقرير الأرقام المسلسلة وسجل حركتها", 255),
                ("Inventory.SerialNumbersReport.Print", "Inventory", "SerialNumbersReport", "Print", "طباعة تقرير الأرقام المسلسلة وسجل حركتها", "Print Serial Numbers Movement Report", "تقرير الأرقام المسلسلة وسجل حركتها", 256),
                ("Inventory.SerialNumbersReport.Export", "Inventory", "SerialNumbersReport", "Export", "تصدير تقرير الأرقام المسلسلة وسجل حركتها", "Export Serial Numbers Movement Report", "تقرير الأرقام المسلسلة وسجل حركتها", 257),

                ("Inventory.StockDiscrepancy.View", "Inventory", "StockDiscrepancy", "View", "عرض تقرير فروقات الرصيد المحسوب", "View Stock Discrepancy Report", "فروقات الرصيد المحسوب", 258),
                ("Inventory.StockDiscrepancy.Print", "Inventory", "StockDiscrepancy", "Print", "طباعة تقرير فروقات الرصيد المحسوب", "Print Stock Discrepancy Report", "فروقات الرصيد المحسوب", 259),
                ("Inventory.StockDiscrepancy.Export", "Inventory", "StockDiscrepancy", "Export", "تصدير تقرير فروقات الرصيد المحسوب", "Export Stock Discrepancy Report", "فروقات الرصيد المحسوب", 260),

                ("Inventory.SerialNumbers.View", "Inventory", "SerialNumbers", "View", "عرض شاشة استعلام وتتبع الأرقام المسلسلة", "View Serial Numbers Inquiry", "استعلام وتتبع الأرقام المسلسلة", 261),
                ("Inventory.SerialNumbers.Print", "Inventory", "SerialNumbers", "Print", "طباعة استعلام الأرقام المسلسلة", "Print Serial Numbers Inquiry", "استعلام وتتبع الأرقام المسلسلة", 262),
                ("Inventory.SerialNumbers.Export", "Inventory", "SerialNumbers", "Export", "تصدير استعلام الأرقام المسلسلة", "Export Serial Numbers Inquiry", "استعلام وتتبع الأرقام المسلسلة", 263),

                ("Inventory.DocumentAdjustments.View", "Inventory", "DocumentAdjustments", "View", "عرض تسويات الفواتير والمستندات", "View Document Adjustments", "تسويات المستندات والفواتير", 264),
                ("Inventory.DocumentAdjustments.Add", "Inventory", "DocumentAdjustments", "Add", "إنشاء تسوية مستند/فاتورة", "Create Document Adjustment", "تسويات المستندات والفواتير", 265),
                ("Inventory.DocumentAdjustments.Approve", "Inventory", "DocumentAdjustments", "Approve", "اعتماد تسوية مستند/فاتورة", "Approve Document Adjustment", "تسويات المستندات والفواتير", 266),
                ("Inventory.DocumentAdjustments.Cancel", "Inventory", "DocumentAdjustments", "Cancel", "إلغاء تسوية مستند/فاتورة", "Cancel Document Adjustment", "تسويات المستندات والفواتير", 267)
            };

            var existingPermCodes = (await context.Permissions.Select(p => p.Code).ToListAsync()).ToHashSet();
            var newPerms = permissionDefinitions
                .Where(p => !existingPermCodes.Contains(p.Code))
                .Select(p => new Permission
                {
                    Code = p.Code,
                    Module = p.Module,
                    Screen = p.Screen,
                    Action = p.Action,
                    NameAr = p.NameAr,
                    NameEn = p.NameEn,
                    SortOrder = p.Sort
                })
                .Concat(advancedReportAndScreenPermissions
                    .Where(p => !existingPermCodes.Contains(p.Code))
                    .Select(p => new Permission
                    {
                        Code = p.Code,
                        Module = p.Module,
                        Screen = p.Screen,
                        Action = p.Action,
                        NameAr = p.NameAr,
                        NameEn = p.NameEn,
                        Description = p.Description,
                        SortOrder = p.Sort
                    }))
                .ToList();

            if (newPerms.Any())
            {
                context.Permissions.AddRange(newPerms);
                await context.SaveChangesAsync();
            }

            // Also update Description for existing permissions if it was null
            var permDescMap = advancedReportAndScreenPermissions.ToDictionary(p => p.Code, p => p.Description);
            var existingWithNullDesc = await context.Permissions
                .Where(p => permDescMap.Keys.Contains(p.Code) && (p.Description == null || p.Description == ""))
                .ToListAsync();
            if (existingWithNullDesc.Any())
            {
                foreach (var ep in existingWithNullDesc)
                {
                    if (permDescMap.TryGetValue(ep.Code, out var desc))
                    {
                        ep.Description = desc;
                    }
                }
                await context.SaveChangesAsync();
            }

            // ─── 3. Assign all permissions to Admin Role ──────────────────────────────
            var adminRole = await roleManager.FindByNameAsync("Admin");
            if (adminRole != null)
            {
                var allPermissions = await context.Permissions.ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == adminRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingRolePerms = allPermissions
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = adminRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingRolePerms.Any())
                {
                    context.RolePermissions.AddRange(missingRolePerms);
                    await context.SaveChangesAsync();
                }
            }

            // Assign Accounting, Cash, and Reports to Account Manager
            var accountManagerRole = await roleManager.FindByNameAsync("Account Manager");
            if (accountManagerRole != null)
            {
                var targetPerms = await context.Permissions
                    .Where(p => p.Module == "Accounting" || p.Module == "Cash" || p.Module == "Reports")
                    .ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == accountManagerRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingPerms = targetPerms
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = accountManagerRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingPerms.Any())
                {
                    context.RolePermissions.AddRange(missingPerms);
                    await context.SaveChangesAsync();
                }
            }

            // Assign Cash Voucher permissions to Cashier
            var cashierRole = await roleManager.FindByNameAsync("Cashier");
            if (cashierRole != null)
            {
                var targetPerms = await context.Permissions
                    .Where(p => p.Module == "Cash" && (p.Action == "View" || p.Action == "Add" || p.Action == "Print"))
                    .ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == cashierRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingPerms = targetPerms
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = cashierRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingPerms.Any())
                {
                    context.RolePermissions.AddRange(missingPerms);
                    await context.SaveChangesAsync();
                }
            }

            // Assign Sales permissions to Sales Manager
            var salesManagerRole = await roleManager.FindByNameAsync("Sales Manager");
            if (salesManagerRole != null)
            {
                var targetPerms = await context.Permissions
                    .Where(p => p.Module == "Sales")
                    .ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == salesManagerRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingPerms = targetPerms
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = salesManagerRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingPerms.Any())
                {
                    context.RolePermissions.AddRange(missingPerms);
                    await context.SaveChangesAsync();
                }
            }

            // Assign Purchase permissions to Purchase Manager
            var purchaseManagerRole = await roleManager.FindByNameAsync("Purchase Manager");
            if (purchaseManagerRole != null)
            {
                var targetPerms = await context.Permissions
                    .Where(p => p.Module == "Purchases")
                    .ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == purchaseManagerRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingPerms = targetPerms
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = purchaseManagerRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingPerms.Any())
                {
                    context.RolePermissions.AddRange(missingPerms);
                    await context.SaveChangesAsync();
                }
            }

            // Assign Inventory permissions to Inventory Manager
            var inventoryManagerRole = await roleManager.FindByNameAsync("Inventory Manager");
            if (inventoryManagerRole != null)
            {
                var targetPerms = await context.Permissions
                    .Where(p => p.Module == "Inventory")
                    .ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == inventoryManagerRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingPerms = targetPerms
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = inventoryManagerRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingPerms.Any())
                {
                    context.RolePermissions.AddRange(missingPerms);
                    await context.SaveChangesAsync();
                }
            }

            // Assign Read/Report permissions to Auditor
            var auditorRole = await roleManager.FindByNameAsync("Auditor");
            if (auditorRole != null)
            {
                var targetPerms = await context.Permissions
                    .Where(p => (p.Module == "Accounting" || p.Module == "Cash" || p.Module == "Reports" || p.Module == "Administration" || p.Module == "Sales" || p.Module == "Purchases" || p.Module == "Inventory") &&
                                (p.Action == "View" || p.Action == "Print" || p.Action == "Export"))
                    .ToListAsync();
                var existingRolePermIds = (await context.RolePermissions
                    .Where(rp => rp.RoleId == auditorRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync()).ToHashSet();

                var missingPerms = targetPerms
                    .Where(p => !existingRolePermIds.Contains(p.Id))
                    .Select(p => new RolePermission
                    {
                        RoleId = auditorRole.Id,
                        PermissionId = p.Id,
                        IsGranted = true
                    })
                    .ToList();

                if (missingPerms.Any())
                {
                    context.RolePermissions.AddRange(missingPerms);
                    await context.SaveChangesAsync();
                }
            }

            // Fix typo in database if exists
            var typoPerm = await context.Permissions.FirstOrDefaultAsync(p => p.Code == "Cash.ReceiptVoucher.View" && p.NameAr.Contains("القبط"));
            if (typoPerm != null)
            {
                typoPerm.NameAr = "عرض سندات القبض";
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

            var existingCompanies = await context.Companies.ToDictionaryAsync(c => c.Code);
            var existingBranches = await context.Branches.ToDictionaryAsync(b => b.Code);

            foreach (var item in sisterCompaniesSetup)
            {
                if (!existingCompanies.TryGetValue(item.CompCode, out var company))
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
                    existingCompanies[item.CompCode] = company;
                }
                else
                {
                    company.Name = item.NameAr;
                    company.NameAr = item.NameAr;
                    company.NameEn = item.NameEn;
                    company.IsActive = true;
                }
            }
            await context.SaveChangesAsync();

            foreach (var item in sisterCompaniesSetup)
            {
                var company = existingCompanies[item.CompCode];
                SeedBranchesInMemory(context, item.Branches, company.Id, existingBranches);
            }
            await context.SaveChangesAsync();

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
                    new Currency { Code = "SAR", NameAr = "ريال سعودي", NameEn = "Saudi Riyal", Symbol = "ر.س", ExchangeRate = 12.9m, IsActive = true },
                    new Currency { Code = "AED", NameAr = "درهم إماراتي", NameEn = "UAE Dirham", Symbol = "د.إ", ExchangeRate = 13.2m, IsActive = true },
                    new Currency { Code = "CNY", NameAr = "يوان صيني", NameEn = "Chinese Yuan", Symbol = "¥", ExchangeRate = 6.8m, IsActive = true },
                    new Currency { Code = "GBP", NameAr = "جنيه إسترليني", NameEn = "British Pound", Symbol = "£", ExchangeRate = 62.0m, IsActive = true }
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

            // Seed Customer & Vendor Default Categories for each Branch
            var allBranchesList = await context.Branches.ToListAsync();
            var existingCustDefaultBranchIds = (await context.CustomerCategories
                .Where(cc => cc.IsDefault)
                .Select(cc => cc.BranchId)
                .ToListAsync()).ToHashSet();

            var existingVendDefaultBranchIds = (await context.VendorCategories
                .Where(vc => vc.IsDefault)
                .Select(vc => vc.BranchId)
                .ToListAsync()).ToHashSet();

            foreach (var b in allBranchesList)
            {
                if (!existingCustDefaultBranchIds.Contains(b.Id))
                {
                    bool isSpareParts = (b.NameAr ?? b.Name ?? "").Contains("قطع غيار");
                    context.CustomerCategories.AddRange(
                        new CustomerCategory
                        {
                            BranchId = b.Id,
                            Code = "EGP-MAC",
                            NameAr = "عملاء مصري ماكينات",
                            NameEn = "Egyptian Customers - Machines",
                            ControlAccountId = 620,
                            IsDefault = !isSpareParts,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        },
                        new CustomerCategory
                        {
                            BranchId = b.Id,
                            Code = "EGP-SPR",
                            NameAr = "عملاء مصري قطع غيار",
                            NameEn = "Egyptian Customers - Spare Parts",
                            ControlAccountId = 622,
                            IsDefault = isSpareParts,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        },
                        new CustomerCategory
                        {
                            BranchId = b.Id,
                            Code = "USD-MAC",
                            NameAr = "عملاء دولار ماكينات",
                            NameEn = "Dollar Customers - Machines",
                            ControlAccountId = 624,
                            IsDefault = false,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        },
                        new CustomerCategory
                        {
                            BranchId = b.Id,
                            Code = "USD-SPR",
                            NameAr = "عملاء دولار قطع غيار",
                            NameEn = "Dollar Customers - Spare Parts",
                            ControlAccountId = 626,
                            IsDefault = false,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        }
                    );
                    existingCustDefaultBranchIds.Add(b.Id);
                }

                if (!existingVendDefaultBranchIds.Contains(b.Id))
                {
                    context.VendorCategories.Add(new VendorCategory
                    {
                        BranchId = b.Id,
                        Code = b.BranchCode,
                        NameAr = b.NameAr ?? b.Name,
                        NameEn = b.NameEn ?? b.Name,
                        IsDefault = true,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                    existingVendDefaultBranchIds.Add(b.Id);
                }
            }
            await context.SaveChangesAsync();

            // Seed Code Sequences
            await SeedCodeSequencesAsync(context);

            // Seed Default Warehouses for Branches
            await SeedDefaultWarehousesAsync(context);

            // Seed Chart of Accounts (GLAccounts)
            await SeedChartOfAccountsAsync(context);

            // Ensure PartyGroups schema & core currencies
            await EnsurePartyGroupsSchemaAndCurrenciesAsync(context);
            await EnsureCurrencyDailyRatesSchemaAsync(context);
        }

        private static async Task SeedCodeSequencesAsync(ErpDbContext context)
        {
            // Global Entities
            var globalConfigs = new (string Entity, string Prefix)[]
            {
                ("Governorate",      "GOV"),
                ("City",             "CITY"),
                ("Currency",         "CUR"),
                ("Unit",             "UNT"),
                ("ItemGroup",        "GRP"),
                ("Item",             "ITM"),
                ("CustomerCategory", "CCAT"),
                ("VendorCategory",   "VCAT")
            };

            var existingGlobalSeq = (await context.CodeSequences
                .Where(s => s.BranchId == null)
                .Select(s => s.EntityName)
                .ToListAsync()).ToHashSet();

            foreach (var g in globalConfigs)
            {
                if (!existingGlobalSeq.Contains(g.Entity))
                {
                    context.CodeSequences.Add(new CodeSequence
                    {
                        EntityName      = g.Entity,
                        BranchId        = null,
                        Prefix          = g.Prefix,
                        LastNumber      = 0,
                        PaddingLength   = 6,
                        IsAuto          = true,
                        IsManualAllowed = true,
                        CreatedAt       = DateTime.UtcNow
                    });
                    existingGlobalSeq.Add(g.Entity);
                }
            }
            await context.SaveChangesAsync();

            // Branch-based Entities
            var branches = await context.Branches.ToListAsync();
            var existingBranchSeqs = await context.CodeSequences
                .Where(s => s.BranchId != null)
                .ToListAsync();
            var seqDict = existingBranchSeqs.ToDictionary(s => (s.EntityName, s.BranchId!.Value));

            foreach (var b in branches)
            {
                var branchEntities = new (string Entity, string Prefix)[]
                {
                    ("Customer",  $"{b.BranchCode}-C"),
                    ("Vendor",    $"{b.BranchCode}-V"),
                    ("Salesman",  "SLM"),
                    ("Warehouse", "WH")
                };

                foreach (var be in branchEntities)
                {
                    if (!seqDict.TryGetValue((be.Entity, b.Id), out var existingSeq))
                    {
                        var newSeq = new CodeSequence
                        {
                            EntityName      = be.Entity,
                            BranchId        = b.Id,
                            Prefix          = be.Prefix,
                            LastNumber      = 0,
                            PaddingLength   = 6,
                            IsAuto          = true,
                            IsManualAllowed = true,
                            CreatedAt       = DateTime.UtcNow
                        };
                        context.CodeSequences.Add(newSeq);
                        seqDict[(be.Entity, b.Id)] = newSeq;
                    }
                    else if ((be.Entity == "Customer" || be.Entity == "Vendor") && existingSeq.Prefix != be.Prefix)
                    {
                        existingSeq.Prefix = be.Prefix;
                    }
                }
            }
            await context.SaveChangesAsync();
        }

        private static async Task SeedDefaultWarehousesAsync(ErpDbContext context)
        {
            var branchesWithoutWarehouse = await context.Branches
                .Where(b => b.IsActive && !context.Warehouses.Any(w => w.BranchId == b.Id))
                .ToListAsync();

            if (!branchesWithoutWarehouse.Any())
                return;

            foreach (var b in branchesWithoutWarehouse)
            {
                var whCode = (!string.IsNullOrWhiteSpace(b.BranchCode) && b.BranchCode.StartsWith("B", StringComparison.OrdinalIgnoreCase))
                    ? "WH" + b.BranchCode.Substring(1)
                    : "WH" + b.BranchCode;

                var wh = new Warehouse
                {
                    BranchId = b.Id,
                    Code = whCode,
                    NameAr = !string.IsNullOrWhiteSpace(b.NameAr) ? b.NameAr.Trim() : b.Name,
                    NameEn = !string.IsNullOrWhiteSpace(b.NameEn) ? b.NameEn.Trim() : b.Name,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                context.Warehouses.Add(wh);

                var seq = await context.CodeSequences
                    .FirstOrDefaultAsync(s => s.EntityName == "Warehouse" && s.BranchId == b.Id);
                if (seq != null && seq.LastNumber == 0)
                {
                    seq.LastNumber = 1;
                    seq.UpdatedAt = DateTime.UtcNow;
                }
            }

            await context.SaveChangesAsync();
        }

        private static void SeedBranchesInMemory(
            ErpDbContext context,
            (string Code, string NameAr, string NameEn)[] list,
            int companyId,
            Dictionary<string, Branch> existingBranches)
        {
            foreach (var item in list)
            {
                if (!existingBranches.TryGetValue(item.Code, out var existing))
                {
                    var newBranch = new Branch
                    {
                        CompanyId = companyId,
                        Code = item.Code,
                        BranchCode = item.Code,
                        Name = item.NameAr,
                        NameAr = item.NameAr,
                        NameEn = item.NameEn,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    context.Branches.Add(newBranch);
                    existingBranches[item.Code] = newBranch;
                }
                else
                {
                    existing.CompanyId = companyId;
                    if (string.IsNullOrWhiteSpace(existing.BranchCode))
                    {
                        existing.BranchCode = existing.Code;
                    }
                    existing.NameAr = item.NameAr;
                    existing.NameEn = item.NameEn;
                }
            }
        }

        private static async Task SeedChartOfAccountsAsync(ErpDbContext context)
        {
            if (await context.GLAccounts.AnyAsync()) return;

            var currencies = await context.Currencies.ToDictionaryAsync(c => c.Code, c => c.Id);
            int? egpId = currencies.TryGetValue("EGP", out var eId) ? eId : null;
            int? usdId = currencies.TryGetValue("USD", out var uId) ? uId : null;

            var seedList = new List<(
                string Code, string NameAr, string NameEn, string ParentCode,
                bool IsGroup, int Level, AccountCategory Category, AccountNature Nature,
                bool IsControl, SubLedgerType SubLedger, bool IsRec, bool CostCenter, int? CurrencyId)>
            {
                // Level 1: الأصول
                ("1", "الأصول", "Assets", "", true, 1, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("11", "الأصول المتداولة", "Current Assets", "1", true, 2, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("1101", "النقدية وما في حكمها", "Cash and Cash Equivalents", "11", true, 3, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("110101", "صناديق وخزائن الفروع", "Cash in Hand", "1101", true, 4, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("11010101", "صندوق الإدارة العامة", "Main HQ Cash", "110101", false, 5, AccountCategory.Assets, AccountNature.Debit, true, SubLedgerType.Bank, false, false, egpId),
                ("11010102", "صندوق فرع القاهرة الرئيسي", "Cairo Branch Cash", "110101", false, 5, AccountCategory.Assets, AccountNature.Debit, true, SubLedgerType.Bank, false, false, egpId),
                ("11010103", "صندوق فرع الإسكندرية", "Alex Branch Cash", "110101", false, 5, AccountCategory.Assets, AccountNature.Debit, true, SubLedgerType.Bank, false, false, egpId),
                ("110102", "حسابات البنوك الجارية", "Banks Current Accounts", "1101", true, 4, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("11010201", "البنك الأهلي المصري - جاري محلي", "NBE - EGP Account", "110102", false, 5, AccountCategory.Assets, AccountNature.Debit, true, SubLedgerType.Bank, false, false, egpId),
                ("11010202", "بنك مصر - جاري دولار", "BM - USD Account", "110102", false, 5, AccountCategory.Assets, AccountNature.Debit, true, SubLedgerType.Bank, false, false, usdId),
                ("1102", "العملاء والمدينون", "Accounts Receivable", "11", true, 3, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("110201", "حساب مراقبة العملاء المحليين", "AR - Local Customers Control", "1102", false, 4, AccountCategory.Assets, AccountNature.Debit, true, SubLedgerType.Customer, false, false, null),
                ("110202", "حساب مراقبة عملاء التصدير", "AR - Export Customers Control", "1102", false, 4, AccountCategory.Assets, AccountNature.Debit, true, SubLedgerType.Customer, false, false, usdId),
                ("1103", "المخزون السلعي", "Inventory", "11", true, 3, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("110301", "مخزون البضائع الجاهزة", "Finished Goods Inventory", "1103", false, 4, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, false, egpId),
                ("110302", "مخزون الخامات ومستلزمات الإنتاج", "Raw Materials Inventory", "1103", false, 4, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, false, egpId),
                ("1104", "أرصدة مدينة أخرى", "Other Debit Balances", "11", true, 3, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("110401", "عهد وسلف الموظفين", "Employee Custody and Advances", "1104", false, 4, AccountCategory.Assets, AccountNature.Debit, true, SubLedgerType.Employee, false, false, egpId),
                ("110402", "مصروفات مدفوعة مقدماً", "Prepaid Expenses", "1104", false, 4, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, true, false, egpId),
                ("12", "الأصول غير المتداولة (الثابتة)", "Non-Current / Fixed Assets", "1", true, 2, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("120101", "الأراضي والمباني", "Lands and Buildings", "12", false, 3, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, false, egpId),
                ("120201", "الآلات والمعدات", "Machinery and Equipment", "12", false, 3, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, true, egpId),
                ("120301", "وسائل النقل والانتقال", "Vehicles and Transport", "12", false, 3, AccountCategory.Assets, AccountNature.Debit, false, SubLedgerType.None, false, true, egpId),

                // Level 1: الخصوم
                ("2", "الخصوم والالتزامات", "Liabilities", "", true, 1, AccountCategory.Liabilities, AccountNature.Credit, false, SubLedgerType.None, false, false, null),
                ("21", "الالتزامات المتداولة", "Current Liabilities", "2", true, 2, AccountCategory.Liabilities, AccountNature.Credit, false, SubLedgerType.None, false, false, null),
                ("2101", "الموردون والدائنون", "Accounts Payable", "21", true, 3, AccountCategory.Liabilities, AccountNature.Credit, false, SubLedgerType.None, false, false, null),
                ("210101", "حساب مراقبة الموردين المحليين", "AP - Local Vendors Control", "2101", false, 4, AccountCategory.Liabilities, AccountNature.Credit, true, SubLedgerType.Vendor, false, false, null),
                ("210102", "حساب مراقبة الموردين الخارجيين", "AP - Foreign Vendors Control", "2101", false, 4, AccountCategory.Liabilities, AccountNature.Credit, true, SubLedgerType.Vendor, false, false, usdId),
                ("2102", "الضرائب المستحقة", "Taxes Payable", "21", true, 3, AccountCategory.Liabilities, AccountNature.Credit, false, SubLedgerType.None, false, false, null),
                ("210201", "مصلحة الضرائب - ضريبة القيمة المضافة", "VAT Payable", "2102", false, 4, AccountCategory.Liabilities, AccountNature.Credit, false, SubLedgerType.None, true, false, egpId),
                ("210202", "مصلحة الضرائب - ضريبة كسب العمل", "Payroll Tax Payable", "2102", false, 4, AccountCategory.Liabilities, AccountNature.Credit, false, SubLedgerType.None, true, false, egpId),
                ("210203", "ضريبة الخصم والإضافة من المنبع", "Withholding Tax Payable", "2102", false, 4, AccountCategory.Liabilities, AccountNature.Credit, false, SubLedgerType.None, true, false, egpId),

                // Level 1: حقوق الملكية
                ("3", "حقوق الملكية", "Equity", "", true, 1, AccountCategory.Equity, AccountNature.Credit, false, SubLedgerType.None, false, false, null),
                ("310101", "رأس المال المصدر والمدفوع", "Paid-in Capital", "3", false, 2, AccountCategory.Equity, AccountNature.Credit, false, SubLedgerType.None, false, false, egpId),
                ("310201", "الأرباح / (الخسائر) المرحلة والمحتجزة", "Retained Earnings", "3", false, 2, AccountCategory.Equity, AccountNature.Credit, false, SubLedgerType.None, false, false, egpId),
                ("310301", "جاري الشركاء / المساهمين", "Current Partners Account", "3", false, 2, AccountCategory.Equity, AccountNature.Credit, false, SubLedgerType.None, false, false, egpId),

                // Level 1: الإيرادات
                ("4", "الإيرادات", "Revenues", "", true, 1, AccountCategory.Revenues, AccountNature.Credit, false, SubLedgerType.None, false, false, null),
                ("41", "إيرادات النشاط الرئيسي", "Operating Revenues", "4", true, 2, AccountCategory.Revenues, AccountNature.Credit, false, SubLedgerType.None, false, false, null),
                ("410101", "إيرادات مبيعات البضائع المحلية", "Domestic Sales Revenue", "41", false, 3, AccountCategory.Revenues, AccountNature.Credit, false, SubLedgerType.None, false, true, egpId),
                ("410102", "إيرادات مبيعات التصدير", "Export Sales Revenue", "41", false, 3, AccountCategory.Revenues, AccountNature.Credit, false, SubLedgerType.None, false, true, usdId),
                ("410201", "مردودات ومسموحات المبيعات", "Sales Returns and Allowances", "41", false, 3, AccountCategory.Revenues, AccountNature.Debit, false, SubLedgerType.None, false, true, egpId),
                ("420101", "إيرادات استثمارات وفوائد بنكية", "Interest and Investment Income", "4", false, 2, AccountCategory.Revenues, AccountNature.Credit, false, SubLedgerType.None, false, false, egpId),

                // Level 1: المصروفات
                ("5", "المصروفات والتكاليف", "Expenses", "", true, 1, AccountCategory.Expenses, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("51", "تكلفة المبيعات (تكلفة النشاط)", "Cost of Goods Sold", "5", true, 2, AccountCategory.Expenses, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("510101", "تكلفة البضاعة المباعة", "Cost of Goods Sold", "51", false, 3, AccountCategory.Expenses, AccountNature.Debit, false, SubLedgerType.None, false, true, egpId),
                ("52", "المصروفات العمومية والإدارية", "General & Admin Expenses", "5", true, 2, AccountCategory.Expenses, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("520101", "رواتب وأجور الموظفين", "Salaries & Wages", "52", false, 3, AccountCategory.Expenses, AccountNature.Debit, false, SubLedgerType.None, false, true, egpId),
                ("520201", "مصروف إيجار مقرات وفروع", "Rent Expense", "52", false, 3, AccountCategory.Expenses, AccountNature.Debit, false, SubLedgerType.None, false, true, egpId),
                ("520301", "استهلاك كهرباء ومياه وإنترنت", "Utilities Expense", "52", false, 3, AccountCategory.Expenses, AccountNature.Debit, false, SubLedgerType.None, false, true, egpId),
                ("520401", "مصروفات بنكية وعمولات", "Bank Charges & Commissions", "52", false, 3, AccountCategory.Expenses, AccountNature.Debit, false, SubLedgerType.None, false, false, egpId),
                ("53", "المصروفات التسويقية والبيعية", "Selling & Marketing Expenses", "5", true, 2, AccountCategory.Expenses, AccountNature.Debit, false, SubLedgerType.None, false, false, null),
                ("530101", "مصروفات الدعاية والإعلان", "Advertising & Marketing", "53", false, 3, AccountCategory.Expenses, AccountNature.Debit, false, SubLedgerType.None, false, true, egpId),
                ("530201", "عمولات بيعية لمندوبي المبيعات", "Sales Commissions", "53", false, 3, AccountCategory.Expenses, AccountNature.Debit, false, SubLedgerType.None, false, true, egpId)
            };

            var addedAccounts = new Dictionary<string, GLAccount>();

            foreach (var item in seedList)
            {
                int? parentId = null;
                if (!string.IsNullOrWhiteSpace(item.ParentCode) && addedAccounts.TryGetValue(item.ParentCode, out var parent))
                {
                    parentId = parent.Id;
                }

                var acc = new GLAccount
                {
                    Code = item.Code,
                    NameAr = item.NameAr,
                    NameEn = item.NameEn,
                    ParentId = parentId,
                    Level = item.Level,
                    IsGroup = item.IsGroup,
                    AccountCategory = item.Category,
                    AccountNature = item.Nature,
                    IsControlAccount = item.IsControl,
                    SubLedgerType = item.SubLedger,
                    IsReconciliationAccount = item.IsRec,
                    RequiresCostCenter = item.CostCenter,
                    CurrencyId = item.CurrencyId,
                    BranchId = item.Code == "11010102" ? (await context.Branches.FirstOrDefaultAsync(b => b.Code == "BR-CAI" || b.BranchCode == "BR-CAI" || b.NameAr.Contains("القاهرة")))?.Id
                             : item.Code == "11010103" ? (await context.Branches.FirstOrDefaultAsync(b => b.Code == "BR-ALX" || b.BranchCode == "BR-ALX" || b.NameAr.Contains("الإسكندرية")))?.Id
                             : null,
                    AccountUsage = AccountUsage.All,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                context.GLAccounts.Add(acc);
                await context.SaveChangesAsync();
                addedAccounts[acc.Code] = acc;
            }

            // Link CustomerCategory and VendorCategory default Control Accounts
            if (addedAccounts.TryGetValue("110201", out var arControl))
            {
                var custCats = await context.CustomerCategories.Where(c => c.ControlAccountId == null).ToListAsync();
                foreach (var cc in custCats)
                {
                    cc.ControlAccountId = arControl.Id;
                }
            }

            if (addedAccounts.TryGetValue("210101", out var apControl))
            {
                var vendCats = await context.VendorCategories.Where(v => v.ControlAccountId == null).ToListAsync();
                foreach (var vc in vendCats)
                {
                    vc.ControlAccountId = apControl.Id;
                }
            }

            await context.SaveChangesAsync();
        }

        private static async Task EnsurePartyGroupsSchemaAndCurrenciesAsync(ErpDbContext context)
        {
            if (context.Database.IsRelational())
            {
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PartyGroups')
                        BEGIN
                            CREATE TABLE PartyGroups (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                Code NVARCHAR(50) NOT NULL,
                                NameAr NVARCHAR(200) NOT NULL,
                                NameEn NVARCHAR(200) NOT NULL,
                                BranchId INT NULL,
                                Notes NVARCHAR(1000) NULL,
                                IsActive BIT NOT NULL DEFAULT 1,
                                CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                                UpdatedAt DATETIME2 NULL,
                                CONSTRAINT FK_PartyGroups_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id)
                            );
                            CREATE UNIQUE INDEX IX_PartyGroups_Code ON PartyGroups(Code);
                        END

                        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Customers')
                           AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Customers' AND COLUMN_NAME = 'PartyGroupId')
                        BEGIN
                            ALTER TABLE Customers ADD PartyGroupId INT NULL;
                            ALTER TABLE Customers ADD CONSTRAINT FK_Customers_PartyGroups_PartyGroupId
                                FOREIGN KEY (PartyGroupId) REFERENCES PartyGroups(Id) ON DELETE SET NULL;
                            CREATE INDEX IX_Customers_PartyGroupId ON Customers(PartyGroupId);
                        END

                        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Vendors')
                           AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Vendors' AND COLUMN_NAME = 'PartyGroupId')
                        BEGIN
                            ALTER TABLE Vendors ADD PartyGroupId INT NULL;
                            ALTER TABLE Vendors ADD CONSTRAINT FK_Vendors_PartyGroups_PartyGroupId
                                FOREIGN KEY (PartyGroupId) REFERENCES PartyGroups(Id) ON DELETE SET NULL;
                            CREATE INDEX IX_Vendors_PartyGroupId ON Vendors(PartyGroupId);
                        END
                    ");
                }
                catch
                {
                    // Ignore if schema is not yet initialized on a fresh DB (handled later in InitializeAsync)
                }
            }

            try
            {
                var requiredCurrencies = new (string Code, string NameAr, string NameEn, string Symbol, decimal Rate)[]
                {
                    ("EGP", "جنيه مصري", "Egyptian Pound", "ج.م", 1.0m),
                    ("USD", "دولار أمريكي", "US Dollar", "$", 48.5m),
                    ("EUR", "يورو", "Euro", "€", 53.0m),
                    ("CNY", "يوان صيني", "Chinese Yuan", "¥", 6.8m)
                };

                var existingCodes = (await context.Currencies.Select(c => c.Code).ToListAsync())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                bool added = false;
                foreach (var rc in requiredCurrencies)
                {
                    if (!existingCodes.Contains(rc.Code))
                    {
                        context.Currencies.Add(new Currency
                        {
                            Code = rc.Code,
                            NameAr = rc.NameAr,
                            NameEn = rc.NameEn,
                            Symbol = rc.Symbol,
                            ExchangeRate = rc.Rate,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        });
                        added = true;
                    }
                }
                if (added)
                {
                    await context.SaveChangesAsync();
                }
            }
            catch
            {
                // Ignore if Currencies table not created yet on fresh DB
            }
        }

        private static async Task EnsureCurrencyDailyRatesSchemaAsync(ErpDbContext context)
        {
            if (context.Database.IsRelational())
            {
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        IF COL_LENGTH('Currencies', 'IsBaseCurrency') IS NULL
                        BEGIN
                            ALTER TABLE Currencies ADD IsBaseCurrency BIT NOT NULL DEFAULT 0;
                        END

                        IF OBJECT_ID('CurrencyDailyRates', 'U') IS NULL
                        BEGIN
                            CREATE TABLE CurrencyDailyRates (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                CurrencyId INT NOT NULL,
                                RateDate DATE NOT NULL,
                                ExchangeRate DECIMAL(18, 6) NOT NULL,
                                Notes NVARCHAR(500) NULL,
                                CreatedByUserId NVARCHAR(100) NULL,
                                CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                                UpdatedByUserId NVARCHAR(100) NULL,
                                UpdatedAt DATETIME2 NULL,
                                RowVersion ROWVERSION NOT NULL,
                                CONSTRAINT FK_CurrencyDailyRates_Currencies_CurrencyId FOREIGN KEY (CurrencyId) REFERENCES Currencies(Id),
                                CONSTRAINT UQ_CurrencyDailyRates_Currency_Date UNIQUE (CurrencyId, RateDate)
                            );
                            CREATE INDEX IX_CurrencyDailyRates_RateDate ON CurrencyDailyRates(RateDate);
                        END
                    ");
                }
                catch
                {
                    // Ignore on non-SQL Server or uninitialized schema
                }
            }

            try
            {
                // Ensure base currency flag
                var egp = await context.Currencies.FirstOrDefaultAsync(c => c.Code == "EGP");
                if (egp != null && !egp.IsBaseCurrency)
                {
                    egp.IsBaseCurrency = true;
                    egp.ExchangeRate = 1.0m;
                    await context.SaveChangesAsync();
                }

                // Seed initial daily rates for foreign currencies if none exist
                if (!await context.CurrencyDailyRates.AnyAsync())
                {
                    var foreignCurrencies = await context.Currencies.Where(c => !c.IsBaseCurrency && c.IsActive).ToListAsync();
                    var today = DateTime.UtcNow.Date;
                    foreach (var curr in foreignCurrencies)
                    {
                        context.CurrencyDailyRates.Add(new CurrencyDailyRate
                        {
                            CurrencyId = curr.Id,
                            RateDate = today,
                            ExchangeRate = curr.ExchangeRate > 0 ? curr.ExchangeRate : 1.0m,
                            Notes = "سعر صرف افتراضي أولي مسجل في النظام",
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    await context.SaveChangesAsync();
                }
            }
            catch
            {
                // Ignore if tables not yet created
            }
        }
    }
}
