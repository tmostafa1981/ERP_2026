using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase9_FixedAssetsAndDepreciation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DefaultUsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    DefaultDepreciationMethod = table.Column<int>(type: "int", nullable: false),
                    DefaultResidualValueRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AssetCostAccountId = table.Column<int>(type: "int", nullable: false),
                    AccumulatedDepreciationAccountId = table.Column<int>(type: "int", nullable: false),
                    DepreciationExpenseAccountId = table.Column<int>(type: "int", nullable: false),
                    DisposalGainAccountId = table.Column<int>(type: "int", nullable: false),
                    DisposalLossAccountId = table.Column<int>(type: "int", nullable: false),
                    CipAccountId = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetCategories_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetCategories_GLAccounts_AccumulatedDepreciationAccountId",
                        column: x => x.AccumulatedDepreciationAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetCategories_GLAccounts_AssetCostAccountId",
                        column: x => x.AssetCostAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetCategories_GLAccounts_CipAccountId",
                        column: x => x.CipAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetCategories_GLAccounts_DepreciationExpenseAccountId",
                        column: x => x.DepreciationExpenseAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetCategories_GLAccounts_DisposalGainAccountId",
                        column: x => x.DisposalGainAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetCategories_GLAccounts_DisposalLossAccountId",
                        column: x => x.DisposalLossAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetDepreciationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    RunNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RunDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    FiscalYearId = table.Column<int>(type: "int", nullable: false),
                    FiscalPeriodId = table.Column<int>(type: "int", nullable: false),
                    AssetCategoryId = table.Column<int>(type: "int", nullable: true),
                    TotalDepreciationAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AssetsCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    JournalEntryId = table.Column<int>(type: "int", nullable: true),
                    ReversalJournalEntryId = table.Column<int>(type: "int", nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetDepreciationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationRuns_AssetCategories_AssetCategoryId",
                        column: x => x.AssetCategoryId,
                        principalTable: "AssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationRuns_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationRuns_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationRuns_FiscalPeriods_FiscalPeriodId",
                        column: x => x.FiscalPeriodId,
                        principalTable: "FiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationRuns_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationRuns_JournalEntryHeaders_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntryHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationRuns_JournalEntryHeaders_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntryHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FixedAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AssetCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AssetCategoryId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Barcode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    Department = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    CustodianEmployeeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PurchaseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlacedInServiceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VendorId = table.Column<int>(type: "int", nullable: true),
                    PurchaseDocumentHeaderId = table.Column<int>(type: "int", nullable: true),
                    PurchaseInvoiceReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CurrencyId = table.Column<int>(type: "int", nullable: false),
                    PurchasePriceForeign = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    InitialCostLocal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AdditionalExpensesLocal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrentCostLocal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ResidualValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    DepreciationStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DepreciationMethod = table.Column<int>(type: "int", nullable: false),
                    DepreciationRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalExpectedUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalProducedUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FirstMonthPolicy = table.Column<int>(type: "int", nullable: false),
                    AssetCostAccountId = table.Column<int>(type: "int", nullable: true),
                    AccumulatedDepreciationAccountId = table.Column<int>(type: "int", nullable: true),
                    DepreciationExpenseAccountId = table.Column<int>(type: "int", nullable: true),
                    DisposalGainAccountId = table.Column<int>(type: "int", nullable: true),
                    DisposalLossAccountId = table.Column<int>(type: "int", nullable: true),
                    IsOpeningAsset = table.Column<bool>(type: "bit", nullable: false),
                    OpeningCarryingValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OpeningAccumulatedDepreciation = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrentAccumulatedDepreciation = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrentBookValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LastDepreciationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsFullyDepreciated = table.Column<bool>(type: "bit", nullable: false),
                    OpeningJournalEntryId = table.Column<int>(type: "int", nullable: true),
                    DisposalDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisposalProceeds = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    DisposalExpenses = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    DisposalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DisposalJournalEntryId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixedAssets_AssetCategories_AssetCategoryId",
                        column: x => x.AssetCategoryId,
                        principalTable: "AssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_DocumentHeaders_PurchaseDocumentHeaderId",
                        column: x => x.PurchaseDocumentHeaderId,
                        principalTable: "DocumentHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_GLAccounts_AccumulatedDepreciationAccountId",
                        column: x => x.AccumulatedDepreciationAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_GLAccounts_AssetCostAccountId",
                        column: x => x.AssetCostAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_GLAccounts_DepreciationExpenseAccountId",
                        column: x => x.DepreciationExpenseAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_GLAccounts_DisposalGainAccountId",
                        column: x => x.DisposalGainAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_GLAccounts_DisposalLossAccountId",
                        column: x => x.DisposalLossAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_JournalEntryHeaders_DisposalJournalEntryId",
                        column: x => x.DisposalJournalEntryId,
                        principalTable: "JournalEntryHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetDepreciationHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetDepreciationRunId = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    DepreciationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FiscalYearId = table.Column<int>(type: "int", nullable: false),
                    FiscalPeriodId = table.Column<int>(type: "int", nullable: false),
                    AssetCostAtRun = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PriorAccumulatedDepreciation = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DepreciationAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PostAccumulatedDepreciation = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BookValueBefore = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BookValueAfter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    ExpenseAccountId = table.Column<int>(type: "int", nullable: false),
                    AccumulatedAccountId = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetDepreciationHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationHistories_AssetDepreciationRuns_AssetDepreciationRunId",
                        column: x => x.AssetDepreciationRunId,
                        principalTable: "AssetDepreciationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationHistories_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationHistories_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationHistories_FixedAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "FixedAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationHistories_GLAccounts_AccumulatedAccountId",
                        column: x => x.AccumulatedAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationHistories_GLAccounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalTable: "GLAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    TransactionType = table.Column<int>(type: "int", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromBranchId = table.Column<int>(type: "int", nullable: true),
                    ToBranchId = table.Column<int>(type: "int", nullable: true),
                    FromWarehouseId = table.Column<int>(type: "int", nullable: true),
                    ToWarehouseId = table.Column<int>(type: "int", nullable: true),
                    FromDepartment = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ToDepartment = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    FromCustodian = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ToCustodian = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    FromCostCenterId = table.Column<int>(type: "int", nullable: true),
                    ToCostCenterId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsCapitalized = table.Column<bool>(type: "bit", nullable: false),
                    ExpenseAccountId = table.Column<int>(type: "int", nullable: true),
                    JournalEntryId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetTransactions_Branches_FromBranchId",
                        column: x => x.FromBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetTransactions_Branches_ToBranchId",
                        column: x => x.ToBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetTransactions_CostCenters_FromCostCenterId",
                        column: x => x.FromCostCenterId,
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetTransactions_CostCenters_ToCostCenterId",
                        column: x => x.ToCostCenterId,
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetTransactions_FixedAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "FixedAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetTransactions_JournalEntryHeaders_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntryHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetTransactions_Warehouses_FromWarehouseId",
                        column: x => x.FromWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetTransactions_Warehouses_ToWarehouseId",
                        column: x => x.ToWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_AccumulatedDepreciationAccountId",
                table: "AssetCategories",
                column: "AccumulatedDepreciationAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_AssetCostAccountId",
                table: "AssetCategories",
                column: "AssetCostAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_BranchId",
                table: "AssetCategories",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_CipAccountId",
                table: "AssetCategories",
                column: "CipAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_Code",
                table: "AssetCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_DepreciationExpenseAccountId",
                table: "AssetCategories",
                column: "DepreciationExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_DisposalGainAccountId",
                table: "AssetCategories",
                column: "DisposalGainAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_DisposalLossAccountId",
                table: "AssetCategories",
                column: "DisposalLossAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationHistories_AccumulatedAccountId",
                table: "AssetDepreciationHistories",
                column: "AccumulatedAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationHistories_AssetDepreciationRunId_AssetId_DepreciationDate",
                table: "AssetDepreciationHistories",
                columns: new[] { "AssetDepreciationRunId", "AssetId", "DepreciationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationHistories_AssetId_FiscalPeriodId",
                table: "AssetDepreciationHistories",
                columns: new[] { "AssetId", "FiscalPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationHistories_BranchId",
                table: "AssetDepreciationHistories",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationHistories_CostCenterId",
                table: "AssetDepreciationHistories",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationHistories_ExpenseAccountId",
                table: "AssetDepreciationHistories",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationRuns_AssetCategoryId",
                table: "AssetDepreciationRuns",
                column: "AssetCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationRuns_BranchId",
                table: "AssetDepreciationRuns",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationRuns_CompanyId_BranchId_RunDate_FiscalPeriodId",
                table: "AssetDepreciationRuns",
                columns: new[] { "CompanyId", "BranchId", "RunDate", "FiscalPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationRuns_FiscalPeriodId",
                table: "AssetDepreciationRuns",
                column: "FiscalPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationRuns_FiscalYearId",
                table: "AssetDepreciationRuns",
                column: "FiscalYearId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationRuns_JournalEntryId",
                table: "AssetDepreciationRuns",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationRuns_ReversalJournalEntryId",
                table: "AssetDepreciationRuns",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationRuns_RunNumber",
                table: "AssetDepreciationRuns",
                column: "RunNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransactions_AssetId_TransactionDate_TransactionType",
                table: "AssetTransactions",
                columns: new[] { "AssetId", "TransactionDate", "TransactionType" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransactions_FromBranchId",
                table: "AssetTransactions",
                column: "FromBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransactions_FromCostCenterId",
                table: "AssetTransactions",
                column: "FromCostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransactions_FromWarehouseId",
                table: "AssetTransactions",
                column: "FromWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransactions_JournalEntryId",
                table: "AssetTransactions",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransactions_ToBranchId",
                table: "AssetTransactions",
                column: "ToBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransactions_ToCostCenterId",
                table: "AssetTransactions",
                column: "ToCostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransactions_ToWarehouseId",
                table: "AssetTransactions",
                column: "ToWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_AccumulatedDepreciationAccountId",
                table: "FixedAssets",
                column: "AccumulatedDepreciationAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_AssetCategoryId",
                table: "FixedAssets",
                column: "AssetCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_AssetCode",
                table: "FixedAssets",
                column: "AssetCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_AssetCostAccountId",
                table: "FixedAssets",
                column: "AssetCostAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_Barcode",
                table: "FixedAssets",
                column: "Barcode");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_BranchId_AssetCategoryId_Status",
                table: "FixedAssets",
                columns: new[] { "BranchId", "AssetCategoryId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_CostCenterId",
                table: "FixedAssets",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_CurrencyId",
                table: "FixedAssets",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_DepreciationExpenseAccountId",
                table: "FixedAssets",
                column: "DepreciationExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_DisposalGainAccountId",
                table: "FixedAssets",
                column: "DisposalGainAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_DisposalJournalEntryId",
                table: "FixedAssets",
                column: "DisposalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_DisposalLossAccountId",
                table: "FixedAssets",
                column: "DisposalLossAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_PurchaseDocumentHeaderId",
                table: "FixedAssets",
                column: "PurchaseDocumentHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_SerialNumber",
                table: "FixedAssets",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_VendorId",
                table: "FixedAssets",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_WarehouseId",
                table: "FixedAssets",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetDepreciationHistories");

            migrationBuilder.DropTable(
                name: "AssetTransactions");

            migrationBuilder.DropTable(
                name: "AssetDepreciationRuns");

            migrationBuilder.DropTable(
                name: "FixedAssets");

            migrationBuilder.DropTable(
                name: "AssetCategories");
        }
    }
}
