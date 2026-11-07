using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class InventoryAvailabilityService : IInventoryAvailabilityService
    {
        private readonly ErpDbContext _context;
        private readonly Microsoft.Extensions.Configuration.IConfiguration? _configuration;

        public bool PreventNegativeStock { get; set; }

        public InventoryAvailabilityService(
            ErpDbContext context,
            Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
        {
            _context = context;
            _configuration = configuration;
            PreventNegativeStock = configuration != null 
                && bool.TryParse(configuration["InventorySettings:PreventNegativeStock"], out var pns) && pns;
        }

        public async Task<ItemAvailabilityDto> GetAvailabilityAsync(
            int itemId,
            int branchId,
            int? warehouseId,
            int? unitId = null,
            decimal requestedQuantity = 0m,
            int? excludeDocumentId = null,
            CancellationToken cancellationToken = default)
        {
            var item = await _context.Items
                .AsNoTracking()
                .Include(i => i.ItemUnits).ThenInclude(iu => iu.Unit)
                .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

            if (item == null)
            {
                return new ItemAvailabilityDto
                {
                    ItemId = itemId,
                    Message = "الصنف غير موجود بالدليل.",
                    IsAvailable = false
                };
            }

            var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
            var warehouse = warehouseId.HasValue
                ? await _context.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == warehouseId.Value, cancellationToken)
                : null;

            // Unit and conversion factor resolution
            decimal conversionFactor = 1.0m;
            int selectedUnitId = unitId ?? 0;
            string unitName = "";

            if (selectedUnitId > 0 && item.ItemUnits != null)
            {
                var matchingItemUnit = item.ItemUnits.FirstOrDefault(iu => iu.UnitId == selectedUnitId && iu.IsActive);
                if (matchingItemUnit != null)
                {
                    conversionFactor = matchingItemUnit.ConversionFactor > 0 ? matchingItemUnit.ConversionFactor : 1.0m;
                    unitName = matchingItemUnit.Unit?.LocalizedName ?? "";
                }
            }

            if (string.IsNullOrEmpty(unitName))
            {
                var defaultUnit = item.ItemUnits?.FirstOrDefault(iu => iu.IsDefaultSales && iu.IsActive)
                               ?? item.ItemUnits?.FirstOrDefault(iu => iu.ConversionFactor == 1.0m && iu.IsActive)
                               ?? item.ItemUnits?.FirstOrDefault(iu => iu.IsActive);

                if (defaultUnit != null)
                {
                    selectedUnitId = defaultUnit.UnitId;
                    conversionFactor = defaultUnit.ConversionFactor > 0 ? defaultUnit.ConversionFactor : 1.0m;
                    unitName = defaultUnit.Unit?.LocalizedName ?? "";
                }
                else
                {
                    var baseUnit = await _context.Units.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
                    selectedUnitId = baseUnit?.Id ?? 1;
                    unitName = baseUnit?.LocalizedName ?? "قطعة";
                }
            }

            // Services have no stock limits
            if (item.IsService)
            {
                return new ItemAvailabilityDto
                {
                    ItemId = item.Id,
                    ItemCode = item.Code,
                    ItemName = item.LocalizedName,
                    BranchId = branchId,
                    BranchName = branch?.LocalizedName,
                    WarehouseId = warehouseId,
                    WarehouseName = warehouse?.LocalizedName,
                    UnitId = selectedUnitId,
                    UnitName = unitName,
                    ConversionFactor = conversionFactor,
                    OnHandQuantity = 0m,
                    ReservedQuantity = 0m,
                    AvailableQuantity = 999999m,
                    OnHandBaseQuantity = 0m,
                    ReservedBaseQuantity = 0m,
                    AvailableBaseQuantity = 999999m,
                    RequestedQuantity = requestedQuantity,
                    RemainingAfterRequest = 999999m,
                    IsService = true,
                    IsAvailable = true,
                    Message = "صنف خدمي (لا يتطلب رصيد مخزني)."
                };
            }

            // 1. Calculate Base On-Hand Stock (الرصيد الفعلي التراكمي من الحركات المرحلة)
            // Query receipts into this warehouse/branch
            var receiptsQuery = _context.DocumentLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(l => l.ItemId == itemId &&
                            l.DocumentHeader != null &&
                            l.DocumentHeader.Status == DocumentStatus.Posted &&
                            (!excludeDocumentId.HasValue || l.DocumentHeaderId != excludeDocumentId.Value));

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                receiptsQuery = receiptsQuery.Where(l =>
                    // Standard ReceiveStock
                    (l.DocumentHeader!.InventoryEffect == InventoryEffect.ReceiveStock &&
                     (l.WarehouseId ?? l.DocumentHeader.WarehouseId) == warehouseId.Value &&
                     l.DocumentHeader.BranchId == branchId)
                    ||
                    // Direct Transfer Inbound to this warehouse
                    (l.DocumentHeader.TransactionType != null &&
                     l.DocumentHeader.TransactionType.BaseNature == DocumentBaseNature.Transfer &&
                     l.DocumentHeader.PartyId == warehouseId.Value)
                );
            }
            else
            {
                // Branch-wide receipts
                receiptsQuery = receiptsQuery.Where(l =>
                    l.DocumentHeader!.BranchId == branchId &&
                    l.DocumentHeader.InventoryEffect == InventoryEffect.ReceiveStock
                );
            }

            decimal totalReceivedBase = await receiptsQuery
                .SumAsync(l => (decimal?)(l.Quantity * (l.ConversionRate > 0 ? l.ConversionRate : 1.0m)), cancellationToken) ?? 0m;

            // Query issues from this warehouse/branch
            var issuesQuery = _context.DocumentLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(l => l.ItemId == itemId &&
                            l.DocumentHeader != null &&
                            l.DocumentHeader.Status == DocumentStatus.Posted &&
                            (!excludeDocumentId.HasValue || l.DocumentHeaderId != excludeDocumentId.Value));

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                issuesQuery = issuesQuery.Where(l =>
                    // Standard IssueStock
                    (l.DocumentHeader!.InventoryEffect == InventoryEffect.IssueStock &&
                     (l.WarehouseId ?? l.DocumentHeader.WarehouseId) == warehouseId.Value &&
                     l.DocumentHeader.BranchId == branchId)
                    ||
                    // Direct Transfer Outbound from this warehouse
                    (l.DocumentHeader.TransactionType != null &&
                     l.DocumentHeader.TransactionType.BaseNature == DocumentBaseNature.Transfer &&
                     (l.WarehouseId ?? l.DocumentHeader.WarehouseId) == warehouseId.Value &&
                     l.DocumentHeader.BranchId == branchId)
                );
            }
            else
            {
                // Branch-wide issues
                issuesQuery = issuesQuery.Where(l =>
                    l.DocumentHeader!.BranchId == branchId &&
                    l.DocumentHeader.InventoryEffect == InventoryEffect.IssueStock
                );
            }

            decimal totalIssuedBase = await issuesQuery
                .SumAsync(l => (decimal?)(l.Quantity * (l.ConversionRate > 0 ? l.ConversionRate : 1.0m)), cancellationToken) ?? 0m;

            decimal onHandBase = totalReceivedBase - totalIssuedBase;

            // 2. Calculate Base Reserved Stock (الحجوزات المفتوحة من أوامر البيع والحجز المرحلة)
            var reservationsQuery = _context.DocumentLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(l => l.ItemId == itemId &&
                            l.DocumentHeader != null &&
                            l.DocumentHeader.Status == DocumentStatus.Posted &&
                            l.DocumentHeader.InventoryEffect == InventoryEffect.ReserveStock &&
                            l.DocumentHeader.ConversionStatus != DocumentConversionStatus.FullyConverted &&
                            (!excludeDocumentId.HasValue || l.DocumentHeaderId != excludeDocumentId.Value));

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                reservationsQuery = reservationsQuery.Where(l =>
                    (l.WarehouseId ?? l.DocumentHeader!.WarehouseId) == warehouseId.Value &&
                    l.DocumentHeader!.BranchId == branchId);
            }
            else
            {
                reservationsQuery = reservationsQuery.Where(l => l.DocumentHeader!.BranchId == branchId);
            }

            decimal reservedBase = await reservationsQuery
                .SumAsync(l => (decimal?)((l.Quantity - l.ConvertedQuantity > 0 ? l.Quantity - l.ConvertedQuantity : 0m) * (l.ConversionRate > 0 ? l.ConversionRate : 1.0m)), cancellationToken) ?? 0m;

            decimal availableBase = onHandBase - reservedBase;

            // 3. Convert quantities to requested Unit
            decimal onHandUnit = Math.Round(onHandBase / conversionFactor, 4);
            decimal reservedUnit = Math.Round(reservedBase / conversionFactor, 4);
            decimal availableUnit = Math.Round(availableBase / conversionFactor, 4);
            decimal remainingAfterRequest = availableUnit - requestedQuantity;

            // 4. Check Serial Tracking
            bool isSerial = item.TrackingType == ItemTrackingType.SerialNumber;
            int availableSerialCount = 0;
            var availableSerials = new List<string>();

            if (isSerial)
            {
                var serialQuery = _context.ItemSerialNumbers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(s => s.ItemId == itemId &&
                                s.BranchId == branchId &&
                                (s.Status == ItemSerialStatus.Available || s.Status == ItemSerialStatus.InStock));

                if (warehouseId.HasValue && warehouseId.Value > 0)
                {
                    serialQuery = serialQuery.Where(s => s.WarehouseId == warehouseId.Value);
                }

                availableSerials = await serialQuery
                    .OrderBy(s => s.SerialNumber)
                    .Select(s => s.SerialNumber)
                    .ToListAsync(cancellationToken);

                availableSerialCount = availableSerials.Count;
                onHandBase = availableSerialCount;
                availableBase = Math.Max(0m, onHandBase - reservedBase);
                onHandUnit = onHandBase;
                availableUnit = availableBase;
                remainingAfterRequest = availableUnit - requestedQuantity;
            }

            bool isAvailable = availableUnit >= requestedQuantity;
            string message = isAvailable
                ? "الرصيد متاح وكافٍ للعملية."
                : $"الكمية المطلوبة ({requestedQuantity} {unitName}) تتجاوز المتاح للبيع ({availableUnit} {unitName}). العجز: {requestedQuantity - availableUnit} {unitName}.";

            return new ItemAvailabilityDto
            {
                ItemId = item.Id,
                ItemCode = item.Code,
                ItemName = item.LocalizedName,
                BranchId = branchId,
                BranchName = branch?.LocalizedName,
                WarehouseId = warehouseId,
                WarehouseName = warehouse?.LocalizedName,
                UnitId = selectedUnitId,
                UnitName = unitName,
                ConversionFactor = conversionFactor,
                OnHandQuantity = onHandUnit,
                ReservedQuantity = reservedUnit,
                AvailableQuantity = availableUnit,
                OnHandBaseQuantity = onHandBase,
                ReservedBaseQuantity = reservedBase,
                AvailableBaseQuantity = availableBase,
                RequestedQuantity = requestedQuantity,
                RemainingAfterRequest = remainingAfterRequest,
                IsService = false,
                IsSerialTracked = isSerial,
                AvailableSerialCount = availableSerialCount,
                AvailableSerials = availableSerials,
                IsAvailable = isAvailable,
                Message = message
            };
        }

        public async Task<ItemAvailabilityValidationResultDto> ValidateLineAvailabilityAsync(
            int itemId,
            int branchId,
            int? warehouseId,
            int? unitId,
            decimal requestedQuantity,
            int? excludeDocumentId = null,
            bool allowNegativeStock = false,
            CancellationToken cancellationToken = default)
        {
            var avail = await GetAvailabilityAsync(itemId, branchId, warehouseId, unitId, requestedQuantity, excludeDocumentId, cancellationToken);

            if (avail.IsService || requestedQuantity <= 0)
            {
                return new ItemAvailabilityValidationResultDto
                {
                    IsValid = true,
                    Message = avail.Message,
                    OnHandQuantity = avail.OnHandQuantity,
                    ReservedQuantity = avail.ReservedQuantity,
                    AvailableQuantity = avail.AvailableQuantity,
                    RequestedQuantity = requestedQuantity,
                    ShortageQuantity = 0m,
                    Details = avail
                };
            }

            if (avail.AvailableQuantity >= requestedQuantity)
            {
                return new ItemAvailabilityValidationResultDto
                {
                    IsValid = true,
                    Message = "الرصيد كافٍ ومتاح.",
                    OnHandQuantity = avail.OnHandQuantity,
                    ReservedQuantity = avail.ReservedQuantity,
                    AvailableQuantity = avail.AvailableQuantity,
                    RequestedQuantity = requestedQuantity,
                    ShortageQuantity = 0m,
                    Details = avail
                };
            }

            decimal shortage = requestedQuantity - avail.AvailableQuantity;

            if (allowNegativeStock)
            {
                return new ItemAvailabilityValidationResultDto
                {
                    IsValid = true,
                    Message = $"الكمية المطلوبة ({requestedQuantity} {avail.UnitName}) أكبر من المتاح ({avail.AvailableQuantity} {avail.UnitName})، ولكن سياسة النظام تسمح بالرصيد السالب.",
                    OnHandQuantity = avail.OnHandQuantity,
                    ReservedQuantity = avail.ReservedQuantity,
                    AvailableQuantity = avail.AvailableQuantity,
                    RequestedQuantity = requestedQuantity,
                    ShortageQuantity = shortage,
                    Details = avail
                };
            }

            return new ItemAvailabilityValidationResultDto
            {
                IsValid = false,
                Message = $"عجز مخزني في الصنف ({avail.ItemName} - {avail.ItemCode}): المطلوب ({requestedQuantity} {avail.UnitName}) بينما المتاح للبيع حالياً ({avail.AvailableQuantity} {avail.UnitName}) فقط بمستودع ({avail.WarehouseName ?? "الفرع"}). العجز: {shortage} {avail.UnitName}.",
                OnHandQuantity = avail.OnHandQuantity,
                ReservedQuantity = avail.ReservedQuantity,
                AvailableQuantity = avail.AvailableQuantity,
                RequestedQuantity = requestedQuantity,
                ShortageQuantity = shortage,
                Details = avail
            };
        }

        public async Task<(bool IsValid, string Message, List<ItemAvailabilityValidationResultDto> LineResults)> ValidateDocumentAvailabilityAsync(
            DocumentHeaderDto document,
            int? excludeDocumentId = null,
            bool allowNegativeStock = false,
            CancellationToken cancellationToken = default)
        {
            if (document == null || document.Lines == null || !document.Lines.Any())
            {
                return (true, "المستند فارغ.", new List<ItemAvailabilityValidationResultDto>());
            }

            // Check if document requires inventory check
            var tt = await _context.TransactionTypes.FindAsync(new object[] { document.TransactionTypeId }, cancellationToken);
            if (tt == null || tt.InventoryEffect == InventoryEffect.None || tt.InventoryEffect == InventoryEffect.ReceiveStock)
            {
                return (true, "نوع الحركة لا يتطلب خصم أو حجز رصيد.", new List<ItemAvailabilityValidationResultDto>());
            }

            // Aggregate requested quantities by (ItemId, WarehouseId) to avoid false pass when same item is split across lines
            var itemWhGroups = document.Lines
                .GroupBy(l => new
                {
                    l.ItemId,
                    WarehouseId = l.WarehouseId ?? document.WarehouseId ?? tt.DefaultWarehouseId ?? 0
                });

            var lineResults = new List<ItemAvailabilityValidationResultDto>();
            var errorMessages = new List<string>();

            foreach (var grp in itemWhGroups)
            {
                int itemId = grp.Key.ItemId;
                int whId = grp.Key.WarehouseId;

                // Total requested in base units
                decimal totalRequestedBase = 0m;
                foreach (var line in grp)
                {
                    decimal rate = line.ConversionRate > 0 ? line.ConversionRate : 1.0m;
                    totalRequestedBase += (line.Quantity * rate);
                }

                var avail = await GetAvailabilityAsync(itemId, document.BranchId, whId, null, totalRequestedBase, excludeDocumentId, cancellationToken);
                if (avail.IsService) continue;

                if (avail.AvailableBaseQuantity < totalRequestedBase && !allowNegativeStock)
                {
                    decimal shortageBase = totalRequestedBase - avail.AvailableBaseQuantity;
                    string err = $"عجز مخزني في الصنف ({avail.ItemName} - {avail.ItemCode}): الإجمالي المطلوب ({totalRequestedBase} {avail.UnitName}) بينما المتاح للبيع حالياً ({avail.AvailableBaseQuantity} {avail.UnitName}). مقدار العجز: {shortageBase} {avail.UnitName}.";
                    errorMessages.Add(err);

                    lineResults.Add(new ItemAvailabilityValidationResultDto
                    {
                        IsValid = false,
                        Message = err,
                        OnHandQuantity = avail.OnHandQuantity,
                        ReservedQuantity = avail.ReservedQuantity,
                        AvailableQuantity = avail.AvailableQuantity,
                        RequestedQuantity = totalRequestedBase,
                        ShortageQuantity = shortageBase,
                        Details = avail
                    });
                }
            }

            if (errorMessages.Any())
            {
                return (false, string.Join("\n", errorMessages), lineResults);
            }

            return (true, "كافة الأصناف متوفرة ومتاحة للبيع.", lineResults);
        }

        public async Task<Dictionary<int, ItemAvailabilityDto>> GetBulkAvailabilityAsync(
            IEnumerable<int> itemIds,
            int branchId,
            int? warehouseId,
            CancellationToken cancellationToken = default)
        {
            var distinctIds = itemIds.Distinct().ToList();
            var result = new Dictionary<int, ItemAvailabilityDto>();

            foreach (var id in distinctIds)
            {
                var avail = await GetAvailabilityAsync(id, branchId, warehouseId, null, 0m, null, cancellationToken);
                result[id] = avail;
            }

            return result;
        }
    }
}
