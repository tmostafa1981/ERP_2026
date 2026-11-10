using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs.InventoryCosting;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class InventoryCostingService : IInventoryCostingService
    {
        private readonly ErpDbContext _context;
        private readonly IAuditService _audit;
        private readonly IFiscalPeriodService _fiscalPeriodService;

        public InventoryCostingService(
            ErpDbContext context,
            IAuditService audit,
            IFiscalPeriodService fiscalPeriodService)
        {
            _context = context;
            _audit = audit;
            _fiscalPeriodService = fiscalPeriodService;
        }

        public async Task<decimal> GetCurrentUnitCostAsync(int itemId, int warehouseId, int branchId, CancellationToken cancellationToken = default)
        {
            var cost = await _context.ItemWarehouseCosts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ItemId == itemId && c.WarehouseId == warehouseId && c.BranchId == branchId, cancellationToken);

            if (cost != null && cost.MovingAverageCost > 0)
            {
                return cost.MovingAverageCost;
            }

            // Fallback: check other warehouses in same branch
            var branchCost = await _context.ItemWarehouseCosts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.ItemId == itemId && c.BranchId == branchId && c.MovingAverageCost > 0)
                .OrderByDescending(c => c.LastUpdatedDate)
                .Select(c => c.MovingAverageCost)
                .FirstOrDefaultAsync(cancellationToken);

            if (branchCost > 0)
            {
                return branchCost;
            }

            // Fallback: check item purchase price
            var defaultUnit = await _context.ItemUnits
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => u.ItemId == itemId && u.IsActive && u.PurchasePrice > 0)
                .OrderByDescending(u => u.IsDefaultPurchase)
                .FirstOrDefaultAsync(cancellationToken);

            return defaultUnit?.PurchasePrice ?? 0m;
        }

        public async Task<ItemWarehouseCostDto?> GetCostDetailsAsync(int itemId, int warehouseId, int branchId, CancellationToken cancellationToken = default)
        {
            var cost = await _context.ItemWarehouseCosts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(c => c.Item)
                .Include(c => c.Warehouse)
                .Include(c => c.Branch)
                .FirstOrDefaultAsync(c => c.ItemId == itemId && c.WarehouseId == warehouseId && c.BranchId == branchId, cancellationToken);

            if (cost == null) return null;

            return new ItemWarehouseCostDto
            {
                Id = cost.Id,
                BranchId = cost.BranchId,
                BranchName = cost.Branch?.LocalizedName,
                WarehouseId = cost.WarehouseId,
                WarehouseName = cost.Warehouse?.LocalizedName,
                ItemId = cost.ItemId,
                ItemCode = cost.Item?.Code,
                ItemName = cost.Item?.LocalizedName,
                CurrentQuantity = cost.CurrentQuantity,
                TotalCost = cost.TotalCost,
                MovingAverageCost = cost.MovingAverageCost,
                LastUpdatedDate = cost.LastUpdatedDate,
                LastDocumentId = cost.LastDocumentId
            };
        }

        public async Task<(bool Success, string Message)> ProcessReceiptCostAsync(DocumentHeader doc, CancellationToken cancellationToken = default)
        {
            if (doc.Lines == null || !doc.Lines.Any())
                return (true, "لا توجد سطور للمعالجة.");

            decimal rate = doc.ExchangeRate <= 0 ? 1.0m : doc.ExchangeRate;
            var tt = doc.TransactionType ?? (doc.TransactionTypeId > 0 ? await _context.TransactionTypes.FindAsync(new object[] { doc.TransactionTypeId }, cancellationToken) : null);
            bool isSalesReturn = tt != null &&
                                tt.Module == TransactionModule.Sales &&
                                tt.BaseNature == DocumentBaseNature.Return;

            // Calculate total net line values for non-service items to distribute header adjustments
            decimal totalLinesNet = doc.Lines
                .Where(l => l.Item == null || !l.Item.IsService)
                .Sum(l => Math.Max(0, l.UnitPrice * l.Quantity - l.DiscountAmount));

            decimal totalHeaderAdditionsLocal = Math.Round(doc.TotalAdditions * rate, 4);
            var discountAdjustments = doc.Adjustments?.Where(a => a.AdjustmentType == AdjustmentType.Discount).ToList();
            decimal totalHeaderDiscountsLocal = discountAdjustments != null ? Math.Round(discountAdjustments.Sum(a => a.Amount) * rate, 4) : 0m;

            foreach (var line in doc.Lines)
            {
                var item = line.Item ?? await _context.Items.FindAsync(new object[] { line.ItemId }, cancellationToken);
                if (item == null || item.IsService)
                    continue;

                int whId = line.WarehouseId ?? doc.WarehouseId ?? doc.TransactionType?.DefaultWarehouseId ?? 0;
                if (whId <= 0) continue;

                decimal conv = line.ConversionRate > 0 ? line.ConversionRate : 1.0m;
                decimal baseQty = line.Quantity * conv;
                if (baseQty <= 0) continue;

                decimal lineNetLocal = Math.Round(Math.Max(0, line.UnitPrice * line.Quantity - line.DiscountAmount) * rate, 4);

                decimal allocatedAdditionsLocal = 0m;
                decimal allocatedDiscountsLocal = 0m;
                if (totalLinesNet > 0)
                {
                    decimal lineRatio = (line.UnitPrice * line.Quantity - line.DiscountAmount) / totalLinesNet;
                    allocatedAdditionsLocal = Math.Round(totalHeaderAdditionsLocal * lineRatio, 4);
                    allocatedDiscountsLocal = Math.Round(totalHeaderDiscountsLocal * lineRatio, 4);
                }

                decimal lineAcquisitionCostLocal;
                decimal unitCostLocal;

                if (isSalesReturn)
                {
                    // Sales Return: Recover historical original unit cost if linked
                    decimal originalCost = 0m;
                    int srcLineId = 0;
                    if (line.SourceLineLinks != null && line.SourceLineLinks.Any())
                    {
                        srcLineId = line.SourceLineLinks.First().SourceDocumentLineId;
                    }
                    else if (line.Id > 0)
                    {
                        var link = await _context.DocumentLineLinks.AsNoTracking()
                            .FirstOrDefaultAsync(l => l.TargetDocumentLineId == line.Id, cancellationToken);
                        srcLineId = link?.SourceDocumentLineId ?? 0;
                    }

                    if (srcLineId > 0)
                    {
                        var srcLine = await _context.DocumentLines.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(sl => sl.Id == srcLineId, cancellationToken);
                        if (srcLine != null && srcLine.UnitCost > 0)
                        {
                            originalCost = srcLine.UnitCost;
                        }
                    }

                    if (originalCost <= 0)
                    {
                        // Fallback to current average cost
                        originalCost = await GetCurrentUnitCostAsync(line.ItemId, whId, doc.BranchId, cancellationToken);
                    }

                    unitCostLocal = originalCost;
                    lineAcquisitionCostLocal = Math.Round(baseQty * unitCostLocal, 4);
                }
                else
                {
                    // Purchase Invoice / Receipt Voucher
                    lineAcquisitionCostLocal = Math.Max(0, lineNetLocal + allocatedAdditionsLocal - allocatedDiscountsLocal);
                    unitCostLocal = baseQty > 0 ? Math.Round(lineAcquisitionCostLocal / baseQty, 4) : 0m;
                }

                line.UnitCost = unitCostLocal;
                line.TotalCost = lineAcquisitionCostLocal;

                // Load or create ItemWarehouseCost
                var costRecord = await _context.ItemWarehouseCosts
                    .FirstOrDefaultAsync(c => c.ItemId == line.ItemId && c.WarehouseId == whId && c.BranchId == doc.BranchId, cancellationToken);

                if (costRecord == null)
                {
                    costRecord = new ItemWarehouseCost
                    {
                        BranchId = doc.BranchId,
                        WarehouseId = whId,
                        ItemId = line.ItemId,
                        CurrentQuantity = 0m,
                        TotalCost = 0m,
                        MovingAverageCost = unitCostLocal,
                        LastUpdatedDate = doc.DocumentDate,
                        LastDocumentId = doc.Id,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.ItemWarehouseCosts.Add(costRecord);
                }

                decimal preQty = costRecord.CurrentQuantity;
                decimal preTotal = costRecord.TotalCost;
                decimal preAvg = costRecord.MovingAverageCost;

                decimal postQty = preQty + baseQty;
                decimal postTotal = preTotal + lineAcquisitionCostLocal;
                decimal postAvg = postQty > 0 ? Math.Round(postTotal / postQty, 4) : unitCostLocal;

                costRecord.CurrentQuantity = postQty;
                costRecord.TotalCost = postTotal;
                costRecord.MovingAverageCost = postAvg;
                costRecord.LastUpdatedDate = doc.DocumentDate;
                costRecord.LastDocumentId = doc.Id;
                costRecord.UpdatedAt = DateTime.UtcNow;

                // Log into ItemCostHistories
                var history = new ItemCostHistory
                {
                    BranchId = doc.BranchId,
                    WarehouseId = whId,
                    ItemId = line.ItemId,
                    DocumentHeaderId = doc.Id,
                    DocumentLineId = line.Id > 0 ? line.Id : null,
                    MovementType = isSalesReturn ? CostMovementType.SalesReturn : CostMovementType.Purchase,
                    MovementDate = doc.DocumentDate,
                    Quantity = baseQty,
                    UnitCost = unitCostLocal,
                    TotalCost = lineAcquisitionCostLocal,
                    PreQuantity = preQty,
                    PreTotalCost = preTotal,
                    PreAverageCost = preAvg,
                    PostQuantity = postQty,
                    PostTotalCost = postTotal,
                    PostAverageCost = postAvg,
                    Notes = $"استلام مخزني مستند ({doc.DocumentNumber})",
                    CreatedAt = DateTime.UtcNow
                };
                _context.ItemCostHistories.Add(history);

                // If serialized items exist, assign unit cost to individual serials
                if (line.Serials != null && line.Serials.Any())
                {
                    var serialStrings = line.Serials.Select(s => s.SerialNumber.Trim()).ToList();
                    var serialEntities = await _context.ItemSerialNumbers
                        .Where(s => s.ItemId == line.ItemId && serialStrings.Contains(s.SerialNumber))
                        .ToListAsync(cancellationToken);

                    foreach (var s in serialEntities)
                    {
                        s.UnitCost = unitCostLocal;
                    }
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            return (true, "تم تحديث التكلفة بنجاح.");
        }

        public async Task<(bool Success, string Message, decimal TotalCogs)> ProcessIssueCostAsync(DocumentHeader doc, CancellationToken cancellationToken = default)
        {
            if (doc.Lines == null || !doc.Lines.Any())
                return (true, "لا توجد سطور للمعالجة.", 0m);

            bool isPurchaseReturn = doc.TransactionType != null &&
                                    doc.TransactionType.Module == TransactionModule.Purchase &&
                                    doc.TransactionType.BaseNature == DocumentBaseNature.Return;

            decimal totalCogs = 0m;

            foreach (var line in doc.Lines)
            {
                var item = line.Item ?? await _context.Items.FindAsync(new object[] { line.ItemId }, cancellationToken);
                if (item == null || item.IsService)
                    continue;

                int whId = line.WarehouseId ?? doc.WarehouseId ?? doc.TransactionType?.DefaultWarehouseId ?? 0;
                if (whId <= 0) continue;

                decimal conv = line.ConversionRate > 0 ? line.ConversionRate : 1.0m;
                decimal baseQty = line.Quantity * conv;
                if (baseQty <= 0) continue;

                var costRecord = await _context.ItemWarehouseCosts
                    .FirstOrDefaultAsync(c => c.ItemId == line.ItemId && c.WarehouseId == whId && c.BranchId == doc.BranchId, cancellationToken);

                decimal unitCost = costRecord?.MovingAverageCost ?? 0m;

                // For serialized items: check if specific serial unit costs are tracked
                if (line.Serials != null && line.Serials.Any())
                {
                    var serialStrings = line.Serials.Select(s => s.SerialNumber.Trim()).ToList();
                    var serialCosts = await _context.ItemSerialNumbers
                        .AsNoTracking()
                        .Where(s => s.ItemId == line.ItemId && serialStrings.Contains(s.SerialNumber) && s.UnitCost > 0)
                        .Select(s => s.UnitCost)
                        .ToListAsync(cancellationToken);

                    if (serialCosts.Any())
                    {
                        unitCost = Math.Round(serialCosts.Average(), 4);
                    }
                }

                if (unitCost <= 0)
                {
                    // Fallback to purchase price from catalog
                    unitCost = await GetCurrentUnitCostAsync(line.ItemId, whId, doc.BranchId, cancellationToken);
                }

                decimal lineTotalCost = Math.Round(baseQty * unitCost, 4);
                line.UnitCost = unitCost;
                line.TotalCost = lineTotalCost;
                totalCogs += lineTotalCost;

                if (costRecord != null)
                {
                    decimal preQty = costRecord.CurrentQuantity;
                    decimal preTotal = costRecord.TotalCost;
                    decimal preAvg = costRecord.MovingAverageCost;

                    decimal postQty = Math.Max(0m, preQty - baseQty);
                    decimal postTotal = Math.Max(0m, preTotal - lineTotalCost);
                    decimal postAvg = postQty > 0 ? Math.Round(postTotal / postQty, 4) : preAvg;

                    costRecord.CurrentQuantity = postQty;
                    costRecord.TotalCost = postTotal;
                    costRecord.MovingAverageCost = postAvg;
                    costRecord.LastUpdatedDate = doc.DocumentDate;
                    costRecord.LastDocumentId = doc.Id;
                    costRecord.UpdatedAt = DateTime.UtcNow;

                    var history = new ItemCostHistory
                    {
                        BranchId = doc.BranchId,
                        WarehouseId = whId,
                        ItemId = line.ItemId,
                        DocumentHeaderId = doc.Id,
                        DocumentLineId = line.Id > 0 ? line.Id : null,
                        MovementType = isPurchaseReturn ? CostMovementType.PurchaseReturn : CostMovementType.SalesIssue,
                        MovementDate = doc.DocumentDate,
                        Quantity = baseQty,
                        UnitCost = unitCost,
                        TotalCost = lineTotalCost,
                        PreQuantity = preQty,
                        PreTotalCost = preTotal,
                        PreAverageCost = preAvg,
                        PostQuantity = postQty,
                        PostTotalCost = postTotal,
                        PostAverageCost = postAvg,
                        Notes = $"صرف مخزني مستند ({doc.DocumentNumber})",
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.ItemCostHistories.Add(history);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            return (true, "تم احتساب تكلفة الصرف بنجاح.", totalCogs);
        }

        public async Task<(bool Success, string Message)> ProcessTransferCostAsync(DocumentHeader doc, CancellationToken cancellationToken = default)
        {
            if (doc.Lines == null || !doc.Lines.Any())
                return (true, "لا توجد سطور للمعالجة.");

            int targetWhId = doc.PartyId ?? 0;
            if (targetWhId <= 0)
                return (false, "لم يتم تحديد المستودع المستلم في حركة التحويل.");

            foreach (var line in doc.Lines)
            {
                var item = line.Item ?? await _context.Items.FindAsync(new object[] { line.ItemId }, cancellationToken);
                if (item == null || item.IsService)
                    continue;

                int sourceWhId = line.WarehouseId ?? doc.WarehouseId ?? doc.TransactionType?.DefaultWarehouseId ?? 0;
                if (sourceWhId <= 0 || sourceWhId == targetWhId)
                    continue;

                decimal conv = line.ConversionRate > 0 ? line.ConversionRate : 1.0m;
                decimal baseQty = line.Quantity * conv;
                if (baseQty <= 0) continue;

                // Cost from source warehouse
                decimal unitCost = await GetCurrentUnitCostAsync(line.ItemId, sourceWhId, doc.BranchId, cancellationToken);
                decimal transferTotalCost = Math.Round(baseQty * unitCost, 4);

                line.UnitCost = unitCost;
                line.TotalCost = transferTotalCost;

                // 1. Deduct from Source Warehouse
                var sourceCostRecord = await _context.ItemWarehouseCosts
                    .FirstOrDefaultAsync(c => c.ItemId == line.ItemId && c.WarehouseId == sourceWhId && c.BranchId == doc.BranchId, cancellationToken);

                if (sourceCostRecord != null)
                {
                    decimal preQty = sourceCostRecord.CurrentQuantity;
                    decimal preTotal = sourceCostRecord.TotalCost;
                    decimal preAvg = sourceCostRecord.MovingAverageCost;

                    decimal postQty = Math.Max(0m, preQty - baseQty);
                    decimal postTotal = Math.Max(0m, preTotal - transferTotalCost);
                    decimal postAvg = postQty > 0 ? Math.Round(postTotal / postQty, 4) : preAvg;

                    sourceCostRecord.CurrentQuantity = postQty;
                    sourceCostRecord.TotalCost = postTotal;
                    sourceCostRecord.MovingAverageCost = postAvg;
                    sourceCostRecord.LastUpdatedDate = doc.DocumentDate;
                    sourceCostRecord.LastDocumentId = doc.Id;
                    sourceCostRecord.UpdatedAt = DateTime.UtcNow;

                    _context.ItemCostHistories.Add(new ItemCostHistory
                    {
                        BranchId = doc.BranchId,
                        WarehouseId = sourceWhId,
                        ItemId = line.ItemId,
                        DocumentHeaderId = doc.Id,
                        DocumentLineId = line.Id > 0 ? line.Id : null,
                        MovementType = CostMovementType.TransferOut,
                        MovementDate = doc.DocumentDate,
                        Quantity = baseQty,
                        UnitCost = unitCost,
                        TotalCost = transferTotalCost,
                        PreQuantity = preQty,
                        PreTotalCost = preTotal,
                        PreAverageCost = preAvg,
                        PostQuantity = postQty,
                        PostTotalCost = postTotal,
                        PostAverageCost = postAvg,
                        Notes = $"تحويل صادر إلى مستودع رقم ({targetWhId})",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // 2. Add to Target Warehouse
                var targetCostRecord = await _context.ItemWarehouseCosts
                    .FirstOrDefaultAsync(c => c.ItemId == line.ItemId && c.WarehouseId == targetWhId && c.BranchId == doc.BranchId, cancellationToken);

                if (targetCostRecord == null)
                {
                    targetCostRecord = new ItemWarehouseCost
                    {
                        BranchId = doc.BranchId,
                        WarehouseId = targetWhId,
                        ItemId = line.ItemId,
                        CurrentQuantity = 0m,
                        TotalCost = 0m,
                        MovingAverageCost = unitCost,
                        LastUpdatedDate = doc.DocumentDate,
                        LastDocumentId = doc.Id,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.ItemWarehouseCosts.Add(targetCostRecord);
                }

                {
                    decimal preQty = targetCostRecord.CurrentQuantity;
                    decimal preTotal = targetCostRecord.TotalCost;
                    decimal preAvg = targetCostRecord.MovingAverageCost;

                    decimal postQty = preQty + baseQty;
                    decimal postTotal = preTotal + transferTotalCost;
                    decimal postAvg = postQty > 0 ? Math.Round(postTotal / postQty, 4) : unitCost;

                    targetCostRecord.CurrentQuantity = postQty;
                    targetCostRecord.TotalCost = postTotal;
                    targetCostRecord.MovingAverageCost = postAvg;
                    targetCostRecord.LastUpdatedDate = doc.DocumentDate;
                    targetCostRecord.LastDocumentId = doc.Id;
                    targetCostRecord.UpdatedAt = DateTime.UtcNow;

                    _context.ItemCostHistories.Add(new ItemCostHistory
                    {
                        BranchId = doc.BranchId,
                        WarehouseId = targetWhId,
                        ItemId = line.ItemId,
                        DocumentHeaderId = doc.Id,
                        DocumentLineId = line.Id > 0 ? line.Id : null,
                        MovementType = CostMovementType.TransferIn,
                        MovementDate = doc.DocumentDate,
                        Quantity = baseQty,
                        UnitCost = unitCost,
                        TotalCost = transferTotalCost,
                        PreQuantity = preQty,
                        PreTotalCost = preTotal,
                        PreAverageCost = preAvg,
                        PostQuantity = postQty,
                        PostTotalCost = postTotal,
                        PostAverageCost = postAvg,
                        Notes = $"تحويل وارد من مستودع رقم ({sourceWhId})",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            return (true, "تم ترحيل تكلفة التحويل المخزني بنجاح.");
        }

        public async Task<(bool CanModify, string? ErrorMessage)> ValidatePurchaseQuantityReductionAsync(
            int purchaseInvoiceId,
            int itemId,
            int warehouseId,
            decimal newQuantityBase,
            CancellationToken cancellationToken = default)
        {
            var invoice = await _context.DocumentHeaders
                .Include(h => h.Lines)
                .FirstOrDefaultAsync(h => h.Id == purchaseInvoiceId, cancellationToken);

            if (invoice == null)
                return (false, "فاتورة الشراء غير موجودة.");

            var line = invoice.Lines.FirstOrDefault(l => l.ItemId == itemId && (l.WarehouseId ?? invoice.WarehouseId) == warehouseId);
            if (line == null)
                return (true, null);

            decimal currentLineQtyBase = line.Quantity * (line.ConversionRate > 0 ? line.ConversionRate : 1.0m);
            if (newQuantityBase >= currentLineQtyBase)
            {
                return (true, null); // Increasing quantity is fine
            }

            decimal reductionAmount = currentLineQtyBase - newQuantityBase;

            // Check current on-hand stock for this item in this warehouse
            var costRecord = await _context.ItemWarehouseCosts
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ItemId == itemId && c.WarehouseId == warehouseId && c.BranchId == invoice.BranchId, cancellationToken);

            decimal currentOnHand = costRecord?.CurrentQuantity ?? 0m;

            if (currentOnHand < reductionAmount)
            {
                return (false, $"لا يمكن تقليل كمية الصنف بمقدار ({reductionAmount}) لأن الرصيد الحالي المتوفر في المخزن هو ({currentOnHand}) فقط، وتقليل الكمية سيؤدي إلى رصيد سالب بسبب صرف أو بيع أجزاء من هذه الشحنة.");
            }

            return (true, null);
        }

        public async Task<(bool Success, string Message, CostRecalculationSummaryDto? Summary)> RecalculateCostsAfterPurchaseModificationAsync(
            int purchaseInvoiceId,
            string userId,
            CancellationToken cancellationToken = default)
        {
            var purchaseDoc = await _context.DocumentHeaders
                .Include(h => h.Lines).ThenInclude(l => l.Item)
                .Include(h => h.Adjustments)
                .Include(h => h.TransactionType)
                .FirstOrDefaultAsync(h => h.Id == purchaseInvoiceId, cancellationToken);

            if (purchaseDoc == null)
                return (false, "فاتورة الشراء غير موجودة.", null);

            // Recompute acquisition cost of the purchase invoice itself
            await ProcessReceiptCostAsync(purchaseDoc, cancellationToken);

            string batchNumber = $"RECALC-{DateTime.UtcNow:yyyyMMddHHmmss}-{purchaseDoc.Id}";
            var summary = new CostRecalculationSummaryDto
            {
                BatchNumber = batchNumber,
                SourcePurchaseInvoiceId = purchaseDoc.Id,
                SourcePurchaseInvoiceNumber = purchaseDoc.DocumentNumber
            };

            decimal totalOpenCogsDiff = 0m;
            decimal totalClosedCogsDiff = 0m;
            int affectedSalesCount = 0;
            int affectedTransfersCount = 0;

            // For each item in the modified purchase invoice
            var distinctItemWhPairs = purchaseDoc.Lines
                .Where(l => l.Item == null || !l.Item.IsService)
                .Select(l => new { l.ItemId, WarehouseId = l.WarehouseId ?? purchaseDoc.WarehouseId ?? purchaseDoc.TransactionType?.DefaultWarehouseId ?? 0 })
                .Where(x => x.WarehouseId > 0)
                .Distinct()
                .ToList();

            foreach (var pair in distinctItemWhPairs)
            {
                int itemId = pair.ItemId;
                int whId = pair.WarehouseId;

                // Load all posted documents affecting this item in this warehouse from the purchase document date onwards
                var candidateDocs = await _context.DocumentHeaders
                    .IgnoreQueryFilters()
                    .Include(h => h.Lines).ThenInclude(l => l.Item)
                    .Include(h => h.TransactionType)
                    .Where(h => h.BranchId == purchaseDoc.BranchId &&
                                h.Status == DocumentStatus.Posted &&
                                h.DocumentDate >= purchaseDoc.DocumentDate &&
                                h.Lines.Any(l => l.ItemId == itemId))
                    .OrderBy(h => h.DocumentDate)
                    .ThenBy(h => h.Id)
                    .ToListAsync(cancellationToken);

                var affectedDocs = candidateDocs
                    .Where(h => h.Lines.Any(l => l.ItemId == itemId && (l.WarehouseId ?? h.WarehouseId ?? h.TransactionType?.DefaultWarehouseId ?? 0) == whId))
                    .ToList();

                // Load initial state before purchase invoice date
                var lastHistoryBefore = await _context.ItemCostHistories
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(h => h.ItemId == itemId && h.WarehouseId == whId && h.BranchId == purchaseDoc.BranchId && h.MovementDate < purchaseDoc.DocumentDate)
                    .OrderByDescending(h => h.MovementDate)
                    .ThenByDescending(h => h.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                decimal runningQty = lastHistoryBefore?.PostQuantity ?? 0m;
                decimal runningTotalCost = lastHistoryBefore?.PostTotalCost ?? 0m;
                decimal runningAvgCost = lastHistoryBefore?.PostAverageCost ?? 0m;

                foreach (var doc in affectedDocs)
                {
                    var linesForItem = doc.Lines.Where(l => l.ItemId == itemId && (l.WarehouseId ?? doc.WarehouseId ?? doc.TransactionType?.DefaultWarehouseId) == whId).ToList();

                    bool isDocClosedPeriod = !(await _fiscalPeriodService.ValidateDatePeriodAsync(doc.DocumentDate)).IsAllowed;

                    if (doc.TransactionType?.InventoryEffect == InventoryEffect.ReceiveStock ||
                        (doc.TransactionType?.Module == TransactionModule.Sales && doc.TransactionType.BaseNature == DocumentBaseNature.Return))
                    {
                        // Inbound movement
                        foreach (var l in linesForItem)
                        {
                            decimal conv = l.ConversionRate > 0 ? l.ConversionRate : 1.0m;
                            decimal qtyBase = l.Quantity * conv;
                            decimal lineCost = l.TotalCost; // already recalculated in ProcessReceiptCostAsync
                            decimal unitCost = qtyBase > 0 ? Math.Round(lineCost / qtyBase, 4) : 0m;

                            runningQty += qtyBase;
                            runningTotalCost += lineCost;
                            runningAvgCost = runningQty > 0 ? Math.Round(runningTotalCost / runningQty, 4) : unitCost;
                        }
                    }
                    else if (doc.TransactionType?.InventoryEffect == InventoryEffect.IssueStock)
                    {
                        // Outbound movement (Sales Invoice or Purchase Return)
                        bool isSales = doc.TransactionType.Module == TransactionModule.Sales;
                        if (isSales) affectedSalesCount++;

                        foreach (var l in linesForItem)
                        {
                            decimal conv = l.ConversionRate > 0 ? l.ConversionRate : 1.0m;
                            decimal qtyBase = l.Quantity * conv;
                            decimal origUnitCost = l.UnitCost;
                            decimal origTotalCost = l.TotalCost;

                            decimal newUnitCost = runningAvgCost;
                            decimal newTotalCost = Math.Round(qtyBase * newUnitCost, 4);
                            decimal diff = newTotalCost - origTotalCost;

                            l.UnitCost = newUnitCost;
                            l.TotalCost = newTotalCost;

                            runningQty = Math.Max(0m, runningQty - qtyBase);
                            runningTotalCost = Math.Max(0m, runningTotalCost - newTotalCost);
                            runningAvgCost = runningQty > 0 ? Math.Round(runningTotalCost / runningQty, 4) : newUnitCost;

                            if (Math.Abs(diff) > 0.0001m)
                            {
                                var adjRecord = new CostAdjustmentRecord
                                {
                                    BatchNumber = batchNumber,
                                    BranchId = doc.BranchId,
                                    WarehouseId = whId,
                                    ItemId = itemId,
                                    SourceDocumentHeaderId = purchaseDoc.Id,
                                    AffectedDocumentHeaderId = doc.Id,
                                    AffectedDocumentLineId = l.Id,
                                    AffectedMovementType = isSales ? CostMovementType.SalesIssue : CostMovementType.PurchaseReturn,
                                    MovementDate = doc.DocumentDate,
                                    OriginalUnitCost = origUnitCost,
                                    RecalculatedUnitCost = newUnitCost,
                                    OriginalTotalCost = origTotalCost,
                                    RecalculatedTotalCost = newTotalCost,
                                    CostDifference = diff,
                                    EffectiveDate = DateTime.Today,
                                    IsClosedPeriod = isDocClosedPeriod,
                                    Reason = $"إعادة احتساب تكلفة بعد تعديل فاتورة شراء رقم {purchaseDoc.DocumentNumber}",
                                    CreatedByUserId = userId,
                                    CreatedAt = DateTime.UtcNow
                                };
                                _context.CostAdjustmentRecords.Add(adjRecord);

                                if (isDocClosedPeriod)
                                {
                                    totalClosedCogsDiff += diff;
                                }
                                else
                                {
                                    totalOpenCogsDiff += diff;
                                }

                                summary.Records.Add(new CostAdjustmentRecordDto
                                {
                                    BatchNumber = batchNumber,
                                    BranchId = doc.BranchId,
                                    WarehouseId = whId,
                                    ItemId = itemId,
                                    SourceDocumentHeaderId = purchaseDoc.Id,
                                    SourceDocumentNumber = purchaseDoc.DocumentNumber,
                                    AffectedDocumentHeaderId = doc.Id,
                                    AffectedDocumentNumber = doc.DocumentNumber,
                                    AffectedMovementType = adjRecord.AffectedMovementType,
                                    MovementDate = doc.DocumentDate,
                                    OriginalUnitCost = origUnitCost,
                                    RecalculatedUnitCost = newUnitCost,
                                    OriginalTotalCost = origTotalCost,
                                    RecalculatedTotalCost = newTotalCost,
                                    CostDifference = diff,
                                    EffectiveDate = DateTime.Today,
                                    IsClosedPeriod = isDocClosedPeriod,
                                    Reason = adjRecord.Reason,
                                    CreatedAt = DateTime.UtcNow
                                });
                            }
                        }
                    }
                    else if (doc.TransactionType?.BaseNature == DocumentBaseNature.Transfer)
                    {
                        affectedTransfersCount++;
                        // Transfer out from this warehouse
                        foreach (var l in linesForItem)
                        {
                            decimal conv = l.ConversionRate > 0 ? l.ConversionRate : 1.0m;
                            decimal qtyBase = l.Quantity * conv;
                            decimal newUnitCost = runningAvgCost;
                            decimal newTotalCost = Math.Round(qtyBase * newUnitCost, 4);

                            l.UnitCost = newUnitCost;
                            l.TotalCost = newTotalCost;

                            runningQty = Math.Max(0m, runningQty - qtyBase);
                            runningTotalCost = Math.Max(0m, runningTotalCost - newTotalCost);
                            runningAvgCost = runningQty > 0 ? Math.Round(runningTotalCost / runningQty, 4) : newUnitCost;
                        }
                    }
                }

                // Update final ItemWarehouseCost
                var costRecord = await _context.ItemWarehouseCosts
                    .FirstOrDefaultAsync(c => c.ItemId == itemId && c.WarehouseId == whId && c.BranchId == purchaseDoc.BranchId, cancellationToken);

                if (costRecord != null)
                {
                    costRecord.CurrentQuantity = runningQty;
                    costRecord.TotalCost = runningTotalCost;
                    costRecord.MovingAverageCost = runningAvgCost;
                    costRecord.LastUpdatedDate = DateTime.UtcNow;
                    costRecord.UpdatedAt = DateTime.UtcNow;
                }
            }

            // Post Accounting Adjustments
            int? openJeId = null;
            string? openJeNum = null;
            if (Math.Abs(totalOpenCogsDiff) > 0.0001m)
            {
                var jeRes = await CreateCostAdjustmentJournalEntryAsync(
                    purchaseDoc.BranchId, purchaseDoc.FiscalYearId, DateTime.Today,
                    totalOpenCogsDiff, $"تسوية فروق تكلفة بضاعة مباعة ناتجة عن تعديل فاتورة شراء رقم {purchaseDoc.DocumentNumber}",
                    userId, cancellationToken);

                if (jeRes.Success)
                {
                    openJeId = jeRes.JournalEntryId;
                    openJeNum = jeRes.JournalEntryNumber;
                }
            }

            int? closedJeId = null;
            string? closedJeNum = null;
            if (Math.Abs(totalClosedCogsDiff) > 0.0001m)
            {
                // Must be applied in the first open date
                DateTime adjustmentDate = DateTime.Today;
                var (isTodayAllowed, _) = await _fiscalPeriodService.ValidateDatePeriodAsync(adjustmentDate);
                if (!isTodayAllowed)
                {
                    var firstOpenPeriod = await _context.FiscalPeriods
                        .Where(p => p.FiscalYearId == purchaseDoc.FiscalYearId && !p.IsClosed && p.StartDate > purchaseDoc.DocumentDate)
                        .OrderBy(p => p.StartDate)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (firstOpenPeriod != null)
                    {
                        adjustmentDate = firstOpenPeriod.StartDate;
                    }
                    else
                    {
                        adjustmentDate = purchaseDoc.DocumentDate;
                    }
                }

                var jeRes = await CreateCostAdjustmentJournalEntryAsync(
                    purchaseDoc.BranchId, purchaseDoc.FiscalYearId, adjustmentDate,
                    totalClosedCogsDiff, $"تسوية فروق تكلفة فترات سابقة مغلقة ناتجة عن تعديل فاتورة شراء رقم {purchaseDoc.DocumentNumber}",
                    userId, cancellationToken);

                if (jeRes.Success)
                {
                    closedJeId = jeRes.JournalEntryId;
                    closedJeNum = jeRes.JournalEntryNumber;
                }
            }

            // Link created JVs to adjustment records
            foreach (var rec in _context.CostAdjustmentRecords.Local.Where(r => r.BatchNumber == batchNumber))
            {
                rec.AdjustmentJournalEntryId = rec.IsClosedPeriod ? closedJeId : openJeId;
            }

            await _context.SaveChangesAsync(cancellationToken);

            summary.AffectedItemsCount = distinctItemWhPairs.Count;
            summary.AffectedSalesInvoicesCount = affectedSalesCount;
            summary.AffectedTransfersCount = affectedTransfersCount;
            summary.TotalCogsDifference = totalOpenCogsDiff + totalClosedCogsDiff;
            summary.OpenPeriodJournalEntryId = openJeId;
            summary.OpenPeriodJournalEntryNumber = openJeNum;
            summary.ClosedPeriodAdjustmentJournalEntryId = closedJeId;
            summary.ClosedPeriodAdjustmentJournalEntryNumber = closedJeNum;

            await _audit.LogCreateAsync(
                screenCode: "COST_RECALC",
                screenName: "إعادة احتساب تكلفة المخزون",
                moduleName: "المخازن",
                entityName: "CostAdjustmentRecord",
                entityId: batchNumber,
                newValues: new
                {
                    batchNumber,
                    SourcePurchaseInvoice = purchaseDoc.DocumentNumber,
                    summary.AffectedItemsCount,
                    summary.TotalCogsDifference,
                    openJeNum,
                    closedJeNum
                },
                description: $"تنفيذ إعادة احتساب التكلفة بعد تعديل فاتورة شراء {purchaseDoc.DocumentNumber} بدفعة {batchNumber}"
            );

            return (true, "تمت إعادة احتساب التكلفة بنجاح وتوليد قيود التسوية المطلوبة.", summary);
        }

        private async Task<(bool Success, string Message, int? JournalEntryId, string? JournalEntryNumber)> CreateCostAdjustmentJournalEntryAsync(
            int branchId, int fiscalYearId, DateTime entryDate, decimal cogsDiff, string description, string userId, CancellationToken cancellationToken)
        {
            var fy = await _context.FiscalYears.FindAsync(new object[] { fiscalYearId }, cancellationToken);
            string fyYear = fy?.Year ?? DateTime.Today.Year.ToString();
            string jvPrefix = $"JV-{branchId}-{fyYear}-";

            var existingJVNumbers = await _context.JournalEntryHeaders
                .IgnoreQueryFilters()
                .Where(h => h.BranchId == branchId && h.FiscalYearId == fiscalYearId && h.EntryNumber.StartsWith(jvPrefix))
                .OrderByDescending(h => h.Id)
                .Select(h => h.EntryNumber)
                .Take(200)
                .ToListAsync(cancellationToken);

            long maxJV = 0;
            foreach (var num in existingJVNumbers)
            {
                if (num.Length > jvPrefix.Length)
                {
                    var suffix = num.Substring(jvPrefix.Length);
                    if (long.TryParse(suffix, out long n) && n > maxJV)
                    {
                        maxJV = n;
                    }
                }
            }

            int totalInFY = await _context.JournalEntryHeaders
                .IgnoreQueryFilters()
                .Where(h => h.BranchId == branchId && h.FiscalYearId == fiscalYearId)
                .CountAsync(cancellationToken);

            if (totalInFY > maxJV) maxJV = totalInFY;

            long candidateJV = maxJV + 1;
            string entryNumber = $"{jvPrefix}{candidateJV:D5}";

            while (await _context.JournalEntryHeaders.IgnoreQueryFilters().AnyAsync(h => h.BranchId == branchId && h.EntryNumber == entryNumber, cancellationToken))
            {
                candidateJV++;
                entryNumber = $"{jvPrefix}{candidateJV:D5}";
            }

            // Accounts: COGS (510101) & Inventory (110301)
            var cogsAccount = await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code == "510101" && a.IsActive && !a.IsGroup, cancellationToken);
            var invAccount = await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code == "110301" && a.IsActive && !a.IsGroup, cancellationToken);

            if (cogsAccount == null || invAccount == null)
            {
                return (false, "لم يتم العثور على حساب تكلفة البضاعة المباعة (510101) أو حساب المخزون (110301) في شجرة الحسابات.", null, null);
            }

            decimal absDiff = Math.Abs(cogsDiff);

            var je = new JournalEntryHeader
            {
                BranchId = branchId,
                FiscalYearId = fiscalYearId,
                EntryNumber = entryNumber,
                EntryDate = entryDate,
                SourceDocumentType = SourceDocumentType.ManualJV,
                Status = JournalEntryStatus.Posted,
                PostedAt = DateTime.UtcNow,
                PostedByUserId = userId,
                Description = description,
                TotalDebitLocal = absDiff,
                TotalCreditLocal = absDiff,
                CreatedAt = DateTime.UtcNow
            };

            if (cogsDiff > 0)
            {
                // COGS increased: Debit COGS, Credit Inventory
                je.Lines.Add(new JournalEntryLine
                {
                    LineNumber = 1,
                    AccountId = cogsAccount.Id,
                    DebitLocal = absDiff,
                    CreditLocal = 0m,
                    Description = description + " (فروق زيادة تكلفة مبيعات)",
                    CreatedAt = DateTime.UtcNow
                });
                je.Lines.Add(new JournalEntryLine
                {
                    LineNumber = 2,
                    AccountId = invAccount.Id,
                    DebitLocal = 0m,
                    CreditLocal = absDiff,
                    Description = description + " (تخفيض قيمة المخزون)",
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                // COGS decreased: Debit Inventory, Credit COGS
                je.Lines.Add(new JournalEntryLine
                {
                    LineNumber = 1,
                    AccountId = invAccount.Id,
                    DebitLocal = absDiff,
                    CreditLocal = 0m,
                    Description = description + " (زيادة قيمة المخزون)",
                    CreatedAt = DateTime.UtcNow
                });
                je.Lines.Add(new JournalEntryLine
                {
                    LineNumber = 2,
                    AccountId = cogsAccount.Id,
                    DebitLocal = 0m,
                    CreditLocal = absDiff,
                    Description = description + " (تخفيض تكلفة المبيعات)",
                    CreatedAt = DateTime.UtcNow
                });
            }

            _context.JournalEntryHeaders.Add(je);
            await _context.SaveChangesAsync(cancellationToken);

            return (true, "تم إنشاء قيد التسوية بنجاح.", je.Id, je.EntryNumber);
        }
    }
}
