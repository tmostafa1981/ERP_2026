using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Domain.Entities;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Tools.DataSeeder
{
    public static class ChequeMigrationRunner
    {
        public static async Task ApplyMigrationAsync(ErpDbContext context)
        {
            Console.WriteLine("=================================================================");
            Console.WriteLine("       APPLYING CHEQUE DESIGN MODIFICATION DB SCHEMA & SEED      ");
            Console.WriteLine("=================================================================");

            // 1. Create Banks Table if not exists
            string sqlBanks = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Banks')
                BEGIN
                    CREATE TABLE [dbo].[Banks] (
                        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [Code] NVARCHAR(20) NOT NULL,
                        [NameAr] NVARCHAR(150) NOT NULL,
                        [NameEn] NVARCHAR(150) NOT NULL,
                        [SwiftCode] NVARCHAR(20) NULL,
                        [IsActive] BIT NOT NULL DEFAULT 1,
                        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        [UpdatedAt] DATETIME2 NULL
                    );
                    CREATE UNIQUE INDEX [IX_Banks_Code] ON [dbo].[Banks] ([Code]);
                    CREATE INDEX [IX_Banks_IsActive] ON [dbo].[Banks] ([IsActive]);
                    PRINT 'Created table Banks successfully.';
                END";

            await context.Database.ExecuteSqlRawAsync(sqlBanks);
            Console.WriteLine("   -> Table [Banks] verified.");

            // 2. Add columns to VoucherLines if not exist
            string sqlVoucherLines = @"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('VoucherLines') AND name = 'PaymentMethod')
                BEGIN
                    ALTER TABLE [dbo].[VoucherLines] ADD [PaymentMethod] TINYINT NOT NULL DEFAULT 1;
                    PRINT 'Added column PaymentMethod to VoucherLines.';
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('VoucherLines') AND name = 'ChequeNumber')
                BEGIN
                    ALTER TABLE [dbo].[VoucherLines] ADD [ChequeNumber] NVARCHAR(50) NULL;
                    PRINT 'Added column ChequeNumber to VoucherLines.';
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('VoucherLines') AND name = 'ChequeDate')
                BEGIN
                    ALTER TABLE [dbo].[VoucherLines] ADD [ChequeDate] DATETIME2 NULL;
                    PRINT 'Added column ChequeDate to VoucherLines.';
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('VoucherLines') AND name = 'DueDate')
                BEGIN
                    ALTER TABLE [dbo].[VoucherLines] ADD [DueDate] DATETIME2 NULL;
                    PRINT 'Added column DueDate to VoucherLines.';
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('VoucherLines') AND name = 'BankId')
                BEGIN
                    ALTER TABLE [dbo].[VoucherLines] ADD [BankId] INT NULL;
                    PRINT 'Added column BankId to VoucherLines.';
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('VoucherLines') AND name = 'BankName')
                BEGIN
                    ALTER TABLE [dbo].[VoucherLines] ADD [BankName] NVARCHAR(150) NULL;
                    PRINT 'Added column BankName to VoucherLines.';
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('VoucherLines') AND name = 'DrawerName')
                BEGIN
                    ALTER TABLE [dbo].[VoucherLines] ADD [DrawerName] NVARCHAR(200) NULL;
                    PRINT 'Added column DrawerName to VoucherLines.';
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('VoucherLines') AND name = 'ChequeStatus')
                BEGIN
                    ALTER TABLE [dbo].[VoucherLines] ADD [ChequeStatus] TINYINT NOT NULL DEFAULT 0;
                    PRINT 'Added column ChequeStatus to VoucherLines.';
                END

                -- Add Foreign Key if not exists
                IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_VoucherLines_Banks_BankId')
                BEGIN
                    ALTER TABLE [dbo].[VoucherLines] ADD CONSTRAINT [FK_VoucherLines_Banks_BankId]
                    FOREIGN KEY ([BankId]) REFERENCES [dbo].[Banks] ([Id]);
                    PRINT 'Added FK_VoucherLines_Banks_BankId.';
                END

                -- Add Indexes
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_VoucherLines_ChequeNumber_BankId')
                BEGIN
                    CREATE INDEX [IX_VoucherLines_ChequeNumber_BankId] ON [dbo].[VoucherLines] ([ChequeNumber], [BankId]);
                END

                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_VoucherLines_PaymentMethod_Status_DueDate')
                BEGIN
                    CREATE INDEX [IX_VoucherLines_PaymentMethod_Status_DueDate] ON [dbo].[VoucherLines] ([PaymentMethod], [ChequeStatus], [DueDate]);
                END
            ";

            await context.Database.ExecuteSqlRawAsync(sqlVoucherLines);
            Console.WriteLine("   -> Columns on [VoucherLines] verified.");

            // 3. Seed Standard Commercial Banks into Banks table
            var defaultBanks = new List<(string Code, string NameAr, string NameEn, string Swift)>
            {
                ("NBE", "البنك الأهلي المصري", "National Bank of Egypt", "NBEGEGCX"),
                ("MISR", "بنك مصر", "Banque Misr", "BMISEGCX"),
                ("CIB", "البنك التجاري الدولي", "Commercial International Bank", "CIBEEGCX"),
                ("QNB", "بنك قطر الوطني الأهلي", "QNB Alahli", "QNBAEGCX"),
                ("AAIB", "البنك العربي الأفريقي الدولي", "Arab African International Bank", "ARAFEGCX"),
                ("BDC", "بنك القاهرة", "Banque du Caire", "BCAIEGCX"),
                ("ALEX", "بنك الإسكندرية - إنتيسا سان باولو", "Bank of Alexandria", "ALEXEGCX"),
                ("HSBC", "بنك إتش إس بي سي مصر", "HSBC Bank Egypt", "HSBCEGCX"),
                ("FAISAL", "بنك فيصل الإسلامي المصري", "Faisal Islamic Bank of Egypt", "FAISEGCX"),
                ("ADIB", "مصرف أبوظبي الإسلامي - مصر", "Abu Dhabi Islamic Bank Egypt", "ADIBEGCX"),
                ("FAB", "بنك أبوظبي الأول مصر", "First Abu Dhabi Bank Egypt", "FABEGCX"),
                ("EGB", "البنك المصري الخليجي", "Egyptian Gulf Bank", "EGBEEGCX"),
                ("SAIB", "بنك الشركة المصرفية العربية الدولية", "Société Arabe Internationale de Banque", "SAIBEGCX"),
                ("MIDB", "بنك ميدبنك (مصر إيران سابقاً)", "MIDBANK", "MIDBEGCX"),
                ("ABK", "البنك الأهلي الكويتي - مصر", "Al Ahli Bank of Kuwait Egypt", "ABKEEGCX"),
                ("AUB", "البنك الأهلي المتحد - مصر", "Ahli United Bank Egypt", "AUBEEGCX"),
                ("UB", "المصرف المتحد", "The United Bank", "UBTEEGCX"),
                ("SCB", "بنك قناة السويس", "Suez Canal Bank", "SCANEGCX"),
                ("CA", "بنك كريدي أجريكول مصر", "Crédit Agricole Egypt", "AGRIEGCX"),
                ("EBE", "البنك المصري لتنمية الصادرات", "Export Development Bank of Egypt", "EXPAEGCX"),
                ("BLOM", "بنك المؤسسة العربية المصرفية - ABC مصر", "Bank ABC Egypt", "ABCOEGCX"),
                ("ENBD", "بنك الإمارات دبي الوطني مصر", "Emirates NBD Egypt", "EBILINX")
            };

            int addedCount = 0;
            foreach (var b in defaultBanks)
            {
                if (!await context.Banks.AnyAsync(x => x.Code == b.Code))
                {
                    context.Banks.Add(new Bank
                    {
                        Code = b.Code,
                        NameAr = b.NameAr,
                        NameEn = b.NameEn,
                        SwiftCode = b.Swift,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                await context.SaveChangesAsync();
                Console.WriteLine($"   -> Seeded {addedCount} Commercial Banks successfully.");
            }
            else
            {
                Console.WriteLine("   -> Commercial Banks already seeded.");
            }

            Console.WriteLine("=================================================================\n");
        }
    }
}
