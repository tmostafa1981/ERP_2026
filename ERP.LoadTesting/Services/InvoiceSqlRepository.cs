using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using ERP.LoadTesting.Models;

namespace ERP.LoadTesting.Services
{
    public class InvoiceSqlRepository
    {
        private readonly string _connectionString;

        public InvoiceSqlRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<(bool IsSuccess, int StatusCode, string? DocumentNumber, string? ErrorMessage)> ExecuteCreateInvoiceSpAsync(InvoiceHeaderPayload payload)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new SqlCommand("dbo.sp_CreateInvoiceWithItems", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                // Header Parameters
                command.Parameters.Add(new SqlParameter("@BranchId", SqlDbType.Int) { Value = payload.BranchId });
                command.Parameters.Add(new SqlParameter("@FiscalYearId", SqlDbType.Int) { Value = payload.FiscalYearId });
                command.Parameters.Add(new SqlParameter("@TransactionTypeId", SqlDbType.Int) { Value = payload.TransactionTypeId });
                command.Parameters.Add(new SqlParameter("@DocumentDate", SqlDbType.DateTime2) { Value = payload.DocumentDate });
                command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = payload.CustomerId });
                command.Parameters.Add(new SqlParameter("@WarehouseId", SqlDbType.Int) { Value = payload.WarehouseId });
                command.Parameters.Add(new SqlParameter("@CurrencyId", SqlDbType.Int) { Value = payload.CurrencyId });
                command.Parameters.Add(new SqlParameter("@ExchangeRate", SqlDbType.Decimal) { Precision = 18, Scale = 6, Value = payload.ExchangeRate });
                command.Parameters.Add(new SqlParameter("@PaymentMethod", SqlDbType.Int) { Value = payload.PaymentMethod });
                command.Parameters.Add(new SqlParameter("@TotalAmountBeforeTax", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = payload.TotalAmountBeforeTax });
                command.Parameters.Add(new SqlParameter("@DiscountAmount", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = payload.DiscountAmount });
                command.Parameters.Add(new SqlParameter("@TaxAmount", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = payload.TaxAmount });
                command.Parameters.Add(new SqlParameter("@TotalAmountNet", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = payload.TotalAmountNet });
                command.Parameters.Add(new SqlParameter("@Notes", SqlDbType.NVarChar, 500) { Value = (object?)payload.Notes ?? DBNull.Value });
                command.Parameters.Add(new SqlParameter("@CreatedByUserId", SqlDbType.NVarChar, 100) { Value = payload.CreatedByUserId });

                // Output Parameter for Generated Document Number
                var outDocNum = new SqlParameter("@GeneratedDocNumber", SqlDbType.NVarChar, 50)
                {
                    Direction = ParameterDirection.Output
                };
                command.Parameters.Add(outDocNum);

                // Table-Valued Parameter (TVP) for Lines
                DataTable linesTable = BuildLinesDataTable(payload);
                var linesParam = command.Parameters.AddWithValue("@Lines", linesTable);
                linesParam.SqlDbType = SqlDbType.Structured;
                linesParam.TypeName = "dbo.InvoiceLineTableType";

                // Execute Stored Procedure
                await command.ExecuteNonQueryAsync();

                string generatedCode = outDocNum.Value?.ToString() ?? "OK";
                return (true, 200, generatedCode, null);
            }
            catch (SqlException ex)
            {
                // Classify SQL Server Concurrency Errors
                string errorType = ex.Number switch
                {
                    1205 => "[DEADLOCK VICTIM (Error 1205)]",
                    1222 => "[LOCK REQUEST TIMEOUT (Error 1222)]",
                    2627 => "[PRIMARY KEY / UNIQUE VIOLATION (Error 2627)]",
                    2601 => "[DUPLICATE KEY SEQUENCE VIOLATION (Error 2601)]",
                    50000 => $"[BUSINESS / STOCK RULE ERROR: {ex.Message}]",
                    _ => $"[SQL ERROR {ex.Number}: {ex.Message}]"
                };

                return (false, ex.Number, null, $"{errorType} {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, 500, null, $"[GENERAL EXCEPTION: {ex.Message}]");
            }
        }

        private static DataTable BuildLinesDataTable(InvoiceHeaderPayload payload)
        {
            var dt = new DataTable();
            dt.Columns.Add("LineNumber", typeof(int));
            dt.Columns.Add("ItemId", typeof(int));
            dt.Columns.Add("UnitId", typeof(int));
            dt.Columns.Add("WarehouseId", typeof(int));
            dt.Columns.Add("Quantity", typeof(decimal));
            dt.Columns.Add("UnitPrice", typeof(decimal));
            dt.Columns.Add("DiscountAmount", typeof(decimal));
            dt.Columns.Add("TaxRate", typeof(decimal));
            dt.Columns.Add("TaxAmount", typeof(decimal));
            dt.Columns.Add("LineTotal", typeof(decimal));

            foreach (var line in payload.Lines)
            {
                dt.Rows.Add(
                    line.LineNumber,
                    line.ItemId,
                    line.UnitId,
                    line.WarehouseId,
                    line.Quantity,
                    line.UnitPrice,
                    line.DiscountAmount,
                    line.TaxRate,
                    line.TaxAmount,
                    line.LineTotal
                );
            }

            return dt;
        }
    }
}
