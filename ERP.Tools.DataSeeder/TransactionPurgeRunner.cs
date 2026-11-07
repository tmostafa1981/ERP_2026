using System;
using System.Data;
using System.Threading.Tasks;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Tools.DataSeeder
{
    public static class TransactionPurgeRunner
    {
        public static async Task<bool> RunPurgeAsync(ErpDbContext context)
        {
            Console.WriteLine("=================================================================");
            Console.WriteLine("        PURGE ALL TRANSACTIONS (مسح كل الحركات للاختبار)        ");
            Console.WriteLine("=================================================================");
            Console.WriteLine($"Database Target: {context.Database.GetDbConnection().Database}");
            Console.WriteLine($"Start Time     : {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            string purgeSql = @"
BEGIN TRY
    -- 1. Unlink Journal Entries from DocumentHeaders and VoucherHeaders
    UPDATE [DocumentHeaders] SET [JournalEntryHeaderId] = NULL WHERE [JournalEntryHeaderId] IS NOT NULL;
    UPDATE [VoucherHeaders]  SET [JournalEntryHeaderId] = NULL WHERE [JournalEntryHeaderId] IS NOT NULL;

    -- 2. Clear Document Lines and Headers
    DELETE FROM [DocumentAdjustmentLines];
    DELETE FROM [DocumentLineSerials];
    DELETE FROM [DocumentLineLinks];
    DELETE FROM [DocumentConversionLinks];
    DELETE FROM [DocumentLines];
    DELETE FROM [DocumentHeaders];

    -- 3. Clear Serial Numbers and Movements
    DELETE FROM [ItemSerialMovements];
    DELETE FROM [ItemSerialNumbers];

    -- 4. Clear Cash & Bank Vouchers
    DELETE FROM [VoucherLines];
    DELETE FROM [VoucherHeaders];

    -- 5. Clear Journal Entries
    DELETE FROM [JournalEntryLines];
    DELETE FROM [JournalEntryHeaders];

    -- 6. Clear Audit Logs
    DELETE FROM [AuditLogs];

    -- 7. Reset Identity Seeds to 0 (next inserted row gets 1)
    DBCC CHECKIDENT ('DocumentAdjustmentLines', RESEED, 0);
    DBCC CHECKIDENT ('DocumentLineSerials',     RESEED, 0);
    DBCC CHECKIDENT ('DocumentLineLinks',       RESEED, 0);
    DBCC CHECKIDENT ('DocumentConversionLinks', RESEED, 0);
    DBCC CHECKIDENT ('DocumentLines',           RESEED, 0);
    DBCC CHECKIDENT ('DocumentHeaders',         RESEED, 0);
    DBCC CHECKIDENT ('ItemSerialMovements',     RESEED, 0);
    DBCC CHECKIDENT ('ItemSerialNumbers',       RESEED, 0);
    DBCC CHECKIDENT ('VoucherLines',            RESEED, 0);
    DBCC CHECKIDENT ('VoucherHeaders',          RESEED, 0);
    DBCC CHECKIDENT ('JournalEntryLines',       RESEED, 0);
    DBCC CHECKIDENT ('JournalEntryHeaders',     RESEED, 0);
    DBCC CHECKIDENT ('AuditLogs',               RESEED, 0);

END TRY
BEGIN CATCH
    THROW;
END CATCH;
";

            try
            {
                using var connection = context.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                using var transaction = await context.Database.BeginTransactionAsync();

                Console.WriteLine("[1/3] Executing transaction wipe & identity reseed script...");
                await context.Database.ExecuteSqlRawAsync(purgeSql);
                await transaction.CommitAsync();
                Console.WriteLine("      -> All transactional tables cleared and reseeded successfully.");

                Console.WriteLine("\n[2/3] Verification: Checking Transactional Tables (Must be 0)...");
                var tables = new[]
                {
                    "DocumentHeaders", "DocumentLines", "DocumentLineSerials",
                    "DocumentAdjustmentLines", "DocumentConversionLinks", "DocumentLineLinks",
                    "VoucherHeaders", "VoucherLines", "JournalEntryHeaders",
                    "JournalEntryLines", "ItemSerialNumbers", "ItemSerialMovements", "AuditLogs"
                };

                bool allZero = true;
                foreach (var table in tables)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = $"SELECT COUNT(*) FROM [{table}]";
                    var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                    Console.WriteLine($"      -> {table,-28} : {count} rows");
                    if (count > 0) allZero = false;
                }

                Console.WriteLine("\n[3/3] Master Data Preservation Verification...");
                var masterTables = new[]
                {
                    "GLAccounts", "Companies", "Branches", "FiscalYears", "FiscalPeriods",
                    "Customers", "Vendors", "Items", "Warehouses", "Banks", "Currencies", "AspNetUsers"
                };

                foreach (var masterTable in masterTables)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = $"SELECT COUNT(*) FROM [{masterTable}]";
                    var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                    Console.WriteLine($"      -> {masterTable,-28} : {count,5} records preserved");
                }

                if (allZero)
                {
                    Console.WriteLine("\n=================================================================");
                    Console.WriteLine(" SUCCESS: ALL TRANSACTIONS CLEARED AND RESET TO 1 SUCCESSFULLY!  ");
                    Console.WriteLine("=================================================================");
                    return true;
                }
                else
                {
                    Console.WriteLine("\n[WARNING] Some tables still have remaining rows.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERROR] Failed to purge transactions: {ex.Message}");
                Console.ResetColor();
                return false;
            }
        }
    }
}
