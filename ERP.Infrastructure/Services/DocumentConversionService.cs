using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class DocumentConversionService : IDocumentConversionService
    {
        private readonly ErpDbContext _context;
        private readonly IUniversalDocumentService _docService;
        private readonly ITransactionTypeService _ttService;

        public DocumentConversionService(
            ErpDbContext context,
            IUniversalDocumentService docService,
            ITransactionTypeService ttService)
        {
            _context = context;
            _docService = docService;
            _ttService = ttService;
        }

        public async Task<bool> CanConvertAsync(int sourceTransactionTypeId, int targetTransactionTypeId)
        {
            return await _context.TransactionTypeConversions
                .AsNoTracking()
                .AnyAsync(c => c.SourceTransactionTypeId == sourceTransactionTypeId &&
                               c.TargetTransactionTypeId == targetTransactionTypeId &&
                               c.IsActive);
        }

        public async Task<List<TransactionTypeDto>> GetAllowedTargetTypesAsync(int sourceTransactionTypeId)
        {
            var targets = await _context.TransactionTypeConversions
                .AsNoTracking()
                .Include(c => c.TargetTransactionType)
                .Where(c => c.SourceTransactionTypeId == sourceTransactionTypeId && c.IsActive && c.TargetTransactionType != null && c.TargetTransactionType.IsActive)
                .Select(c => c.TargetTransactionType!)
                .ToListAsync();

            return targets.Select(t => new TransactionTypeDto
            {
                Id = t.Id,
                Code = t.Code,
                NameAr = t.NameAr,
                NameEn = t.NameEn,
                LocalizedName = t.LocalizedName,
                Module = t.Module,
                BaseNature = t.BaseNature,
                CodeSequencePrefix = t.CodeSequencePrefix
            }).ToList();
        }

        public async Task<List<AvailableForConversionDocumentDto>> GetAvailableSourceDocumentsAsync(
            int targetTransactionTypeId, int branchId, int? partyId = null)
        {
            var allowedSourceTypeIds = await _context.TransactionTypeConversions
                .AsNoTracking()
                .Where(c => c.TargetTransactionTypeId == targetTransactionTypeId && c.IsActive)
                .Select(c => c.SourceTransactionTypeId)
                .ToListAsync();

            if (!allowedSourceTypeIds.Any())
                return new List<AvailableForConversionDocumentDto>();

            var query = _context.DocumentHeaders
                .AsNoTracking()
                .Include(h => h.TransactionType)
                .Include(h => h.Lines).ThenInclude(l => l.Item)
                .Include(h => h.Lines).ThenInclude(l => l.Unit)
                .Where(h => h.BranchId == branchId &&
                            allowedSourceTypeIds.Contains(h.TransactionTypeId) &&
                            h.Status == DocumentStatus.Posted &&
                            h.ConversionStatus != DocumentConversionStatus.FullyConverted);

            if (partyId.HasValue)
            {
                query = query.Where(h => h.PartyId == partyId.Value);
            }

            var docs = await query.OrderByDescending(h => h.DocumentDate).ToListAsync();

            var result = new List<AvailableForConversionDocumentDto>();
            foreach (var doc in docs)
            {
                string partyName = "";
                if (doc.PartyType == DocumentPartyType.Customer && doc.PartyId.HasValue)
                {
                    var cust = await _context.Customers.FindAsync(doc.PartyId.Value);
                    partyName = cust?.LocalizedName ?? "";
                }
                else if (doc.PartyType == DocumentPartyType.Vendor && doc.PartyId.HasValue)
                {
                    var vend = await _context.Vendors.FindAsync(doc.PartyId.Value);
                    partyName = vend?.LocalizedName ?? "";
                }

                var availableLines = doc.Lines
                    .Where(l => l.RemainingQuantity > 0)
                    .Select(l => new AvailableForConversionLineDto
                    {
                        LineId = l.Id,
                        ItemId = l.ItemId,
                        ItemCode = l.Item?.Code ?? "",
                        ItemName = l.Item?.LocalizedName ?? "",
                        UnitId = l.UnitId,
                        UnitName = l.Unit?.LocalizedName ?? "",
                        OriginalQuantity = l.Quantity,
                        ConvertedQuantity = l.ConvertedQuantity,
                        RemainingQuantity = l.RemainingQuantity,
                        UnitPrice = l.UnitPrice,
                        WarehouseId = l.WarehouseId,
                        IsSerial = l.Item != null && l.Item.TrackingType == ERP.Domain.Enums.ItemTrackingType.SerialNumber
                    }).ToList();

                if (availableLines.Any())
                {
                    result.Add(new AvailableForConversionDocumentDto
                    {
                        DocumentId = doc.Id,
                        DocumentNumber = doc.DocumentNumber,
                        DocumentDate = doc.DocumentDate,
                        TransactionTypeName = doc.TransactionType?.LocalizedName ?? "",
                        PartyName = partyName,
                        TotalAmountNet = doc.TotalAmountNet,
                        AvailableLines = availableLines
                    });
                }
            }

            return result;
        }

        public async Task<(bool Success, string Message, int? NewDocumentId, string? DocumentNumber)> ConvertDocumentsAsync(
            ConvertDocumentRequestDto request, string userId, int branchId)
        {
            if (request == null || !request.SelectedLines.Any())
                return (false, "لم يتم اختيار أي بنود للتحويل.", null, null);

            var targetTT = await _context.TransactionTypes.FindAsync(request.TargetTransactionTypeId);
            if (targetTT == null || !targetTT.IsActive)
                return (false, "نوع المستند الهدف غير موجود أو غير نشط.", null, null);

            bool canConvert = await _ttService.HasPermissionAsync(userId, targetTT.Id, "convert");
            if (!canConvert)
                return (false, $"غير مصرح لك بإنشاء تحويل إلى ({targetTT.LocalizedName}).", null, null);

            var lineIds = request.SelectedLines.Select(sl => sl.SourceDocumentLineId).ToList();
            var sourceLines = await _context.DocumentLines
                .Include(l => l.DocumentHeader)
                .Include(l => l.Item)
                .Where(l => lineIds.Contains(l.Id))
                .ToListAsync();

            if (!sourceLines.Any())
                return (false, "البنود المختارة غير موجودة.", null, null);

            // Validate that conversion is allowed for each source line's TransactionType
            var sourceHeaders = sourceLines.Select(l => l.DocumentHeader!).Distinct().ToList();
            foreach (var sh in sourceHeaders)
            {
                bool allowed = await CanConvertAsync(sh.TransactionTypeId, targetTT.Id);
                if (!allowed)
                {
                    return (false, $"مسار التحويل من ({sh.DocumentNumber}) إلى ({targetTT.LocalizedName}) غير مصرح به في النظام.", null, null);
                }
            }

            // Check if multiple sources are allowed
            if (sourceHeaders.Count > 1)
            {
                var convRule = await _context.TransactionTypeConversions
                    .FirstOrDefaultAsync(c => c.TargetTransactionTypeId == targetTT.Id && c.IsActive);
                if (convRule != null && !convRule.AllowMultipleSources)
                {
                    return (false, "نوع المستند الهدف لا يسمح بدمج أكثر من مستند مصدر في مستند واحد.", null, null);
                }
            }

            var firstSource = sourceHeaders.First();

            int? chosenWarehouseId = request.WarehouseId 
                ?? firstSource.WarehouseId 
                ?? targetTT.DefaultWarehouseId;

            if ((!chosenWarehouseId.HasValue || chosenWarehouseId.Value <= 0) && targetTT.InventoryEffect != ERP.Domain.Enums.InventoryEffect.None)
            {
                chosenWarehouseId = await _context.Warehouses
                    .Where(w => w.IsActive && w.BranchId == branchId)
                    .OrderBy(w => w.Code)
                    .Select(w => (int?)w.Id)
                    .FirstOrDefaultAsync();
            }

            // Build new document DTO
            var newDocDto = new DocumentHeaderDto
            {
                BranchId = branchId,
                TransactionTypeId = targetTT.Id,
                DocumentDate = request.DocumentDate,
                PartyType = firstSource.PartyType,
                PartyId = firstSource.PartyId,
                PaymentMethod = firstSource.PaymentMethod,
                CashBankAccountId = firstSource.CashBankAccountId,
                WarehouseId = chosenWarehouseId,
                CurrencyId = firstSource.CurrencyId,
                ExchangeRate = firstSource.ExchangeRate,
                Notes = string.IsNullOrWhiteSpace(request.Notes)
                    ? $"محول من مستند رقم: {string.Join(", ", sourceHeaders.Select(s => s.DocumentNumber))}"
                    : request.Notes,
                Lines = new List<DocumentLineDto>()
            };

            var lineSelectionDict = request.SelectedLines.ToDictionary(sl => sl.SourceDocumentLineId);

            foreach (var sl in sourceLines)
            {
                var selection = lineSelectionDict[sl.Id];
                if (selection.QuantityToConvert <= 0)
                    continue;

                if (selection.QuantityToConvert > sl.RemainingQuantity)
                {
                    return (false, $"الكمية المطلوبة ({selection.QuantityToConvert}) للصنف ({sl.Item?.LocalizedName}) تتجاوز الكمية المتبقية ({sl.RemainingQuantity}).", null, null);
                }

                newDocDto.Lines.Add(new DocumentLineDto
                {
                    ItemId = sl.ItemId,
                    UnitId = sl.UnitId,
                    ConversionRate = sl.ConversionRate,
                    Quantity = selection.QuantityToConvert,
                    UnitPrice = sl.UnitPrice,
                    DiscountAmount = 0m,
                    TaxRate = sl.TaxRate,
                    WarehouseId = chosenWarehouseId ?? sl.WarehouseId,
                    CostCenterId = sl.CostCenterId,
                    SerialNumbers = selection.SerialNumbers ?? new List<string>()
                });
            }

            // Create target document
            var createRes = await _docService.CreateDocumentAsync(newDocDto, userId, postImmediately: false);
            if (!createRes.Success || !createRes.DocumentId.HasValue)
            {
                return (false, createRes.Message, null, null);
            }

            int targetDocId = createRes.DocumentId.Value;

            // Link conversion headers
            foreach (var sh in sourceHeaders)
            {
                _context.DocumentConversionLinks.Add(new DocumentConversionLink
                {
                    SourceDocumentId = sh.Id,
                    TargetDocumentId = targetDocId,
                    ConversionDate = DateTime.UtcNow,
                    ConvertedByUserId = userId,
                    Notes = $"تحويل إلى مستند {createRes.DocumentNumber}"
                });
            }

            // Link conversion lines and update remaining quantities
            var targetDocLines = await _context.DocumentLines
                .Where(l => l.DocumentHeaderId == targetDocId)
                .OrderBy(l => l.LineNumber)
                .ToListAsync();

            int targetLineIdx = 0;
            foreach (var sl in sourceLines)
            {
                var selection = lineSelectionDict[sl.Id];
                if (selection.QuantityToConvert <= 0) continue;

                var targetLine = targetDocLines[targetLineIdx++];

                _context.DocumentLineLinks.Add(new DocumentLineLink
                {
                    SourceDocumentLineId = sl.Id,
                    TargetDocumentLineId = targetLine.Id,
                    ItemId = sl.ItemId,
                    ConvertedQuantity = selection.QuantityToConvert,
                    CreatedAt = DateTime.UtcNow
                });

                // Update source line converted qty
                sl.ConvertedQuantity += selection.QuantityToConvert;
            }

            // Update source headers conversion status
            foreach (var sh in sourceHeaders)
            {
                var allLinesForHeader = await _context.DocumentLines
                    .Where(l => l.DocumentHeaderId == sh.Id)
                    .ToListAsync();

                bool allFullyConverted = allLinesForHeader.All(l => l.RemainingQuantity == 0);
                sh.ConversionStatus = allFullyConverted
                    ? DocumentConversionStatus.FullyConverted
                    : DocumentConversionStatus.PartiallyConverted;
            }

            await _context.SaveChangesAsync();

            return (true, $"تم إنشاء المستند بنجاح بالتحويل برقم ({createRes.DocumentNumber}).", targetDocId, createRes.DocumentNumber);
        }
    }
}
