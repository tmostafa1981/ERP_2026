using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_CashBankVoucherModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VoucherHeaders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    FiscalYearId = table.Column<int>(type: "int", nullable: false),
                    VoucherType = table.Column<byte>(type: "tinyint", nullable: false),
                    VoucherNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    VoucherDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CashBankAccountId = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<byte>(type: "tinyint", nullable: false),
                    ChequeNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ChequeDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChequeDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BankName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CurrencyId = table.Column<int>(type: "int", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false, defaultValue: 1.000000m),
                    TotalAmountForeign = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    TotalAmountLocal = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    JournalEntryHeaderId = table.Column<int>(type: "int", nullable: true),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherHeaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherHeaders_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherHeaders_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherHeaders_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherHeaders_GLAccounts_CashBankAccountId",
                        column: x => x.CashBankAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherHeaders_JournalEntryHeaders_JournalEntryHeaderId",
                        column: x => x.JournalEntryHeaderId,
                        principalTable: "JournalEntryHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "VoucherLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VoucherHeaderId = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    LineType = table.Column<byte>(type: "tinyint", nullable: false),
                    SubLedgerId = table.Column<int>(type: "int", nullable: true),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    AmountForeign = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    AmountLocal = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherLines_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherLines_GLAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherLines_VoucherHeaders_VoucherHeaderId",
                        column: x => x.VoucherHeaderId,
                        principalTable: "VoucherHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherHeaders_Branch_Fiscal_Type_Num",
                table: "VoucherHeaders",
                columns: new[] { "BranchId", "FiscalYearId", "VoucherType", "VoucherNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherHeaders_CashBankAccountId",
                table: "VoucherHeaders",
                column: "CashBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherHeaders_CurrencyId",
                table: "VoucherHeaders",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherHeaders_FiscalYearId",
                table: "VoucherHeaders",
                column: "FiscalYearId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherHeaders_JournalEntryHeaderId",
                table: "VoucherHeaders",
                column: "JournalEntryHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherHeaders_Status",
                table: "VoucherHeaders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherHeaders_VoucherDate",
                table: "VoucherHeaders",
                column: "VoucherDate");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_AccountId",
                table: "VoucherLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_CostCenterId",
                table: "VoucherLines",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_Header_LineNumber",
                table: "VoucherLines",
                columns: new[] { "VoucherHeaderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_LineType_SubLedgerId",
                table: "VoucherLines",
                columns: new[] { "LineType", "SubLedgerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoucherLines");

            migrationBuilder.DropTable(
                name: "VoucherHeaders");
        }
    }
}
