using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCompanyAndEmployeeCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── FiscalYears: add columns only if they don't already exist ────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='FiscalYears' AND COLUMN_NAME='CreatedAt')
                    ALTER TABLE [FiscalYears] ADD [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE();
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='FiscalYears' AND COLUMN_NAME='NameAr')
                    ALTER TABLE [FiscalYears] ADD [NameAr] nvarchar(max) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='FiscalYears' AND COLUMN_NAME='NameEn')
                    ALTER TABLE [FiscalYears] ADD [NameEn] nvarchar(max) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='FiscalYears' AND COLUMN_NAME='UpdatedAt')
                    ALTER TABLE [FiscalYears] ADD [UpdatedAt] datetime2 NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='FiscalYears' AND COLUMN_NAME='Year')
                    ALTER TABLE [FiscalYears] ADD [Year] nvarchar(max) NOT NULL DEFAULT '';
            ");

            // ── Branches: add columns only if they don't already exist ──────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Branches' AND COLUMN_NAME='AddressAr')
                    ALTER TABLE [Branches] ADD [AddressAr] nvarchar(max) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Branches' AND COLUMN_NAME='AddressEn')
                    ALTER TABLE [Branches] ADD [AddressEn] nvarchar(max) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Branches' AND COLUMN_NAME='CompanyId')
                    ALTER TABLE [Branches] ADD [CompanyId] int NOT NULL DEFAULT 1;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Branches' AND COLUMN_NAME='CreatedAt')
                    ALTER TABLE [Branches] ADD [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE();
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Branches' AND COLUMN_NAME='Email')
                    ALTER TABLE [Branches] ADD [Email] nvarchar(max) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Branches' AND COLUMN_NAME='NameAr')
                    ALTER TABLE [Branches] ADD [NameAr] nvarchar(max) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Branches' AND COLUMN_NAME='NameEn')
                    ALTER TABLE [Branches] ADD [NameEn] nvarchar(max) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Branches' AND COLUMN_NAME='Phone')
                    ALTER TABLE [Branches] ADD [Phone] nvarchar(max) NULL;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Branches' AND COLUMN_NAME='UpdatedAt')
                    ALTER TABLE [Branches] ADD [UpdatedAt] datetime2 NULL;
            ");

            // ── AspNetUsers: add new columns only if they don't already exist ────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AspNetUsers' AND COLUMN_NAME='EmployeeCode')
                    ALTER TABLE [AspNetUsers] ADD [EmployeeCode] nvarchar(max) NOT NULL DEFAULT '';
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AspNetUsers' AND COLUMN_NAME='HasAllBranches')
                    ALTER TABLE [AspNetUsers] ADD [HasAllBranches] bit NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AspNetUsers' AND COLUMN_NAME='HasAllCompanies')
                    ALTER TABLE [AspNetUsers] ADD [HasAllCompanies] bit NOT NULL DEFAULT 0;
            ");

            // ── Companies table: create only if it doesn't already exist ─────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Companies')
                BEGIN
                    CREATE TABLE [Companies] (
                        [Id]        int IDENTITY(1,1) NOT NULL,
                        [Code]      nvarchar(max) NOT NULL,
                        [Name]      nvarchar(max) NOT NULL,
                        [NameAr]    nvarchar(max) NULL,
                        [NameEn]    nvarchar(max) NULL,
                        [AddressAr] nvarchar(max) NULL,
                        [AddressEn] nvarchar(max) NULL,
                        [Phone]     nvarchar(max) NULL,
                        [Email]     nvarchar(max) NULL,
                        [TaxNumber] nvarchar(max) NULL,
                        [IsActive]  bit NOT NULL DEFAULT 1,
                        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
                        [UpdatedAt] datetime2 NULL,
                        CONSTRAINT [PK_Companies] PRIMARY KEY ([Id])
                    );
                END
            ");

            // ── UserCompanies table: create only if it doesn't already exist ─────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='UserCompanies')
                BEGIN
                    CREATE TABLE [UserCompanies] (
                        [Id]        int IDENTITY(1,1) NOT NULL,
                        [UserId]    nvarchar(max) NOT NULL,
                        [CompanyId] int NOT NULL,
                        CONSTRAINT [PK_UserCompanies] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_UserCompanies_Companies_CompanyId]
                            FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_UserCompanies_CompanyId] ON [UserCompanies]([CompanyId]);
                END
            ");

            // ── Branches FK and Index: safe creation ─────────────────────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name='IX_Branches_CompanyId' AND object_id=OBJECT_ID('Branches'))
                BEGIN
                    CREATE INDEX [IX_Branches_CompanyId] ON [Branches]([CompanyId]);
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name='FK_Branches_Companies_CompanyId')
                AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Companies')
                BEGIN
                    ALTER TABLE [Branches]
                        ADD CONSTRAINT [FK_Branches_Companies_CompanyId]
                        FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id])
                        ON DELETE NO ACTION;
                END
            ");

            // ── Register this migration in __EFMigrationsHistory ─────────────────
            // (EF Core does this automatically after Up() succeeds)
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Branches_Companies_CompanyId",
                table: "Branches");

            migrationBuilder.DropTable(
                name: "UserCompanies");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Branches_CompanyId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "FiscalYears");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "FiscalYears");

            migrationBuilder.DropColumn(
                name: "NameEn",
                table: "FiscalYears");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "FiscalYears");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "FiscalYears");

            migrationBuilder.DropColumn(
                name: "AddressAr",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "AddressEn",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "NameEn",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "EmployeeCode",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HasAllBranches",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HasAllCompanies",
                table: "AspNetUsers");
        }
    }
}
