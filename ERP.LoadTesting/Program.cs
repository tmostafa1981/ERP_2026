using System;
using System.Text;
using System.Threading.Tasks;
using ERP.LoadTesting.Models;
using ERP.LoadTesting.Services;
using NBomber.Contracts;
using NBomber.CSharp;

namespace ERP.LoadTesting
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("================================================================================");
            Console.WriteLine("      ERP 2026 - Concurrency & Load Testing Suite (NBomber v5.x)               ");
            Console.WriteLine("   Multi-Tenant / 42 Branches Stress Testing (Deadlocks & Running Totals)      ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();

            var config = new LoadTestConfig();

            // Interactive or command-line selection
            Console.WriteLine("\nاختر وضع التشغيل (Select Execution Target Mode):");
            Console.WriteLine(" [1] استدعاء مباشر لإجراء SQL Server المخزن (Direct Stored Procedure - sp_CreateInvoiceWithItems)");
            Console.WriteLine(" [2] استدعاء واجهة برمجة التطبيقات (HTTP API - POST /api/invoices)");
            Console.Write("الاختيار الافتراضي [1]: ");

            string? choice = Console.ReadLine()?.Trim();
            if (choice == "2")
            {
                config.Mode = ExecutionMode.HttpApi;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"-> تم اختيار وضع: Web API Endpoint ({config.ApiBaseUrl}{config.ApiEndpoint})");
                Console.ResetColor();
            }
            else
            {
                config.Mode = ExecutionMode.DirectSqlStoredProcedure;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("-> تم اختيار وضع: Direct SQL Server Stored Procedure via ADO.NET");
                Console.ResetColor();
            }

            Console.WriteLine($"\nمواصفات الحمل (Load Profile):");
            Console.WriteLine($" - معدل التزامن (Concurrency): {config.ConcurrencyRate} مستخدم يرسلون طلباتهم بالتوازي كل ثانية.");
            Console.WriteLine($" - مدة الفحص (Duration): {config.DurationSeconds} ثانية.");
            Console.WriteLine($" - توزيع الفروع (Branch Range): من الفرع 1 إلى الفرع {config.MaxBranchId}.");
            Console.WriteLine("--------------------------------------------------------------------------------\n");

            // Prepare clients
            InvoiceApiClient? apiClient = null;
            InvoiceSqlRepository? sqlRepo = null;

            if (config.Mode == ExecutionMode.HttpApi)
            {
                apiClient = new InvoiceApiClient(config.ApiBaseUrl, config.ApiEndpoint);
            }
            else
            {
                sqlRepo = new InvoiceSqlRepository(config.SqlConnectionString);
            }

            // Define NBomber Scenario
            var scenario = Scenario.Create("erp_invoice_concurrency_scenario", async context =>
            {
                // 1. توليد بيانات ديناميكية عشوائية لكل طلب
                var payload = PayloadGenerator.GenerateRandomInvoice(config.MinBranchId, config.MaxBranchId);

                // 2. تنفيذ العملية حسب الوضع المختار
                if (config.Mode == ExecutionMode.HttpApi)
                {
                    var result = await apiClient!.SendInvoiceAsync(payload);
                    if (result.IsSuccess)
                    {
                        return Response.Ok(
                            statusCode: result.StatusCode.ToString(),
                            sizeBytes: (int)result.ResponseBytes
                        );
                    }
                    else
                    {
                        return Response.Fail(
                            statusCode: result.StatusCode.ToString(),
                            message: result.ErrorMessage ?? "API Failure"
                        );
                    }
                }
                else
                {
                    var result = await sqlRepo!.ExecuteCreateInvoiceSpAsync(payload);
                    if (result.IsSuccess)
                    {
                        return Response.Ok(
                            statusCode: result.StatusCode.ToString(),
                            message: $"Created Doc: {result.DocumentNumber}"
                        );
                    }
                    else
                    {
                        // يسجل تفاصيل الخطأ (سواء كان Deadlock 1205 أو Timeout 1222 أو Duplicate 2601)
                        return Response.Fail(
                            statusCode: result.StatusCode.ToString(),
                            message: result.ErrorMessage ?? "SQL Procedure Failure"
                        );
                    }
                }
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(config.WarmUpDurationSeconds))
            .WithLoadSimulations(
                // محاكاة 50 مستخدم بالتوازي يرسلون طلبات الحفظ
                Simulation.KeepConstant(copies: config.ConcurrencyRate, during: TimeSpan.FromSeconds(config.DurationSeconds))
            );

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("جارٍ بدء اختبار NBomber... يرجى الانتظار لحين اكتمال السيناريو وتوليد التقرير.");
            Console.ResetColor();

            // Run NBomber
            var stats = NBomberRunner
                .RegisterScenarios(scenario)
                .WithReportFolder("./nbomber_reports")
                .WithReportFormats(ReportFormat.Html, ReportFormat.Md, ReportFormat.Txt)
                .Run();

            apiClient?.Dispose();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n================================================================================");
            Console.WriteLine("                     اكتمل اختبار التحمل والتزامن بنجاح!                        ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();
            Console.WriteLine("تم حفظ تقارير NBomber التفصيلية (HTML/Markdown) في المجلد: ./nbomber_reports");
            Console.WriteLine("\nالخطوة التالية الموصى بها:");
            Console.WriteLine("نفذ نصوص التحقق T-SQL المرفقة في مجلد (Scripts/) للتحقق من:");
            Console.WriteLine(" 1. سلامة تسلسل أرقام الفواتير وعدم وجود تكرار أو فجوات (02_Validation_InvoiceSequencesAndGaps.sql)");
            Console.WriteLine(" 2. مطابقة أرصدة المخزون والحسابات التراكمية وعدم البيع بالسالب (03_Validation_InventoryConsistencyAndRunningTotals.sql)");
            Console.WriteLine(" 3. رصد ومراقبة الـ Deadlocks و Lock Escalation من سجلات SQL Server (04_Diagnostics_DeadlocksAndLockWaits.sql)");
            Console.WriteLine("================================================================================\n");
        }
    }
}
