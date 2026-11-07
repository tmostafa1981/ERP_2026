using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultPartiesAndCashAccountToTransactionType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('TransactionTypes') AND name = 'DefaultCustomerId')
BEGIN
    ALTER TABLE [TransactionTypes] ADD [DefaultCustomerId] int NULL;
    ALTER TABLE [TransactionTypes] ADD CONSTRAINT [FK_TransactionTypes_Customers_DefaultCustomerId] FOREIGN KEY ([DefaultCustomerId]) REFERENCES [Customers] ([Id]);
    CREATE INDEX [IX_TransactionTypes_DefaultCustomerId] ON [TransactionTypes] ([DefaultCustomerId]);
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('TransactionTypes') AND name = 'DefaultVendorId')
BEGIN
    ALTER TABLE [TransactionTypes] ADD [DefaultVendorId] int NULL;
    ALTER TABLE [TransactionTypes] ADD CONSTRAINT [FK_TransactionTypes_Vendors_DefaultVendorId] FOREIGN KEY ([DefaultVendorId]) REFERENCES [Vendors] ([Id]);
    CREATE INDEX [IX_TransactionTypes_DefaultVendorId] ON [TransactionTypes] ([DefaultVendorId]);
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('TransactionTypes') AND name = 'DefaultCashBankAccountId')
BEGIN
    ALTER TABLE [TransactionTypes] ADD [DefaultCashBankAccountId] int NULL;
    ALTER TABLE [TransactionTypes] ADD CONSTRAINT [FK_TransactionTypes_GLAccounts_DefaultCashBankAccountId] FOREIGN KEY ([DefaultCashBankAccountId]) REFERENCES [GLAccounts] ([Id]);
    CREATE INDEX [IX_TransactionTypes_DefaultCashBankAccountId] ON [TransactionTypes] ([DefaultCashBankAccountId]);
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TransactionTypes_Customers_DefaultCustomerId')
    ALTER TABLE [TransactionTypes] DROP CONSTRAINT [FK_TransactionTypes_Customers_DefaultCustomerId];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TransactionTypes_DefaultCustomerId')
    DROP INDEX [IX_TransactionTypes_DefaultCustomerId] ON [TransactionTypes];
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('TransactionTypes') AND name = 'DefaultCustomerId')
    ALTER TABLE [TransactionTypes] DROP COLUMN [DefaultCustomerId];

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TransactionTypes_Vendors_DefaultVendorId')
    ALTER TABLE [TransactionTypes] DROP CONSTRAINT [FK_TransactionTypes_Vendors_DefaultVendorId];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TransactionTypes_DefaultVendorId')
    DROP INDEX [IX_TransactionTypes_DefaultVendorId] ON [TransactionTypes];
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('TransactionTypes') AND name = 'DefaultVendorId')
    ALTER TABLE [TransactionTypes] DROP COLUMN [DefaultVendorId];

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TransactionTypes_GLAccounts_DefaultCashBankAccountId')
    ALTER TABLE [TransactionTypes] DROP CONSTRAINT [FK_TransactionTypes_GLAccounts_DefaultCashBankAccountId];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TransactionTypes_DefaultCashBankAccountId')
    DROP INDEX [IX_TransactionTypes_DefaultCashBankAccountId] ON [TransactionTypes];
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('TransactionTypes') AND name = 'DefaultCashBankAccountId')
    ALTER TABLE [TransactionTypes] DROP COLUMN [DefaultCashBankAccountId];
");
        }
    }
}
