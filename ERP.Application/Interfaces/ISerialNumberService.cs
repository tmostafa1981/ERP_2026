using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;

namespace ERP.Application.Interfaces
{
    public interface ISerialNumberService
    {
        Task<List<SerialLookupDto>> GetAvailableSerialsAsync(int itemId, int warehouseId, int branchId);
        Task<PagedSerialLookupResultDto> GetAvailableSerialsPagedAsync(AvailableSerialsQueryDto query);
        Task<SerialBatchValidationResponseDto> ValidateBatchForReceiveAsync(SerialBatchValidationRequestDto request);
        Task<SerialValidationResultDto> ValidateSerialForIssueAsync(int itemId, int warehouseId, int branchId, string serialNumber);
        Task<SerialValidationResultDto> ValidateSerialForReceiveAsync(int itemId, string serialNumber);
        
        Task RegisterReceivedSerialsAsync(int documentId, string docType, int itemId, int warehouseId, int branchId, IEnumerable<string> serialNumbers, int? partyId = null, string? partyName = null, string? docNumber = null, string? userId = null);
        Task MarkSerialsAsIssuedAsync(int documentId, string docType, int itemId, int warehouseId, int branchId, IEnumerable<string> serialNumbers, int? partyId = null, string? partyName = null, string? docNumber = null, string? userId = null, int? warrantyMonths = null);
        Task RegisterSalesReturnSerialsAsync(int documentId, string docType, int itemId, int warehouseId, int branchId, IEnumerable<string> serialNumbers, int? partyId = null, string? partyName = null, string? docNumber = null, string? userId = null);
        Task TransferSerialsDirectAsync(int documentId, string docType, int itemId, int fromWarehouseId, int fromBranchId, int toWarehouseId, int toBranchId, IEnumerable<string> serialNumbers, string? docNumber = null, string? userId = null);
        
        Task RevertIssuedSerialsAsync(int documentId, string docType);
        Task RevertReceivedSerialsAsync(int documentId, string docType);

        Task<SerialInquiryDto?> GetSerialDetailsAsync(string serialNumber, int? itemId = null);
        Task<List<SerialInquiryDto>> SearchSerialsAsync(SerialInquiryFilterDto filter);
    }
}
