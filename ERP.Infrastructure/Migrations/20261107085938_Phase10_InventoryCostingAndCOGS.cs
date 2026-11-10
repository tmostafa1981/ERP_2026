using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase10_InventoryCostingAndCOGS : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultCogsAccountId",
                table: "TransactionTypes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultInventoryAccountId",
                table: "TransactionTypes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "ItemSerialNumbers",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCost",
                table: "DocumentLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "DocumentLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CostAdjustmentRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    SourceDocumentHeaderId = table.Column<int>(type: "int", nullable: false),
                    AffectedDocumentHeaderId = table.Column<int>(type: "int", nullable: true),
                    AffectedDocumentLineId = table.Column<int>(type: "int", nullable: true),
                    AffectedMovementType = table.Column<int>(type: "int", nullable: false),
                    MovementDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OriginalUnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RecalculatedUnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OriginalTotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RecalculatedTotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostDifference = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AdjustmentJournalEntryId = table.Column<int>(type: "int", nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsClosedPeriod = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostAdjustmentRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostAdjustmentRecords_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CostAdjustmentRecords_DocumentHeaders_AffectedDocumentHeaderId",
                        column: x => x.AffectedDocumentHeaderId,
                        principalTable: "DocumentHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CostAdjustmentRecords_DocumentHeaders_SourceDocumentHeaderId",
                        column: x => x.SourceDocumentHeaderId,
                        principalTable: "DocumentHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CostAdjustmentRecords_DocumentLines_AffectedDocumentLineId",
                        column: x => x.AffectedDocumentLineId,
                        principalTable: "DocumentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CostAdjustmentRecords_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CostAdjustmentRecords_JournalEntryHeaders_AdjustmentJournalEntryId",
                        column: x => x.AdjustmentJournalEntryId,
                        principalTable: "JournalEntryHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CostAdjustmentRecords_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemCostHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    DocumentHeaderId = table.Column<int>(type: "int", nullable: false),
                    DocumentLineId = table.Column<int>(type: "int", nullable: true),
                    MovementType = table.Column<int>(type: "int", nullable: false),
                    MovementDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PreQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PreTotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PreAverageCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PostQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PostTotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PostAverageCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCostHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemCostHistories_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostHistories_DocumentHeaders_DocumentHeaderId",
                        column: x => x.DocumentHeaderId,
                        principalTable: "DocumentHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostHistories_DocumentLines_DocumentLineId",
                        column: x => x.DocumentLineId,
                        principalTable: "DocumentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostHistories_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostHistories_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemWarehouseCosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    CurrentQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MovingAverageCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LastUpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastDocumentId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemWarehouseCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemWarehouseCosts_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemWarehouseCosts_DocumentHeaders_LastDocumentId",
                        column: x => x.LastDocumentId,
                        principalTable: "DocumentHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemWarehouseCosts_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemWarehouseCosts_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionTypes_DefaultCogsAccountId",
                table: "TransactionTypes",
                column: "DefaultCogsAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionTypes_DefaultInventoryAccountId",
                table: "TransactionTypes",
                column: "DefaultInventoryAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CostAdjustmentRecords_AdjustmentJournalEntryId",
                table: "CostAdjustmentRecords",
                column: "AdjustmentJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CostAdjustmentRecords_AffectedDoc",
                table: "CostAdjustmentRecords",
                column: "AffectedDocumentHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CostAdjustmentRecords_AffectedDocumentLineId",
                table: "CostAdjustmentRecords",
                column: "AffectedDocumentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CostAdjustmentRecords_BatchNumber",
                table: "CostAdjustmentRecords",
                column: "BatchNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CostAdjustmentRecords_BranchId",
                table: "CostAdjustmentRecords",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CostAdjustmentRecords_Item_WH_Branch",
                table: "CostAdjustmentRecords",
                columns: new[] { "ItemId", "WarehouseId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CostAdjustmentRecords_SourceDoc",
                table: "CostAdjustmentRecords",
                column: "SourceDocumentHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CostAdjustmentRecords_WarehouseId",
                table: "CostAdjustmentRecords",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostHistories_BranchId",
                table: "ItemCostHistories",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostHistories_DocumentHeaderId",
                table: "ItemCostHistories",
                column: "DocumentHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostHistories_DocumentLineId",
                table: "ItemCostHistories",
                column: "DocumentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostHistories_Item_WH_Branch_Date",
                table: "ItemCostHistories",
                columns: new[] { "ItemId", "WarehouseId", "BranchId", "MovementDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostHistories_WarehouseId",
                table: "ItemCostHistories",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemWarehouseCosts_BranchId",
                table: "ItemWarehouseCosts",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemWarehouseCosts_Item_WH_Branch",
                table: "ItemWarehouseCosts",
                columns: new[] { "ItemId", "WarehouseId", "BranchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemWarehouseCosts_LastDocumentId",
                table: "ItemWarehouseCosts",
                column: "LastDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemWarehouseCosts_WarehouseId",
                table: "ItemWarehouseCosts",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_TransactionTypes_GLAccounts_DefaultCogsAccountId",
                table: "TransactionTypes",
                column: "DefaultCogsAccountId",
                principalTable: "GLAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TransactionTypes_GLAccounts_DefaultInventoryAccountId",
                table: "TransactionTypes",
                column: "DefaultInventoryAccountId",
                principalTable: "GLAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransactionTypes_GLAccounts_DefaultCogsAccountId",
                table: "TransactionTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_TransactionTypes_GLAccounts_DefaultInventoryAccountId",
                table: "TransactionTypes");

            migrationBuilder.DropTable(
                name: "CostAdjustmentRecords");

            migrationBuilder.DropTable(
                name: "ItemCostHistories");

            migrationBuilder.DropTable(
                name: "ItemWarehouseCosts");

            migrationBuilder.DropIndex(
                name: "IX_TransactionTypes_DefaultCogsAccountId",
                table: "TransactionTypes");

            migrationBuilder.DropIndex(
                name: "IX_TransactionTypes_DefaultInventoryAccountId",
                table: "TransactionTypes");

            migrationBuilder.DropColumn(
                name: "DefaultCogsAccountId",
                table: "TransactionTypes");

            migrationBuilder.DropColumn(
                name: "DefaultInventoryAccountId",
                table: "TransactionTypes");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "ItemSerialNumbers");

            migrationBuilder.DropColumn(
                name: "TotalCost",
                table: "DocumentLines");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "DocumentLines");
        }
    }
}
