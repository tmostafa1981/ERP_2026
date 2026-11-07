using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOptimisticConcurrencyRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- 1. Drop old foreign keys if they exist
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AccountPermissions_Accounts_AccountId')
    ALTER TABLE [AccountPermissions] DROP CONSTRAINT [FK_AccountPermissions_Accounts_AccountId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Customers_Accounts_FinancialAccountId')
    ALTER TABLE [Customers] DROP CONSTRAINT [FK_Customers_Accounts_FinancialAccountId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_JournalEntryLines_Accounts_AccountId')
    ALTER TABLE [JournalEntryLines] DROP CONSTRAINT [FK_JournalEntryLines_Accounts_AccountId];
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Vendors_Accounts_FinancialAccountId')
    ALTER TABLE [Vendors] DROP CONSTRAINT [FK_Vendors_Accounts_FinancialAccountId];

-- 2. Drop old tables if they exist
IF OBJECT_ID('dbo.Accounts', 'U') IS NOT NULL DROP TABLE [dbo].[Accounts];
IF OBJECT_ID('dbo.AccountGroups', 'U') IS NOT NULL DROP TABLE [dbo].[AccountGroups];

-- 3. Ensure GLAccounts exists and has RowVersion
IF OBJECT_ID('dbo.GLAccounts', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[GLAccounts] (
        [Id] int NOT NULL IDENTITY(1,1),
        [RowVersion] rowversion NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [NameAr] nvarchar(200) NOT NULL,
        [NameEn] nvarchar(200) NOT NULL,
        [ParentId] int NULL,
        [Level] int NOT NULL DEFAULT 1,
        [IsGroup] bit NOT NULL DEFAULT 0,
        [AccountNature] int NOT NULL,
        [AccountCategory] int NOT NULL,
        [CurrencyId] int NULL,
        [IsControlAccount] bit NOT NULL DEFAULT 0,
        [SubLedgerType] int NOT NULL,
        [IsReconciliationAccount] bit NOT NULL DEFAULT 0,
        [RequiresCostCenter] bit NOT NULL DEFAULT 0,
        [AccountUsage] int NOT NULL,
        [IsActive] bit NOT NULL DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_GLAccounts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_GLAccounts_Currencies_CurrencyId] FOREIGN KEY ([CurrencyId]) REFERENCES [dbo].[Currencies] ([Id]),
        CONSTRAINT [FK_GLAccounts_GLAccounts_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [dbo].[GLAccounts] ([Id])
    );
END
ELSE
BEGIN
    IF COL_LENGTH('dbo.GLAccounts', 'RowVersion') IS NULL
        ALTER TABLE [dbo].[GLAccounts] ADD [RowVersion] rowversion NOT NULL;
END

-- 4. Add RowVersion to all other 18 tables
DECLARE @tables TABLE (tbl NVARCHAR(100));
INSERT INTO @tables VALUES 
('Warehouses'), ('Vendors'), ('VendorCategories'), ('Units'), ('Salesmen'), 
('JournalEntryHeaders'), ('ItemUnits'), ('Items'), ('ItemGroups'), ('Governorates'), 
('FiscalYears'), ('Customers'), ('CustomerCategories'), ('Currencies'), ('CostCenters'), 
('Companies'), ('Cities'), ('Branches');

DECLARE @t NVARCHAR(100), @sql NVARCHAR(MAX);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT tbl FROM @tables;
OPEN cur;
FETCH NEXT FROM cur INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF COL_LENGTH('dbo.' + @t, 'RowVersion') IS NULL
    BEGIN
        SET @sql = 'ALTER TABLE [dbo].[' + @t + '] ADD [RowVersion] rowversion NOT NULL;';
        EXEC sp_executesql @sql;
    END
    FETCH NEXT FROM cur INTO @t;
END
CLOSE cur;
DEALLOCATE cur;

-- 5. Indexes on GLAccounts
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GLAccounts_Code' AND object_id = OBJECT_ID('dbo.GLAccounts'))
    CREATE UNIQUE INDEX [IX_GLAccounts_Code] ON [dbo].[GLAccounts] ([Code]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GLAccounts_Control_SubLedger' AND object_id = OBJECT_ID('dbo.GLAccounts'))
    CREATE INDEX [IX_GLAccounts_Control_SubLedger] ON [dbo].[GLAccounts] ([IsControlAccount], [SubLedgerType]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GLAccounts_CurrencyId' AND object_id = OBJECT_ID('dbo.GLAccounts'))
    CREATE INDEX [IX_GLAccounts_CurrencyId] ON [dbo].[GLAccounts] ([CurrencyId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GLAccounts_IsGroup' AND object_id = OBJECT_ID('dbo.GLAccounts'))
    CREATE INDEX [IX_GLAccounts_IsGroup] ON [dbo].[GLAccounts] ([IsGroup]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GLAccounts_ParentId' AND object_id = OBJECT_ID('dbo.GLAccounts'))
    CREATE INDEX [IX_GLAccounts_ParentId] ON [dbo].[GLAccounts] ([ParentId]);

-- 6. Indexes on categories
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CustomerCategories_ControlAccountId' AND object_id = OBJECT_ID('dbo.CustomerCategories'))
    CREATE INDEX [IX_CustomerCategories_ControlAccountId] ON [dbo].[CustomerCategories] ([ControlAccountId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorCategories_ControlAccountId' AND object_id = OBJECT_ID('dbo.VendorCategories'))
    CREATE INDEX [IX_VendorCategories_ControlAccountId] ON [dbo].[VendorCategories] ([ControlAccountId]);

-- 7. Foreign keys referencing GLAccounts
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AccountPermissions_GLAccounts_AccountId')
    ALTER TABLE [dbo].[AccountPermissions] ADD CONSTRAINT [FK_AccountPermissions_GLAccounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[GLAccounts] ([Id]) ON DELETE CASCADE;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CustomerCategories_GLAccounts_ControlAccountId')
    ALTER TABLE [dbo].[CustomerCategories] ADD CONSTRAINT [FK_CustomerCategories_GLAccounts_ControlAccountId] FOREIGN KEY ([ControlAccountId]) REFERENCES [dbo].[GLAccounts] ([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Customers_GLAccounts_FinancialAccountId')
    ALTER TABLE [dbo].[Customers] ADD CONSTRAINT [FK_Customers_GLAccounts_FinancialAccountId] FOREIGN KEY ([FinancialAccountId]) REFERENCES [dbo].[GLAccounts] ([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_JournalEntryLines_GLAccounts_AccountId')
    ALTER TABLE [dbo].[JournalEntryLines] ADD CONSTRAINT [FK_JournalEntryLines_GLAccounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[GLAccounts] ([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_VendorCategories_GLAccounts_ControlAccountId')
    ALTER TABLE [dbo].[VendorCategories] ADD CONSTRAINT [FK_VendorCategories_GLAccounts_ControlAccountId] FOREIGN KEY ([ControlAccountId]) REFERENCES [dbo].[GLAccounts] ([Id]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Vendors_GLAccounts_FinancialAccountId')
    ALTER TABLE [dbo].[Vendors] ADD CONSTRAINT [FK_Vendors_GLAccounts_FinancialAccountId] FOREIGN KEY ([FinancialAccountId]) REFERENCES [dbo].[GLAccounts] ([Id]);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccountPermissions_GLAccounts_AccountId",
                table: "AccountPermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerCategories_GLAccounts_ControlAccountId",
                table: "CustomerCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_GLAccounts_FinancialAccountId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_GLAccounts_AccountId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorCategories_GLAccounts_ControlAccountId",
                table: "VendorCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendors_GLAccounts_FinancialAccountId",
                table: "Vendors");

            migrationBuilder.DropTable(
                name: "GLAccounts");

            migrationBuilder.DropIndex(
                name: "IX_VendorCategories_ControlAccountId",
                table: "VendorCategories");

            migrationBuilder.DropIndex(
                name: "IX_CustomerCategories_ControlAccountId",
                table: "CustomerCategories");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "VendorCategories");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Salesmen");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "JournalEntryHeaders");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ItemUnits");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ItemGroups");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Governorates");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "FiscalYears");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CustomerCategories");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Currencies");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CostCenters");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Cities");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Branches");

            migrationBuilder.CreateTable(
                name: "AccountGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    AccountNature = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountGroups_AccountGroups_ParentId",
                        column: x => x.ParentId,
                        principalTable: "AccountGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountGroupId = table.Column<int>(type: "int", nullable: false),
                    CurrencyId = table.Column<int>(type: "int", nullable: true),
                    AccountUsage = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsReconciliationAccount = table.Column<bool>(type: "bit", nullable: false),
                    IsTransactional = table.Column<bool>(type: "bit", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequiresCostCenter = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_AccountGroups_AccountGroupId",
                        column: x => x.AccountGroupId,
                        principalTable: "AccountGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Accounts_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountGroups_Code",
                table: "AccountGroups",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountGroups_ParentId",
                table: "AccountGroups",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_AccountGroupId",
                table: "Accounts",
                column: "AccountGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Code",
                table: "Accounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CurrencyId",
                table: "Accounts",
                column: "CurrencyId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountPermissions_Accounts_AccountId",
                table: "AccountPermissions",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Accounts_FinancialAccountId",
                table: "Customers",
                column: "FinancialAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Accounts_AccountId",
                table: "JournalEntryLines",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vendors_Accounts_FinancialAccountId",
                table: "Vendors",
                column: "FinancialAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
