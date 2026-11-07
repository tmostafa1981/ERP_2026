using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_UserPaymentMethodPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BankId",
                table: "VoucherLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "VoucherLines",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChequeDate",
                table: "VoucherLines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChequeNumber",
                table: "VoucherLines",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ChequeStatus",
                table: "VoucherLines",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "DrawerName",
                table: "VoucherLines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "VoucherLines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "PaymentMethod",
                table: "VoucherLines",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "Banks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SwiftCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Banks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPaymentMethodPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    VoucherType = table.Column<byte>(type: "tinyint", nullable: true),
                    PaymentMethod = table.Column<byte>(type: "tinyint", nullable: false),
                    CanView = table.Column<bool>(type: "bit", nullable: false),
                    CanCreate = table.Column<bool>(type: "bit", nullable: false),
                    CanEdit = table.Column<bool>(type: "bit", nullable: false),
                    CanDelete = table.Column<bool>(type: "bit", nullable: false),
                    CanPost = table.Column<bool>(type: "bit", nullable: false),
                    CanUnpost = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPaymentMethodPermissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_BankId",
                table: "VoucherLines",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_ChequeNumber_BankId",
                table: "VoucherLines",
                columns: new[] { "ChequeNumber", "BankId" });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_PaymentMethod_Status_DueDate",
                table: "VoucherLines",
                columns: new[] { "PaymentMethod", "ChequeStatus", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Banks_Code",
                table: "Banks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Banks_IsActive",
                table: "Banks",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_UserPaymentMethodPermissions_User_Type_Method",
                table: "UserPaymentMethodPermissions",
                columns: new[] { "UserId", "VoucherType", "PaymentMethod" },
                unique: true,
                filter: "[VoucherType] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_VoucherLines_Banks_BankId",
                table: "VoucherLines",
                column: "BankId",
                principalTable: "Banks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VoucherLines_Banks_BankId",
                table: "VoucherLines");

            migrationBuilder.DropTable(
                name: "Banks");

            migrationBuilder.DropTable(
                name: "UserPaymentMethodPermissions");

            migrationBuilder.DropIndex(
                name: "IX_VoucherLines_BankId",
                table: "VoucherLines");

            migrationBuilder.DropIndex(
                name: "IX_VoucherLines_ChequeNumber_BankId",
                table: "VoucherLines");

            migrationBuilder.DropIndex(
                name: "IX_VoucherLines_PaymentMethod_Status_DueDate",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "BankId",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "ChequeDate",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "ChequeNumber",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "ChequeStatus",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "DrawerName",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "VoucherLines");
        }
    }
}
