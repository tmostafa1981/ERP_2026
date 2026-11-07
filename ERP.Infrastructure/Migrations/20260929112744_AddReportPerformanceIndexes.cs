using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReportPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_Account_Currency",
                table: "JournalEntryLines",
                columns: new[] { "AccountId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_SubLedger_Currency",
                table: "JournalEntryLines",
                columns: new[] { "SubLedgerType", "SubLedgerId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryHeaders_Branch_Status_Date",
                table: "JournalEntryHeaders",
                columns: new[] { "BranchId", "Status", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryHeaders_Status_Date",
                table: "JournalEntryHeaders",
                columns: new[] { "Status", "EntryDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_Account_Currency",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_SubLedger_Currency",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryHeaders_Branch_Status_Date",
                table: "JournalEntryHeaders");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryHeaders_Status_Date",
                table: "JournalEntryHeaders");
        }
    }
}
