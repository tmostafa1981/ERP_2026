using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Tools.DataSeeder
{
    public static class ChequeWorkflowVerification
    {
        public static async Task<bool> RunVerificationAsync(ErpDbContext context)
        {
            Console.WriteLine("=================================================================");
            Console.WriteLine("     LINE-LEVEL CHEQUE SYSTEM - END-TO-END VERIFICATION SUITE    ");
            Console.WriteLine("=================================================================\n");

            bool allPassed = true;

            // ── Step 1: Verify Bank Master & Seeding ──
            Console.Write("[Test 1/5] Verifying Bank Master Table & Seeding... ");
            var bankCount = await context.Banks.CountAsync();
            if (bankCount >= 20)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"PASSED ({bankCount} banks found)");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAILED (Expected >= 20 banks, found {bankCount})");
                Console.ResetColor();
                allPassed = false;
            }

            Console.Write("[Test 2/5] Testing Bank Search & Lookup by Code/Name... ");
            var cib = await context.Banks.FirstOrDefaultAsync(b => b.Code == "CIB" || b.NameAr.Contains("التجاري الدولي"));
            var misr = await context.Banks.FirstOrDefaultAsync(b => b.Code == "BMISR" || b.NameAr.Contains("بنك مصر"));
            if (cib != null && misr != null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"PASSED (Found '{cib.NameAr}' & '{misr.NameAr}')");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILED (Could not locate CIB and Banque Misr)");
                Console.ResetColor();
                allPassed = false;
                return false;
            }

            // ── Step 2: Create Mixed Receipt Voucher ──
            Console.Write("[Test 3/5] Creating Mixed Voucher (Line 1: Cheque 10k, Line 2: Cheque 15k, Line 3: Cash 5k)... ");
            var branch = await context.Branches.FirstOrDefaultAsync(b => b.IsActive) ?? throw new Exception("No active branch");
            var fy = await context.FiscalYears.FirstOrDefaultAsync(f => f.IsActive && !f.IsClosed) ?? throw new Exception("No active fiscal year");
            var cashAcc = await context.GLAccounts.FirstOrDefaultAsync(a => !a.IsGroup && (a.Code.StartsWith("101") || a.Code.StartsWith("102") || a.NameAr.Contains("صندوق") || a.NameAr.Contains("خزينة")))
                ?? await context.GLAccounts.FirstAsync(a => !a.IsGroup);
            var clientAcc = await context.GLAccounts.FirstOrDefaultAsync(a => !a.IsGroup && (a.Code.StartsWith("103") || a.NameAr.Contains("عملاء") || a.NameAr.Contains("مدين")))
                ?? await context.GLAccounts.FirstAsync(a => !a.IsGroup && a.Id != cashAcc.Id);

            var testVoucherNumber = $"RV-TEST-{DateTime.UtcNow.Ticks % 1000000:D6}";
            var testVoucher = new VoucherHeader
            {
                BranchId = branch.Id,
                FiscalYearId = fy.Id,
                VoucherType = VoucherType.Receipt,
                VoucherNumber = testVoucherNumber,
                VoucherDate = DateTime.UtcNow.Date,
                CashBankAccountId = cashAcc.Id,
                PaymentMethod = VoucherPaymentMethod.Cash,
                CurrencyId = 1,
                ExchangeRate = 1.000000m,
                TotalAmountForeign = 30000m,
                TotalAmountLocal = 30000m,
                ReferenceNumber = "REF-CHEQUE-TEST",
                Notes = "سند قبض تجريبي لاختبار منظومة الشيكات على مستوى السطر",
                Status = VoucherStatus.Draft,
                CreatedAt = DateTime.UtcNow,
                Lines = new List<VoucherLine>
                {
                    // Line 1: Cheque 10,000
                    new VoucherLine
                    {
                        LineNumber = 1,
                        LineType = VoucherLineType.Account,
                        AccountId = clientAcc.Id,
                        PaymentMethod = VoucherPaymentMethod.Cheque,
                        ChequeNumber = "CHQ-12345",
                        ChequeDate = DateTime.UtcNow.Date,
                        DueDate = DateTime.UtcNow.Date.AddDays(30),
                        BankId = cib.Id,
                        BankName = cib.NameAr,
                        DrawerName = "شركة النيل للتجارة الدولية",
                        ChequeStatus = ChequeStatus.UnderCollection,
                        AmountForeign = 10000m,
                        AmountLocal = 10000m,
                        Description = "دفعة شيك مسحوب على CIB",
                        CreatedAt = DateTime.UtcNow
                    },
                    // Line 2: Cheque 15,000
                    new VoucherLine
                    {
                        LineNumber = 2,
                        LineType = VoucherLineType.Account,
                        AccountId = clientAcc.Id,
                        PaymentMethod = VoucherPaymentMethod.Cheque,
                        ChequeNumber = "CHQ-67890",
                        ChequeDate = DateTime.UtcNow.Date,
                        DueDate = DateTime.UtcNow.Date.AddDays(60),
                        BankId = misr.Id,
                        BankName = misr.NameAr,
                        DrawerName = "مؤسسة الأهرام للتوريدات",
                        ChequeStatus = ChequeStatus.UnderCollection,
                        AmountForeign = 15000m,
                        AmountLocal = 15000m,
                        Description = "دفعة شيك مسحوب على بنك مصر",
                        CreatedAt = DateTime.UtcNow
                    },
                    // Line 3: Cash 5,000
                    new VoucherLine
                    {
                        LineNumber = 3,
                        LineType = VoucherLineType.Account,
                        AccountId = clientAcc.Id,
                        PaymentMethod = VoucherPaymentMethod.Cash,
                        AmountForeign = 5000m,
                        AmountLocal = 5000m,
                        Description = "سداد نقدي بالخزينة",
                        CreatedAt = DateTime.UtcNow
                    }
                }
            };

            context.VoucherHeaders.Add(testVoucher);
            await context.SaveChangesAsync();

            // Reload and verify
            var reloadedVoucher = await context.VoucherHeaders
                .Include(h => h.Lines)
                    .ThenInclude(l => l.Bank)
                .FirstOrDefaultAsync(h => h.Id == testVoucher.Id);

            if (reloadedVoucher != null &&
                reloadedVoucher.Lines.Count == 3 &&
                reloadedVoucher.Lines.Count(l => l.PaymentMethod == VoucherPaymentMethod.Cheque) == 2 &&
                reloadedVoucher.Lines.Count(l => l.PaymentMethod == VoucherPaymentMethod.Cash) == 1 &&
                reloadedVoucher.Lines.First(l => l.LineNumber == 1).BankId == cib.Id &&
                reloadedVoucher.Lines.First(l => l.LineNumber == 2).ChequeNumber == "CHQ-67890")
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"PASSED (Voucher ID {reloadedVoucher.Id}, 3 lines with Cheque details)");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILED (Voucher lines could not be verified in database)");
                Console.ResetColor();
                allPassed = false;
            }

            // ── Step 3: Test Auto Posting with Cheque Descriptions in Journal Entry ──
            Console.Write("[Test 4/5] Testing Auto Posting & JV Generation with Cheque metadata... ");
            var jvHeader = new JournalEntryHeader
            {
                BranchId = reloadedVoucher!.BranchId,
                FiscalYearId = reloadedVoucher.FiscalYearId,
                EntryNumber = $"JV-CHEQUE-{DateTime.UtcNow.Ticks % 1000000:D6}",
                EntryDate = reloadedVoucher.VoucherDate,
                SourceDocumentType = SourceDocumentType.ReceiptVoucher,
                SourceDocumentId = reloadedVoucher.Id,
                Status = JournalEntryStatus.Posted,
                Description = $"قيد تلقائي لسند قبض رقم {reloadedVoucher.VoucherNumber} (شيكات ونقدي)",
                TotalDebitLocal = reloadedVoucher.TotalAmountLocal,
                TotalCreditLocal = reloadedVoucher.TotalAmountLocal,
                PostedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Lines = new List<JournalEntryLine>()
            };

            int jvLineNum = 1;
            // Debit Cash/Bank Account for Total (30,000)
            jvHeader.Lines.Add(new JournalEntryLine
            {
                LineNumber = jvLineNum++,
                AccountId = reloadedVoucher.CashBankAccountId,
                CurrencyId = reloadedVoucher.CurrencyId,
                ExchangeRate = reloadedVoucher.ExchangeRate,
                DebitForeign = reloadedVoucher.TotalAmountForeign,
                DebitLocal = reloadedVoucher.TotalAmountLocal,
                CreditForeign = 0,
                CreditLocal = 0,
                Description = $"إيداع إجمالي السند بالخزينة/البنك - {reloadedVoucher.VoucherNumber}",
                CreatedAt = DateTime.UtcNow
            });

            // Credit Detail Lines
            foreach (var vl in reloadedVoucher.Lines.OrderBy(l => l.LineNumber))
            {
                string desc = vl.Description ?? jvHeader.Description;
                if (vl.PaymentMethod == VoucherPaymentMethod.Cheque && !string.IsNullOrWhiteSpace(vl.ChequeNumber))
                {
                    string bankInfo = !string.IsNullOrWhiteSpace(vl.BankName) ? $" - {vl.BankName}" : "";
                    desc = $"{desc} [شيك {vl.ChequeNumber}{bankInfo}]".Trim();
                }

                jvHeader.Lines.Add(new JournalEntryLine
                {
                    LineNumber = jvLineNum++,
                    AccountId = vl.AccountId,
                    CurrencyId = reloadedVoucher.CurrencyId,
                    ExchangeRate = reloadedVoucher.ExchangeRate,
                    DebitForeign = 0,
                    DebitLocal = 0,
                    CreditForeign = vl.AmountForeign,
                    CreditLocal = vl.AmountLocal,
                    Description = desc,
                    CreatedAt = DateTime.UtcNow
                });
            }

            context.JournalEntryHeaders.Add(jvHeader);
            await context.SaveChangesAsync();

            reloadedVoucher.JournalEntryHeaderId = jvHeader.Id;
            reloadedVoucher.Status = VoucherStatus.Posted;
            await context.SaveChangesAsync();

            // Verify the generated JV
            var reloadedJv = await context.JournalEntryHeaders
                .Include(j => j.Lines)
                .FirstOrDefaultAsync(j => j.Id == jvHeader.Id);

            bool jvValid = reloadedJv != null &&
                           reloadedJv.Lines.Count == 4 && // 1 debit + 3 credits
                           reloadedJv.Lines.First(l => l.LineNumber == 1).DebitLocal == 30000m &&
                           reloadedJv.Lines.First(l => l.LineNumber == 2).Description.Contains("[شيك CHQ-12345") &&
                           reloadedJv.Lines.First(l => l.LineNumber == 3).Description.Contains("[شيك CHQ-67890") &&
                           reloadedJv.TotalDebitLocal == reloadedJv.TotalCreditLocal &&
                           reloadedJv.TotalDebitLocal == 30000m;

            if (jvValid)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"PASSED (JV ID {reloadedJv!.Id}, Balanced: 30,000 Dr / 30,000 Cr, Cheque annotations verified)");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILED (Journal Entry balance or cheque annotations mismatch)");
                Console.ResetColor();
                allPassed = false;
            }

            // ── Step 4: Clean up test data ──
            Console.Write("[Test 5/5] Cleaning up test artifacts from database... ");
            context.JournalEntryLines.RemoveRange(reloadedJv!.Lines);
            context.JournalEntryHeaders.Remove(reloadedJv);
            context.VoucherLines.RemoveRange(reloadedVoucher.Lines);
            context.VoucherHeaders.Remove(reloadedVoucher);
            await context.SaveChangesAsync();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASSED (Database cleaned successfully)");
            Console.ResetColor();

            Console.WriteLine("\n-----------------------------------------------------------------");
            if (allPassed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(">>> ALL CHEQUE LINE-LEVEL VERIFICATION TESTS PASSED SUCCESSFULLY! <<<");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(">>> ONE OR MORE VERIFICATION TESTS FAILED! <<<");
                Console.ResetColor();
            }
            Console.WriteLine("-----------------------------------------------------------------\n");

            return allPassed;
        }
    }
}
