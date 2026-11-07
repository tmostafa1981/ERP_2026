using System.Threading.Tasks;
using ERP.Domain.Entities;

namespace ERP.Application.Interfaces
{
    public interface IJournalEntryValidationService
    {
        Task ValidateAsync(JournalEntryHeader header, string? currentUserId = null, bool isManualAction = true);
    }
}
