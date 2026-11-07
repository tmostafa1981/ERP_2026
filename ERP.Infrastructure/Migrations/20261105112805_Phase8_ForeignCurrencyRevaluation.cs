using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase8_ForeignCurrencyRevaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RevaluationHeaders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    RevaluationNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RevaluationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    FiscalYearId = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    CurrencyId = table.Column<int>(type: "int", nullable: true),
                    UnrealizedGainAccountId = table.Column<int>(type: "int", nullable: false),
                    UnrealizedLossAccountId = table.Column<int>(type: "int", nullable: false),
                    TotalGain = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalLoss = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NetDifference = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    JournalEntryId = table.Column<int>(type: "int", nullable: true),
                    ReversalJournalEntryId = table.Column<int>(type: "int", nullable: true),
                    ReversalDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevaluationHeaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RevaluationHeaders_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevaluationHeaders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevaluationHeaders_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevaluationHeaders_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevaluationHeaders_GLAccounts_UnrealizedGainAccountId",
                        column: x => x.UnrealizedGainAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevaluationHeaders_GLAccounts_UnrealizedLossAccountId",
                        column: x => x.UnrealizedLossAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevaluationHeaders_JournalEntryHeaders_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntryHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevaluationHeaders_JournalEntryHeaders_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntryHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RevaluationLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RevaluationHeaderId = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    VendorId = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    CurrencyId = table.Column<int>(type: "int", nullable: false),
                    ForeignBalance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CarryingLocalAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RevaluedLocalAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DifferenceAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DifferenceType = table.Column<int>(type: "int", nullable: false),
                    BalanceNature = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevaluationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RevaluationLines_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevaluationLines_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevaluationLines_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevaluationLines_GLAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevaluationLines_RevaluationHeaders_RevaluationHeaderId",
                        column: x => x.RevaluationHeaderId,
                        principalTable: "RevaluationHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RevaluationLines_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationHeaders_BranchId",
                table: "RevaluationHeaders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationHeaders_Company_Branch_Date",
                table: "RevaluationHeaders",
                columns: new[] { "CompanyId", "BranchId", "RevaluationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationHeaders_CurrencyId",
                table: "RevaluationHeaders",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationHeaders_FiscalYearId",
                table: "RevaluationHeaders",
                column: "FiscalYearId");

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationHeaders_JournalEntryId",
                table: "RevaluationHeaders",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationHeaders_Number",
                table: "RevaluationHeaders",
                column: "RevaluationNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationHeaders_ReversalJournalEntryId",
                table: "RevaluationHeaders",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationHeaders_Status_Date",
                table: "RevaluationHeaders",
                columns: new[] { "Status", "RevaluationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationHeaders_UnrealizedGainAccountId",
                table: "RevaluationHeaders",
                column: "UnrealizedGainAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationHeaders_UnrealizedLossAccountId",
                table: "RevaluationHeaders",
                column: "UnrealizedLossAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationLines_Account_Currency",
                table: "RevaluationLines",
                columns: new[] { "AccountId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationLines_Branch",
                table: "RevaluationLines",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationLines_CurrencyId",
                table: "RevaluationLines",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationLines_CustomerId",
                table: "RevaluationLines",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationLines_Header_LineNumber",
                table: "RevaluationLines",
                columns: new[] { "RevaluationHeaderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationLines_Source",
                table: "RevaluationLines",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationLines_VendorId",
                table: "RevaluationLines",
                column: "VendorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RevaluationLines");

            migrationBuilder.DropTable(
                name: "RevaluationHeaders");
        }
    }
}
