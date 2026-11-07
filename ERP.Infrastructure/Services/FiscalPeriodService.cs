using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class FiscalPeriodService : IFiscalPeriodService
    {
        private readonly ErpDbContext _context;
        private readonly IAuditService _audit;

        public FiscalPeriodService(ErpDbContext context, IAuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        public async Task<(bool IsAllowed, string? ErrorMessage)> ValidateDatePeriodAsync(DateTime documentDate)
        {
            var fy = await _context.FiscalYears
                .AsNoTracking()
                .FirstOrDefaultAsync(y => y.IsActive && documentDate >= y.StartDate && documentDate <= y.EndDate);

            if (fy == null)
            {
                return (false, $"لا توجد سنة مالية معرّفة أو نشطة في النظام تغطي التاريخ ({documentDate:yyyy/MM/dd}).");
            }

            if (fy.IsClosed)
            {
                return (false, $"لا يمكن إجراء العملية: السنة المالية ({fy.LocalizedName}) مغلقة نهائياً.");
            }

            var period = await _context.FiscalPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.FiscalYearId == fy.Id && documentDate >= p.StartDate && documentDate <= p.EndDate);

            if (period != null && period.IsClosed)
            {
                return (false, $"لا يمكن إجراء العملية: الفترة المالية / الشهر ({period.LocalizedName}) مغلق.");
            }

            return (true, null);
        }

        public async Task<List<FiscalPeriodDto>> GetPeriodsByYearIdAsync(int fiscalYearId)
        {
            await EnsurePeriodsForYearAsync(fiscalYearId);

            var periods = await _context.FiscalPeriods
                .AsNoTracking()
                .Include(p => p.FiscalYear)
                .Where(p => p.FiscalYearId == fiscalYearId)
                .OrderBy(p => p.PeriodNumber)
                .ToListAsync();

            return periods.Select(p => new FiscalPeriodDto
            {
                Id = p.Id,
                FiscalYearId = p.FiscalYearId,
                FiscalYearName = p.FiscalYear?.LocalizedName ?? "",
                PeriodNumber = p.PeriodNumber,
                NameAr = p.NameAr,
                NameEn = p.NameEn,
                LocalizedName = p.LocalizedName,
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                IsClosed = p.IsClosed,
                ClosedAt = p.ClosedAt,
                ClosedByUserId = p.ClosedByUserId
            }).ToList();
        }

        public async Task<(bool Success, string Message, bool NewStatus)> TogglePeriodCloseAsync(int periodId, string userId)
        {
            var period = await _context.FiscalPeriods
                .Include(p => p.FiscalYear)
                .FirstOrDefaultAsync(p => p.Id == periodId);

            if (period == null)
            {
                return (false, "الفترة المالية غير موجودة.", false);
            }

            if (!period.IsClosed && period.FiscalYear != null && period.FiscalYear.IsClosed)
            {
                return (false, "لا يمكن فتح شهر أو فترة تابعة لسنة مالية مغلقة كلياً.", false);
            }

            bool oldStatus = period.IsClosed;
            period.IsClosed = !oldStatus;
            period.ClosedAt = period.IsClosed ? DateTime.UtcNow : null;
            period.ClosedByUserId = period.IsClosed ? userId : null;
            period.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _audit.LogUpdateAsync(
                screenCode: "FYR_PERIOD",
                screenName: "الأشهر والفترات المالية",
                moduleName: "إدارة النظام",
                entityName: "FiscalPeriod",
                entityId: period.Id.ToString(),
                oldValues: new { period.Id, IsClosed = oldStatus },
                newValues: new { period.Id, IsClosed = period.IsClosed },
                description: $"تغيير حالة إغلاق الفترة ({period.NameAr}): {(period.IsClosed ? "مغلقة" : "مفتوحة")}"
            );

            string actionName = period.IsClosed ? "إغلاق" : "فتح";
            return (true, $"تم {actionName} فترة ({period.NameAr}) بنجاح.", period.IsClosed);
        }

        public async Task EnsurePeriodsForYearAsync(int fiscalYearId)
        {
            bool exists = await _context.FiscalPeriods.AnyAsync(p => p.FiscalYearId == fiscalYearId);
            if (exists) return;

            var fy = await _context.FiscalYears.FindAsync(fiscalYearId);
            if (fy == null) return;

            var start = fy.StartDate;
            var end = fy.EndDate;
            int periodNum = 1;
            var cur = new DateTime(start.Year, start.Month, 1);

            var arCulture = new CultureInfo("ar-EG");
            var enCulture = CultureInfo.InvariantCulture;

            while (cur <= end && periodNum <= 12)
            {
                var pStart = cur < start ? start : cur;
                var nextMonth = cur.AddMonths(1);
                var pEnd = nextMonth.AddDays(-1);
                if (pEnd > end) pEnd = end;
                pEnd = new DateTime(pEnd.Year, pEnd.Month, pEnd.Day, 23, 59, 59);

                string[] arabicMonths = { "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
                string arName = periodNum >= 1 && periodNum <= 12 ? $"{arabicMonths[periodNum]} {pStart.Year}" : pStart.ToString("MMMM yyyy", arCulture);
                string enName = pStart.ToString("MMMM yyyy", enCulture);

                _context.FiscalPeriods.Add(new FiscalPeriod
                {
                    FiscalYearId = fy.Id,
                    PeriodNumber = periodNum,
                    NameAr = arName,
                    NameEn = enName,
                    StartDate = pStart,
                    EndDate = pEnd,
                    IsClosed = fy.IsClosed // If year is already closed, inherit closed status
                });

                cur = nextMonth;
                periodNum++;
            }

            await _context.SaveChangesAsync();
        }
    }
}
