using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IFiscalPeriodService
    {
        /// <summary>
        /// Validates whether the given date falls in an open fiscal year and an open fiscal period (month).
        /// Returns IsAllowed=false with a detailed Arabic message if the year or month is closed.
        /// </summary>
        Task<(bool IsAllowed, string? ErrorMessage)> ValidateDatePeriodAsync(DateTime documentDate);

        /// <summary>
        /// Gets all monthly periods for a given fiscal year.
        /// </summary>
        Task<List<FiscalPeriodDto>> GetPeriodsByYearIdAsync(int fiscalYearId);

        /// <summary>
        /// Toggles the closed status of a specific monthly period.
        /// </summary>
        Task<(bool Success, string Message, bool NewStatus)> TogglePeriodCloseAsync(int periodId, string userId);

        /// <summary>
        /// Ensures periods are generated for a fiscal year if not already present.
        /// </summary>
        Task EnsurePeriodsForYearAsync(int fiscalYearId);
    }
}
