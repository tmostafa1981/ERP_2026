using System;

namespace ERP.LoadTesting.Models
{
    public enum ExecutionMode
    {
        HttpApi = 1,
        DirectSqlStoredProcedure = 2
    }

    public class LoadTestConfig
    {
        public ExecutionMode Mode { get; set; } = ExecutionMode.DirectSqlStoredProcedure;

        // HTTP API Configuration (تطبيق ERP.Web الفعلي)
        public string ApiBaseUrl { get; set; } = "https://localhost:7148";
        public string ApiEndpoint { get; set; } = "/Documents/Create";

        // SQL Server Configuration (قاعدة بيانات النظام الفعلية)
        public string SqlConnectionString { get; set; } = 
            "Server=tcp:SQL1002.site4now.net,1433;Database=db_ab95d6_sewtec2026;User Id=db_ab95d6_sewtec2026_admin;Password=Mostafa@1981$;Encrypt=True;TrustServerCertificate=True;Max Pool Size=200;Min Pool Size=20;Connect Timeout=30;";

        // NBomber Concurrency Load Settings
        public int ConcurrencyRate { get; set; } = 50;       // 50 requests per second / 50 concurrent copies
        public int DurationSeconds { get; set; } = 30;       // Test duration in seconds
        public int WarmUpDurationSeconds { get; set; } = 5;  // Warmup duration in seconds

        // Multi-Tenant / Multi-Branch Configuration
        public int MinBranchId { get; set; } = 1;
        public int MaxBranchId { get; set; } = 42;
    }
}
