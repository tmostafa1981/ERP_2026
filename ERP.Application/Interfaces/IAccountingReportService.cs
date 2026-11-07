using System.Threading.Tasks;
using ERP.Application.DTOs.Reports;

namespace ERP.Application.Interfaces
{
    public interface IAccountingReportService
    {
        Task<AccountStatementDto> GetAccountStatementAsync(ReportFilterDto filter);
        Task<SubLedgerStatementDto> GetCustomerStatementAsync(ReportFilterDto filter);
        Task<SubLedgerStatementDto> GetVendorStatementAsync(ReportFilterDto filter);
        Task<SubLedgerSummaryDto> GetSubLedgerSummaryAsync(SubLedgerSummaryFilterDto filter);
        Task<TrialBalanceDto> GetTrialBalanceAsync(ReportFilterDto filter);
        Task<GeneralLedgerDto> GetGeneralLedgerAsync(ReportFilterDto filter);
        Task<IncomeStatementDto> GetIncomeStatementAsync(ReportFilterDto filter);
        Task<BalanceSheetDto> GetBalanceSheetAsync(ReportFilterDto filter);
    }
}
