using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ERP.Tools.DataSeeder
{
    internal class Program
    {
        private const string ConnectionString = "Server=tcp:SQL1002.site4now.net,1433;Database=db_ab95d6_sewtec2026;User Id=db_ab95d6_sewtec2026_admin;Password=Mostafa@1981$;Encrypt=True;TrustServerCertificate=True;Connect Timeout=60;";

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=================================================================");
            Console.WriteLine("        NEXTGEN ERP SYSTEM - TEST DATA ENVIRONMENT SEEDER       ");
            Console.WriteLine("=================================================================");
            Console.WriteLine($"Database Target: db_ab95d6_sewtec2026 on SQL1002.site4now.net");
            Console.WriteLine($"Start Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            var optionsBuilder = new DbContextOptionsBuilder<ErpDbContext>();
            optionsBuilder.UseSqlServer(ConnectionString, sqlOpts =>
            {
                sqlOpts.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sqlOpts.CommandTimeout(120);
            });

            using var context = new ErpDbContext(optionsBuilder.Options);

            if (args.Length > 0 && args.Contains("--migrate-cheques"))
            {
                await ChequeMigrationRunner.ApplyMigrationAsync(context);
                Environment.Exit(0);
                return;
            }

            if (args.Length > 0 && args.Contains("--validate-tb"))
            {
                var success = await TrialBalanceValidator.RunValidationAsync(context);
                Environment.Exit(success ? 0 : 1);
                return;
            }

            if (args.Length > 0 && args.Contains("--verify-cheques"))
            {
                var success = await ChequeWorkflowVerification.RunVerificationAsync(context);
                Environment.Exit(success ? 0 : 1);
                return;
            }

            if (args.Length > 0 && args.Contains("--clear-transactions"))
            {
                var success = await TransactionPurgeRunner.RunPurgeAsync(context);
                Environment.Exit(success ? 0 : 1);
                return;
            }

            if (args.Length > 0 && args.Contains("--seed-permissions"))
            {
                Console.WriteLine("Syncing Permissions & Roles to database...");
                await ERP.Infrastructure.Data.SeedData.EnsurePermissionsAndRolesAsync(context);
                Console.WriteLine("Permissions & Roles synced successfully!");
                Environment.Exit(0);
                return;
            }

            if (args.Length > 0 && args.Contains("--verify-permissions"))
            {
                var perms = await context.Permissions
                    .Where(p => p.Module == "Sales" || p.Module == "Purchases" || p.Module == "Inventory" || p.Module == "Reports")
                    .OrderBy(p => p.Module).ThenBy(p => p.Screen)
                    .ToListAsync();
                Console.WriteLine($"Total Permissions in Sales/Purchases/Inventory/Reports: {perms.Count}");
                var grouped = perms.GroupBy(p => p.Module);
                foreach (var g in grouped)
                {
                    Console.WriteLine($"\n--- Module: {g.Key} ({g.Count()} permissions) ---");
                    var screens = g.GroupBy(s => s.Screen);
                    foreach (var s in screens)
                    {
                        var sample = s.First();
                        Console.WriteLine($"  Screen: {s.Key} | Title: {sample.Description ?? sample.NameAr} | Actions: {string.Join(", ", s.Select(x => x.Action))}");
                    }
                }
                Environment.Exit(0);
                return;
            }

            if (args.Length > 0 && args.Contains("--fix-fiscal-periods"))
            {
                Console.WriteLine("Fixing Arabic month names in FiscalPeriods...");
                string[] arabicMonths = { "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
                var periods = await context.FiscalPeriods.Include(p => p.FiscalYear).ToListAsync();
                foreach (var p in periods)
                {
                    string yearStr = p.FiscalYear?.Year ?? p.StartDate.Year.ToString();
                    if (p.PeriodNumber >= 1 && p.PeriodNumber <= 12)
                    {
                        p.NameAr = $"{arabicMonths[p.PeriodNumber]} {yearStr}";
                        Console.WriteLine($"Period {p.PeriodNumber}: {p.NameAr} | {p.NameEn}");
                    }
                }
                await context.SaveChangesAsync();
                Console.WriteLine("FiscalPeriods Arabic names updated successfully.");
                Environment.Exit(0);
                return;
            }

            if (args.Length > 0 && args.Contains("--seed-inventory-types"))
            {
                Console.WriteLine("--- Checking and Seeding Standard Inventory Transaction Types ---");
                var existingTypes = await context.TransactionTypes.ToListAsync();
                Console.WriteLine($"Existing Transaction Types count: {existingTypes.Count}");
                foreach (var et in existingTypes)
                {
                    Console.WriteLine($"  - [{et.Code}] {et.NameAr} (Module: {et.Module})");
                }

                var defaultWarehouse = await context.Warehouses.FirstOrDefaultAsync(w => w.IsActive);
                int? defWhId = defaultWarehouse?.Id;

                var inventoryAcc = await context.GLAccounts.FirstOrDefaultAsync(a => (a.Code == "110301" || a.Code.StartsWith("1103")) && a.IsActive && !a.IsGroup);
                var cogsAcc = await context.GLAccounts.FirstOrDefaultAsync(a => (a.Code == "510101" || a.Code.StartsWith("51")) && a.IsActive && !a.IsGroup);
                var equityAcc = await context.GLAccounts.FirstOrDefaultAsync(a => (a.Code == "310101" || a.Code.StartsWith("3")) && a.IsActive && !a.IsGroup);

                var typesToCreate = new List<TransactionType>
                {
                    new TransactionType
                    {
                        Code = "WH-RCV",
                        NameAr = "إذن إضافة مخزني",
                        NameEn = "Warehouse Receipt Voucher",
                        Module = TransactionModule.Inventory,
                        BaseNature = DocumentBaseNature.ReceiptVoucher,
                        InventoryEffect = InventoryEffect.ReceiveStock,
                        AccountingEffect = AccountingEffect.None,
                        IsCashAllowed = true,
                        IsCreditAllowed = true,
                        AutoPost = true,
                        ShowLineNotes = true,
                        CodeSequencePrefix = "RCV-WH-",
                        DefaultWarehouseId = defWhId,
                        DefaultDebitAccountId = inventoryAcc?.Id,
                        DefaultCreditAccountId = cogsAcc?.Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new TransactionType
                    {
                        Code = "WH-ISS",
                        NameAr = "إذن صرف مخزني",
                        NameEn = "Warehouse Issue Voucher",
                        Module = TransactionModule.Inventory,
                        BaseNature = DocumentBaseNature.IssueVoucher,
                        InventoryEffect = InventoryEffect.IssueStock,
                        AccountingEffect = AccountingEffect.None,
                        IsCashAllowed = true,
                        IsCreditAllowed = true,
                        AutoPost = true,
                        ShowLineNotes = true,
                        CodeSequencePrefix = "ISS-WH-",
                        DefaultWarehouseId = defWhId,
                        DefaultDebitAccountId = cogsAcc?.Id,
                        DefaultCreditAccountId = inventoryAcc?.Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new TransactionType
                    {
                        Code = "WH-OPB",
                        NameAr = "رصيد أول مدة مخزني",
                        NameEn = "Inventory Opening Balance",
                        Module = TransactionModule.Inventory,
                        BaseNature = DocumentBaseNature.ReceiptVoucher,
                        InventoryEffect = InventoryEffect.ReceiveStock,
                        AccountingEffect = AccountingEffect.None,
                        IsCashAllowed = true,
                        IsCreditAllowed = true,
                        AutoPost = true,
                        ShowLineNotes = true,
                        CodeSequencePrefix = "OPB-WH-",
                        DefaultWarehouseId = defWhId,
                        DefaultDebitAccountId = inventoryAcc?.Id,
                        DefaultCreditAccountId = equityAcc?.Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new TransactionType
                    {
                        Code = "WH-ADJ-IN",
                        NameAr = "تسوية مخزنية - إضافة (فروق جرد)",
                        NameEn = "Inventory Adjustment (Surplus)",
                        Module = TransactionModule.Inventory,
                        BaseNature = DocumentBaseNature.ReceiptVoucher,
                        InventoryEffect = InventoryEffect.ReceiveStock,
                        AccountingEffect = AccountingEffect.None,
                        IsCashAllowed = true,
                        IsCreditAllowed = true,
                        AutoPost = true,
                        ShowLineNotes = true,
                        CodeSequencePrefix = "ADJ-IN-",
                        DefaultWarehouseId = defWhId,
                        DefaultDebitAccountId = inventoryAcc?.Id,
                        DefaultCreditAccountId = cogsAcc?.Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new TransactionType
                    {
                        Code = "WH-ADJ-OUT",
                        NameAr = "تسوية مخزنية - صرف (عجز جرد / تالف)",
                        NameEn = "Inventory Adjustment (Deficit/Damaged)",
                        Module = TransactionModule.Inventory,
                        BaseNature = DocumentBaseNature.IssueVoucher,
                        InventoryEffect = InventoryEffect.IssueStock,
                        AccountingEffect = AccountingEffect.None,
                        IsCashAllowed = true,
                        IsCreditAllowed = true,
                        AutoPost = true,
                        ShowLineNotes = true,
                        CodeSequencePrefix = "ADJ-OUT-",
                        DefaultWarehouseId = defWhId,
                        DefaultDebitAccountId = cogsAcc?.Id,
                        DefaultCreditAccountId = inventoryAcc?.Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new TransactionType
                    {
                        Code = "WH-TRF",
                        NameAr = "إذن تحويل مخزني",
                        NameEn = "Warehouse Transfer Voucher",
                        Module = TransactionModule.Transfer,
                        BaseNature = DocumentBaseNature.Transfer,
                        InventoryEffect = InventoryEffect.None,
                        AccountingEffect = AccountingEffect.None,
                        IsCashAllowed = true,
                        IsCreditAllowed = true,
                        AutoPost = true,
                        ShowLineNotes = true,
                        CodeSequencePrefix = "TRF-WH-",
                        DefaultWarehouseId = defWhId,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    }
                };

                int createdCount = 0;
                foreach (var tt in typesToCreate)
                {
                    if (!existingTypes.Any(x => x.Code.Equals(tt.Code, StringComparison.OrdinalIgnoreCase)))
                    {
                        context.TransactionTypes.Add(tt);
                        createdCount++;
                        Console.WriteLine($"[+] Adding: {tt.Code} - {tt.NameAr}");
                    }
                    else
                    {
                        Console.WriteLine($"[=] Already exists: {tt.Code}");
                    }
                }

                if (createdCount > 0)
                {
                    await context.SaveChangesAsync();
                    Console.WriteLine($"Successfully saved {createdCount} new Transaction Types.");
                }
                else
                {
                    Console.WriteLine("All standard inventory transaction types are already present.");
                }

                Environment.Exit(0);
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Step 1: Ensure 5 Branches
                Console.WriteLine("\n[1/7] Ensuring 5 Active Branches...");
                var branches = await SeedBranchesAsync(context);
                Console.WriteLine($"      -> Active Branches Available: {branches.Count}");

                // Step 2: Ensure 500+ GL Accounts
                Console.WriteLine("\n[2/7] Expanding Chart of Accounts to >= 500 GL Accounts...");
                var accounts = await SeedAccountsAsync(context, branches);
                Console.WriteLine($"      -> Total GL Accounts in System: {accounts.Count}");

                // Step 3: Ensure 200+ Customers
                Console.WriteLine("\n[3/7] Seeding Customers to reach >= 200 Customers...");
                var customers = await SeedCustomersAsync(context, branches, accounts);
                Console.WriteLine($"      -> Total Customers in System: {customers.Count}");

                // Step 4: Ensure 100+ Vendors
                Console.WriteLine("\n[4/7] Seeding Vendors to reach >= 100 Vendors...");
                var vendors = await SeedVendorsAsync(context, branches, accounts);
                Console.WriteLine($"      -> Total Vendors in System: {vendors.Count}");

                // Step 5: Seed 200 Receipt Vouchers (سندات قبض) + Auto Posted JVs
                Console.WriteLine("\n[5/7] Seeding 200 Receipt Vouchers (RV) with Auto Posting...");
                int rvCreated = await SeedReceiptVouchersAsync(context, branches, customers, accounts, 200);
                Console.WriteLine($"      -> Created & Posted Receipt Vouchers: {rvCreated}");

                // Step 6: Seed 200 Payment Vouchers (سندات صرف) + Auto Posted JVs
                Console.WriteLine("\n[6/7] Seeding 200 Payment Vouchers (PV) with Auto Posting...");
                int pvCreated = await SeedPaymentVouchersAsync(context, branches, vendors, accounts, 200);
                Console.WriteLine($"      -> Created & Posted Payment Vouchers: {pvCreated}");

                // Step 7: Seed 1000 Balanced Journal Entries
                Console.WriteLine("\n[7/7] Seeding 1,000 Balanced Journal Entries (JVs)...");
                int jvCreated = await SeedJournalEntriesAsync(context, branches, customers, vendors, accounts, 1000);
                Console.WriteLine($"      -> Created & Committed Journal Entries: {jvCreated}");

                stopwatch.Stop();
                Console.WriteLine("\n=================================================================");
                Console.WriteLine($"  ALL TEST DATA SEEDING COMPLETED SUCCESSFULLY IN {stopwatch.Elapsed.TotalSeconds:N1}s!");
                Console.WriteLine("=================================================================");

                // Final summary counts
                await PrintSummaryCountsAsync(context);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERROR OCCURRED] {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }
        }

        private static async Task<List<Branch>> SeedBranchesAsync(ErpDbContext context)
        {
            var branches = await context.Branches.Where(b => b.IsActive).OrderBy(b => b.Id).Take(10).ToListAsync();
            var company = await context.Companies.FirstOrDefaultAsync(c => c.IsActive) ?? new Company
            {
                Code = "COMP01",
                Name = "Sewtec Headquarters",
                NameAr = "سوي تك المركز الرئيسي",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            if (company.Id == 0)
            {
                context.Companies.Add(company);
                await context.SaveChangesAsync();
            }

            var branchDefs = new[]
            {
                ("B001", "المركز الرئيسي - القاهرة", "Cairo HQ Branch"),
                ("B002", "فرع الإسكندرية التجاري", "Alexandria Commercial Branch"),
                ("B003", "فرع العاشر من رمضان الصناعي", "10th of Ramadan Industrial Branch"),
                ("B004", "فرع الجيزة ووسط الصعيد", "Giza & Upper Egypt Branch"),
                ("B005", "فرع الدلتا - طنطا", "Delta - Tanta Branch")
            };

            foreach (var (code, nameAr, nameEn) in branchDefs)
            {
                var existing = await context.Branches.FirstOrDefaultAsync(b => b.Code == code || b.BranchCode == code);
                if (existing == null)
                {
                    var newBranch = new Branch
                    {
                        Code = code,
                        BranchCode = code,
                        Name = nameEn,
                        NameAr = nameAr,
                        NameEn = nameEn,
                        CompanyId = company.Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    context.Branches.Add(newBranch);
                }
            }
            await context.SaveChangesAsync();

            return await context.Branches.Where(b => b.IsActive).OrderBy(b => b.Id).Take(5).ToListAsync();
        }

        private static async Task<List<GLAccount>> SeedAccountsAsync(ErpDbContext context, List<Branch> branches)
        {
            var existingAccounts = await context.GLAccounts.AsNoTracking().ToDictionaryAsync(a => a.Code);
            var egp = await context.Currencies.FirstOrDefaultAsync(c => c.Code == "EGP") ?? new Currency { Code = "EGP", NameAr = "جنيه مصري", NameEn = "Egyptian Pound", ExchangeRate = 1m, IsActive = true };
            var usd = await context.Currencies.FirstOrDefaultAsync(c => c.Code == "USD") ?? new Currency { Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar", ExchangeRate = 48.5m, IsActive = true };
            var eur = await context.Currencies.FirstOrDefaultAsync(c => c.Code == "EUR") ?? new Currency { Code = "EUR", NameAr = "يورو أوروبي", NameEn = "Euro", ExchangeRate = 53.2m, IsActive = true };

            var newAccounts = new List<GLAccount>();

            // Generate structured tree up to 520 accounts
            // 1. Cash Boxes per branch (110101xx)
            for (int i = 1; i <= 20; i++)
            {
                string code = $"110101{i:D2}";
                if (!existingAccounts.ContainsKey(code))
                {
                    newAccounts.Add(new GLAccount
                    {
                        Code = code,
                        NameAr = $"خزينة فرعية / صندوق رقم {i}",
                        NameEn = $"Sub-Cash Box #{i}",
                        ParentId = existingAccounts.GetValueOrDefault("110101")?.Id ?? existingAccounts["1101"].Id,
                        Level = 5,
                        IsGroup = false,
                        AccountCategory = AccountCategory.Assets,
                        AccountNature = AccountNature.Debit,
                        CurrencyId = egp.Id,
                        IsControlAccount = true,
                        SubLedgerType = SubLedgerType.Bank,
                        BranchId = branches[(i - 1) % branches.Count].Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // 2. Bank Accounts (110102xx)
            for (int i = 3; i <= 25; i++)
            {
                string code = $"110102{i:D2}";
                if (!existingAccounts.ContainsKey(code))
                {
                    var cur = (i % 3 == 0) ? usd : (i % 5 == 0 ? eur : egp);
                    newAccounts.Add(new GLAccount
                    {
                        Code = code,
                        NameAr = $"حساب بنكي جاري رقم {i} ({cur.Code})",
                        NameEn = $"Bank Account #{i} ({cur.Code})",
                        ParentId = existingAccounts.GetValueOrDefault("110102")?.Id ?? existingAccounts["1101"].Id,
                        Level = 5,
                        IsGroup = false,
                        AccountCategory = AccountCategory.Assets,
                        AccountNature = AccountNature.Debit,
                        CurrencyId = cur.Id,
                        IsControlAccount = true,
                        SubLedgerType = SubLedgerType.Bank,
                        BranchId = branches[(i - 1) % branches.Count].Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // 3. Raw Materials & Inventory Sub-accounts (110303xx to 110330xx)
            for (int i = 3; i <= 35; i++)
            {
                string code = $"1103{i:D2}";
                if (!existingAccounts.ContainsKey(code))
                {
                    newAccounts.Add(new GLAccount
                    {
                        Code = code,
                        NameAr = $"مخزون صنف / مجموعة خامات {i}",
                        NameEn = $"Inventory Category Item #{i}",
                        ParentId = existingAccounts.GetValueOrDefault("1103")?.Id,
                        Level = 4,
                        IsGroup = false,
                        AccountCategory = AccountCategory.Assets,
                        AccountNature = AccountNature.Debit,
                        CurrencyId = egp.Id,
                        IsControlAccount = false,
                        SubLedgerType = SubLedgerType.None,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // 4. Fixed Assets (1201xx to 1208xx)
            for (int i = 1; i <= 50; i++)
            {
                string code = $"1204{i:D2}";
                if (!existingAccounts.ContainsKey(code))
                {
                    newAccounts.Add(new GLAccount
                    {
                        Code = code,
                        NameAr = $"أصل ثابت - معدة / خط إنتاج / جهاز #{i}",
                        NameEn = $"Fixed Asset Machinery/Equipment #{i}",
                        ParentId = existingAccounts.GetValueOrDefault("12")?.Id,
                        Level = 3,
                        IsGroup = false,
                        AccountCategory = AccountCategory.Assets,
                        AccountNature = AccountNature.Debit,
                        CurrencyId = egp.Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // 5. Operating & Admin Expenses (5201xx to 5280xx)
            for (int i = 1; i <= 200; i++)
            {
                string code = $"52{i:D4}";
                if (!existingAccounts.ContainsKey(code))
                {
                    newAccounts.Add(new GLAccount
                    {
                        Code = code,
                        NameAr = $"مصروف تشغيلي / إداري تفصيلي رقم {i}",
                        NameEn = $"Operating/Admin Detailed Expense #{i}",
                        ParentId = existingAccounts.GetValueOrDefault("52")?.Id ?? existingAccounts["5"].Id,
                        Level = 3,
                        IsGroup = false,
                        AccountCategory = AccountCategory.Expenses,
                        AccountNature = AccountNature.Debit,
                        CurrencyId = egp.Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // 6. Selling & Marketing Expenses (5301xx to 5350xx)
            for (int i = 1; i <= 80; i++)
            {
                string code = $"53{i:D4}";
                if (!existingAccounts.ContainsKey(code))
                {
                    newAccounts.Add(new GLAccount
                    {
                        Code = code,
                        NameAr = $"مصروف بيعي وتسويقي رقم {i}",
                        NameEn = $"Selling & Marketing Expense #{i}",
                        ParentId = existingAccounts.GetValueOrDefault("53")?.Id ?? existingAccounts["5"].Id,
                        Level = 3,
                        IsGroup = false,
                        AccountCategory = AccountCategory.Expenses,
                        AccountNature = AccountNature.Debit,
                        CurrencyId = egp.Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // 7. Cost of Goods Sold (5101xx to 5140xx)
            for (int i = 2; i <= 50; i++)
            {
                string code = $"5101{i:D2}";
                if (!existingAccounts.ContainsKey(code))
                {
                    newAccounts.Add(new GLAccount
                    {
                        Code = code,
                        NameAr = $"تكلفة مبيعات - قطاع {i}",
                        NameEn = $"COGS - Sector #{i}",
                        ParentId = existingAccounts.GetValueOrDefault("51")?.Id ?? existingAccounts["5"].Id,
                        Level = 4,
                        IsGroup = false,
                        AccountCategory = AccountCategory.Expenses,
                        AccountNature = AccountNature.Debit,
                        CurrencyId = egp.Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // 8. Sales & Revenues (4101xx to 4150xx)
            for (int i = 1; i <= 60; i++)
            {
                string code = $"41{i:D4}";
                if (!existingAccounts.ContainsKey(code))
                {
                    newAccounts.Add(new GLAccount
                    {
                        Code = code,
                        NameAr = $"إيراد مبيعات منتج / خدمة {i}",
                        NameEn = $"Sales Revenue Product/Service #{i}",
                        ParentId = existingAccounts.GetValueOrDefault("41")?.Id ?? existingAccounts["4"].Id,
                        Level = 3,
                        IsGroup = false,
                        AccountCategory = AccountCategory.Revenues,
                        AccountNature = AccountNature.Credit,
                        CurrencyId = egp.Id,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // Save in batches
            int batchSize = 100;
            for (int k = 0; k < newAccounts.Count; k += batchSize)
            {
                var batch = newAccounts.Skip(k).Take(batchSize);
                context.GLAccounts.AddRange(batch);
                await context.SaveChangesAsync();
                Console.Write($"\r      -> Inserted {Math.Min(k + batchSize, newAccounts.Count)}/{newAccounts.Count} GL Accounts...");
            }
            Console.WriteLine();

            return await context.GLAccounts.AsNoTracking().Where(a => a.IsActive).ToListAsync();
        }

        private static async Task<List<Customer>> SeedCustomersAsync(ErpDbContext context, List<Branch> branches, List<GLAccount> accounts)
        {
            int currentCount = await context.Customers.CountAsync();
            int needed = Math.Max(0, 200 - currentCount);

            var controlAccount = accounts.FirstOrDefault(a => a.Code == "110201") 
                              ?? accounts.FirstOrDefault(a => a.SubLedgerType == SubLedgerType.Customer && !a.IsGroup)
                              ?? accounts.First(a => a.Code.StartsWith("11"));

            var egp = await context.Currencies.FirstAsync(c => c.Code == "EGP");
            var category = await context.CustomerCategories.FirstOrDefaultAsync() ?? new CustomerCategory
            {
                Code = "CAT-GEN",
                NameAr = "عملاء تجاري عام",
                NameEn = "General Commercial",
                IsActive = true
            };
            if (category.Id == 0)
            {
                context.CustomerCategories.Add(category);
                await context.SaveChangesAsync();
            }

            var prefixes = new[] { "شركة", "مؤسسة", "مجموعة", "توكيلات", "مصنع", "مكتب" };
            var names = new[] { "الأمل للتجارة", "النيل للصناعات", "الأهرام الدولية", "الدلتا للتوزيع", "القاهرة الحديثة", "الإسكندرية للمنسوجات", "الشرق الأوسط للتوريدات", "المستقبل للملابس", "البركة للمهمات", "طيبة للمقاولات", "الفيروز للحلول الصناعية", "الصفوة للتجارة", "الهدى للمستلزمات", "النور للاستيراد والتصدير", "الرواد العالمية" };

            var newCustomers = new List<Customer>();
            for (int i = 1; i <= needed; i++)
            {
                int totalIdx = currentCount + i;
                string custCode = $"CUST-{totalIdx:D4}";
                string nameAr = $"{prefixes[i % prefixes.Length]} {names[i % names.Length]} فرع {i}";
                string nameEn = $"Customer {totalIdx} Trading Co.";

                newCustomers.Add(new Customer
                {
                    Code = custCode,
                    NameAr = nameAr,
                    NameEn = nameEn,
                    BranchId = branches[(i - 1) % branches.Count].Id,
                    FinancialAccountId = controlAccount.Id,
                    CurrencyId = egp.Id,
                    CustomerCategoryId = category.Id,
                    Phone = $"010{totalIdx:D8}",
                    Mobile = $"011{totalIdx:D8}",
                    TaxNumber = $"100-{totalIdx:D3}-{i % 99:D2}",
                    CommercialRegister = $"CR-{totalIdx:D5}",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Batch insert
            for (int k = 0; k < newCustomers.Count; k += 100)
            {
                var batch = newCustomers.Skip(k).Take(100);
                context.Customers.AddRange(batch);
                await context.SaveChangesAsync();
                Console.Write($"\r      -> Inserted {Math.Min(k + 100, newCustomers.Count)}/{newCustomers.Count} Customers...");
            }
            if (newCustomers.Any()) Console.WriteLine();

            return await context.Customers.AsNoTracking().Where(c => c.IsActive).ToListAsync();
        }

        private static async Task<List<Vendor>> SeedVendorsAsync(ErpDbContext context, List<Branch> branches, List<GLAccount> accounts)
        {
            int currentCount = await context.Vendors.CountAsync();
            int needed = Math.Max(0, 100 - currentCount);

            var controlAccount = accounts.FirstOrDefault(a => a.Code == "210101")
                              ?? accounts.FirstOrDefault(a => a.SubLedgerType == SubLedgerType.Vendor && !a.IsGroup)
                              ?? accounts.First(a => a.Code.StartsWith("21"));

            var egp = await context.Currencies.FirstAsync(c => c.Code == "EGP");
            var category = await context.VendorCategories.FirstOrDefaultAsync() ?? new VendorCategory
            {
                Code = "VND-GEN",
                NameAr = "موردون محليون رئيسيون",
                NameEn = "Primary Domestic Vendors",
                IsActive = true
            };
            if (category.Id == 0)
            {
                context.VendorCategories.Add(category);
                await context.SaveChangesAsync();
            }

            var supplierTypes = new[] { "للغزول والمنسوجات", "لقطع الغيار والمهمات", "للآلات والمعدات", "لخامات التعبئة والتغليف", "للإلكترونيات والتحكم", "للخدمات واللوجستيات", "للمواد الكيماوية والصباغة", "لمستلزمات الصيانة" };
            var companyNames = new[] { "السويس الوطنية", "الدلتا الصناعية", "العاشر للمهمات", "المصرية الدولية", "العربية للصناعات", "القاهرة للتوريد", "الشرق للتجارة الهندسية", "الأهرام لماكينات الحياكة" };

            var newVendors = new List<Vendor>();
            for (int i = 1; i <= needed; i++)
            {
                int totalIdx = currentCount + i;
                string vndCode = $"VND-{totalIdx:D4}";
                string nameAr = $"شركة {companyNames[i % companyNames.Length]} {supplierTypes[i % supplierTypes.Length]} #{i}";
                string nameEn = $"Supplier {totalIdx} Industrial Solutions";

                newVendors.Add(new Vendor
                {
                    Code = vndCode,
                    NameAr = nameAr,
                    NameEn = nameEn,
                    BranchId = branches[(i - 1) % branches.Count].Id,
                    FinancialAccountId = controlAccount.Id,
                    CurrencyId = egp.Id,
                    VendorCategoryId = category.Id,
                    Phone = $"012{totalIdx:D8}",
                    Mobile = $"015{totalIdx:D8}",
                    TaxNumber = $"200-{totalIdx:D3}-{i % 99:D2}",
                    CommercialRegister = $"CR-VND-{totalIdx:D5}",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            for (int k = 0; k < newVendors.Count; k += 50)
            {
                var batch = newVendors.Skip(k).Take(50);
                context.Vendors.AddRange(batch);
                await context.SaveChangesAsync();
                Console.Write($"\r      -> Inserted {Math.Min(k + 50, newVendors.Count)}/{newVendors.Count} Vendors...");
            }
            if (newVendors.Any()) Console.WriteLine();

            return await context.Vendors.AsNoTracking().Where(v => v.IsActive).ToListAsync();
        }

        private static async Task<int> SeedReceiptVouchersAsync(
            ErpDbContext context, 
            List<Branch> branches, 
            List<Customer> customers, 
            List<GLAccount> accounts, 
            int count)
        {
            int existingCount = await context.VoucherHeaders.CountAsync(v => v.VoucherType == VoucherType.Receipt);
            int needed = Math.Max(0, count - existingCount);
            if (needed == 0) return existingCount;

            var fy = await context.FiscalYears.FirstAsync(f => f.IsActive && !f.IsClosed);
            var egp = await context.Currencies.FirstAsync(c => c.Code == "EGP");
            var cashAccounts = accounts.Where(a => a.Code.StartsWith("1101") && !a.IsGroup).ToList();
            var defaultArAccount = accounts.First(a => a.Code == "110201");

            var vouchers = new List<VoucherHeader>();
            var jvs = new List<JournalEntryHeader>();

            var baseDate = new DateTime(2026, 1, 15);
            var rnd = new Random(42);

            for (int i = 1; i <= needed; i++)
            {
                int voucherNum = existingCount + i;
                var branch = branches[(i - 1) % branches.Count];
                var customer = customers[(i - 1) % customers.Count];
                var cashAccount = cashAccounts[(i - 1) % cashAccounts.Count];
                var vDate = baseDate.AddDays(rnd.Next(0, 260));

                decimal amount = rnd.Next(5, 120) * 1000m;
                bool isBank = (i % 2 == 0);
                int arAccountId = customer.FinancialAccountId ?? defaultArAccount.Id;

                var voucher = new VoucherHeader
                {
                    VoucherType = VoucherType.Receipt,
                    VoucherNumber = $"RV-{branch.Id}-{fy.Year}-{voucherNum:D5}",
                    VoucherDate = vDate,
                    FiscalYearId = fy.Id,
                    BranchId = branch.Id,
                    CashBankAccountId = cashAccount.Id,
                    PaymentMethod = isBank ? VoucherPaymentMethod.Cheque : VoucherPaymentMethod.Cash,
                    ChequeNumber = isBank ? $"CHQ-{rnd.Next(100000, 999999)}" : null,
                    ChequeDate = isBank ? vDate : null,
                    ChequeDueDate = isBank ? vDate.AddDays(30) : null,
                    BankName = isBank ? "البنك الأهلي المصري - فرع الشركات" : null,
                    CurrencyId = egp.Id,
                    ExchangeRate = 1.000000m,
                    TotalAmountForeign = amount,
                    TotalAmountLocal = amount,
                    ReferenceNumber = $"REF-REC-{voucherNum}",
                    Notes = $"سند قبض تحصيل من العميل {customer.NameAr} - دفعة تحت الحساب",
                    Status = VoucherStatus.Posted,
                    CreatedAt = vDate,
                    PostedAt = vDate,
                    Lines = new List<VoucherLine>
                    {
                        new VoucherLine
                        {
                            LineNumber = 1,
                            LineType = VoucherLineType.Customer,
                            SubLedgerId = customer.Id,
                            AccountId = arAccountId,
                            AmountForeign = amount,
                            AmountLocal = amount,
                            Description = $"تحصيل نقدي / بنكي من {customer.NameAr}",
                            CreatedAt = vDate
                        }
                    }
                };

                // Create corresponding linked Journal Entry
                var jv = new JournalEntryHeader
                {
                    BranchId = branch.Id,
                    FiscalYearId = fy.Id,
                    EntryNumber = $"JV-RV-{branch.Id}-{fy.Year}-{voucherNum:D5}",
                    EntryDate = vDate,
                    Description = $"قيد ترحيل سند قبض رقم {voucher.VoucherNumber} - العميل {customer.NameAr}",
                    ReferenceNumber = voucher.VoucherNumber,
                    SourceDocumentType = SourceDocumentType.ReceiptVoucher,
                    Status = JournalEntryStatus.Posted,
                    TotalDebitLocal = amount,
                    TotalCreditLocal = amount,
                    CreatedAt = vDate,
                    PostedAt = vDate,
                    Lines = new List<JournalEntryLine>
                    {
                        new JournalEntryLine
                        {
                            LineNumber = 1,
                            AccountId = cashAccount.Id,
                            SubLedgerType = SubLedgerType.Bank,
                            CurrencyId = egp.Id,
                            ExchangeRate = 1m,
                            DebitForeign = amount,
                            DebitLocal = amount,
                            CreditForeign = 0,
                            CreditLocal = 0,
                            Description = $"إيداع بالخزينة/البنك بسند قبض {voucher.VoucherNumber}",
                            CreatedAt = vDate
                        },
                        new JournalEntryLine
                        {
                            LineNumber = 2,
                            AccountId = arAccountId,
                            SubLedgerType = SubLedgerType.Customer,
                            SubLedgerId = customer.Id,
                            CurrencyId = egp.Id,
                            ExchangeRate = 1m,
                            DebitForeign = 0,
                            DebitLocal = 0,
                            CreditForeign = amount,
                            CreditLocal = amount,
                            Description = $"تحصيل من العميل {customer.NameAr}",
                            CreatedAt = vDate
                        }
                    }
                };

                vouchers.Add(voucher);
                jvs.Add(jv);
            }

            // Save in batches of 50
            for (int k = 0; k < vouchers.Count; k += 50)
            {
                var vBatch = vouchers.Skip(k).Take(50).ToList();
                var jBatch = jvs.Skip(k).Take(50).ToList();

                context.JournalEntryHeaders.AddRange(jBatch);
                await context.SaveChangesAsync();

                for (int m = 0; m < vBatch.Count; m++)
                {
                    vBatch[m].JournalEntryHeaderId = jBatch[m].Id;
                }
                context.VoucherHeaders.AddRange(vBatch);
                await context.SaveChangesAsync();

                Console.Write($"\r      -> Created {Math.Min(k + 50, vouchers.Count)}/{vouchers.Count} Receipt Vouchers & JVs...");
            }
            Console.WriteLine();

            return await context.VoucherHeaders.CountAsync(v => v.VoucherType == VoucherType.Receipt);
        }

        private static async Task<int> SeedPaymentVouchersAsync(
            ErpDbContext context, 
            List<Branch> branches, 
            List<Vendor> vendors, 
            List<GLAccount> accounts, 
            int count)
        {
            int existingCount = await context.VoucherHeaders.CountAsync(v => v.VoucherType == VoucherType.Payment);
            int needed = Math.Max(0, count - existingCount);
            if (needed == 0) return existingCount;

            var fy = await context.FiscalYears.FirstAsync(f => f.IsActive && !f.IsClosed);
            var egp = await context.Currencies.FirstAsync(c => c.Code == "EGP");
            var cashAccounts = accounts.Where(a => a.Code.StartsWith("1101") && !a.IsGroup).ToList();
            var expenseAccounts = accounts.Where(a => a.Code.StartsWith("52") && !a.IsGroup).ToList();
            var defaultApAccount = accounts.First(a => a.Code == "210101");

            var vouchers = new List<VoucherHeader>();
            var jvs = new List<JournalEntryHeader>();

            var baseDate = new DateTime(2026, 1, 15);
            var rnd = new Random(84);

            for (int i = 1; i <= needed; i++)
            {
                int voucherNum = existingCount + i;
                var branch = branches[(i - 1) % branches.Count];
                var cashAccount = cashAccounts[(i - 1) % cashAccounts.Count];
                var vDate = baseDate.AddDays(rnd.Next(0, 260));

                decimal amount = rnd.Next(3, 90) * 1000m;
                bool isSupplierPayment = (i % 2 == 1);
                bool isBank = (i % 3 == 0);

                VoucherHeader voucher;
                JournalEntryHeader jv;

                if (isSupplierPayment)
                {
                    var vendor = vendors[(i - 1) % vendors.Count];
                    int apAccountId = vendor.FinancialAccountId ?? defaultApAccount.Id;

                    voucher = new VoucherHeader
                    {
                        VoucherType = VoucherType.Payment,
                        VoucherNumber = $"PV-{branch.Id}-{fy.Year}-{voucherNum:D5}",
                        VoucherDate = vDate,
                        FiscalYearId = fy.Id,
                        BranchId = branch.Id,
                        CashBankAccountId = cashAccount.Id,
                        PaymentMethod = isBank ? VoucherPaymentMethod.BankTransfer : VoucherPaymentMethod.Cash,
                        ChequeNumber = isBank ? $"TRF-{rnd.Next(100000, 999999)}" : null,
                        BankName = isBank ? "بنك مصر - فرع المعاملات الدولية" : null,
                        CurrencyId = egp.Id,
                        ExchangeRate = 1.000000m,
                        TotalAmountForeign = amount,
                        TotalAmountLocal = amount,
                        ReferenceNumber = $"SUPP-PAY-{voucherNum}",
                        Notes = $"سداد مستحقات وفواتير المورد {vendor.NameAr}",
                        Status = VoucherStatus.Posted,
                        CreatedAt = vDate,
                        PostedAt = vDate,
                        Lines = new List<VoucherLine>
                        {
                            new VoucherLine
                            {
                                LineNumber = 1,
                                LineType = VoucherLineType.Vendor,
                                SubLedgerId = vendor.Id,
                                AccountId = apAccountId,
                                AmountForeign = amount,
                                AmountLocal = amount,
                                Description = $"سداد للمورد {vendor.NameAr} بسند صرف",
                                CreatedAt = vDate
                            }
                        }
                    };

                    jv = new JournalEntryHeader
                    {
                        BranchId = branch.Id,
                        FiscalYearId = fy.Id,
                        EntryNumber = $"JV-PV-{branch.Id}-{fy.Year}-{voucherNum:D5}",
                        EntryDate = vDate,
                        Description = $"قيد ترحيل سند صرف رقم {voucher.VoucherNumber} - سداد المورد {vendor.NameAr}",
                        ReferenceNumber = voucher.VoucherNumber,
                        SourceDocumentType = SourceDocumentType.PaymentVoucher,
                        Status = JournalEntryStatus.Posted,
                        TotalDebitLocal = amount,
                        TotalCreditLocal = amount,
                        CreatedAt = vDate,
                        PostedAt = vDate,
                        Lines = new List<JournalEntryLine>
                        {
                            new JournalEntryLine
                            {
                                LineNumber = 1,
                                AccountId = apAccountId,
                                SubLedgerType = SubLedgerType.Vendor,
                                SubLedgerId = vendor.Id,
                                CurrencyId = egp.Id,
                                ExchangeRate = 1m,
                                DebitForeign = amount,
                                DebitLocal = amount,
                                CreditForeign = 0,
                                CreditLocal = 0,
                                Description = $"مدين - حساب المورد {vendor.NameAr}",
                                CreatedAt = vDate
                            },
                            new JournalEntryLine
                            {
                                LineNumber = 2,
                                AccountId = cashAccount.Id,
                                SubLedgerType = SubLedgerType.Bank,
                                CurrencyId = egp.Id,
                                ExchangeRate = 1m,
                                DebitForeign = 0,
                                DebitLocal = 0,
                                CreditForeign = amount,
                                CreditLocal = amount,
                                Description = $"دائن - صرف من الخزينة/البنك بسند صرف {voucher.VoucherNumber}",
                                CreatedAt = vDate
                            }
                        }
                    };
                }
                else
                {
                    // Expense Payment
                    var expAcc = expenseAccounts[(i - 1) % expenseAccounts.Count];
                    voucher = new VoucherHeader
                    {
                        VoucherType = VoucherType.Payment,
                        VoucherNumber = $"PV-{branch.Id}-{fy.Year}-{voucherNum:D5}",
                        VoucherDate = vDate,
                        FiscalYearId = fy.Id,
                        BranchId = branch.Id,
                        CashBankAccountId = cashAccount.Id,
                        PaymentMethod = isBank ? VoucherPaymentMethod.Cheque : VoucherPaymentMethod.Cash,
                        ChequeNumber = isBank ? $"CHQ-EXP-{rnd.Next(100000, 999999)}" : null,
                        ChequeDate = isBank ? vDate : null,
                        ChequeDueDate = isBank ? vDate.AddDays(15) : null,
                        BankName = isBank ? "البنك الأهلي المصري" : null,
                        CurrencyId = egp.Id,
                        ExchangeRate = 1.000000m,
                        TotalAmountForeign = amount,
                        TotalAmountLocal = amount,
                        ReferenceNumber = $"EXP-PAY-{voucherNum}",
                        Notes = $"صرف مصروفات تشغيلية - {expAcc.NameAr}",
                        Status = VoucherStatus.Posted,
                        CreatedAt = vDate,
                        PostedAt = vDate,
                        Lines = new List<VoucherLine>
                        {
                            new VoucherLine
                            {
                                LineNumber = 1,
                                LineType = VoucherLineType.Account,
                                AccountId = expAcc.Id,
                                AmountForeign = amount,
                                AmountLocal = amount,
                                Description = $"مصروف: {expAcc.NameAr}",
                                CreatedAt = vDate
                            }
                        }
                    };

                    jv = new JournalEntryHeader
                    {
                        BranchId = branch.Id,
                        FiscalYearId = fy.Id,
                        EntryNumber = $"JV-PV-{branch.Id}-{fy.Year}-{voucherNum:D5}",
                        EntryDate = vDate,
                        Description = $"قيد ترحيل سند صرف رقم {voucher.VoucherNumber} - {expAcc.NameAr}",
                        ReferenceNumber = voucher.VoucherNumber,
                        SourceDocumentType = SourceDocumentType.PaymentVoucher,
                        Status = JournalEntryStatus.Posted,
                        TotalDebitLocal = amount,
                        TotalCreditLocal = amount,
                        CreatedAt = vDate,
                        PostedAt = vDate,
                        Lines = new List<JournalEntryLine>
                        {
                            new JournalEntryLine
                            {
                                LineNumber = 1,
                                AccountId = expAcc.Id,
                                SubLedgerType = SubLedgerType.None,
                                CurrencyId = egp.Id,
                                ExchangeRate = 1m,
                                DebitForeign = amount,
                                DebitLocal = amount,
                                CreditForeign = 0,
                                CreditLocal = 0,
                                Description = $"مدين - مصروف {expAcc.NameAr}",
                                CreatedAt = vDate
                            },
                            new JournalEntryLine
                            {
                                LineNumber = 2,
                                AccountId = cashAccount.Id,
                                SubLedgerType = SubLedgerType.Bank,
                                CurrencyId = egp.Id,
                                ExchangeRate = 1m,
                                DebitForeign = 0,
                                DebitLocal = 0,
                                CreditForeign = amount,
                                CreditLocal = amount,
                                Description = $"دائن - صرف من الخزينة/البنك بسند صرف {voucher.VoucherNumber}",
                                CreatedAt = vDate
                            }
                        }
                    };
                }

                vouchers.Add(voucher);
                jvs.Add(jv);
            }

            // Save in batches of 50
            for (int k = 0; k < vouchers.Count; k += 50)
            {
                var vBatch = vouchers.Skip(k).Take(50).ToList();
                var jBatch = jvs.Skip(k).Take(50).ToList();

                context.JournalEntryHeaders.AddRange(jBatch);
                await context.SaveChangesAsync();

                for (int m = 0; m < vBatch.Count; m++)
                {
                    vBatch[m].JournalEntryHeaderId = jBatch[m].Id;
                }
                context.VoucherHeaders.AddRange(vBatch);
                await context.SaveChangesAsync();

                Console.Write($"\r      -> Created {Math.Min(k + 50, vouchers.Count)}/{vouchers.Count} Payment Vouchers & JVs...");
            }
            Console.WriteLine();

            return await context.VoucherHeaders.CountAsync(v => v.VoucherType == VoucherType.Payment);
        }

        private static async Task<int> SeedJournalEntriesAsync(
            ErpDbContext context, 
            List<Branch> branches, 
            List<Customer> customers, 
            List<Vendor> vendors, 
            List<GLAccount> accounts, 
            int count)
        {
            int currentCount = await context.JournalEntryHeaders.CountAsync();
            int needed = Math.Max(0, count - currentCount);
            if (needed == 0) return currentCount;

            var fy = await context.FiscalYears.FirstAsync(f => f.IsActive && !f.IsClosed);
            var egp = await context.Currencies.FirstAsync(c => c.Code == "EGP");
            var usd = await context.Currencies.FirstOrDefaultAsync(c => c.Code == "USD") ?? egp;

            var cashAccounts = accounts.Where(a => a.Code.StartsWith("1101") && !a.IsGroup).ToList();
            var salesAccounts = accounts.Where(a => a.Code.StartsWith("41") && !a.IsGroup).ToList();
            var inventoryAccounts = accounts.Where(a => a.Code.StartsWith("1103") && !a.IsGroup).ToList();
            var expenseAccounts = accounts.Where(a => a.Code.StartsWith("52") && !a.IsGroup).ToList();
            var defaultArAccount = accounts.First(a => a.Code == "110201");
            var defaultApAccount = accounts.First(a => a.Code == "210101");

            var rnd = new Random(101);
            var baseDate = new DateTime(2026, 1, 2);

            var newJVs = new List<JournalEntryHeader>();

            for (int i = 1; i <= needed; i++)
            {
                int jvSeq = currentCount + i;
                var branch = branches[(i - 1) % branches.Count];
                var entryDate = baseDate.AddDays(rnd.Next(0, 270));

                int scenario = i % 5;
                decimal amount = rnd.Next(2, 150) * 500m;

                var jv = new JournalEntryHeader
                {
                    BranchId = branch.Id,
                    FiscalYearId = fy.Id,
                    EntryNumber = $"JV-{branch.Id}-{fy.Year}-{jvSeq:D5}",
                    EntryDate = entryDate,
                    ReferenceNumber = $"REF-MANUAL-{jvSeq}",
                    SourceDocumentType = SourceDocumentType.ManualJV,
                    Status = (i % 25 == 0) ? JournalEntryStatus.Draft : JournalEntryStatus.Posted,
                    TotalDebitLocal = amount,
                    TotalCreditLocal = amount,
                    CreatedAt = entryDate,
                    PostedAt = (i % 25 == 0) ? null : entryDate,
                    Lines = new List<JournalEntryLine>()
                };

                switch (scenario)
                {
                    case 0:
                        // Cash Sales
                        var cashAcc = cashAccounts[i % cashAccounts.Count];
                        var salesAcc = salesAccounts[i % salesAccounts.Count];
                        jv.Description = $"قيد إثبات مبيعات نقدية يومية - فرع {branch.NameAr}";
                        jv.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = 1,
                            AccountId = cashAcc.Id,
                            SubLedgerType = SubLedgerType.Bank,
                            CurrencyId = egp.Id,
                            ExchangeRate = 1m,
                            DebitForeign = amount,
                            DebitLocal = amount,
                            CreditForeign = 0,
                            CreditLocal = 0,
                            Description = "إيداع مبيعات نقدية بالخزينة",
                            CreatedAt = entryDate
                        });
                        jv.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = 2,
                            AccountId = salesAcc.Id,
                            SubLedgerType = SubLedgerType.None,
                            CurrencyId = egp.Id,
                            ExchangeRate = 1m,
                            DebitForeign = 0,
                            DebitLocal = 0,
                            CreditForeign = amount,
                            CreditLocal = amount,
                            Description = "إيرادات مبيعات بضائع",
                            CreatedAt = entryDate
                        });
                        break;

                    case 1:
                        // Credit Sales to Customer
                        var cust = customers[i % customers.Count];
                        int custAccountId = cust.FinancialAccountId ?? defaultArAccount.Id;
                        var revAcc = salesAccounts[(i + 1) % salesAccounts.Count];
                        jv.Description = $"قيد إثبات فاتورة بيع آجل للعميل {cust.NameAr}";
                        jv.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = 1,
                            AccountId = custAccountId,
                            SubLedgerType = SubLedgerType.Customer,
                            SubLedgerId = cust.Id,
                            CurrencyId = egp.Id,
                            ExchangeRate = 1m,
                            DebitForeign = amount,
                            DebitLocal = amount,
                            CreditForeign = 0,
                            CreditLocal = 0,
                            Description = $"مدين - العميل {cust.NameAr}",
                            CreatedAt = entryDate
                        });
                        jv.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = 2,
                            AccountId = revAcc.Id,
                            SubLedgerType = SubLedgerType.None,
                            CurrencyId = egp.Id,
                            ExchangeRate = 1m,
                            DebitForeign = 0,
                            DebitLocal = 0,
                            CreditForeign = amount,
                            CreditLocal = amount,
                            Description = "إيراد مبيعات بضائع",
                            CreatedAt = entryDate
                        });
                        break;

                    case 2:
                        // Raw Material Purchase from Vendor on Credit
                        var vnd = vendors[i % vendors.Count];
                        int vndAccountId = vnd.FinancialAccountId ?? defaultApAccount.Id;
                        var invAcc = inventoryAccounts[i % inventoryAccounts.Count];
                        jv.Description = $"قيد إثبات توريد خامات من المورد {vnd.NameAr}";
                        jv.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = 1,
                            AccountId = invAcc.Id,
                            SubLedgerType = SubLedgerType.None,
                            CurrencyId = egp.Id,
                            ExchangeRate = 1m,
                            DebitForeign = amount,
                            DebitLocal = amount,
                            CreditForeign = 0,
                            CreditLocal = 0,
                            Description = $"إضافة خامات ومستلزمات إنتاج للمخزن",
                            CreatedAt = entryDate
                        });
                        jv.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = 2,
                            AccountId = vndAccountId,
                            SubLedgerType = SubLedgerType.Vendor,
                            SubLedgerId = vnd.Id,
                            CurrencyId = egp.Id,
                            ExchangeRate = 1m,
                            DebitForeign = 0,
                            DebitLocal = 0,
                            CreditForeign = amount,
                            CreditLocal = amount,
                            Description = $"دائن - استحقاق المورد {vnd.NameAr}",
                            CreatedAt = entryDate
                        });
                        break;

                    case 3:
                        // Operating Expenses Settlement
                        var exp = expenseAccounts[i % expenseAccounts.Count];
                        var payCash = cashAccounts[(i + 2) % cashAccounts.Count];
                        jv.Description = $"قيد إثبات وسداد مصروفات تشغيلية - {exp.NameAr}";
                        jv.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = 1,
                            AccountId = exp.Id,
                            SubLedgerType = SubLedgerType.None,
                            CurrencyId = egp.Id,
                            ExchangeRate = 1m,
                            DebitForeign = amount,
                            DebitLocal = amount,
                            CreditForeign = 0,
                            CreditLocal = 0,
                            Description = $"مدين - مصروف {exp.NameAr}",
                            CreatedAt = entryDate
                        });
                        jv.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = 2,
                            AccountId = payCash.Id,
                            SubLedgerType = SubLedgerType.Bank,
                            CurrencyId = egp.Id,
                            ExchangeRate = 1m,
                            DebitForeign = 0,
                            DebitLocal = 0,
                            CreditForeign = amount,
                            CreditLocal = amount,
                            Description = "دائن - سداد من الخزينة",
                            CreatedAt = entryDate
                        });
                        break;

                    case 4:
                        // Multi-line Compound Entry (Cost of Sales & Inventory allocation)
                        var cogsAcc = accounts.First(a => a.Code == "510101");
                        var stockAcc = inventoryAccounts[(i + 3) % inventoryAccounts.Count];
                        jv.Description = $"قيد إثبات تكلفة البضاعة المباعة للطلبيات المنفذة";
                        jv.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = 1,
                            AccountId = cogsAcc.Id,
                            SubLedgerType = SubLedgerType.None,
                            CurrencyId = egp.Id,
                            ExchangeRate = 1m,
                            DebitForeign = amount,
                            DebitLocal = amount,
                            CreditForeign = 0,
                            CreditLocal = 0,
                            Description = "مدين - تكلفة البضاعة المباعة",
                            CreatedAt = entryDate
                        });
                        jv.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = 2,
                            AccountId = stockAcc.Id,
                            SubLedgerType = SubLedgerType.None,
                            CurrencyId = egp.Id,
                            ExchangeRate = 1m,
                            DebitForeign = 0,
                            DebitLocal = 0,
                            CreditForeign = amount,
                            CreditLocal = amount,
                            Description = "دائن - خفض مخزون البضائع المسلمة",
                            CreatedAt = entryDate
                        });
                        break;
                }

                newJVs.Add(jv);
            }

            // Save in batches of 100
            for (int k = 0; k < newJVs.Count; k += 100)
            {
                var batch = newJVs.Skip(k).Take(100).ToList();
                context.JournalEntryHeaders.AddRange(batch);
                await context.SaveChangesAsync();
                Console.Write($"\r      -> Inserted {Math.Min(k + 100, newJVs.Count)}/{newJVs.Count} Journal Entries...");
            }
            Console.WriteLine();

            return await context.JournalEntryHeaders.CountAsync();
        }

        private static async Task PrintSummaryCountsAsync(ErpDbContext context)
        {
            Console.WriteLine("\n--- CURRENT SYSTEM STATE SUMMARY ---");
            var branchCount = await context.Branches.CountAsync(b => b.IsActive);
            var accountCount = await context.GLAccounts.CountAsync(a => a.IsActive);
            var customerCount = await context.Customers.CountAsync(c => c.IsActive);
            var vendorCount = await context.Vendors.CountAsync(v => v.IsActive);
            var jvCount = await context.JournalEntryHeaders.CountAsync();
            var rvCount = await context.VoucherHeaders.CountAsync(v => v.VoucherType == VoucherType.Receipt);
            var pvCount = await context.VoucherHeaders.CountAsync(v => v.VoucherType == VoucherType.Payment);
            var totalLines = await context.JournalEntryLines.CountAsync();

            Console.WriteLine($"• Active Branches:       {branchCount,6}");
            Console.WriteLine($"• Active GL Accounts:    {accountCount,6}");
            Console.WriteLine($"• Active Customers:      {customerCount,6}");
            Console.WriteLine($"• Active Vendors:        {vendorCount,6}");
            Console.WriteLine($"• Total Journal Entries: {jvCount,6} ({totalLines} total transaction lines)");
            Console.WriteLine($"• Receipt Vouchers:      {rvCount,6}");
            Console.WriteLine($"• Payment Vouchers:      {pvCount,6}");
            Console.WriteLine("------------------------------------\n");
        }
    }
}
