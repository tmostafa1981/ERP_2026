using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase4_CoreAccountingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VendorCategories_Code",
                table: "VendorCategories");

            migrationBuilder.DropIndex(
                name: "IX_CustomerCategories_Code",
                table: "CustomerCategories");

            migrationBuilder.AddColumn<int>(
                name: "FinancialAccountId",
                table: "Vendors",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mobile2",
                table: "Vendors",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Vendors",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "VendorCategories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "VendorCategories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FinancialAccountId",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mobile2",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Customers",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "CustomerCategories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "CustomerCategories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "Branches",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AccountGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Level = table.Column<int>(type: "int", nullable: false),
                    AccountNature = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                name: "CodeSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    Prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LastNumber = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    PaddingLength = table.Column<int>(type: "int", nullable: false, defaultValue: 6),
                    IsAuto = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsManualAllowed = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeSequences_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CostCenters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    IsTransactional = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCenters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostCenters_CostCenters_ParentId",
                        column: x => x.ParentId,
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryHeaders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    FiscalYearId = table.Column<int>(type: "int", nullable: false),
                    EntryNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceDocumentType = table.Column<int>(type: "int", nullable: false),
                    SourceDocumentId = table.Column<int>(type: "int", nullable: true),
                    TotalDebitLocal = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    TotalCreditLocal = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryHeaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntryHeaders_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntryHeaders_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AccountGroupId = table.Column<int>(type: "int", nullable: false),
                    IsReconciliationAccount = table.Column<bool>(type: "bit", nullable: false),
                    IsTransactional = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyId = table.Column<int>(type: "int", nullable: true),
                    AccountUsage = table.Column<int>(type: "int", nullable: false),
                    RequiresCostCenter = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "AccountPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CanView = table.Column<bool>(type: "bit", nullable: false),
                    CanPost = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountPermissions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JournalEntryHeaderId = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    SubLedgerType = table.Column<int>(type: "int", nullable: false),
                    SubLedgerId = table.Column<int>(type: "int", nullable: true),
                    CurrencyId = table.Column<int>(type: "int", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false, defaultValue: 1.000000m),
                    DebitForeign = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    CreditForeign = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    DebitLocal = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    CreditLocal = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_JournalEntryHeaders_JournalEntryHeaderId",
                        column: x => x.JournalEntryHeaderId,
                        principalTable: "JournalEntryHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_FinancialAccountId",
                table: "Vendors",
                column: "FinancialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorCategories_BranchId_Code",
                table: "VendorCategories",
                columns: new[] { "BranchId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_FinancialAccountId",
                table: "Customers",
                column: "FinancialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCategories_BranchId_Code",
                table: "CustomerCategories",
                columns: new[] { "BranchId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_BranchCode",
                table: "Branches",
                column: "BranchCode",
                unique: true);

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
                name: "IX_AccountPermissions_Account_User",
                table: "AccountPermissions",
                columns: new[] { "AccountId", "UserId" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_CodeSequences_BranchId",
                table: "CodeSequences",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeSequences_EntityName_BranchId",
                table: "CodeSequences",
                columns: new[] { "EntityName", "BranchId" },
                unique: true,
                filter: "[BranchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_Code",
                table: "CostCenters",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_ParentId",
                table: "CostCenters",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryHeaders_Branch_FY_Date",
                table: "JournalEntryHeaders",
                columns: new[] { "BranchId", "FiscalYearId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryHeaders_Branch_Number",
                table: "JournalEntryHeaders",
                columns: new[] { "BranchId", "EntryNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryHeaders_FiscalYearId",
                table: "JournalEntryHeaders",
                column: "FiscalYearId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryHeaders_SourceDoc",
                table: "JournalEntryHeaders",
                columns: new[] { "SourceDocumentType", "SourceDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_Account_SubLedger",
                table: "JournalEntryLines",
                columns: new[] { "AccountId", "SubLedgerType", "SubLedgerId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_CostCenterId",
                table: "JournalEntryLines",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_CurrencyId",
                table: "JournalEntryLines",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_Header_LineNumber",
                table: "JournalEntryLines",
                columns: new[] { "JournalEntryHeaderId", "LineNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerCategories_Branches_BranchId",
                table: "CustomerCategories",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Accounts_FinancialAccountId",
                table: "Customers",
                column: "FinancialAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorCategories_Branches_BranchId",
                table: "VendorCategories",
                column: "BranchId",
                principalTable: "Branches",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerCategories_Branches_BranchId",
                table: "CustomerCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Accounts_FinancialAccountId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorCategories_Branches_BranchId",
                table: "VendorCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendors_Accounts_FinancialAccountId",
                table: "Vendors");

            migrationBuilder.DropTable(
                name: "AccountPermissions");

            migrationBuilder.DropTable(
                name: "CodeSequences");

            migrationBuilder.DropTable(
                name: "JournalEntryLines");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "CostCenters");

            migrationBuilder.DropTable(
                name: "JournalEntryHeaders");

            migrationBuilder.DropTable(
                name: "AccountGroups");

            migrationBuilder.DropIndex(
                name: "IX_Vendors_FinancialAccountId",
                table: "Vendors");

            migrationBuilder.DropIndex(
                name: "IX_VendorCategories_BranchId_Code",
                table: "VendorCategories");

            migrationBuilder.DropIndex(
                name: "IX_Customers_FinancialAccountId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_CustomerCategories_BranchId_Code",
                table: "CustomerCategories");

            migrationBuilder.DropIndex(
                name: "IX_Branches_BranchCode",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FinancialAccountId",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "Mobile2",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "VendorCategories");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "VendorCategories");

            migrationBuilder.DropColumn(
                name: "FinancialAccountId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Mobile2",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "CustomerCategories");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "CustomerCategories");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "Branches");

            migrationBuilder.CreateIndex(
                name: "IX_VendorCategories_Code",
                table: "VendorCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCategories_Code",
                table: "CustomerCategories",
                column: "Code",
                unique: true);
        }
    }
}
