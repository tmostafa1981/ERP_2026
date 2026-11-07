using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchScopeToGLAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "GLAccounts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GLAccounts_BranchId",
                table: "GLAccounts",
                column: "BranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_GLAccounts_Branches_BranchId",
                table: "GLAccounts",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GLAccounts_Branches_BranchId",
                table: "GLAccounts");

            migrationBuilder.DropIndex(
                name: "IX_GLAccounts_BranchId",
                table: "GLAccounts");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "GLAccounts");
        }
    }
}
