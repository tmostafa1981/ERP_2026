using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs.Reports;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class AccountingReportService : IAccountingReportService
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public AccountingReportService(ErpDbContext context, ICurrentUserContext currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        #region Helper: Base Queryable with Security & Filtering
        private IQueryable<JournalEntryLine> GetSecuredLinesQuery(ReportFilterDto filter)
        {
            var query = _context.JournalEntryLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(l => l.JournalEntryHeader)
                .Include(l => l.Currency)
                .Include(l => l.Account)
                .AsQueryable();

            // Branch filtering:
            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                if (!_currentUser.HasAllBranches && !_currentUser.CanAccessBranch(filter.BranchId.Value))
                {
                    query = query.Where(l => false);
                }
                else
                {
                    query = query.Where(l => l.JournalEntryHeader != null && l.JournalEntryHeader.BranchId == filter.BranchId.Value);
                }
            }
            else if (!_currentUser.HasAllBranches)
            {
                var allowedBranchIds = _context.UserBranches
                    .Where(ub => ub.UserId == _currentUser.UserId && ub.Branch.IsActive)
                    .Select(ub => ub.BranchId)
                    .ToList();

                if (allowedBranchIds.Any())
                {
                    query = query.Where(l => l.JournalEntryHeader != null && allowedBranchIds.Contains(l.JournalEntryHeader.BranchId));
                }
                else if (_currentUser.BranchId > 0)
                {
                    query = query.Where(l => l.JournalEntryHeader != null && l.JournalEntryHeader.BranchId == _currentUser.BranchId);
                }
            }

            // Fiscal Year filtering
            if (filter.FiscalYearId.HasValue && filter.FiscalYearId.Value > 0)
            {
                query = query.Where(l => l.JournalEntryHeader != null && l.JournalEntryHeader.FiscalYearId == filter.FiscalYearId.Value);
            }

            // Status filtering (If IncludeDrafts is false, only include Posted)
            if (!filter.IncludeDrafts)
            {
                query = query.Where(l => l.JournalEntryHeader != null && l.JournalEntryHeader.Status == JournalEntryStatus.Posted);
            }

            return query;
        }
        #endregion

        #region 1. Account Statement
        public async Task<AccountStatementDto> GetAccountStatementAsync(ReportFilterDto filter)
        {
            var result = new AccountStatementDto
            {
                AccountId = filter.AccountId ?? 0,
                DateFrom = filter.DateFrom,
                DateTo = filter.DateTo,
                Mode = filter.Mode
            };

            if (!filter.AccountId.HasValue || filter.AccountId.Value <= 0)
            {
                return result;
            }

            var account = await _context.GLAccounts
                .AsNoTracking()
                .Include(a => a.Currency)
                .FirstOrDefaultAsync(a => a.Id == filter.AccountId.Value);

            if (account != null)
            {
                result.AccountCode = account.Code;
                result.AccountNameAr = account.NameAr;
                result.AccountNameEn = account.NameEn;
                result.CurrencyCode = account.Currency?.Code ?? string.Empty;
                result.CurrencyName = account.Currency?.NameAr ?? string.Empty;
            }

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == filter.BranchId.Value);
                result.BranchName = branch?.NameAr ?? branch?.Name ?? string.Empty;
            }
            else
            {
                result.BranchName = _currentUser.HasAllBranches ? "كافة الفروع" : _currentUser.BranchName;
            }

            if (filter.FiscalYearId.HasValue && filter.FiscalYearId.Value > 0)
            {
                var fy = await _context.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == filter.FiscalYearId.Value);
                result.FiscalYearName = fy?.LocalizedName ?? string.Empty;
            }

            var baseLines = GetSecuredLinesQuery(filter)
                .Where(l => l.AccountId == filter.AccountId.Value);

            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                baseLines = baseLines.Where(l => l.CurrencyId == filter.CurrencyId.Value);
            }

            // Opening Balances (Prior to DateFrom)
            if (filter.DateFrom.HasValue)
            {
                var openingAgg = await baseLines
                    .Where(l => l.JournalEntryHeader!.EntryDate < filter.DateFrom.Value)
                    .GroupBy(l => 1)
                    .Select(g => new
                    {
                        DebitLocal = g.Sum(x => x.DebitLocal),
                        CreditLocal = g.Sum(x => x.CreditLocal),
                        DebitForeign = g.Sum(x => x.DebitForeign),
                        CreditForeign = g.Sum(x => x.CreditForeign)
                    })
                    .FirstOrDefaultAsync();

                if (openingAgg != null)
                {
                    result.OpeningDebitLocal = openingAgg.DebitLocal;
                    result.OpeningCreditLocal = openingAgg.CreditLocal;
                    result.OpeningBalanceLocal = openingAgg.DebitLocal - openingAgg.CreditLocal;

                    result.OpeningDebitForeign = openingAgg.DebitForeign;
                    result.OpeningCreditForeign = openingAgg.CreditForeign;
                    result.OpeningBalanceForeign = openingAgg.DebitForeign - openingAgg.CreditForeign;
                }
            }

            // Period Lines
            var periodQuery = baseLines;
            if (filter.DateFrom.HasValue)
            {
                periodQuery = periodQuery.Where(l => l.JournalEntryHeader!.EntryDate >= filter.DateFrom.Value);
            }
            if (filter.DateTo.HasValue)
            {
                periodQuery = periodQuery.Where(l => l.JournalEntryHeader!.EntryDate <= filter.DateTo.Value);
            }

            var rawLines = await periodQuery
                .OrderBy(l => l.JournalEntryHeader!.EntryDate)
                .ThenBy(l => l.JournalEntryHeader!.EntryNumber)
                .ThenBy(l => l.LineNumber)
                .Select(l => new
                {
                    l.Id,
                    l.JournalEntryHeaderId,
                    EntryNumber = l.JournalEntryHeader!.EntryNumber,
                    EntryDate = l.JournalEntryHeader.EntryDate,
                    Description = l.Description,
                    ReferenceNumber = l.JournalEntryHeader.ReferenceNumber,
                    CurrencyCode = l.Currency != null ? l.Currency.Code : string.Empty,
                    ExchangeRate = l.ExchangeRate,
                    DebitForeign = l.DebitForeign,
                    CreditForeign = l.CreditForeign,
                    DebitLocal = l.DebitLocal,
                    CreditLocal = l.CreditLocal,
                    SubLedgerType = l.SubLedgerType,
                    SubLedgerId = l.SubLedgerId
                })
                .ToListAsync();

            decimal runningLocal = result.OpeningBalanceLocal;
            decimal runningForeign = result.OpeningBalanceForeign;

            foreach (var item in rawLines)
            {
                runningLocal += (item.DebitLocal - item.CreditLocal);
                runningForeign += (item.DebitForeign - item.CreditForeign);

                var lineDto = new AccountStatementLineDto
                {
                    Id = item.Id,
                    JournalEntryHeaderId = item.JournalEntryHeaderId,
                    EntryNumber = item.EntryNumber,
                    EntryDate = item.EntryDate,
                    Description = item.Description,
                    ReferenceNumber = item.ReferenceNumber,
                    CurrencyCode = item.CurrencyCode,
                    ExchangeRate = item.ExchangeRate,
                    DebitForeign = item.DebitForeign,
                    CreditForeign = item.CreditForeign,
                    RunningBalanceForeign = runningForeign,
                    DebitLocal = item.DebitLocal,
                    CreditLocal = item.CreditLocal,
                    RunningBalanceLocal = runningLocal
                };

                result.Lines.Add(lineDto);

                result.TotalPeriodDebitLocal += item.DebitLocal;
                result.TotalPeriodCreditLocal += item.CreditLocal;
                result.TotalPeriodDebitForeign += item.DebitForeign;
                result.TotalPeriodCreditForeign += item.CreditForeign;
            }

            result.ClosingBalanceLocal = runningLocal;
            result.ClosingBalanceForeign = runningForeign;

            return result;
        }
        #endregion

        #region 3b. Sub-Ledger Summary (Customer & Vendor Balances Trial Balance)
        public async Task<SubLedgerSummaryDto> GetSubLedgerSummaryAsync(SubLedgerSummaryFilterDto filter)
        {
            var result = new SubLedgerSummaryDto
            {
                PartyType = filter.PartyType ?? "All",
                DateFrom = filter.DateFrom,
                DateTo = filter.DateTo,
                Mode = filter.Mode
            };

            // 1. Branch Name
            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == filter.BranchId.Value);
                result.BranchName = branch?.NameAr ?? branch?.Name ?? string.Empty;
            }
            else
            {
                result.BranchName = _currentUser.HasAllBranches ? "كافة الفروع" : _currentUser.BranchName;
            }

            // 2. Fiscal Year Name
            if (filter.FiscalYearId.HasValue && filter.FiscalYearId.Value > 0)
            {
                var fy = await _context.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == filter.FiscalYearId.Value);
                result.FiscalYearName = fy?.LocalizedName ?? string.Empty;
            }

            // 3. Currency Code
            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                var cur = await _context.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == filter.CurrencyId.Value);
                result.CurrencyCode = cur?.Code;
            }

            // 4. Query Parties (Customers and/or Vendors)
            bool includeCustomers = filter.PartyType == "All" || filter.PartyType.Equals("Customer", StringComparison.OrdinalIgnoreCase);
            bool includeVendors = filter.PartyType == "All" || filter.PartyType.Equals("Vendor", StringComparison.OrdinalIgnoreCase);

            var parties = new List<SubLedgerSummaryItemDto>();

            // Determine authorized branches for party filtering
            List<int>? allowedBranchIds = null;
            if (!_currentUser.HasAllBranches)
            {
                allowedBranchIds = await _context.UserBranches
                    .Where(ub => ub.UserId == _currentUser.UserId && ub.Branch.IsActive)
                    .Select(ub => ub.BranchId)
                    .ToListAsync();
                if (!allowedBranchIds.Any() && _currentUser.BranchId > 0)
                {
                    allowedBranchIds = new List<int> { _currentUser.BranchId };
                }
            }

            if (includeCustomers)
            {
                var custQuery = _context.Customers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(c => c.IsActive);

                if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
                {
                    custQuery = custQuery.Where(c => c.BranchId == filter.BranchId.Value);
                }
                else if (allowedBranchIds != null)
                {
                    custQuery = custQuery.Where(c => allowedBranchIds.Contains(c.BranchId));
                }

                if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
                {
                    custQuery = custQuery.Where(c => c.CurrencyId == filter.CurrencyId.Value);
                }

                if (filter.CustomerId.HasValue && filter.CustomerId.Value > 0)
                {
                    custQuery = custQuery.Where(c => c.Id == filter.CustomerId.Value);
                }

                if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
                {
                    var s = filter.SearchTerm.Trim().ToLower();
                    custQuery = custQuery.Where(c => c.Code.ToLower().Contains(s) || c.NameAr.ToLower().Contains(s) || (c.NameEn != null && c.NameEn.ToLower().Contains(s)));
                }

                var custList = await custQuery.OrderBy(c => c.Code).ToListAsync();
                foreach (var c in custList)
                {
                    parties.Add(new SubLedgerSummaryItemDto
                    {
                        PartyId = c.Id,
                        PartyType = "Customer",
                        Code = c.Code,
                        NameAr = c.NameAr,
                        NameEn = c.NameEn ?? string.Empty,
                        Phone = c.Phone ?? c.Mobile ?? string.Empty,
                        TaxNumber = c.TaxNumber ?? string.Empty,
                        BranchId = c.BranchId,
                        CurrencyId = c.CurrencyId,
                        FinancialAccountId = c.FinancialAccountId
                    });
                }
            }

            if (includeVendors)
            {
                var vendQuery = _context.Vendors
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(v => v.IsActive);

                if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
                {
                    vendQuery = vendQuery.Where(v => v.BranchId == filter.BranchId.Value);
                }
                else if (allowedBranchIds != null)
                {
                    vendQuery = vendQuery.Where(v => allowedBranchIds.Contains(v.BranchId));
                }

                if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
                {
                    vendQuery = vendQuery.Where(v => v.CurrencyId == filter.CurrencyId.Value);
                }

                if (filter.VendorId.HasValue && filter.VendorId.Value > 0)
                {
                    vendQuery = vendQuery.Where(v => v.Id == filter.VendorId.Value);
                }

                if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
                {
                    var s = filter.SearchTerm.Trim().ToLower();
                    vendQuery = vendQuery.Where(v => v.Code.ToLower().Contains(s) || v.NameAr.ToLower().Contains(s) || (v.NameEn != null && v.NameEn.ToLower().Contains(s)));
                }

                var vendList = await vendQuery.OrderBy(v => v.Code).ToListAsync();
                foreach (var v in vendList)
                {
                    parties.Add(new SubLedgerSummaryItemDto
                    {
                        PartyId = v.Id,
                        PartyType = "Vendor",
                        Code = v.Code,
                        NameAr = v.NameAr,
                        NameEn = v.NameEn ?? string.Empty,
                        Phone = v.Phone ?? v.Mobile ?? string.Empty,
                        TaxNumber = v.TaxNumber ?? string.Empty,
                        BranchId = v.BranchId,
                        CurrencyId = v.CurrencyId,
                        FinancialAccountId = v.FinancialAccountId
                    });
                }
            }

            if (!parties.Any())
            {
                return result;
            }

            // Lookup Dictionaries for Branch, Currency, and GL Account names
            var branchDict = await _context.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.LocalizedName);
            var curDict = await _context.Currencies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code);
            var glDict = await _context.GLAccounts.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(a => a.Id, a => new { a.Code, Name = a.LocalizedName });

            foreach (var p in parties)
            {
                if (p.BranchId.HasValue && branchDict.TryGetValue(p.BranchId.Value, out var bn))
                    p.BranchName = bn;
                if (p.CurrencyId.HasValue && curDict.TryGetValue(p.CurrencyId.Value, out var cc))
                    p.CurrencyCode = cc;
                if (p.FinancialAccountId.HasValue && glDict.TryGetValue(p.FinancialAccountId.Value, out var gla))
                {
                    p.ControlAccountCode = gla.Code;
                    p.ControlAccountName = gla.Name;
                }
            }

            // 5. Query Aggregated Transactions from JournalEntryLines
            var baseLines = GetSecuredLinesQuery(filter)
                .Where(l => (l.SubLedgerType == SubLedgerType.Customer || l.SubLedgerType == SubLedgerType.Vendor) && l.SubLedgerId.HasValue);

            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                baseLines = baseLines.Where(l => l.CurrencyId == filter.CurrencyId.Value);
            }

            // Opening Balances (< DateFrom)
            var openingDict = new Dictionary<(SubLedgerType, int), (decimal DebitLocal, decimal CreditLocal, decimal DebitForeign, decimal CreditForeign)>();
            if (filter.DateFrom.HasValue)
            {
                var openingGroups = await baseLines
                    .Where(l => l.JournalEntryHeader != null && l.JournalEntryHeader.EntryDate < filter.DateFrom.Value)
                    .GroupBy(l => new { l.SubLedgerType, SubLedgerId = l.SubLedgerId!.Value })
                    .Select(g => new
                    {
                        g.Key.SubLedgerType,
                        g.Key.SubLedgerId,
                        DebitLocal = g.Sum(x => x.DebitLocal),
                        CreditLocal = g.Sum(x => x.CreditLocal),
                        DebitForeign = g.Sum(x => x.DebitForeign),
                        CreditForeign = g.Sum(x => x.CreditForeign)
                    })
                    .ToListAsync();

                foreach (var g in openingGroups)
                {
                    openingDict[(g.SubLedgerType, g.SubLedgerId)] = (g.DebitLocal, g.CreditLocal, g.DebitForeign, g.CreditForeign);
                }
            }

            // Period Movements (>= DateFrom and <= DateTo)
            var periodQuery = baseLines;
            if (filter.DateFrom.HasValue)
            {
                periodQuery = periodQuery.Where(l => l.JournalEntryHeader != null && l.JournalEntryHeader.EntryDate >= filter.DateFrom.Value);
            }
            if (filter.DateTo.HasValue)
            {
                periodQuery = periodQuery.Where(l => l.JournalEntryHeader != null && l.JournalEntryHeader.EntryDate <= filter.DateTo.Value);
            }

            var periodGroups = await periodQuery
                .GroupBy(l => new { l.SubLedgerType, SubLedgerId = l.SubLedgerId!.Value })
                .Select(g => new
                {
                    g.Key.SubLedgerType,
                    g.Key.SubLedgerId,
                    DebitLocal = g.Sum(x => x.DebitLocal),
                    CreditLocal = g.Sum(x => x.CreditLocal),
                    DebitForeign = g.Sum(x => x.DebitForeign),
                    CreditForeign = g.Sum(x => x.CreditForeign)
                })
                .ToListAsync();

            var periodDict = periodGroups.ToDictionary(
                g => (g.SubLedgerType, g.SubLedgerId),
                g => (g.DebitLocal, g.CreditLocal, g.DebitForeign, g.CreditForeign)
            );

            // 6. Populate Item Balances
            foreach (var p in parties)
            {
                var slType = p.PartyType == "Customer" ? SubLedgerType.Customer : SubLedgerType.Vendor;
                var key = (slType, p.PartyId);

                if (openingDict.TryGetValue(key, out var op))
                {
                    p.OpeningDebitLocal = op.DebitLocal;
                    p.OpeningCreditLocal = op.CreditLocal;
                    p.OpeningDebitForeign = op.DebitForeign;
                    p.OpeningCreditForeign = op.CreditForeign;
                }

                if (periodDict.TryGetValue(key, out var pr))
                {
                    p.PeriodDebitLocal = pr.DebitLocal;
                    p.PeriodCreditLocal = pr.CreditLocal;
                    p.PeriodDebitForeign = pr.DebitForeign;
                    p.PeriodCreditForeign = pr.CreditForeign;
                }
            }

            // 7. Filtering Options
            var filteredItems = parties.AsEnumerable();
            if (filter.OnlyWithTransactions)
            {
                filteredItems = filteredItems.Where(p => 
                    p.OpeningDebitLocal != 0 || p.OpeningCreditLocal != 0 ||
                    p.PeriodDebitLocal != 0 || p.PeriodCreditLocal != 0);
            }

            if (filter.OnlyWithBalances)
            {
                filteredItems = filteredItems.Where(p => Math.Abs(p.ClosingBalanceLocal) >= 0.01m);
            }

            result.Items = filteredItems
                .OrderBy(p => p.PartyType)
                .ThenBy(p => p.Code)
                .ToList();

            // 8. Compute Statistics & Totals
            result.TotalCustomersCount = result.Items.Count(p => p.PartyType == "Customer");
            result.TotalVendorsCount = result.Items.Count(p => p.PartyType == "Vendor");

            // Receivables = Customer Net Debits (what customers owe us)
            result.TotalCustomerReceivablesLocal = result.Items
                .Where(p => p.PartyType == "Customer" && p.ClosingBalanceLocal > 0)
                .Sum(p => p.ClosingBalanceLocal);

            // Payables = Vendor Net Credits (what we owe vendors)
            result.TotalVendorPayablesLocal = result.Items
                .Where(p => p.PartyType == "Vendor" && p.ClosingBalanceLocal < 0)
                .Sum(p => Math.Abs(p.ClosingBalanceLocal));

            result.NetOverallBalanceLocal = result.TotalCustomerReceivablesLocal - result.TotalVendorPayablesLocal;

            // Local Totals
            result.TotalOpeningDebitLocal = result.Items.Sum(p => p.OpeningDebitLocal);
            result.TotalOpeningCreditLocal = result.Items.Sum(p => p.OpeningCreditLocal);
            result.TotalPeriodDebitLocal = result.Items.Sum(p => p.PeriodDebitLocal);
            result.TotalPeriodCreditLocal = result.Items.Sum(p => p.PeriodCreditLocal);
            result.TotalClosingDebitLocal = result.Items.Sum(p => p.ClosingDebitLocal);
            result.TotalClosingCreditLocal = result.Items.Sum(p => p.ClosingCreditLocal);

            // Foreign Totals (if single currency)
            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                result.TotalOpeningDebitForeign = result.Items.Sum(p => p.OpeningDebitForeign);
                result.TotalOpeningCreditForeign = result.Items.Sum(p => p.OpeningCreditForeign);
                result.TotalPeriodDebitForeign = result.Items.Sum(p => p.PeriodDebitForeign);
                result.TotalPeriodCreditForeign = result.Items.Sum(p => p.PeriodCreditForeign);
                result.TotalClosingDebitForeign = result.Items.Sum(p => p.ClosingDebitForeign);
                result.TotalClosingCreditForeign = result.Items.Sum(p => p.ClosingCreditForeign);
            }

            return result;
        }
        #endregion

        #region 2. Customer Statement
        public async Task<SubLedgerStatementDto> GetCustomerStatementAsync(ReportFilterDto filter)
        {
            var result = new SubLedgerStatementDto
            {
                SubLedgerId = filter.CustomerId ?? 0,
                SubLedgerType = "Customer",
                DateFrom = filter.DateFrom,
                DateTo = filter.DateTo,
                Mode = filter.Mode
            };

            if (!filter.CustomerId.HasValue || filter.CustomerId.Value <= 0)
            {
                return result;
            }

            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(c => c.Currency)
                .FirstOrDefaultAsync(c => c.Id == filter.CustomerId.Value);

            if (customer != null)
            {
                result.Code = customer.Code;
                result.NameAr = customer.NameAr;
                result.NameEn = customer.NameEn;
                result.Phone = customer.Phone ?? customer.Mobile ?? string.Empty;
                result.TaxNumber = customer.TaxNumber ?? string.Empty;
                result.CurrencyCode = customer.Currency?.Code ?? string.Empty;
            }

            return await PopulateSubLedgerStatementAsync(result, SubLedgerType.Customer, filter.CustomerId.Value, filter);
        }
        #endregion

        #region 3. Vendor Statement
        public async Task<SubLedgerStatementDto> GetVendorStatementAsync(ReportFilterDto filter)
        {
            var result = new SubLedgerStatementDto
            {
                SubLedgerId = filter.VendorId ?? 0,
                SubLedgerType = "Vendor",
                DateFrom = filter.DateFrom,
                DateTo = filter.DateTo,
                Mode = filter.Mode
            };

            if (!filter.VendorId.HasValue || filter.VendorId.Value <= 0)
            {
                return result;
            }

            var vendor = await _context.Vendors
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(v => v.Currency)
                .FirstOrDefaultAsync(v => v.Id == filter.VendorId.Value);

            if (vendor != null)
            {
                result.Code = vendor.Code;
                result.NameAr = vendor.NameAr;
                result.NameEn = vendor.NameEn;
                result.Phone = vendor.Phone ?? vendor.Mobile ?? string.Empty;
                result.TaxNumber = vendor.TaxNumber ?? string.Empty;
                result.CurrencyCode = vendor.Currency?.Code ?? string.Empty;
            }

            return await PopulateSubLedgerStatementAsync(result, SubLedgerType.Vendor, filter.VendorId.Value, filter);
        }

        private async Task<SubLedgerStatementDto> PopulateSubLedgerStatementAsync(
            SubLedgerStatementDto result, 
            SubLedgerType subLedgerType, 
            int subLedgerId, 
            ReportFilterDto filter)
        {
            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == filter.BranchId.Value);
                result.BranchName = branch?.NameAr ?? branch?.Name ?? string.Empty;
            }
            else
            {
                result.BranchName = _currentUser.HasAllBranches ? "كافة الفروع" : _currentUser.BranchName;
            }

            if (filter.FiscalYearId.HasValue && filter.FiscalYearId.Value > 0)
            {
                var fy = await _context.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == filter.FiscalYearId.Value);
                result.FiscalYearName = fy?.LocalizedName ?? string.Empty;
            }

            var baseLines = GetSecuredLinesQuery(filter)
                .Where(l => l.SubLedgerType == subLedgerType && l.SubLedgerId == subLedgerId);

            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                baseLines = baseLines.Where(l => l.CurrencyId == filter.CurrencyId.Value);
            }

            // Opening Balances
            if (filter.DateFrom.HasValue)
            {
                var openingAgg = await baseLines
                    .Where(l => l.JournalEntryHeader!.EntryDate < filter.DateFrom.Value)
                    .GroupBy(l => 1)
                    .Select(g => new
                    {
                        DebitLocal = g.Sum(x => x.DebitLocal),
                        CreditLocal = g.Sum(x => x.CreditLocal),
                        DebitForeign = g.Sum(x => x.DebitForeign),
                        CreditForeign = g.Sum(x => x.CreditForeign)
                    })
                    .FirstOrDefaultAsync();

                if (openingAgg != null)
                {
                    result.OpeningDebitLocal = openingAgg.DebitLocal;
                    result.OpeningCreditLocal = openingAgg.CreditLocal;
                    result.OpeningBalanceLocal = openingAgg.DebitLocal - openingAgg.CreditLocal;

                    result.OpeningDebitForeign = openingAgg.DebitForeign;
                    result.OpeningCreditForeign = openingAgg.CreditForeign;
                    result.OpeningBalanceForeign = openingAgg.DebitForeign - openingAgg.CreditForeign;
                }
            }

            // Period Lines
            var periodQuery = baseLines;
            if (filter.DateFrom.HasValue)
            {
                periodQuery = periodQuery.Where(l => l.JournalEntryHeader!.EntryDate >= filter.DateFrom.Value);
            }
            if (filter.DateTo.HasValue)
            {
                periodQuery = periodQuery.Where(l => l.JournalEntryHeader!.EntryDate <= filter.DateTo.Value);
            }

            var rawLines = await periodQuery
                .OrderBy(l => l.JournalEntryHeader!.EntryDate)
                .ThenBy(l => l.JournalEntryHeader!.EntryNumber)
                .ThenBy(l => l.LineNumber)
                .Select(l => new
                {
                    l.Id,
                    l.JournalEntryHeaderId,
                    EntryNumber = l.JournalEntryHeader!.EntryNumber,
                    EntryDate = l.JournalEntryHeader.EntryDate,
                    Description = l.Description,
                    ReferenceNumber = l.JournalEntryHeader.ReferenceNumber,
                    CurrencyCode = l.Currency != null ? l.Currency.Code : string.Empty,
                    ExchangeRate = l.ExchangeRate,
                    DebitForeign = l.DebitForeign,
                    CreditForeign = l.CreditForeign,
                    DebitLocal = l.DebitLocal,
                    CreditLocal = l.CreditLocal
                })
                .ToListAsync();

            decimal runningLocal = result.OpeningBalanceLocal;
            decimal runningForeign = result.OpeningBalanceForeign;

            foreach (var item in rawLines)
            {
                runningLocal += (item.DebitLocal - item.CreditLocal);
                runningForeign += (item.DebitForeign - item.CreditForeign);

                result.Lines.Add(new SubLedgerStatementLineDto
                {
                    Id = item.Id,
                    JournalEntryHeaderId = item.JournalEntryHeaderId,
                    EntryNumber = item.EntryNumber,
                    EntryDate = item.EntryDate,
                    Description = item.Description,
                    ReferenceNumber = item.ReferenceNumber,
                    CurrencyCode = item.CurrencyCode,
                    ExchangeRate = item.ExchangeRate,
                    DebitForeign = item.DebitForeign,
                    CreditForeign = item.CreditForeign,
                    BalanceForeign = runningForeign,
                    DebitLocal = item.DebitLocal,
                    CreditLocal = item.CreditLocal,
                    BalanceLocal = runningLocal
                });

                result.TotalPeriodDebitLocal += item.DebitLocal;
                result.TotalPeriodCreditLocal += item.CreditLocal;
                result.TotalPeriodDebitForeign += item.DebitForeign;
                result.TotalPeriodCreditForeign += item.CreditForeign;
            }

            result.ClosingBalanceLocal = runningLocal;
            result.ClosingBalanceForeign = runningForeign;

            return result;
        }
        #endregion

        #region 4. Trial Balance
        public async Task<TrialBalanceDto> GetTrialBalanceAsync(ReportFilterDto filter)
        {
            var result = new TrialBalanceDto
            {
                DateFrom = filter.DateFrom,
                DateTo = filter.DateTo,
                Mode = filter.Mode
            };

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == filter.BranchId.Value);
                result.BranchName = branch?.NameAr ?? branch?.Name ?? string.Empty;
            }
            else
            {
                result.BranchName = _currentUser.HasAllBranches ? "كافة الفروع" : _currentUser.BranchName;
            }

            if (filter.FiscalYearId.HasValue && filter.FiscalYearId.Value > 0)
            {
                var fy = await _context.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == filter.FiscalYearId.Value);
                result.FiscalYearName = fy?.LocalizedName ?? string.Empty;
            }

            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                var cur = await _context.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == filter.CurrencyId.Value);
                result.CurrencyCode = cur?.Code;
            }

            // 1. Fetch All Active Accounts Hierarchy (we need all accounts to perform bottom-up roll-up)
            var allAccounts = await _context.GLAccounts
                .AsNoTracking()
                .Include(a => a.Parent)
                .Include(a => a.Currency)
                .Where(a => a.IsActive)
                .OrderBy(a => a.Code)
                .ToListAsync();

            // 2. Base Lines Query for aggregates
            var baseLines = GetSecuredLinesQuery(filter);

            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                baseLines = baseLines.Where(l => l.CurrencyId == filter.CurrencyId.Value);
            }

            // 3. Opening Movements (Before DateFrom) for direct lines
            Dictionary<int, (decimal DebitLocal, decimal CreditLocal, decimal DebitForeign, decimal CreditForeign)> openingDict = new();
            if (filter.DateFrom.HasValue)
            {
                var openingAgg = await baseLines
                    .Where(l => l.JournalEntryHeader!.EntryDate < filter.DateFrom.Value)
                    .GroupBy(l => l.AccountId)
                    .Select(g => new
                    {
                        AccountId = g.Key,
                        DebitLocal = g.Sum(x => x.DebitLocal),
                        CreditLocal = g.Sum(x => x.CreditLocal),
                        DebitForeign = g.Sum(x => x.DebitForeign),
                        CreditForeign = g.Sum(x => x.CreditForeign)
                    })
                    .ToListAsync();

                openingDict = openingAgg.ToDictionary(
                    x => x.AccountId, 
                    x => (x.DebitLocal, x.CreditLocal, x.DebitForeign, x.CreditForeign)
                );
            }

            // 4. Period Movements (DateFrom <= Date <= DateTo) for direct lines
            var periodLines = baseLines;
            if (filter.DateFrom.HasValue)
            {
                periodLines = periodLines.Where(l => l.JournalEntryHeader!.EntryDate >= filter.DateFrom.Value);
            }
            if (filter.DateTo.HasValue)
            {
                periodLines = periodLines.Where(l => l.JournalEntryHeader!.EntryDate <= filter.DateTo.Value);
            }

            var periodAgg = await periodLines
                .GroupBy(l => l.AccountId)
                .Select(g => new
                {
                    AccountId = g.Key,
                    DebitLocal = g.Sum(x => x.DebitLocal),
                    CreditLocal = g.Sum(x => x.CreditLocal),
                    DebitForeign = g.Sum(x => x.DebitForeign),
                    CreditForeign = g.Sum(x => x.CreditForeign)
                })
                .ToListAsync();

            var periodDict = periodAgg.ToDictionary(
                x => x.AccountId,
                x => (x.DebitLocal, x.CreditLocal, x.DebitForeign, x.CreditForeign)
            );

            // 5. Build Initial TrialBalanceItemDto for ALL accounts (leaf direct balances)
            var itemsDict = new Dictionary<int, TrialBalanceItemDto>();
            foreach (var acc in allAccounts)
            {
                openingDict.TryGetValue(acc.Id, out var openVal);
                periodDict.TryGetValue(acc.Id, out var perVal);

                decimal netOpenLocal = openVal.DebitLocal - openVal.CreditLocal;
                decimal openDebLocal = netOpenLocal > 0 ? netOpenLocal : 0;
                decimal openCredLocal = netOpenLocal < 0 ? -netOpenLocal : 0;

                decimal netOpenForeign = openVal.DebitForeign - openVal.CreditForeign;
                decimal openDebForeign = netOpenForeign > 0 ? netOpenForeign : 0;
                decimal openCredForeign = netOpenForeign < 0 ? -netOpenForeign : 0;

                decimal perDebLocal = perVal.DebitLocal;
                decimal perCredLocal = perVal.CreditLocal;
                decimal perDebForeign = perVal.DebitForeign;
                decimal perCredForeign = perVal.CreditForeign;

                decimal netCloseLocal = netOpenLocal + (perDebLocal - perCredLocal);
                decimal closeDebLocal = netCloseLocal > 0 ? netCloseLocal : 0;
                decimal closeCredLocal = netCloseLocal < 0 ? -netCloseLocal : 0;

                decimal netCloseForeign = netOpenForeign + (perDebForeign - perCredForeign);
                decimal closeDebForeign = netCloseForeign > 0 ? netCloseForeign : 0;
                decimal closeCredForeign = netCloseForeign < 0 ? -netCloseForeign : 0;

                var item = new TrialBalanceItemDto
                {
                    AccountId = acc.Id,
                    AccountCode = acc.Code,
                    AccountNameAr = acc.NameAr,
                    AccountNameEn = acc.NameEn,
                    Level = acc.Level,
                    IsGroup = acc.IsGroup,
                    ParentCode = acc.Parent?.Code,
                    AccountNature = acc.AccountNature == AccountNature.Debit ? "مدين" : "دائن",
                    CurrencyCode = acc.Currency?.Code,

                    OpeningDebitLocal = openDebLocal,
                    OpeningCreditLocal = openCredLocal,
                    PeriodDebitLocal = perDebLocal,
                    PeriodCreditLocal = perCredLocal,
                    ClosingDebitLocal = closeDebLocal,
                    ClosingCreditLocal = closeCredLocal,
                    NetClosingLocal = netCloseLocal,

                    OpeningDebitForeign = openDebForeign,
                    OpeningCreditForeign = openCredForeign,
                    PeriodDebitForeign = perDebForeign,
                    PeriodCreditForeign = perCredForeign,
                    ClosingDebitForeign = closeDebForeign,
                    ClosingCreditForeign = closeCredForeign,
                    NetClosingForeign = netCloseForeign
                };

                itemsDict[acc.Id] = item;
            }

            // 6. Calculate Grand Totals across all leaf accounts (to ensure overall balance integrity regardless of level filter)
            foreach (var acc in allAccounts.Where(a => !a.IsGroup))
            {
                if (itemsDict.TryGetValue(acc.Id, out var leafItem))
                {
                    result.TotalOpeningDebitLocal += leafItem.OpeningDebitLocal;
                    result.TotalOpeningCreditLocal += leafItem.OpeningCreditLocal;
                    result.TotalPeriodDebitLocal += leafItem.PeriodDebitLocal;
                    result.TotalPeriodCreditLocal += leafItem.PeriodCreditLocal;
                    result.TotalClosingDebitLocal += leafItem.ClosingDebitLocal;
                    result.TotalClosingCreditLocal += leafItem.ClosingCreditLocal;

                    result.TotalOpeningDebitForeign += leafItem.OpeningDebitForeign;
                    result.TotalOpeningCreditForeign += leafItem.OpeningCreditForeign;
                    result.TotalPeriodDebitForeign += leafItem.PeriodDebitForeign;
                    result.TotalPeriodCreditForeign += leafItem.PeriodCreditForeign;
                    result.TotalClosingDebitForeign += leafItem.ClosingDebitForeign;
                    result.TotalClosingCreditForeign += leafItem.ClosingCreditForeign;
                }
            }

            // 7. Bottom-Up Hierarchical Roll-Up:
            // Processing accounts in descending order of Level guarantees that leaves roll into intermediate groups,
            // and intermediate groups roll their accumulated totals into parent and root groups.
            var accountsByLevelDesc = allAccounts
                .OrderByDescending(a => a.Level)
                .ToList();

            foreach (var acc in accountsByLevelDesc)
            {
                if (acc.ParentId.HasValue && itemsDict.TryGetValue(acc.ParentId.Value, out var parentItem))
                {
                    var currentItem = itemsDict[acc.Id];

                    parentItem.OpeningDebitLocal += currentItem.OpeningDebitLocal;
                    parentItem.OpeningCreditLocal += currentItem.OpeningCreditLocal;
                    parentItem.PeriodDebitLocal += currentItem.PeriodDebitLocal;
                    parentItem.PeriodCreditLocal += currentItem.PeriodCreditLocal;
                    parentItem.ClosingDebitLocal += currentItem.ClosingDebitLocal;
                    parentItem.ClosingCreditLocal += currentItem.ClosingCreditLocal;
                    parentItem.NetClosingLocal += currentItem.NetClosingLocal;

                    parentItem.OpeningDebitForeign += currentItem.OpeningDebitForeign;
                    parentItem.OpeningCreditForeign += currentItem.OpeningCreditForeign;
                    parentItem.PeriodDebitForeign += currentItem.PeriodDebitForeign;
                    parentItem.PeriodCreditForeign += currentItem.PeriodCreditForeign;
                    parentItem.ClosingDebitForeign += currentItem.ClosingDebitForeign;
                    parentItem.ClosingCreditForeign += currentItem.ClosingCreditForeign;
                    parentItem.NetClosingForeign += currentItem.NetClosingForeign;
                }
            }

            // 8. Populate result items based on the requested display filters (Level, OnlyWithTransactions)
            foreach (var acc in allAccounts)
            {
                if (filter.AccountLevel.HasValue && filter.AccountLevel.Value > 0 && acc.Level > filter.AccountLevel.Value)
                {
                    continue;
                }

                var item = itemsDict[acc.Id];

                bool hasActivity = item.OpeningDebitLocal != 0 || item.OpeningCreditLocal != 0 ||
                                   item.PeriodDebitLocal != 0 || item.PeriodCreditLocal != 0 ||
                                   item.ClosingDebitLocal != 0 || item.ClosingCreditLocal != 0 ||
                                   item.PeriodDebitForeign != 0 || item.PeriodCreditForeign != 0;

                if (filter.OnlyWithTransactions && !hasActivity)
                {
                    continue;
                }

                result.Items.Add(item);
            }

            return result;
        }
        #endregion

        #region 5. General Ledger
        public async Task<GeneralLedgerDto> GetGeneralLedgerAsync(ReportFilterDto filter)
        {
            var result = new GeneralLedgerDto
            {
                DateFrom = filter.DateFrom,
                DateTo = filter.DateTo,
                Mode = filter.Mode
            };

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == filter.BranchId.Value);
                result.BranchName = branch?.NameAr ?? branch?.Name ?? string.Empty;
            }
            else
            {
                result.BranchName = _currentUser.HasAllBranches ? "كافة الفروع" : _currentUser.BranchName;
            }

            if (filter.FiscalYearId.HasValue && filter.FiscalYearId.Value > 0)
            {
                var fy = await _context.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == filter.FiscalYearId.Value);
                result.FiscalYearName = fy?.LocalizedName ?? string.Empty;
            }

            // 3. Base Query for Journal Entry Lines with branch & fiscal year security applied
            var baseLines = GetSecuredLinesQuery(filter);

            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                baseLines = baseLines.Where(l => l.CurrencyId == filter.CurrencyId.Value);
            }

            // If a specific account is requested, filter to that account
            if (filter.AccountId.HasValue && filter.AccountId.Value > 0)
            {
                baseLines = baseLines.Where(l => l.AccountId == filter.AccountId.Value);
            }

            // 4. Batch Query Opening Balances (Prior to DateFrom)
            var openingBalances = new Dictionary<int, (decimal DebitLocal, decimal CreditLocal, decimal DebitForeign, decimal CreditForeign)>();
            if (filter.DateFrom.HasValue)
            {
                var openingAgg = await baseLines
                    .Where(l => l.JournalEntryHeader!.EntryDate < filter.DateFrom.Value)
                    .GroupBy(l => l.AccountId)
                    .Select(g => new
                    {
                        AccountId = g.Key,
                        DebitLocal = g.Sum(x => x.DebitLocal),
                        CreditLocal = g.Sum(x => x.CreditLocal),
                        DebitForeign = g.Sum(x => x.DebitForeign),
                        CreditForeign = g.Sum(x => x.CreditForeign)
                    })
                    .ToListAsync();

                foreach (var item in openingAgg)
                {
                    openingBalances[item.AccountId] = (item.DebitLocal, item.CreditLocal, item.DebitForeign, item.CreditForeign);
                }
            }

            // 5. Batch Query Period Lines (DateFrom <= EntryDate <= DateTo)
            var periodQuery = baseLines;
            if (filter.DateFrom.HasValue)
            {
                periodQuery = periodQuery.Where(l => l.JournalEntryHeader!.EntryDate >= filter.DateFrom.Value);
            }
            if (filter.DateTo.HasValue)
            {
                periodQuery = periodQuery.Where(l => l.JournalEntryHeader!.EntryDate <= filter.DateTo.Value);
            }

            var periodLines = await periodQuery
                .OrderBy(l => l.JournalEntryHeader!.EntryDate)
                .ThenBy(l => l.Id)
                .Select(l => new
                {
                    l.AccountId,
                    l.Id,
                    l.JournalEntryHeaderId,
                    EntryNumber = l.JournalEntryHeader!.EntryNumber,
                    EntryDate = l.JournalEntryHeader!.EntryDate,
                    l.Description,
                    ReferenceNumber = l.JournalEntryHeader!.ReferenceNumber,
                    CurrencyCode = l.Currency != null ? l.Currency.Code : string.Empty,
                    l.ExchangeRate,
                    l.DebitForeign,
                    l.CreditForeign,
                    l.DebitLocal,
                    l.CreditLocal
                })
                .ToListAsync();

            // Group period lines by AccountId
            var linesByAccount = periodLines
                .GroupBy(l => l.AccountId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 6. Determine which accounts we need to fetch
            HashSet<int> relevantAccountIds;
            if (filter.AccountId.HasValue && filter.AccountId.Value > 0)
            {
                relevantAccountIds = new HashSet<int> { filter.AccountId.Value };
            }
            else if (filter.OnlyWithTransactions)
            {
                relevantAccountIds = new HashSet<int>(openingBalances.Keys);
                relevantAccountIds.UnionWith(linesByAccount.Keys);
            }
            else
            {
                var allActiveIds = await _context.GLAccounts
                    .AsNoTracking()
                    .Where(a => a.IsActive && !a.IsGroup)
                    .Select(a => a.Id)
                    .ToListAsync();
                relevantAccountIds = new HashSet<int>(allActiveIds);
            }

            if (!relevantAccountIds.Any())
            {
                return result;
            }

            // 7. Fetch account metadata in ONE query
            var accounts = await _context.GLAccounts
                .AsNoTracking()
                .Include(a => a.Currency)
                .Where(a => relevantAccountIds.Contains(a.Id))
                .OrderBy(a => a.Code)
                .ToListAsync();

            // 8. Build General Ledger groups in-memory
            foreach (var acc in accounts)
            {
                decimal opDebitLocal = 0m, opCreditLocal = 0m, opDebitForeign = 0m, opCreditForeign = 0m;
                if (openingBalances.TryGetValue(acc.Id, out var op))
                {
                    opDebitLocal = op.DebitLocal;
                    opCreditLocal = op.CreditLocal;
                    opDebitForeign = op.DebitForeign;
                    opCreditForeign = op.CreditForeign;
                }

                decimal opBalLocal = opDebitLocal - opCreditLocal;
                decimal opBalForeign = opDebitForeign - opCreditForeign;

                var accLines = linesByAccount.TryGetValue(acc.Id, out var rawList) ? rawList : new();

                if (filter.OnlyWithTransactions && accLines.Count == 0 && opBalLocal == 0 && opBalForeign == 0)
                {
                    continue;
                }

                var statementLines = new List<AccountStatementLineDto>();
                decimal runningLocal = opBalLocal;
                decimal runningForeign = opBalForeign;
                decimal periodDebitLocal = 0m, periodCreditLocal = 0m;
                decimal periodDebitForeign = 0m, periodCreditForeign = 0m;

                foreach (var line in accLines)
                {
                    runningLocal += (line.DebitLocal - line.CreditLocal);
                    runningForeign += (line.DebitForeign - line.CreditForeign);

                    statementLines.Add(new AccountStatementLineDto
                    {
                        Id = line.Id,
                        JournalEntryHeaderId = line.JournalEntryHeaderId,
                        EntryNumber = line.EntryNumber,
                        EntryDate = line.EntryDate,
                        Description = line.Description,
                        ReferenceNumber = line.ReferenceNumber,
                        CurrencyCode = line.CurrencyCode,
                        ExchangeRate = line.ExchangeRate,
                        DebitForeign = line.DebitForeign,
                        CreditForeign = line.CreditForeign,
                        RunningBalanceForeign = runningForeign,
                        DebitLocal = line.DebitLocal,
                        CreditLocal = line.CreditLocal,
                        RunningBalanceLocal = runningLocal
                    });

                    periodDebitLocal += line.DebitLocal;
                    periodCreditLocal += line.CreditLocal;
                    periodDebitForeign += line.DebitForeign;
                    periodCreditForeign += line.CreditForeign;
                }

                var group = new GeneralLedgerAccountGroupDto
                {
                    AccountId = acc.Id,
                    AccountCode = acc.Code,
                    AccountNameAr = acc.NameAr,
                    AccountNameEn = acc.NameEn,
                    CurrencyCode = acc.Currency?.Code ?? string.Empty,
                    OpeningBalanceForeign = opBalForeign,
                    OpeningBalanceLocal = opBalLocal,
                    Lines = statementLines,
                    TotalDebitForeign = periodDebitForeign,
                    TotalCreditForeign = periodCreditForeign,
                    ClosingBalanceForeign = runningForeign,
                    TotalDebitLocal = periodDebitLocal,
                    TotalCreditLocal = periodCreditLocal,
                    ClosingBalanceLocal = runningLocal
                };

                result.Accounts.Add(group);

                result.GrandTotalDebitForeign += periodDebitForeign;
                result.GrandTotalCreditForeign += periodCreditForeign;
                result.GrandTotalDebitLocal += periodDebitLocal;
                result.GrandTotalCreditLocal += periodCreditLocal;
            }

            return result;
        }
        #endregion

        #region 6. Income Statement (قائمة الدخل)
        public async Task<IncomeStatementDto> GetIncomeStatementAsync(ReportFilterDto filter)
        {
            var result = new IncomeStatementDto
            {
                DateFrom = filter.DateFrom,
                DateTo = filter.DateTo,
                Mode = filter.Mode
            };

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == filter.BranchId.Value);
                result.BranchName = branch?.NameAr ?? branch?.Name ?? string.Empty;
            }
            else
            {
                result.BranchName = _currentUser.HasAllBranches ? "كافة الفروع" : _currentUser.BranchName;
            }

            if (filter.FiscalYearId.HasValue && filter.FiscalYearId.Value > 0)
            {
                var fy = await _context.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == filter.FiscalYearId.Value);
                result.FiscalYearName = fy?.LocalizedName ?? string.Empty;
            }

            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                var cur = await _context.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == filter.CurrencyId.Value);
                result.CurrencyCode = cur?.Code;
            }

            // Get P&L accounts (Revenues & Expenses)
            var pnlAccounts = await _context.GLAccounts
                .AsNoTracking()
                .Include(a => a.Parent)
                .Include(a => a.Currency)
                .Where(a => a.IsActive && !a.IsGroup && 
                            (a.AccountCategory == AccountCategory.Revenues || a.AccountCategory == AccountCategory.Expenses ||
                             a.Code.StartsWith("4") || a.Code.StartsWith("5")))
                .OrderBy(a => a.Code)
                .ToListAsync();

            var baseLines = GetSecuredLinesQuery(filter);
            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                baseLines = baseLines.Where(l => l.CurrencyId == filter.CurrencyId.Value);
            }
            if (filter.DateFrom.HasValue)
            {
                baseLines = baseLines.Where(l => l.JournalEntryHeader!.EntryDate >= filter.DateFrom.Value);
            }
            if (filter.DateTo.HasValue)
            {
                baseLines = baseLines.Where(l => l.JournalEntryHeader!.EntryDate <= filter.DateTo.Value);
            }

            var lineAggregates = await baseLines
                .GroupBy(l => l.AccountId)
                .Select(g => new
                {
                    AccountId = g.Key,
                    DebitLocal = g.Sum(x => x.DebitLocal),
                    CreditLocal = g.Sum(x => x.CreditLocal),
                    DebitForeign = g.Sum(x => x.DebitForeign),
                    CreditForeign = g.Sum(x => x.CreditForeign)
                })
                .ToDictionaryAsync(x => x.AccountId);

            // Populate items
            foreach (var acc in pnlAccounts)
            {
                lineAggregates.TryGetValue(acc.Id, out var agg);
                decimal debLocal = agg?.DebitLocal ?? 0m;
                decimal credLocal = agg?.CreditLocal ?? 0m;
                decimal debForeign = agg?.DebitForeign ?? 0m;
                decimal credForeign = agg?.CreditForeign ?? 0m;

                bool isRevenue = acc.AccountCategory == AccountCategory.Revenues || acc.Code.StartsWith("4");
                decimal netAmountLocal = isRevenue ? (credLocal - debLocal) : (debLocal - credLocal);
                decimal netAmountForeign = isRevenue ? (credForeign - debForeign) : (debForeign - credForeign);

                if (filter.OnlyWithTransactions && netAmountLocal == 0 && netAmountForeign == 0)
                {
                    continue;
                }

                var item = new FinancialStatementItemDto
                {
                    AccountId = acc.Id,
                    AccountCode = acc.Code,
                    AccountNameAr = acc.NameAr,
                    AccountNameEn = acc.NameEn,
                    Level = acc.Level,
                    IsGroup = acc.IsGroup,
                    ParentCode = acc.Parent?.Code,
                    AmountLocal = netAmountLocal,
                    AmountForeign = netAmountForeign
                };

                if (isRevenue)
                {
                    if (acc.Code.StartsWith("41"))
                    {
                        item.Section = "إيرادات المبيعات والتشغيل";
                        result.TotalOperatingRevenues += netAmountLocal;
                    }
                    else
                    {
                        item.Section = "إيرادات أخرى";
                        result.TotalOtherRevenues += netAmountLocal;
                    }
                    result.RevenueItems.Add(item);
                }
                else
                {
                    if (acc.Code.StartsWith("51"))
                    {
                        item.Section = "تكلفة المبيعات والإنتاج";
                        result.TotalCostOfSales += netAmountLocal;
                        result.CostOfSalesItems.Add(item);
                    }
                    else if (acc.Code.StartsWith("52"))
                    {
                        item.Section = "مصروفات عمومية وإدارية";
                        result.TotalGeneralAndAdminExpenses += netAmountLocal;
                        result.OperatingExpenseItems.Add(item);
                    }
                    else if (acc.Code.StartsWith("53"))
                    {
                        item.Section = "مصروفات بيعية وتسويقية";
                        result.TotalSellingAndMarketingExpenses += netAmountLocal;
                        result.OperatingExpenseItems.Add(item);
                    }
                    else if (acc.Code.StartsWith("54"))
                    {
                        item.Section = "مصروفات تمويلية وبنكية";
                        result.TotalFinancingExpenses += netAmountLocal;
                        result.OtherExpenseItems.Add(item);
                    }
                    else
                    {
                        item.Section = "مصروفات وأعباء أخرى";
                        result.TotalOtherExpenses += netAmountLocal;
                        result.OtherExpenseItems.Add(item);
                    }
                }
            }

            // Calculate percentage of revenue for all items
            if (result.TotalRevenues > 0)
            {
                foreach (var i in result.RevenueItems) i.PercentageOfRevenue = (i.AmountLocal / result.TotalRevenues) * 100m;
                foreach (var i in result.CostOfSalesItems) i.PercentageOfRevenue = (i.AmountLocal / result.TotalRevenues) * 100m;
                foreach (var i in result.OperatingExpenseItems) i.PercentageOfRevenue = (i.AmountLocal / result.TotalRevenues) * 100m;
                foreach (var i in result.OtherExpenseItems) i.PercentageOfRevenue = (i.AmountLocal / result.TotalRevenues) * 100m;
            }

            return result;
        }
        #endregion

        #region 7. Balance Sheet (قائمة المركز المالي)
        public async Task<BalanceSheetDto> GetBalanceSheetAsync(ReportFilterDto filter)
        {
            var asOfDate = filter.DateTo ?? DateTime.UtcNow.Date;
            var result = new BalanceSheetDto
            {
                AsOfDate = asOfDate,
                Mode = filter.Mode
            };

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == filter.BranchId.Value);
                result.BranchName = branch?.NameAr ?? branch?.Name ?? string.Empty;
            }
            else
            {
                result.BranchName = _currentUser.HasAllBranches ? "كافة الفروع" : _currentUser.BranchName;
            }

            if (filter.FiscalYearId.HasValue && filter.FiscalYearId.Value > 0)
            {
                var fy = await _context.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == filter.FiscalYearId.Value);
                result.FiscalYearName = fy?.LocalizedName ?? string.Empty;
            }

            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                var cur = await _context.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == filter.CurrencyId.Value);
                result.CurrencyCode = cur?.Code;
            }

            // Get All Active Leaf Accounts
            var allAccounts = await _context.GLAccounts
                .AsNoTracking()
                .Include(a => a.Parent)
                .Include(a => a.Currency)
                .Where(a => a.IsActive && !a.IsGroup)
                .OrderBy(a => a.Code)
                .ToListAsync();

            // Query movements up to asOfDate
            var baseLines = GetSecuredLinesQuery(filter);
            if (filter.CurrencyId.HasValue && filter.CurrencyId.Value > 0)
            {
                baseLines = baseLines.Where(l => l.CurrencyId == filter.CurrencyId.Value);
            }
            baseLines = baseLines.Where(l => l.JournalEntryHeader!.EntryDate <= asOfDate);

            var lineAggregates = await baseLines
                .GroupBy(l => l.AccountId)
                .Select(g => new
                {
                    AccountId = g.Key,
                    DebitLocal = g.Sum(x => x.DebitLocal),
                    CreditLocal = g.Sum(x => x.CreditLocal),
                    DebitForeign = g.Sum(x => x.DebitForeign),
                    CreditForeign = g.Sum(x => x.CreditForeign)
                })
                .ToDictionaryAsync(x => x.AccountId);

            decimal totalRevenuesLocal = 0m;
            decimal totalExpensesLocal = 0m;

            foreach (var acc in allAccounts)
            {
                lineAggregates.TryGetValue(acc.Id, out var agg);
                decimal debLocal = agg?.DebitLocal ?? 0m;
                decimal credLocal = agg?.CreditLocal ?? 0m;
                decimal debForeign = agg?.DebitForeign ?? 0m;
                decimal credForeign = agg?.CreditForeign ?? 0m;

                if (debLocal == 0 && credLocal == 0 && filter.OnlyWithTransactions)
                {
                    continue;
                }

                // Check Category
                if (acc.AccountCategory == AccountCategory.Revenues || acc.Code.StartsWith("4"))
                {
                    totalRevenuesLocal += (credLocal - debLocal);
                    continue;
                }

                if (acc.AccountCategory == AccountCategory.Expenses || acc.Code.StartsWith("5"))
                {
                    totalExpensesLocal += (debLocal - credLocal);
                    continue;
                }

                // Balance Sheet Accounts (Assets, Liabilities, Equity)
                if (acc.AccountCategory == AccountCategory.Assets || acc.Code.StartsWith("1"))
                {
                    decimal netAssetLocal = debLocal - credLocal;
                    decimal netAssetForeign = debForeign - credForeign;

                    var item = new FinancialStatementItemDto
                    {
                        AccountId = acc.Id,
                        AccountCode = acc.Code,
                        AccountNameAr = acc.NameAr,
                        AccountNameEn = acc.NameEn,
                        Level = acc.Level,
                        IsGroup = acc.IsGroup,
                        ParentCode = acc.Parent?.Code,
                        AmountLocal = netAssetLocal,
                        AmountForeign = netAssetForeign
                    };

                    if (acc.Code.StartsWith("1101"))
                    {
                        item.Section = "النقدية بالصناديق والبنوك";
                        result.TotalCashAndBanks += netAssetLocal;
                        result.CurrentAssetItems.Add(item);
                    }
                    else if (acc.Code.StartsWith("1102"))
                    {
                        item.Section = "العملاء والمدينون";
                        result.TotalAccountsReceivable += netAssetLocal;
                        result.CurrentAssetItems.Add(item);
                    }
                    else if (acc.Code.StartsWith("1103"))
                    {
                        item.Section = "المخزون السلعي والخامات";
                        result.TotalInventory += netAssetLocal;
                        result.CurrentAssetItems.Add(item);
                    }
                    else if (acc.Code.StartsWith("11"))
                    {
                        item.Section = "أرصدة مدينة وأصول متداولة أخرى";
                        result.TotalOtherCurrentAssets += netAssetLocal;
                        result.CurrentAssetItems.Add(item);
                    }
                    else if (acc.Code.StartsWith("1209") || (acc.Code.StartsWith("12") && acc.AccountNature == AccountNature.Credit))
                    {
                        item.Section = "مجمع إهلاك الأصول الثابتة";
                        result.TotalAccumulatedDepreciation += (credLocal - debLocal);
                        result.NonCurrentAssetItems.Add(item);
                    }
                    else if (acc.Code.StartsWith("12"))
                    {
                        item.Section = "الأصول الثابتة";
                        result.TotalFixedAssets += netAssetLocal;
                        result.NonCurrentAssetItems.Add(item);
                    }
                    else
                    {
                        item.Section = "أصول غير متداولة أخرى";
                        result.TotalOtherNonCurrentAssets += netAssetLocal;
                        result.NonCurrentAssetItems.Add(item);
                    }
                }
                else if (acc.AccountCategory == AccountCategory.Liabilities || acc.Code.StartsWith("2"))
                {
                    decimal netLiabLocal = credLocal - debLocal;
                    decimal netLiabForeign = credForeign - debForeign;

                    var item = new FinancialStatementItemDto
                    {
                        AccountId = acc.Id,
                        AccountCode = acc.Code,
                        AccountNameAr = acc.NameAr,
                        AccountNameEn = acc.NameEn,
                        Level = acc.Level,
                        IsGroup = acc.IsGroup,
                        ParentCode = acc.Parent?.Code,
                        AmountLocal = netLiabLocal,
                        AmountForeign = netLiabForeign
                    };

                    if (acc.Code.StartsWith("2101"))
                    {
                        item.Section = "الموردون والدائنون";
                        result.TotalAccountsPayable += netLiabLocal;
                        result.CurrentLiabilityItems.Add(item);
                    }
                    else if (acc.Code.StartsWith("21"))
                    {
                        item.Section = "أرصدة دائنة والتزامات متداولة أخرى";
                        result.TotalAccruedAndOtherCurrentLiabilities += netLiabLocal;
                        result.CurrentLiabilityItems.Add(item);
                    }
                    else
                    {
                        item.Section = "التزامات غير متداولة وطويلة الأجل";
                        result.TotalLongTermLiabilities += netLiabLocal;
                        result.NonCurrentLiabilityItems.Add(item);
                    }
                }
                else if (acc.AccountCategory == AccountCategory.Equity || acc.Code.StartsWith("3"))
                {
                    decimal netEqLocal = credLocal - debLocal;
                    decimal netEqForeign = credForeign - debForeign;

                    var item = new FinancialStatementItemDto
                    {
                        AccountId = acc.Id,
                        AccountCode = acc.Code,
                        AccountNameAr = acc.NameAr,
                        AccountNameEn = acc.NameEn,
                        Level = acc.Level,
                        IsGroup = acc.IsGroup,
                        ParentCode = acc.Parent?.Code,
                        AmountLocal = netEqLocal,
                        AmountForeign = netEqForeign
                    };

                    if (acc.Code.StartsWith("3101") || acc.Code == "31")
                    {
                        item.Section = "رأس المال المدفوع";
                        result.TotalCapital += netEqLocal;
                    }
                    else if (acc.Code.StartsWith("3102") || acc.Code.StartsWith("31"))
                    {
                        item.Section = "الاحتياطيات";
                        result.TotalReserves += netEqLocal;
                    }
                    else
                    {
                        item.Section = "الأرباح (الخسائر) المرحلة";
                        result.TotalRetainedEarnings += netEqLocal;
                    }
                    result.EquityItems.Add(item);
                }
            }

            // Current Period Net Income transferred from P&L
            result.CurrentPeriodNetIncome = totalRevenuesLocal - totalExpensesLocal;

            return result;
        }
        #endregion
    }
}
