using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Application.Models;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services
{
    public class UniversalDocumentService : IUniversalDocumentService
    {
        private readonly ErpDbContext _context;
        private readonly ITransactionTypeService _ttService;
        private readonly ISerialNumberService _serialService;
        private readonly ICodeSequenceService _codeSeqService;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _audit;
        private readonly IFiscalPeriodService _fiscalPeriodService;
        private readonly IInventoryAvailabilityService? _availabilityService;
        private readonly ICurrencyRateService _currencyRateService;
        private readonly IInventoryCostingService _costingService;
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _branchNumberLocks = new();
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _branchJournalLocks = new();
        public static readonly SemaphoreSlim JournalNumberLock = new(1, 1);

        private static SemaphoreSlim GetBranchNumberLock(int branchId) =>
            _branchNumberLocks.GetOrAdd(branchId, _ => new SemaphoreSlim(1, 1));

        public static SemaphoreSlim GetBranchJournalLock(int branchId) =>
            _branchJournalLocks.GetOrAdd(branchId, _ => new SemaphoreSlim(1, 1));

        public UniversalDocumentService(
            ErpDbContext context,
            ITransactionTypeService ttService,
            ISerialNumberService serialService,
            ICodeSequenceService codeSeqService,
            ICurrentUserContext currentUser,
            IAuditService audit,
            IFiscalPeriodService fiscalPeriodService,
            IInventoryAvailabilityService? availabilityService = null,
            ICurrencyRateService? currencyRateService = null,
            IInventoryCostingService? costingService = null)
        {
            _context = context;
            _ttService = ttService;
            _serialService = serialService;
            _codeSeqService = codeSeqService;
            _currentUser = currentUser;
            _audit = audit;
            _fiscalPeriodService = fiscalPeriodService;
            _availabilityService = availabilityService ?? new InventoryAvailabilityService(context);
            _currencyRateService = currencyRateService ?? new CurrencyRateService(context, audit, currentUser);
            _costingService = costingService ?? new InventoryCostingService(context, audit, fiscalPeriodService);
        }

        public async Task<(bool Success, string Message, int? DocumentId, string? DocumentNumber)> CreateDocumentAsync(
            DocumentHeaderDto dto, string userId, bool postImmediately = false)
        {
            if (dto == null)
                return (false, "لم يتم استلام أي بيانات للمستند.", null, null);

            var tt = await _context.TransactionTypes.FindAsync(dto.TransactionTypeId);
            if (tt == null || !tt.IsActive)
                return (false, "نوع الحركة غير موجود أو غير نشط.", null, null);

            // Permission Check
            bool canCreate = await _ttService.HasPermissionAsync(userId, tt.Id, "create");
            if (!canCreate)
                return (false, $"غير مصرح لك بإنشاء حركة من نوع ({tt.LocalizedName}).", null, null);

            // Credit vs Cash Validation
            if (dto.PaymentMethod == VoucherPaymentMethod.Cash && !tt.IsCashAllowed)
                return (false, "نوع الحركة لا يسمح بالسداد النقدي.", null, null);

            if (dto.PaymentMethod != VoucherPaymentMethod.Cash && !tt.IsCreditAllowed)
                return (false, "نوع الحركة لا يسمح بالتعامل الآجل.", null, null);

            if (dto.Lines == null || !dto.Lines.Any())
                return (false, "يجب إضافة بند واحد على الأقل في المستند.", null, null);

            int branchId = dto.BranchId;
            if (branchId <= 0 && _currentUser != null && _currentUser.BranchId > 0)
            {
                branchId = _currentUser.BranchId;
            }
            if (branchId <= 0 && dto.WarehouseId.HasValue && dto.WarehouseId.Value > 0)
            {
                branchId = await _context.Warehouses
                    .Where(w => w.Id == dto.WarehouseId.Value)
                    .Select(w => w.BranchId)
                    .FirstOrDefaultAsync();
            }
            if (branchId <= 0 && dto.Lines != null)
            {
                var firstLineWh = dto.Lines.FirstOrDefault(l => l.WarehouseId.HasValue && l.WarehouseId.Value > 0)?.WarehouseId;
                if (firstLineWh.HasValue)
                {
                    branchId = await _context.Warehouses
                        .Where(w => w.Id == firstLineWh.Value)
                        .Select(w => w.BranchId)
                        .FirstOrDefaultAsync();
                }
            }
            if (branchId <= 0)
            {
                branchId = await _context.Branches
                    .Where(b => b.IsActive)
                    .OrderBy(b => b.Id)
                    .Select(b => b.Id)
                    .FirstOrDefaultAsync();
            }

            if (branchId <= 0)
                return (false, "تعذر تحديد الفرع التابع له المستند. يرجى اختيار الفرع أو المستودع أولاً.", null, null);

            // Period & Fiscal Year Validation
            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(dto.DocumentDate);
            if (!isPeriodAllowed)
                return (false, periodError!, null, null);

            var activeFY = await _context.FiscalYears
                .FirstOrDefaultAsync(fy => fy.IsActive && !fy.IsClosed &&
                                           dto.DocumentDate >= fy.StartDate && dto.DocumentDate <= fy.EndDate);
            if (activeFY == null)
                return (false, "لا توجد سنة مالية مفتوحة تغطي تاريخ هذا المستند.", null, null);

            // Currency & Exchange Rate Validation against Party & DocumentDate
            string? partyTypeStr = dto.PartyType == DocumentPartyType.Customer ? "Customer" : (dto.PartyType == DocumentPartyType.Vendor ? "Vendor" : null);
            var (isCurrValid, currError, resolvedRate) = await _currencyRateService.ValidateAndResolveDocumentCurrencyAsync(
                partyTypeStr,
                dto.PartyId,
                dto.CashBankAccountId,
                dto.CurrencyId,
                dto.DocumentDate,
                dto.ExchangeRate);

            if (!isCurrValid)
                return (false, currError ?? "خطأ في التحقق من عملة وسعر صرف المستند.", null, null);

            dto.ExchangeRate = resolvedRate;

            var header = new DocumentHeader
            {
                BranchId = branchId,
                FiscalYearId = activeFY.Id,
                TransactionTypeId = tt.Id,
                DocumentDate = dto.DocumentDate,
                DueDate = dto.DueDate,
                PartyType = dto.PartyType,
                PartyId = dto.PartyId,
                PaymentMethod = dto.PaymentMethod,
                CashBankAccountId = dto.CashBankAccountId,
                WarehouseId = dto.WarehouseId ?? tt.DefaultWarehouseId,
                CurrencyId = dto.CurrencyId,
                ExchangeRate = dto.ExchangeRate > 0 ? dto.ExchangeRate : 1.0m,
                InventoryEffect = tt.InventoryEffect,
                AccountingEffect = tt.AccountingEffect,
                Status = DocumentStatus.Posted,
                ConversionStatus = DocumentConversionStatus.Unconverted,
                ReferenceNumber = dto.ReferenceNumber?.Trim(),
                Notes = dto.Notes?.Trim(),
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            decimal totalBeforeTax = 0;
            decimal totalDiscount = 0;
            decimal totalTax = 0;
            decimal totalNet = 0;
            int lineNum = 1;

            var itemsDict = await _context.Items
                .Where(i => dto.Lines.Select(l => l.ItemId).Contains(i.Id))
                .ToDictionaryAsync(i => i.Id);

            var documentItemSerials = new HashSet<string>(StringComparer.Ordinal);

            foreach (var l in dto.Lines)
            {
                if (!itemsDict.TryGetValue(l.ItemId, out var item))
                    return (false, $"الصنف رقم {l.ItemId} غير موجود.", null, null);

                if (l.Quantity <= 0)
                    return (false, $"السطر {lineNum}: يجب أن تكون الكمية أكبر من الصفر.", null, null);

                int lineWarehouseId = l.WarehouseId ?? header.WarehouseId ?? tt.DefaultWarehouseId ?? 0;
                if (lineWarehouseId <= 0 && tt.InventoryEffect != InventoryEffect.None && !item.IsService)
                    return (false, $"السطر {lineNum}: يرجى تحديد المستودع للصنف ({item.LocalizedName}).", null, null);

                // Serial Number Validation
                if (item.TrackingType == ItemTrackingType.SerialNumber && tt.SerialTrackingPolicy != SerialTrackingPolicy.Disabled)
                {
                    var serials = l.SerialNumbers ?? new List<string>();
                    if (serials.Count != (int)l.Quantity)
                    {
                        return (false, $"السطر {lineNum}: الصنف ({item.LocalizedName}) يتطلب إدخال عدد سيريالات ({l.Quantity}) مساوية للكمية تماماً.", null, null);
                    }

                    // Check for duplicates inside the document line
                    if (serials.Select(s => s.Trim()).Distinct(StringComparer.Ordinal).Count() != serials.Count)
                    {
                        var dupSn = serials.Select(s => s.Trim()).GroupBy(s => s, StringComparer.Ordinal).First(g => g.Count() > 1).Key;
                        return (false, $"السطر {lineNum}: تم تكرار رقم السيريال ({dupSn}) في نفس السطر للصنف ({item.LocalizedName}).", null, null);
                    }

                    // Check for duplicates across multiple lines of the same document
                    foreach (var sn in serials)
                    {
                        var trimmedSn = sn.Trim();
                        if (!documentItemSerials.Add($"{item.Id}:{trimmedSn}"))
                        {
                            return (false, $"السطر {lineNum}: رقم السيريال ({trimmedSn}) مكرر في أكثر من سطر داخل نفس المستند للصنف ({item.LocalizedName}).", null, null);
                        }
                    }

                    if (tt.Module == TransactionModule.Sales && tt.BaseNature == DocumentBaseNature.Return)
                    {
                        // Sales Return Validation: Serial must exist and currently be Sold
                        foreach (var sn in serials)
                        {
                            var trimmedSn = sn.Trim();
                            var existingSn = await _context.ItemSerialNumbers.AsNoTracking().FirstOrDefaultAsync(s => s.ItemId == item.Id && s.SerialNumber == trimmedSn);
                            if (existingSn == null)
                                return (false, $"السطر {lineNum}: السيريال ({trimmedSn}) غير مسجل في النظام لهذا الصنف، ولا يمكن إرجاع سيريال لم يتم بيعه.", null, null);
                            if (existingSn.Status != ItemSerialStatus.Sold && existingSn.Status != ItemSerialStatus.Issued)
                                return (false, $"السطر {lineNum}: السيريال ({trimmedSn}) ليس بحالة مباع، ولا يمكن عمل مرتجع لسيريال متواجد بالمخزن بالفعل.", null, null);
                        }
                    }
                    else if (tt.InventoryEffect == InventoryEffect.IssueStock)
                    {
                        foreach (var sn in serials)
                        {
                            var val = await _serialService.ValidateSerialForIssueAsync(item.Id, lineWarehouseId, branchId, sn);
                            if (!val.IsValid)
                                return (false, $"السطر {lineNum}: {val.Message}", null, null);
                        }
                    }
                    else if (tt.InventoryEffect == InventoryEffect.ReceiveStock)
                    {
                        foreach (var sn in serials)
                        {
                            var val = await _serialService.ValidateSerialForReceiveAsync(item.Id, sn);
                            if (!val.IsValid)
                                return (false, $"السطر {lineNum}: {val.Message}", null, null);
                        }
                    }
                }

                decimal gross = Math.Round(l.Quantity * l.UnitPrice, 4);
                decimal disc = Math.Round(l.DiscountAmount, 4);
                decimal afterDisc = Math.Max(0, gross - disc);
                decimal tax = l.TaxRate > 0 ? Math.Round(afterDisc * (l.TaxRate / 100m), 4) : 0m;
                decimal lineTotal = afterDisc + tax;

                var line = new DocumentLine
                {
                    LineNumber = lineNum++,
                    ItemId = l.ItemId,
                    Item = item,
                    UnitId = l.UnitId,
                    ConversionRate = l.ConversionRate > 0 ? l.ConversionRate : 1.0m,
                    Quantity = l.Quantity,
                    ConvertedQuantity = 0m,
                    UnitPrice = l.UnitPrice,
                    DiscountAmount = disc,
                    TaxRate = l.TaxRate,
                    TaxAmount = tax,
                    LineTotal = lineTotal,
                    WarehouseId = lineWarehouseId > 0 ? lineWarehouseId : null,
                    CostCenterId = l.CostCenterId,
                    Notes = l.Notes?.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                if (l.SerialNumbers != null && l.SerialNumbers.Any())
                {
                    foreach (var sn in l.SerialNumbers.Distinct(StringComparer.Ordinal))
                    {
                        int? serialId = null;
                        if (tt.InventoryEffect == InventoryEffect.IssueStock)
                        {
                            var existingSerial = await _context.ItemSerialNumbers
                                .AsNoTracking()
                                .FirstOrDefaultAsync(s => s.ItemId == l.ItemId && s.SerialNumber == sn.Trim() && s.WarehouseId == lineWarehouseId && s.BranchId == branchId);
                            serialId = existingSerial?.Id;
                        }

                        line.Serials.Add(new DocumentLineSerial
                        {
                            ItemSerialNumberId = serialId,
                            SerialNumber = sn.Trim()
                        });
                    }
                }

                totalBeforeTax += gross;
                totalDiscount += disc;
                totalTax += tax;
                totalNet += lineTotal;

                header.Lines.Add(line);
            }

            // 3. Process Header Adjustments (Charges & Discounts)
            decimal totalAdditions = 0m;
            if (dto.Adjustments != null && dto.Adjustments.Any())
            {
                int adjLineNum = 1;
                foreach (var a in dto.Adjustments)
                {
                    var masterAdj = await _context.DocumentAdjustments.FindAsync(a.DocumentAdjustmentId);
                    if (masterAdj == null || !masterAdj.IsActive) continue;

                    decimal adjAmount = 0m;
                    if (a.CalculationMethod == AdjustmentCalculationMethod.Percentage && a.Percentage.HasValue && a.Percentage.Value > 0)
                    {
                        adjAmount = Math.Round(totalBeforeTax * (a.Percentage.Value / 100m), 4);
                    }
                    else
                    {
                        adjAmount = Math.Round(a.Amount, 4);
                    }

                    if (adjAmount <= 0) continue;

                    int adjGlAccId = a.GLAccountId > 0 ? a.GLAccountId : masterAdj.GLAccountId;

                    var adjLine = new DocumentAdjustmentLine
                    {
                        LineNumber = adjLineNum++,
                        DocumentAdjustmentId = masterAdj.Id,
                        AdjustmentType = masterAdj.AdjustmentType,
                        CalculationMethod = a.CalculationMethod,
                        Percentage = a.Percentage,
                        Amount = adjAmount,
                        AmountLocal = Math.Round(adjAmount * (header.ExchangeRate <= 0 ? 1m : header.ExchangeRate), 4),
                        GLAccountId = adjGlAccId,
                        Notes = a.Notes?.Trim(),
                        CreatedAt = DateTime.UtcNow
                    };

                    if (masterAdj.AdjustmentType == AdjustmentType.Addition)
                    {
                        totalAdditions += adjAmount;
                    }
                    else if (masterAdj.AdjustmentType == AdjustmentType.Discount)
                    {
                        totalDiscount += adjAmount;
                    }

                    header.Adjustments.Add(adjLine);
                }
            }

            header.TotalAmountBeforeTax = totalBeforeTax;
            header.DiscountAmount = totalDiscount;
            header.TotalAdditions = totalAdditions;
            header.TaxAmount = totalTax;
            header.TotalAmountNet = Math.Max(0, (totalBeforeTax - totalDiscount + totalAdditions + totalTax));

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<(bool Success, string Message, int? DocumentId, string? DocumentNumber)>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                var branchLock = GetBranchNumberLock(branchId);
                await branchLock.WaitAsync();
                try
                {
                    // Strict Server-side Inventory Availability Validation inside Transaction
                    if (_availabilityService != null && (tt.InventoryEffect == InventoryEffect.IssueStock || tt.InventoryEffect == InventoryEffect.ReserveStock))
                    {
                        bool allowNegative = ResolveAllowNegativeStock(dto);
                        var (isAvail, availMsg, _) = await _availabilityService.ValidateDocumentAvailabilityAsync(
                            dto, excludeDocumentId: null, allowNegativeStock: allowNegative);
                        if (!isAvail)
                        {
                            await transaction.RollbackAsync();
                            return (false, availMsg, (int?)null, (string?)null);
                        }
                    }

                    header.DocumentNumber = await GenerateNextDocumentNumberAsync(tt.Id, branchId, activeFY.Id);
                    header.Status = DocumentStatus.Posted;
                    header.UpdatedAt = DateTime.UtcNow;
                    _context.DocumentHeaders.Add(header);
                    await _context.SaveChangesAsync();

                    string partyName = "";
                    if (header.PartyType == DocumentPartyType.Customer && header.PartyId.HasValue)
                    {
                        var cust = await _context.Customers.FindAsync(header.PartyId.Value);
                        partyName = cust?.LocalizedName ?? "";
                    }
                    else if (header.PartyType == DocumentPartyType.Vendor && header.PartyId.HasValue)
                    {
                        var vend = await _context.Vendors.FindAsync(header.PartyId.Value);
                        partyName = vend?.LocalizedName ?? "";
                    }

                    // 1. Process Inventory Effect & Serials immediately inside transaction
                    if (tt.Module == TransactionModule.Sales && tt.BaseNature == DocumentBaseNature.Return)
                    {
                        foreach (var line in header.Lines)
                        {
                            if (line.Item?.TrackingType == ItemTrackingType.SerialNumber && line.Serials.Any())
                            {
                                int whId = line.WarehouseId ?? header.WarehouseId ?? tt.DefaultWarehouseId ?? 0;
                                await _serialService.RegisterSalesReturnSerialsAsync(
                                    header.Id, tt.Code, line.ItemId, whId, header.BranchId, line.Serials.Select(s => s.SerialNumber),
                                    partyId: header.PartyId, partyName: partyName, docNumber: header.DocumentNumber, userId: userId);
                            }
                        }
                    }
                    else if (tt.InventoryEffect == InventoryEffect.IssueStock)
                    {
                        foreach (var line in header.Lines)
                        {
                            if (line.Item?.TrackingType == ItemTrackingType.SerialNumber && line.Serials.Any())
                            {
                                int whId = line.WarehouseId ?? header.WarehouseId ?? tt.DefaultWarehouseId ?? 0;
                                await _serialService.MarkSerialsAsIssuedAsync(
                                    header.Id, tt.Code, line.ItemId, whId, header.BranchId, line.Serials.Select(s => s.SerialNumber),
                                    partyId: header.PartyId, partyName: partyName, docNumber: header.DocumentNumber, userId: userId);
                            }
                        }
                    }
                    else if (tt.InventoryEffect == InventoryEffect.ReceiveStock)
                    {
                        foreach (var line in header.Lines)
                        {
                            if (line.Item?.TrackingType == ItemTrackingType.SerialNumber && line.Serials.Any())
                            {
                                int whId = line.WarehouseId ?? header.WarehouseId ?? tt.DefaultWarehouseId ?? 0;
                                await _serialService.RegisterReceivedSerialsAsync(
                                    header.Id, tt.Code, line.ItemId, whId, header.BranchId, line.Serials.Select(s => s.SerialNumber),
                                    partyId: header.PartyId, partyName: partyName, docNumber: header.DocumentNumber, userId: userId);
                            }
                        }
                    }

                    // 1.5 Process Inventory Costing (Moving Average & Unit Costing)
                    if (tt.AffectsInventory)
                    {
                        if (tt.InventoryEffect == InventoryEffect.ReceiveStock || (tt.Module == TransactionModule.Sales && tt.BaseNature == DocumentBaseNature.Return))
                        {
                            await _costingService.ProcessReceiptCostAsync(header);
                        }
                        else if (tt.InventoryEffect == InventoryEffect.IssueStock || (tt.Module == TransactionModule.Purchase && tt.BaseNature == DocumentBaseNature.Return))
                        {
                            await _costingService.ProcessIssueCostAsync(header);
                        }
                        else if (tt.Module == TransactionModule.Transfer)
                        {
                            await _costingService.ProcessTransferCostAsync(header);
                        }
                    }

                    // 2. Process Accounting Effect (Automated Journal Entry) immediately inside transaction
                    if (tt.AccountingEffect != AccountingEffect.None)
                    {
                        var jeRes = await GenerateJournalEntryAsync(header, tt, userId);
                        if (!jeRes.Success)
                        {
                            await transaction.RollbackAsync();
                            return (false, $"فشل إنشاء القيد المحاسبي: {jeRes.Message}", (int?)null, (string?)null);
                        }
                        header.JournalEntryHeaderId = jeRes.JournalEntryId;
                        await _context.SaveChangesAsync();
                    }

                    // 3. Audit log creation
                    await _audit.LogCreateAsync(
                        screenCode: tt.Module == TransactionModule.Sales ? "DOC_SAL" : "DOC_PUR",
                        screenName: tt.LocalizedName,
                        moduleName: tt.Module == TransactionModule.Sales ? "المبيعات" : "المشتريات",
                        entityName: "DocumentHeader",
                        entityId: header.Id.ToString(),
                        newValues: new
                        {
                            header.Id,
                            header.DocumentNumber,
                            header.DocumentDate,
                            header.BranchId,
                            header.TransactionTypeId,
                            header.PartyId,
                            header.TotalAmountNet,
                            LinesCount = header.Lines.Count
                        },
                        description: $"إنشاء وترحيل مستند ({tt.LocalizedName}) برقم {header.DocumentNumber}"
                    );

                    await transaction.CommitAsync();
                    return (true, $"تم حفظ وترحيل المستند بنجاح برقم ({header.DocumentNumber}).", (int?)header.Id, (string?)header.DocumentNumber);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    string msg = ex.InnerException?.Message ?? ex.Message;
                    return (false, $"حدث خطأ أثناء حفظ وترحيل المستند: {msg}", (int?)null, (string?)null);
                }
                finally
                {
                    branchLock.Release();
                }
            });
        }

        public async Task<(bool Success, string Message)> UpdateDocumentAsync(DocumentHeaderDto dto, string userId)
        {
            var header = await _context.DocumentHeaders
                .Include(h => h.TransactionType)
                .Include(h => h.Lines).ThenInclude(l => l.Serials)
                .Include(h => h.Adjustments)
                .FirstOrDefaultAsync(h => h.Id == dto.Id);

            if (header == null) return (false, "المستند غير موجود.");
            if (header.Status == DocumentStatus.Cancelled) return (false, "لا يمكن تعديل مستند ملغي.");

            bool canEdit = await _ttService.HasPermissionAsync(userId, header.TransactionTypeId, "edit");
            if (!canEdit) return (false, "غير مصرح لك بتعديل هذا المستند.");

            // Strict Period / Month / Year Closure Validation on DocumentDate
            var (isOrigPeriodAllowed, origPeriodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(header.DocumentDate);
            if (!isOrigPeriodAllowed)
                return (false, origPeriodError!);

            if (dto.DocumentDate != header.DocumentDate)
            {
                var (isNewPeriodAllowed, newPeriodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(dto.DocumentDate);
                if (!isNewPeriodAllowed)
                    return (false, newPeriodError!);
            }

            var tt = header.TransactionType!;

            // Strict Inventory Availability Validation on Edit (excluding current document's prior consumption/reservation)
            if (_availabilityService != null && (tt.InventoryEffect == InventoryEffect.IssueStock || tt.InventoryEffect == InventoryEffect.ReserveStock))
            {
                bool allowNegative = ResolveAllowNegativeStock(dto);
                var (isAvail, availMsg, _) = await _availabilityService.ValidateDocumentAvailabilityAsync(
                    dto, excludeDocumentId: header.Id, allowNegativeStock: allowNegative);
                if (!isAvail)
                    return (false, availMsg);
            }

            // Currency & Exchange Rate Validation on Edit
            string? updPartyTypeStr = dto.PartyType == DocumentPartyType.Customer ? "Customer" : (dto.PartyType == DocumentPartyType.Vendor ? "Vendor" : null);
            int targetCurrId = dto.CurrencyId > 0 ? dto.CurrencyId : header.CurrencyId;
            decimal targetRate = header.ExchangeRate;

            if (dto.DocumentDate != header.DocumentDate || targetCurrId != header.CurrencyId || dto.PartyId != header.PartyId)
            {
                var (isUpdCurrValid, updCurrError, updResolvedRate) = await _currencyRateService.ValidateAndResolveDocumentCurrencyAsync(
                    updPartyTypeStr,
                    dto.PartyId,
                    dto.CashBankAccountId,
                    targetCurrId,
                    dto.DocumentDate,
                    dto.ExchangeRate);

                if (!isUpdCurrValid)
                    return (false, updCurrError ?? "خطأ في التحقق من عملة وسعر صرف المستند.");

                targetRate = updResolvedRate;
            }

            // Purchase Quantity Reduction Validation: prevent reducing quantities below consumed amounts in subsequent issues
            if (header.Status == DocumentStatus.Posted && tt.Module == TransactionModule.Purchase && tt.BaseNature != DocumentBaseNature.Return && dto.Lines != null)
            {
                foreach (var lineDto in dto.Lines)
                {
                    int whId = lineDto.WarehouseId ?? dto.WarehouseId ?? tt.DefaultWarehouseId ?? 0;
                    decimal conv = lineDto.ConversionRate > 0 ? lineDto.ConversionRate : 1.0m;
                    decimal newBaseQty = lineDto.Quantity * conv;
                    var (canModify, qtyError) = await _costingService.ValidatePurchaseQuantityReductionAsync(header.Id, lineDto.ItemId, whId, newBaseQty);
                    if (!canModify)
                        return (false, qtyError ?? "لا يمكن تخفيض الكمية المشتراة لاستهلاك جزء منها في حركات صرف لاحقة.");
                }
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<(bool Success, string Message)>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var oldValues = new
                    {
                        header.DocumentDate,
                        header.DueDate,
                        header.PartyType,
                        header.PartyId,
                        header.PaymentMethod,
                        header.CashBankAccountId,
                        header.WarehouseId,
                        header.CurrencyId,
                        header.ExchangeRate,
                        header.ReferenceNumber,
                        header.Notes
                    };

                    // If document had a linked Journal Entry, detach FK and remove old one so it regenerates updated entries
                    if (header.JournalEntryHeaderId.HasValue)
                    {
                        int oldJeId = header.JournalEntryHeaderId.Value;
                        header.JournalEntryHeaderId = null;
                        await _context.SaveChangesAsync();

                        var oldJe = await _context.JournalEntryHeaders
                            .Include(j => j.Lines)
                            .FirstOrDefaultAsync(j => j.Id == oldJeId);
                        if (oldJe != null)
                        {
                            _context.JournalEntryLines.RemoveRange(oldJe.Lines);
                            _context.JournalEntryHeaders.Remove(oldJe);
                            await _context.SaveChangesAsync();
                        }
                    }

                    header.DocumentDate = dto.DocumentDate;
                    header.DueDate = dto.DueDate;
                    header.PartyType = dto.PartyType;
                    header.PartyId = dto.PartyId;
                    header.PaymentMethod = dto.PaymentMethod;
                    header.CashBankAccountId = dto.CashBankAccountId;
                    header.WarehouseId = dto.WarehouseId;
                    if (dto.CurrencyId > 0)
                    {
                        header.CurrencyId = dto.CurrencyId;
                    }
                    header.ExchangeRate = targetRate > 0 ? targetRate : (dto.ExchangeRate > 0 ? dto.ExchangeRate : 1.0m);
                    header.ReferenceNumber = dto.ReferenceNumber?.Trim();
                    header.Notes = dto.Notes?.Trim();
                    header.UpdatedAt = DateTime.UtcNow;

                    List<ItemSerialNumber> existingReceivedSerials = new();
                    if (dto.Lines != null && dto.Lines.Any())
                    {
                        // 1. Serials validation & revert
                        if (tt.InventoryEffect == InventoryEffect.IssueStock)
                        {
                            await _serialService.RevertIssuedSerialsAsync(header.Id, tt.Code);
                        }
                        else if (tt.InventoryEffect == InventoryEffect.ReceiveStock)
                        {
                            // Load all serials previously received by this document
                            existingReceivedSerials = await _context.ItemSerialNumbers
                                .IgnoreQueryFilters()
                                .Where(s => s.SourceDocId == header.Id && s.SourceDocType == tt.Code)
                                .ToListAsync();

                            var incomingSerialsSet = dto.Lines
                                .Where(l => l.SerialNumbers != null)
                                .SelectMany(l => l.SerialNumbers)
                                .Select(s => s.Trim())
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

                            var removedSerials = existingReceivedSerials
                                .Where(s => !incomingSerialsSet.Contains(s.SerialNumber))
                                .ToList();

                            // Validate: Can NOT remove/cancel any serial that has been sold or issued in subsequent transactions
                            var cannotRemove = removedSerials
                                .Where(s => s.Status != ItemSerialStatus.Available || s.OutDocId.HasValue)
                                .Select(s => s.SerialNumber)
                                .ToList();

                            if (cannotRemove.Any())
                            {
                                return (false, $"لا يمكن إلغاء أو حذف السيريالات التالية من الفاتورة ({header.DocumentNumber}) لأنها تم بيعها أو صرفها بالفعل في حركات لاحقة: {string.Join(", ", cannotRemove)}. يرجى إلغاء حركات البيع أو الصرف المرتبطة بها أولاً.");
                            }

                            // Validate: Cannot change ItemId for sold serials
                            foreach (var s in existingReceivedSerials.Where(s => s.Status != ItemSerialStatus.Available || s.OutDocId.HasValue))
                            {
                                var matchingLine = dto.Lines.FirstOrDefault(l => l.SerialNumbers != null && l.SerialNumbers.Any(sn => string.Equals(sn.Trim(), s.SerialNumber, StringComparison.OrdinalIgnoreCase)));
                                if (matchingLine != null && matchingLine.ItemId != s.ItemId)
                                {
                                    return (false, $"لا يمكن تغيير الصنف للسيريال المباع ({s.SerialNumber}).");
                                }
                            }

                            // Delete only the removed serials that are still Available
                            if (removedSerials.Any())
                            {
                                var removedIds = removedSerials.Select(r => r.Id).ToList();
                                var movsToRemove = await _context.ItemSerialMovements
                                    .Where(m => removedIds.Contains(m.ItemSerialNumberId))
                                    .ToListAsync();
                                _context.ItemSerialMovements.RemoveRange(movsToRemove);
                                _context.ItemSerialNumbers.RemoveRange(removedSerials);
                                await _context.SaveChangesAsync();
                            }
                        }

                        // 2. Remove old lines and adjustments (DocumentLineSerials cascade delete, keeping ItemSerialNumbers intact)
                        _context.DocumentAdjustmentLines.RemoveRange(header.Adjustments);
                        _context.DocumentLines.RemoveRange(header.Lines);
                        await _context.SaveChangesAsync();
                        header.Lines.Clear();
                        header.Adjustments.Clear();

                        decimal totalBeforeTax = 0;
                        decimal totalDiscount = 0;
                        decimal totalTax = 0;
                        decimal totalNet = 0;
                        int lineNum = 1;

                        var itemsDict = await _context.Items
                            .Where(i => dto.Lines.Select(l => l.ItemId).Contains(i.Id))
                            .ToDictionaryAsync(i => i.Id);

                        var documentItemSerials = new HashSet<string>(StringComparer.Ordinal);

                        foreach (var l in dto.Lines)
                        {
                            if (!itemsDict.TryGetValue(l.ItemId, out var item))
                                return (false, $"الصنف رقم {l.ItemId} غير موجود.");

                            if (l.Quantity <= 0)
                                return (false, $"السطر {lineNum}: يجب أن تكون الكمية أكبر من الصفر.");

                            int lineWarehouseId = l.WarehouseId ?? header.WarehouseId ?? tt.DefaultWarehouseId ?? 0;
                            if (lineWarehouseId <= 0 && tt.InventoryEffect != InventoryEffect.None && !item.IsService)
                                return (false, $"السطر {lineNum}: يرجى تحديد المستودع للصنف ({item.LocalizedName}).");

                            // Serial Number Validation
                            if (item.TrackingType == ItemTrackingType.SerialNumber && tt.SerialTrackingPolicy != SerialTrackingPolicy.Disabled)
                            {
                                var serials = l.SerialNumbers ?? new List<string>();
                                if (serials.Count != (int)l.Quantity)
                                {
                                    return (false, $"السطر {lineNum}: الصنف ({item.LocalizedName}) يتطلب إدخال عدد سيريالات ({l.Quantity}) مساوية للكمية تماماً.");
                                }

                                if (serials.Select(s => s.Trim()).Distinct(StringComparer.Ordinal).Count() != serials.Count)
                                {
                                    var dupSn = serials.Select(s => s.Trim()).GroupBy(s => s, StringComparer.Ordinal).First(g => g.Count() > 1).Key;
                                    return (false, $"السطر {lineNum}: تم تكرار رقم السيريال ({dupSn}) في نفس السطر للصنف ({item.LocalizedName}).");
                                }

                                foreach (var sn in serials)
                                {
                                    var trimmedSn = sn.Trim();
                                    if (!documentItemSerials.Add($"{item.Id}:{trimmedSn}"))
                                    {
                                        return (false, $"السطر {lineNum}: رقم السيريال ({trimmedSn}) مكرر في أكثر من سطر داخل نفس المستند للصنف ({item.LocalizedName}).");
                                    }
                                }

                                if (tt.InventoryEffect == InventoryEffect.IssueStock)
                                {
                                    foreach (var sn in serials)
                                    {
                                        var val = await _serialService.ValidateSerialForIssueAsync(item.Id, lineWarehouseId, header.BranchId, sn);
                                        if (!val.IsValid)
                                            return (false, $"السطر {lineNum}: {val.Message}");
                                    }
                                }
                            }

                            decimal gross = Math.Round(l.Quantity * l.UnitPrice, 4);
                            decimal disc = Math.Round(l.DiscountAmount, 4);
                            decimal afterDisc = Math.Max(0, gross - disc);
                            decimal tax = l.TaxRate > 0 ? Math.Round(afterDisc * (l.TaxRate / 100m), 4) : 0m;
                            decimal lineTotal = afterDisc + tax;

                            var line = new DocumentLine
                            {
                                DocumentHeaderId = header.Id,
                                LineNumber = lineNum++,
                                ItemId = l.ItemId,
                                Item = item,
                                UnitId = l.UnitId,
                                ConversionRate = l.ConversionRate > 0 ? l.ConversionRate : 1.0m,
                                Quantity = l.Quantity,
                                ConvertedQuantity = 0m,
                                UnitPrice = l.UnitPrice,
                                DiscountAmount = disc,
                                TaxRate = l.TaxRate,
                                TaxAmount = tax,
                                LineTotal = lineTotal,
                                WarehouseId = lineWarehouseId > 0 ? lineWarehouseId : null,
                                CostCenterId = l.CostCenterId,
                                Notes = l.Notes?.Trim(),
                                CreatedAt = DateTime.UtcNow
                            };

                            if (l.SerialNumbers != null && l.SerialNumbers.Any())
                            {
                                foreach (var sn in l.SerialNumbers.Distinct(StringComparer.Ordinal))
                                {
                                    int? serialId = null;
                                    var snTrim = sn.Trim();
                                    if (tt.InventoryEffect == InventoryEffect.IssueStock)
                                    {
                                        var existingSerial = await _context.ItemSerialNumbers
                                            .AsNoTracking()
                                            .FirstOrDefaultAsync(s => s.ItemId == l.ItemId && s.SerialNumber == snTrim && s.WarehouseId == lineWarehouseId && s.BranchId == header.BranchId);
                                        serialId = existingSerial?.Id;
                                    }
                                    else if (tt.InventoryEffect == InventoryEffect.ReceiveStock)
                                    {
                                        var existingSn = existingReceivedSerials.FirstOrDefault(s => string.Equals(s.SerialNumber, snTrim, StringComparison.OrdinalIgnoreCase));
                                        serialId = existingSn?.Id;
                                    }

                                    line.Serials.Add(new DocumentLineSerial
                                    {
                                        ItemSerialNumberId = serialId,
                                        SerialNumber = snTrim
                                    });
                                }
                            }

                            totalBeforeTax += gross;
                            totalDiscount += disc;
                            totalTax += tax;
                            totalNet += lineTotal;

                            header.Lines.Add(line);
                        }

                        // Adjustments
                        decimal totalAdditions = 0m;
                        if (dto.Adjustments != null && dto.Adjustments.Any())
                        {
                            int adjLineNum = 1;
                            foreach (var a in dto.Adjustments)
                            {
                                var masterAdj = await _context.DocumentAdjustments.FindAsync(a.DocumentAdjustmentId);
                                if (masterAdj == null || !masterAdj.IsActive) continue;

                                decimal adjAmount = 0m;
                                if (a.CalculationMethod == AdjustmentCalculationMethod.Percentage && a.Percentage.HasValue && a.Percentage.Value > 0)
                                {
                                    adjAmount = Math.Round(totalBeforeTax * (a.Percentage.Value / 100m), 4);
                                }
                                else
                                {
                                    adjAmount = Math.Round(a.Amount, 4);
                                }

                                if (adjAmount <= 0) continue;
                                int adjGlAccId = a.GLAccountId > 0 ? a.GLAccountId : masterAdj.GLAccountId;

                                var adjLine = new DocumentAdjustmentLine
                                {
                                    DocumentHeaderId = header.Id,
                                    LineNumber = adjLineNum++,
                                    DocumentAdjustmentId = masterAdj.Id,
                                    AdjustmentType = masterAdj.AdjustmentType,
                                    CalculationMethod = a.CalculationMethod,
                                    Percentage = a.Percentage,
                                    Amount = adjAmount,
                                    AmountLocal = Math.Round(adjAmount * (header.ExchangeRate <= 0 ? 1m : header.ExchangeRate), 4),
                                    GLAccountId = adjGlAccId,
                                    Notes = a.Notes?.Trim(),
                                    CreatedAt = DateTime.UtcNow
                                };

                                if (masterAdj.AdjustmentType == AdjustmentType.Addition)
                                    totalAdditions += adjAmount;
                                else if (masterAdj.AdjustmentType == AdjustmentType.Discount)
                                    totalDiscount += adjAmount;

                                header.Adjustments.Add(adjLine);
                            }
                        }

                        header.TotalAmountBeforeTax = totalBeforeTax;
                        header.DiscountAmount = totalDiscount;
                        header.TotalAdditions = totalAdditions;
                        header.TaxAmount = totalTax;
                        header.TotalAmountNet = Math.Max(0, (totalBeforeTax - totalDiscount + totalAdditions + totalTax));

                        await _context.SaveChangesAsync();

                        // Reapply serials
                        string partyName = "";
                        if (header.PartyType == DocumentPartyType.Customer && header.PartyId.HasValue)
                        {
                            var cust = await _context.Customers.FindAsync(header.PartyId.Value);
                            partyName = cust?.LocalizedName ?? "";
                        }
                        else if (header.PartyType == DocumentPartyType.Vendor && header.PartyId.HasValue)
                        {
                            var vend = await _context.Vendors.FindAsync(header.PartyId.Value);
                            partyName = vend?.LocalizedName ?? "";
                        }

                        if (tt.InventoryEffect == InventoryEffect.IssueStock)
                        {
                            foreach (var line in header.Lines)
                            {
                                if (line.Serials.Any())
                                {
                                    int whId = line.WarehouseId ?? header.WarehouseId ?? tt.DefaultWarehouseId ?? 0;
                                    await _serialService.MarkSerialsAsIssuedAsync(
                                        header.Id, tt.Code, line.ItemId, whId, header.BranchId, line.Serials.Select(s => s.SerialNumber),
                                        partyId: header.PartyId, partyName: partyName, docNumber: header.DocumentNumber, userId: userId);
                                }
                            }
                        }
                        else if (tt.InventoryEffect == InventoryEffect.ReceiveStock)
                        {
                            foreach (var line in header.Lines)
                            {
                                if (line.Serials.Any())
                                {
                                    int whId = line.WarehouseId ?? header.WarehouseId ?? tt.DefaultWarehouseId ?? 0;
                                    var brandNewSerials = line.Serials
                                        .Where(s => !existingReceivedSerials.Any(es => string.Equals(es.SerialNumber, s.SerialNumber, StringComparison.OrdinalIgnoreCase)))
                                        .Select(s => s.SerialNumber)
                                        .ToList();

                                    if (brandNewSerials.Any())
                                    {
                                        await _serialService.RegisterReceivedSerialsAsync(
                                            header.Id, tt.Code, line.ItemId, whId, header.BranchId, brandNewSerials,
                                            partyId: header.PartyId, partyName: partyName, docNumber: header.DocumentNumber, userId: userId);
                                    }

                                    // For kept serials, update warehouse & branch if Available, and preserve sold status
                                    var keptLineSerials = line.Serials
                                        .Where(s => existingReceivedSerials.Any(es => string.Equals(es.SerialNumber, s.SerialNumber, StringComparison.OrdinalIgnoreCase)))
                                        .Select(s => s.SerialNumber)
                                        .ToList();

                                    foreach (var ksn in keptLineSerials)
                                    {
                                        var existingRecord = existingReceivedSerials.FirstOrDefault(es => string.Equals(es.SerialNumber, ksn, StringComparison.OrdinalIgnoreCase));
                                        if (existingRecord != null)
                                        {
                                            if (existingRecord.Status == ItemSerialStatus.Available)
                                            {
                                                existingRecord.WarehouseId = whId;
                                                existingRecord.BranchId = header.BranchId;
                                            }

                                            var dls = line.Serials.FirstOrDefault(s => string.Equals(s.SerialNumber, ksn, StringComparison.OrdinalIgnoreCase));
                                            if (dls != null)
                                            {
                                                dls.ItemSerialNumberId = existingRecord.Id;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    await _context.SaveChangesAsync();

                    // Process Inventory Costing updates & chronological replay if applicable
                    if (header.Status == DocumentStatus.Posted && tt.AffectsInventory)
                    {
                        if (tt.InventoryEffect == InventoryEffect.ReceiveStock || (tt.Module == TransactionModule.Sales && tt.BaseNature == DocumentBaseNature.Return))
                        {
                            await _costingService.ProcessReceiptCostAsync(header);
                            if (tt.Module == TransactionModule.Purchase && tt.BaseNature != DocumentBaseNature.Return)
                            {
                                await _costingService.RecalculateCostsAfterPurchaseModificationAsync(header.Id, userId);
                            }
                        }
                        else if (tt.InventoryEffect == InventoryEffect.IssueStock || (tt.Module == TransactionModule.Purchase && tt.BaseNature == DocumentBaseNature.Return))
                        {
                            await _costingService.ProcessIssueCostAsync(header);
                        }
                        else if (tt.Module == TransactionModule.Transfer)
                        {
                            await _costingService.ProcessTransferCostAsync(header);
                        }
                    }

                    // Regenerate accounting entry if applicable
                    if (tt.AccountingEffect != AccountingEffect.None)
                    {
                        var jeRes = await GenerateJournalEntryAsync(header, tt, userId);
                        if (!jeRes.Success)
                        {
                            await transaction.RollbackAsync();
                            return (false, $"فشل إعادة توليد القيد المحاسبي بعد التعديل: {jeRes.Message}");
                        }
                        header.JournalEntryHeaderId = jeRes.JournalEntryId;
                        await _context.SaveChangesAsync();
                    }

                    var newValues = new
                    {
                        header.DocumentDate,
                        header.DueDate,
                        header.PartyType,
                        header.PartyId,
                        header.PaymentMethod,
                        header.CashBankAccountId,
                        header.WarehouseId,
                        header.CurrencyId,
                        header.ExchangeRate,
                        header.ReferenceNumber,
                        header.Notes
                    };

                    await _audit.LogUpdateAsync(
                        screenCode: tt.Module == TransactionModule.Sales ? "DOC_SAL" : "DOC_PUR",
                        screenName: tt.LocalizedName,
                        moduleName: tt.Module == TransactionModule.Sales ? "المبيعات" : "المشتريات",
                        entityName: "DocumentHeader",
                        entityId: header.Id.ToString(),
                        oldValues: oldValues,
                        newValues: newValues,
                        description: $"تعديل بيانات وترحيل المستند رقم ({header.DocumentNumber})"
                    );

                    await transaction.CommitAsync();
                    return (true, "تم تعديل المستند وإعادة ترحيله بنجاح.");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    string msg = ex.InnerException?.Message ?? ex.Message;
                    return (false, $"حدث خطأ أثناء تعديل المستند: {msg}");
                }
            });
        }

        public async Task<(bool Success, string Message)> DeleteDocumentAsync(int documentId, string userId)
        {
            var header = await _context.DocumentHeaders
                .Include(h => h.TransactionType)
                .FirstOrDefaultAsync(h => h.Id == documentId);
            if (header == null) return (false, "المستند غير موجود.");

            // 1. Period / Month / Fiscal Year Closure Validation strictly on DocumentDate
            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(header.DocumentDate);
            if (!isPeriodAllowed)
                return (false, periodError!);

            // 2. Permission Check
            bool canDel = await _ttService.HasPermissionAsync(userId, header.TransactionTypeId, "delete");
            if (!canDel) return (false, "غير مصرح لك بحذف هذا المستند.");

            var tt = header.TransactionType;

            // 3. Serial validation: Cannot delete a receiving document if any received serial is sold or used
            if (tt != null && tt.InventoryEffect == InventoryEffect.ReceiveStock)
            {
                var receivedSerials = await _context.ItemSerialNumbers
                    .IgnoreQueryFilters()
                    .Where(s => s.SourceDocId == header.Id && s.SourceDocType == tt.Code)
                    .ToListAsync();

                var soldOrUsed = receivedSerials
                    .Where(s => s.Status != ItemSerialStatus.Available || s.OutDocId.HasValue)
                    .Select(s => s.SerialNumber)
                    .ToList();

                if (soldOrUsed.Any())
                {
                    return (false, $"لا يمكن حذف مستند التوريد ({header.DocumentNumber}) لوجود سيريالات تم بيعها أو صرفها في حركات لاحقة: {string.Join(", ", soldOrUsed)}. يجب إلغاء حركات البيع أو الصرف المرتبطة بها أولاً.");
                }
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<(bool Success, string Message)>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Revert Serials and Accounting entries
                    if (tt != null)
                    {
                        if (tt.InventoryEffect == InventoryEffect.IssueStock)
                        {
                            await _serialService.RevertIssuedSerialsAsync(header.Id, tt.Code);
                        }
                        else if (tt.InventoryEffect == InventoryEffect.ReceiveStock)
                        {
                            await _serialService.RevertReceivedSerialsAsync(header.Id, tt.Code);
                        }
                    }

                    if (header.JournalEntryHeaderId.HasValue)
                    {
                        var je = await _context.JournalEntryHeaders
                            .Include(j => j.Lines)
                            .FirstOrDefaultAsync(j => j.Id == header.JournalEntryHeaderId.Value);
                        if (je != null)
                        {
                            _context.JournalEntryLines.RemoveRange(je.Lines);
                            _context.JournalEntryHeaders.Remove(je);
                        }
                        header.JournalEntryHeaderId = null;
                    }

                    var oldValues = new
                    {
                        header.Id,
                        header.DocumentNumber,
                        header.BranchId,
                        header.TransactionTypeId,
                        header.TotalAmountNet
                    };

                    string docNum = header.DocumentNumber;

                    _context.DocumentHeaders.Remove(header);
                    await _context.SaveChangesAsync();

                    await _audit.LogDeleteAsync(
                        screenCode: tt?.Module == TransactionModule.Sales ? "DOC_SAL" : "DOC_PUR",
                        screenName: tt?.LocalizedName ?? "حذف مستند",
                        moduleName: tt?.Module == TransactionModule.Sales ? "المبيعات" : "المشتريات",
                        entityName: "DocumentHeader",
                        entityId: documentId.ToString(),
                        oldValues: oldValues,
                        description: $"حذف المستند رقم ({docNum}) وإلغاء القيد وحركات السيريال المرتبطة"
                    );

                    await transaction.CommitAsync();
                    return (true, "تم حذف المستند وجميع تأثيراته المحاسبية والمخزنية بنجاح.");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    string msg = ex.InnerException?.Message ?? ex.Message;
                    return (false, $"حدث خطأ أثناء حذف المستند: {msg}");
                }
            });
        }

        public async Task<(bool Success, string Message, int? JournalEntryId)> PostDocumentAsync(int documentId, string userId)
        {
            var doc = await _context.DocumentHeaders
                .Include(h => h.TransactionType)
                .Include(h => h.Lines).ThenInclude(l => l.Serials)
                .Include(h => h.Lines).ThenInclude(l => l.Item)
                .Include(h => h.Adjustments).ThenInclude(a => a.DocumentAdjustment)
                .Include(h => h.Adjustments).ThenInclude(a => a.GLAccount)
                .FirstOrDefaultAsync(h => h.Id == documentId);

            if (doc == null) return (false, "المستند غير موجود.", null);
            if (doc.Status == DocumentStatus.Posted) return (false, "المستند مرحل بالفعل.", null);

            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(doc.DocumentDate);
            if (!isPeriodAllowed) return (false, periodError!, null);

            bool canPost = await _ttService.HasPermissionAsync(userId, doc.TransactionTypeId, "post");
            if (!canPost) return (false, "غير مصرح لك بترحيل هذا المستند.", null);

            var tt = doc.TransactionType!;

            // Strict Inventory Availability Validation on Post
            if (_availabilityService != null && (tt.InventoryEffect == InventoryEffect.IssueStock || tt.InventoryEffect == InventoryEffect.ReserveStock))
            {
                var docDto = await GetDocumentByIdAsync(doc.Id);
                if (docDto != null)
                {
                    bool allowNegative = ResolveAllowNegativeStock(docDto);
                    var (isAvail, availMsg, _) = await _availabilityService.ValidateDocumentAvailabilityAsync(
                        docDto, excludeDocumentId: doc.Id, allowNegativeStock: allowNegative);
                    if (!isAvail)
                        return (false, availMsg, null);
                }
            }

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

            // 1. Process Inventory Effect & Serials
            if (tt.Module == TransactionModule.Sales && tt.BaseNature == DocumentBaseNature.Return)
            {
                // Sales Return: Reactivate serial to Available in receiving branch & warehouse
                foreach (var line in doc.Lines)
                {
                    if (line.Item?.TrackingType == ItemTrackingType.SerialNumber && line.Serials.Any())
                    {
                        int whId = line.WarehouseId ?? doc.WarehouseId ?? tt.DefaultWarehouseId ?? 0;
                        await _serialService.RegisterSalesReturnSerialsAsync(
                            doc.Id, tt.Code, line.ItemId, whId, doc.BranchId, line.Serials.Select(s => s.SerialNumber),
                            partyId: doc.PartyId, partyName: partyName, docNumber: doc.DocumentNumber, userId: userId);
                    }
                }
            }
            else if (tt.InventoryEffect == InventoryEffect.IssueStock)
            {
                foreach (var line in doc.Lines)
                {
                    if (line.Item?.TrackingType == ItemTrackingType.SerialNumber && line.Serials.Any())
                    {
                        int whId = line.WarehouseId ?? doc.WarehouseId ?? tt.DefaultWarehouseId ?? 0;
                        await _serialService.MarkSerialsAsIssuedAsync(
                            doc.Id, tt.Code, line.ItemId, whId, doc.BranchId, line.Serials.Select(s => s.SerialNumber),
                            partyId: doc.PartyId, partyName: partyName, docNumber: doc.DocumentNumber, userId: userId);
                    }
                }
            }
            else if (tt.InventoryEffect == InventoryEffect.ReceiveStock)
            {
                foreach (var line in doc.Lines)
                {
                    if (line.Item?.TrackingType == ItemTrackingType.SerialNumber && line.Serials.Any())
                    {
                        int whId = line.WarehouseId ?? doc.WarehouseId ?? tt.DefaultWarehouseId ?? 0;
                        await _serialService.RegisterReceivedSerialsAsync(
                            doc.Id, tt.Code, line.ItemId, whId, doc.BranchId, line.Serials.Select(s => s.SerialNumber),
                            partyId: doc.PartyId, partyName: partyName, docNumber: doc.DocumentNumber, userId: userId);
                    }
                }
            }

            // 1.5 Process Inventory Costing (Moving Average & Unit Costing)
            if (tt.AffectsInventory)
            {
                if (tt.InventoryEffect == InventoryEffect.ReceiveStock || (tt.Module == TransactionModule.Sales && tt.BaseNature == DocumentBaseNature.Return))
                {
                    await _costingService.ProcessReceiptCostAsync(doc);
                }
                else if (tt.InventoryEffect == InventoryEffect.IssueStock || (tt.Module == TransactionModule.Purchase && tt.BaseNature == DocumentBaseNature.Return))
                {
                    await _costingService.ProcessIssueCostAsync(doc);
                }
                else if (tt.Module == TransactionModule.Transfer)
                {
                    await _costingService.ProcessTransferCostAsync(doc);
                }
            }

            // 2. Process Accounting Effect (Automated Journal Entry)
            int? jeId = null;
            if (tt.AccountingEffect != AccountingEffect.None)
            {
                var jeRes = await GenerateJournalEntryAsync(doc, tt, userId);
                if (!jeRes.Success)
                {
                    return (false, $"فشل إنشاء القيد المحاسبي: {jeRes.Message}", null);
                }
                jeId = jeRes.JournalEntryId;
                doc.JournalEntryHeaderId = jeId;
            }

            try
            {
                doc.Status = DocumentStatus.Posted;
                doc.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await _audit.LogAsync(new AuditLogEntry
                {
                    Action = AuditAction.Post,
                    ScreenCode = tt.Module == TransactionModule.Sales ? "DOC_SAL" : "DOC_PUR",
                    ScreenName = tt.LocalizedName,
                    ModuleName = tt.Module == TransactionModule.Sales ? "المبيعات" : "المشتريات",
                    EntityName = "DocumentHeader",
                    EntityId = doc.Id.ToString(),
                    Description = $"ترحيل مستند ({tt.LocalizedName}) رقم {doc.DocumentNumber}"
                });

                return (true, "تم ترحيل المستند بنجاح.", jeId);
            }
            catch (Exception ex)
            {
                string msg = ex.InnerException?.Message ?? ex.Message;
                return (false, $"فشل تحديث حالة ترحيل المستند: {msg}", null);
            }
        }

        public async Task<(bool Success, string Message)> UnpostDocumentAsync(int documentId, string userId)
        {
            var doc = await _context.DocumentHeaders
                .Include(h => h.TransactionType)
                .Include(h => h.Lines)
                .FirstOrDefaultAsync(h => h.Id == documentId);

            if (doc == null) return (false, "المستند غير موجود.");
            if (doc.Status != DocumentStatus.Posted) return (false, "المستند غير مرحل.");

            var (isPeriodAllowed, periodError) = await _fiscalPeriodService.ValidateDatePeriodAsync(doc.DocumentDate);
            if (!isPeriodAllowed) return (false, periodError!);

            bool canUnpost = await _ttService.HasPermissionAsync(userId, doc.TransactionTypeId, "unpost");
            if (!canUnpost) return (false, "غير مصرح لك بإلغاء ترحيل هذا المستند.");

            var tt = doc.TransactionType!;

            // Serial validation: Cannot unpost a receiving document if any received serial is sold or used
            if (tt.InventoryEffect == InventoryEffect.ReceiveStock)
            {
                var receivedSerials = await _context.ItemSerialNumbers
                    .IgnoreQueryFilters()
                    .Where(s => s.SourceDocId == doc.Id && s.SourceDocType == tt.Code)
                    .ToListAsync();

                var soldOrUsed = receivedSerials
                    .Where(s => s.Status != ItemSerialStatus.Available || s.OutDocId.HasValue)
                    .Select(s => s.SerialNumber)
                    .ToList();

                if (soldOrUsed.Any())
                {
                    return (false, $"لا يمكن إلغاء ترحيل مستند التوريد ({doc.DocumentNumber}) لوجود سيريالات تم بيعها أو صرفها في حركات لاحقة: {string.Join(", ", soldOrUsed)}. يجب إلغاء حركات البيع أو الصرف المرتبطة بها أولاً.");
                }

                // Inventory Quantity Reduction validation: Cannot unpost if quantities were already consumed
                if (tt.Module == TransactionModule.Purchase)
                {
                    foreach (var line in doc.Lines)
                    {
                        int whId = line.WarehouseId ?? doc.WarehouseId ?? tt.DefaultWarehouseId ?? 0;
                        var (canModify, qtyError) = await _costingService.ValidatePurchaseQuantityReductionAsync(doc.Id, line.ItemId, whId, 0m);
                        if (!canModify)
                        {
                            return (false, qtyError ?? $"لا يمكن إلغاء ترحيل مستند التوريد ({doc.DocumentNumber}) لاستهلاك جزء من كمياته في حركات بيع أو صرف لاحقة.");
                        }
                    }
                }
            }

            // Revert Serials
            if (tt.InventoryEffect == InventoryEffect.IssueStock)
            {
                await _serialService.RevertIssuedSerialsAsync(doc.Id, tt.Code);
            }
            else if (tt.InventoryEffect == InventoryEffect.ReceiveStock)
            {
                await _serialService.RevertReceivedSerialsAsync(doc.Id, tt.Code);
            }

            // Remove linked Journal Entry if exists
            if (doc.JournalEntryHeaderId.HasValue)
            {
                var je = await _context.JournalEntryHeaders
                    .Include(j => j.Lines)
                    .FirstOrDefaultAsync(j => j.Id == doc.JournalEntryHeaderId.Value);
                if (je != null)
                {
                    _context.JournalEntryLines.RemoveRange(je.Lines);
                    _context.JournalEntryHeaders.Remove(je);
                }
                doc.JournalEntryHeaderId = null;
            }

            doc.Status = DocumentStatus.Draft;
            doc.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Recompute inventory costs after purchase unposting if applicable
            if (tt.InventoryEffect == InventoryEffect.ReceiveStock && tt.Module == TransactionModule.Purchase)
            {
                await _costingService.RecalculateCostsAfterPurchaseModificationAsync(doc.Id, userId);
            }

            await _audit.LogAsync(new AuditLogEntry
            {
                Action = AuditAction.Unpost,
                ScreenCode = tt.Module == TransactionModule.Sales ? "DOC_SAL" : "DOC_PUR",
                ScreenName = tt.LocalizedName,
                ModuleName = tt.Module == TransactionModule.Sales ? "المبيعات" : "المشتريات",
                EntityName = "DocumentHeader",
                EntityId = doc.Id.ToString(),
                Description = $"إلغاء ترحيل مستند ({tt.LocalizedName}) رقم {doc.DocumentNumber}"
            });

            return (true, "تم إلغاء ترحيل المستند بنجاح وإعادته لمسودة.");
        }

        public async Task<DocumentHeaderDto?> GetDocumentByIdAsync(int documentId)
        {
            var h = await _context.DocumentHeaders
                .AsNoTracking()
                .Include(h => h.TransactionType)
                .Include(h => h.Branch)
                .Include(h => h.Warehouse)
                .Include(h => h.Currency)
                .Include(h => h.CashBankAccount)
                .Include(h => h.JournalEntryHeader)
                .Include(h => h.Lines).ThenInclude(l => l.Item)
                .Include(h => h.Lines).ThenInclude(l => l.Unit)
                .Include(h => h.Lines).ThenInclude(l => l.Warehouse)
                .Include(h => h.Lines).ThenInclude(l => l.CostCenter)
                .Include(h => h.Lines).ThenInclude(l => l.Serials)
                .Include(h => h.Adjustments).ThenInclude(a => a.DocumentAdjustment)
                .Include(h => h.Adjustments).ThenInclude(a => a.GLAccount)
                .FirstOrDefaultAsync(h => h.Id == documentId);

            if (h == null) return null;

            string partyName = "";
            string partyCode = "";
            if (h.PartyType == DocumentPartyType.Customer && h.PartyId.HasValue)
            {
                var cust = await _context.Customers.FindAsync(h.PartyId.Value);
                partyName = cust?.LocalizedName ?? "";
                partyCode = cust?.Code ?? "";
            }
            else if (h.PartyType == DocumentPartyType.Vendor && h.PartyId.HasValue)
            {
                var vend = await _context.Vendors.FindAsync(h.PartyId.Value);
                partyName = vend?.LocalizedName ?? "";
                partyCode = vend?.Code ?? "";
            }

            var lockedSerialsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (h.TransactionType != null && h.TransactionType.InventoryEffect == InventoryEffect.ReceiveStock)
            {
                var lockedList = await _context.ItemSerialNumbers
                    .IgnoreQueryFilters()
                    .Where(s => s.SourceDocId == h.Id && s.SourceDocType == h.TransactionType.Code && (s.Status != ItemSerialStatus.Available || s.OutDocId.HasValue))
                    .Select(s => s.SerialNumber)
                    .ToListAsync();
                lockedSerialsSet = new HashSet<string>(lockedList, StringComparer.OrdinalIgnoreCase);
            }

            var dto = new DocumentHeaderDto
            {
                Id = h.Id,
                RowVersion = h.RowVersion,
                BranchId = h.BranchId,
                BranchName = h.Branch?.LocalizedName,
                FiscalYearId = h.FiscalYearId,
                TransactionTypeId = h.TransactionTypeId,
                TransactionTypeCode = h.TransactionType?.Code ?? "",
                TransactionTypeName = h.TransactionType?.LocalizedName ?? "",
                Module = h.TransactionType?.Module ?? TransactionModule.Sales,
                BaseNature = h.TransactionType?.BaseNature ?? DocumentBaseNature.Invoice,
                DocumentNumber = h.DocumentNumber,
                DocumentDate = h.DocumentDate,
                DueDate = h.DueDate,
                PartyType = h.PartyType,
                PartyId = h.PartyId,
                PartyCode = partyCode,
                PartyName = partyName,
                PaymentMethod = h.PaymentMethod,
                CashBankAccountId = h.CashBankAccountId,
                CashBankAccountName = h.CashBankAccount?.LocalizedName,
                WarehouseId = h.WarehouseId,
                WarehouseName = h.Warehouse?.LocalizedName,
                CurrencyId = h.CurrencyId,
                CurrencyCode = h.Currency?.Code,
                ExchangeRate = h.ExchangeRate,
                TotalAmountBeforeTax = h.TotalAmountBeforeTax,
                DiscountAmount = h.DiscountAmount,
                TotalAdditions = h.TotalAdditions,
                TaxAmount = h.TaxAmount,
                TotalAmountNet = h.TotalAmountNet,
                Status = h.Status,
                ConversionStatus = h.ConversionStatus,
                InventoryEffect = h.InventoryEffect,
                AccountingEffect = h.AccountingEffect,
                JournalEntryHeaderId = h.JournalEntryHeaderId,
                JournalEntryNumber = h.JournalEntryHeader?.EntryNumber,
                ReferenceNumber = h.ReferenceNumber,
                Notes = h.Notes,
                Lines = h.Lines.OrderBy(l => l.LineNumber).Select(l => new DocumentLineDto
                {
                    Id = l.Id,
                    DocumentHeaderId = l.DocumentHeaderId,
                    LineNumber = l.LineNumber,
                    ItemId = l.ItemId,
                    ItemCode = l.Item?.Code ?? "",
                    ItemName = l.Item?.LocalizedName ?? "",
                    TrackingType = l.Item?.TrackingType ?? ItemTrackingType.None,
                    UnitId = l.UnitId,
                    UnitName = l.Unit?.LocalizedName ?? "",
                    ConversionRate = l.ConversionRate,
                    Quantity = l.Quantity,
                    ConvertedQuantity = l.ConvertedQuantity,
                    RemainingQuantity = l.RemainingQuantity,
                    UnitPrice = l.UnitPrice,
                    DiscountAmount = l.DiscountAmount,
                    TaxRate = l.TaxRate,
                    TaxAmount = l.TaxAmount,
                    LineTotal = l.LineTotal,
                    WarehouseId = l.WarehouseId,
                    WarehouseName = l.Warehouse?.LocalizedName,
                    CostCenterId = l.CostCenterId,
                    CostCenterName = l.CostCenter?.LocalizedName,
                    Notes = l.Notes,
                    SerialNumbers = l.Serials.Select(s => s.SerialNumber).ToList(),
                    LockedSerialNumbers = l.Serials
                        .Where(s => lockedSerialsSet.Contains(s.SerialNumber))
                        .Select(s => s.SerialNumber)
                        .ToList()
                }).ToList(),
                Adjustments = h.Adjustments.OrderBy(a => a.LineNumber).Select(a => new DocumentAdjustmentLineDto
                {
                    Id = a.Id,
                    DocumentHeaderId = a.DocumentHeaderId,
                    DocumentAdjustmentId = a.DocumentAdjustmentId,
                    AdjustmentCode = a.DocumentAdjustment?.Code ?? "",
                    AdjustmentName = a.DocumentAdjustment?.LocalizedName ?? "",
                    LineNumber = a.LineNumber,
                    AdjustmentType = a.AdjustmentType,
                    CalculationMethod = a.CalculationMethod,
                    Percentage = a.Percentage,
                    Amount = a.Amount,
                    AmountLocal = a.AmountLocal,
                    GLAccountId = a.GLAccountId,
                    GLAccountCode = a.GLAccount?.Code,
                    GLAccountName = a.GLAccount?.LocalizedName,
                    Notes = a.Notes
                }).ToList()
            };

            if (h.JournalEntryHeaderId.HasValue)
            {
                var je = await _context.JournalEntryHeaders
                    .AsNoTracking()
                    .Include(j => j.Lines).ThenInclude(jl => jl.Account)
                    .Include(j => j.Lines).ThenInclude(jl => jl.Currency)
                    .Include(j => j.Lines).ThenInclude(jl => jl.CostCenter)
                    .FirstOrDefaultAsync(j => j.Id == h.JournalEntryHeaderId.Value);

                if (je != null)
                {
                    dto.JournalEntryLines = je.Lines.OrderBy(l => l.LineNumber).Select(l => new DocumentJournalEntryLineDto
                    {
                        LineNumber = l.LineNumber,
                        AccountCode = l.Account?.Code ?? "",
                        AccountName = l.Account?.LocalizedName ?? "",
                        DebitLocal = l.DebitLocal,
                        CreditLocal = l.CreditLocal,
                        DebitForeign = l.DebitForeign,
                        CreditForeign = l.CreditForeign,
                        CurrencyCode = l.Currency?.Code ?? "",
                        CostCenterName = l.CostCenter?.LocalizedName,
                        Description = l.Description
                    }).ToList();
                }
            }

            return dto;
        }

        public async Task<List<DocumentHeaderDto>> GetDocumentsListAsync(
            int branchId, TransactionModule? module = null, int? transactionTypeId = null, DocumentStatus? status = null)
        {
            var query = _context.DocumentHeaders
                .AsNoTracking()
                .Include(h => h.TransactionType)
                .Include(h => h.Branch)
                .Include(h => h.JournalEntryHeader)
                .Where(h => h.BranchId == branchId);

            if (module.HasValue)
                query = query.Where(h => h.TransactionType != null && h.TransactionType.Module == module.Value);

            if (transactionTypeId.HasValue)
                query = query.Where(h => h.TransactionTypeId == transactionTypeId.Value);

            if (status.HasValue)
                query = query.Where(h => h.Status == status.Value);

            var list = await query.OrderByDescending(h => h.DocumentDate).ThenByDescending(h => h.Id).ToListAsync();

            return list.Select(h => new DocumentHeaderDto
            {
                Id = h.Id,
                BranchId = h.BranchId,
                BranchName = h.Branch?.LocalizedName,
                FiscalYearId = h.FiscalYearId,
                TransactionTypeId = h.TransactionTypeId,
                TransactionTypeCode = h.TransactionType?.Code ?? "",
                TransactionTypeName = h.TransactionType?.LocalizedName ?? "",
                Module = h.TransactionType?.Module ?? TransactionModule.Sales,
                BaseNature = h.TransactionType?.BaseNature ?? DocumentBaseNature.Invoice,
                DocumentNumber = h.DocumentNumber,
                DocumentDate = h.DocumentDate,
                DueDate = h.DueDate,
                PartyType = h.PartyType,
                PartyId = h.PartyId,
                PaymentMethod = h.PaymentMethod,
                TotalAmountNet = h.TotalAmountNet,
                Status = h.Status,
                ConversionStatus = h.ConversionStatus,
                InventoryEffect = h.InventoryEffect,
                AccountingEffect = h.AccountingEffect,
                JournalEntryHeaderId = h.JournalEntryHeaderId,
                JournalEntryNumber = h.JournalEntryHeader?.EntryNumber,
                ReferenceNumber = h.ReferenceNumber
            }).ToList();
        }

        public async Task<string> GenerateNextDocumentNumberAsync(int transactionTypeId, int branchId, int fiscalYearId)
        {
            var tt = await _context.TransactionTypes.FindAsync(transactionTypeId);
            string prefix = tt?.CodeSequencePrefix ?? "DOC-";

            var fy = await _context.FiscalYears.FindAsync(fiscalYearId);
            string year = fy?.Year ?? DateTime.Today.Year.ToString();

            string fullPrefix = $"{prefix}{branchId}-{year}-";

            var existingNumbers = await _context.DocumentHeaders
                .IgnoreQueryFilters()
                .Where(h => h.BranchId == branchId && h.FiscalYearId == fiscalYearId && h.DocumentNumber.StartsWith(fullPrefix))
                .OrderByDescending(h => h.Id)
                .Select(h => h.DocumentNumber)
                .Take(200)
                .ToListAsync();

            long maxNumber = 0;
            foreach (var num in existingNumbers)
            {
                if (num.Length > fullPrefix.Length)
                {
                    var suffix = num.Substring(fullPrefix.Length);
                    if (long.TryParse(suffix, out long n) && n > maxNumber)
                    {
                        maxNumber = n;
                    }
                }
            }

            long candidate = maxNumber + 1;
            string candidateCode = $"{fullPrefix}{candidate:D5}";

            while (await _context.DocumentHeaders.IgnoreQueryFilters().AnyAsync(h => h.BranchId == branchId && h.DocumentNumber == candidateCode))
            {
                candidate++;
                candidateCode = $"{fullPrefix}{candidate:D5}";
            }

            return candidateCode;
        }

        private async Task<(bool Success, string Message, int? JournalEntryId)> GenerateJournalEntryAsync(
            DocumentHeader doc, TransactionType tt, string userId)
        {
            var jvLock = GetBranchJournalLock(doc.BranchId);
            await jvLock.WaitAsync();
            try
            {
                var fy = await _context.FiscalYears.FindAsync(doc.FiscalYearId);
                string fyYear = fy?.Year ?? DateTime.Today.Year.ToString();
                string jvPrefix = $"JV-{doc.BranchId}-{fyYear}-";

                var existingJVNumbers = await _context.JournalEntryHeaders
                    .IgnoreQueryFilters()
                    .Where(h => h.BranchId == doc.BranchId && h.FiscalYearId == doc.FiscalYearId && h.EntryNumber.StartsWith(jvPrefix))
                    .OrderByDescending(h => h.Id)
                    .Select(h => h.EntryNumber)
                    .Take(200)
                    .ToListAsync();

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
                    .Where(h => h.BranchId == doc.BranchId && h.FiscalYearId == doc.FiscalYearId)
                    .CountAsync();

                if (totalInFY > maxJV)
                    maxJV = totalInFY;

                long candidateJV = maxJV + 1;
                string entryNumber = $"{jvPrefix}{candidateJV:D5}";

                while (await _context.JournalEntryHeaders.IgnoreQueryFilters().AnyAsync(h => h.BranchId == doc.BranchId && h.EntryNumber == entryNumber))
                {
                    candidateJV++;
                    entryNumber = $"{jvPrefix}{candidateJV:D5}";
                }

            SourceDocumentType sourceDoc = tt.Module switch
            {
                TransactionModule.Sales => SourceDocumentType.SalesInvoice,
                TransactionModule.Purchase => SourceDocumentType.PurchaseInvoice,
                _ => SourceDocumentType.ManualJV
            };

            decimal rate = doc.ExchangeRate <= 0 ? 1m : doc.ExchangeRate;
            decimal beforeTaxLocal = Math.Round(doc.TotalAmountBeforeTax * rate, 4);
            decimal taxLocal = Math.Round(doc.TaxAmount * rate, 4);
            decimal discountLocal = Math.Round(doc.DiscountAmount * rate, 4);

            // Separate adjustments
            var discountAdjustments = doc.Adjustments?.Where(a => a.AdjustmentType == AdjustmentType.Discount).ToList() ?? new List<DocumentAdjustmentLine>();
            var additionAdjustments = doc.Adjustments?.Where(a => a.AdjustmentType == AdjustmentType.Addition).ToList() ?? new List<DocumentAdjustmentLine>();

            decimal headerDiscountsLocal = discountAdjustments.Sum(a => Math.Round(a.Amount * rate, 4));
            decimal itemLinesDiscountLocal = Math.Max(0, discountLocal - headerDiscountsLocal);
            decimal headerAdditionsLocal = additionAdjustments.Sum(a => Math.Round(a.Amount * rate, 4));

            // Net balancing: TotalAmountNet = BeforeTax - Discount + Additions + Tax
            // TotalBalancedLocal represents the gross debit/credit sum:
            decimal totalBalancedLocal = beforeTaxLocal + headerAdditionsLocal + (doc.TaxAmount > 0 ? taxLocal : 0m);
            decimal netLocal = totalBalancedLocal - discountLocal;

            var je = new JournalEntryHeader
            {
                BranchId = doc.BranchId,
                FiscalYearId = doc.FiscalYearId,
                EntryNumber = entryNumber,
                EntryDate = doc.DocumentDate,
                SourceDocumentType = sourceDoc,
                SourceDocumentId = doc.Id,
                Status = JournalEntryStatus.Posted,
                PostedAt = DateTime.UtcNow,
                PostedByUserId = userId,
                Description = $"{tt.LocalizedName} رقم {doc.DocumentNumber}",
                ReferenceNumber = doc.DocumentNumber,
                TotalDebitLocal = totalBalancedLocal,
                TotalCreditLocal = totalBalancedLocal,
                CreatedAt = DateTime.UtcNow
            };

            int lineNum = 1;

            if (tt.Module == TransactionModule.Sales)
            {
                // Account Resolution for Sales
                bool isCashWithCustomer = (doc.PaymentMethod == VoucherPaymentMethod.Cash && doc.PartyType == DocumentPartyType.Customer && doc.PartyId.HasValue);
                int customerAccId = 0;
                int cashAccId = 0;
                int partyAccId = 0;

                if (isCashWithCustomer)
                {
                    var cust = await _context.Customers
                        .Include(c => c.CustomerCategory)
                        .FirstOrDefaultAsync(c => c.Id == doc.PartyId!.Value);
                    customerAccId = cust?.FinancialAccountId ?? cust?.CustomerCategory?.ControlAccountId ?? tt.DefaultDebitAccountId ?? 0;
                    if (customerAccId <= 0)
                    {
                        return (false, "لا يمكن ترحيل المستند: لم يتم تحديد الحساب المالي للعميل أو الفئة أو الحساب المدين الافتراضي لنوع الحركة.", null);
                    }

                    if (doc.CashBankAccountId.HasValue && doc.CashBankAccountId.Value > 0)
                    {
                        cashAccId = doc.CashBankAccountId.Value;
                    }
                    else
                    {
                        cashAccId = tt.DefaultDebitAccountId ?? 0;
                    }
                    if (cashAccId <= 0)
                    {
                        return (false, "لا يمكن ترحيل المستند النقدي: لم يتم تحديد حساب الخزينة / البنك للمستند أو الحساب المدين الافتراضي لنوع الحركة.", null);
                    }
                }
                else
                {
                    if (doc.PaymentMethod == VoucherPaymentMethod.Cash && doc.CashBankAccountId.HasValue)
                    {
                        partyAccId = doc.CashBankAccountId.Value;
                    }
                    else if (doc.PartyType == DocumentPartyType.Customer && doc.PartyId.HasValue)
                    {
                        var cust = await _context.Customers
                            .Include(c => c.CustomerCategory)
                            .FirstOrDefaultAsync(c => c.Id == doc.PartyId.Value);
                        partyAccId = cust?.FinancialAccountId ?? cust?.CustomerCategory?.ControlAccountId ?? 0;
                    }
                    if (partyAccId <= 0) partyAccId = tt.DefaultDebitAccountId ?? 0;
                    if (partyAccId <= 0)
                    {
                        return (false, "لا يمكن ترحيل المستند: لم يتم تحديد الحساب المالي للعميل أو الخزينة أو الحساب المدين الافتراضي لنوع الحركة.", null);
                    }
                }

                int revenueAccId = tt.DefaultCreditAccountId ?? 0;
                if (revenueAccId <= 0)
                {
                    return (false, $"لا يمكن ترحيل المستند: لم يتم تحديد حساب الإيرادات (الحساب الدائن الافتراضي) في إعدادات نوع الحركة '{tt.LocalizedName}'.", null);
                }

                int taxAccId = tt.DefaultTaxAccountId ?? 0;
                if (doc.TaxAmount > 0 && taxAccId <= 0)
                {
                    var vatAcc = await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code == "210201" && a.IsActive && !a.IsGroup);
                    taxAccId = vatAcc?.Id ?? 0;
                    if (taxAccId <= 0)
                    {
                        return (false, $"لا يمكن ترحيل المستند لوجود ضريبة بدون تحديد حساب الضريبة في إعدادات نوع الحركة '{tt.LocalizedName}'.", null);
                    }
                }

                int discountAccId = tt.DefaultDiscountAccountId ?? 0;
                if (itemLinesDiscountLocal > 0 && discountAccId <= 0)
                {
                    var discAcc = await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code == "410202" && a.IsActive && !a.IsGroup)
                               ?? await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code == "410201" && a.IsActive && !a.IsGroup);
                    discountAccId = discAcc?.Id ?? 0;
                    if (discountAccId <= 0)
                    {
                        return (false, $"لا يمكن ترحيل المستند لوجود خصم أصناف بدون تحديد حساب الخصم في إعدادات نوع الحركة '{tt.LocalizedName}'.", null);
                    }
                }

                bool isReturn = tt.BaseNature == DocumentBaseNature.Return;

                if (!isReturn)
                {
                    // Sales Invoice:
                    if (isCashWithCustomer)
                    {
                        // 1. حـ/ العميل (الصافي المستحق على العميل) [مدين]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = customerAccId,
                            SubLedgerType = SubLedgerType.Customer,
                            SubLedgerId = doc.PartyId,
                            DebitLocal = netLocal,
                            CreditLocal = 0m,
                            DebitForeign = doc.TotalAmountNet,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (الصافي المستحق على العميل)",
                            CreatedAt = DateTime.UtcNow
                        });

                        // 2. حـ/ الصندوق أو الخزينة [مدين]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = cashAccId,
                            SubLedgerType = SubLedgerType.None,
                            SubLedgerId = null,
                            DebitLocal = netLocal,
                            CreditLocal = 0m,
                            DebitForeign = doc.TotalAmountNet,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تحصيل نقدي بالخزينة)",
                            CreatedAt = DateTime.UtcNow
                        });

                        // 3. حـ/ العميل (سداد نقدي فوري للفاتورة) [دائن]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = customerAccId,
                            SubLedgerType = SubLedgerType.Customer,
                            SubLedgerId = doc.PartyId,
                            DebitLocal = 0m,
                            CreditLocal = netLocal,
                            DebitForeign = 0m,
                            CreditForeign = doc.TotalAmountNet,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (سداد نقدي فوري للفاتورة)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        // 1. حـ/ الصندوق أو العميل (الصافي) [مدين]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = partyAccId,
                            SubLedgerType = (doc.PartyType == DocumentPartyType.Customer) ? SubLedgerType.Customer : SubLedgerType.None,
                            SubLedgerId = (doc.PartyType == DocumentPartyType.Customer) ? doc.PartyId : null,
                            DebitLocal = netLocal,
                            CreditLocal = 0m,
                            DebitForeign = doc.TotalAmountNet,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (الصافي المقبوض / المستحق)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // 2. سطور الخصومات الإضافية المخصصة لكل بند [مدين]
                    foreach (var da in discountAdjustments)
                    {
                        decimal daLocal = Math.Round(da.Amount * rate, 4);
                        if (daLocal <= 0) continue;
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = da.GLAccountId,
                            DebitLocal = daLocal,
                            CreditLocal = 0m,
                            DebitForeign = da.Amount,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} ({da.DocumentAdjustment?.LocalizedName ?? "خصم إضافي"})",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // 3. خصم الأصناف (إن وُجد متبقي) [مدين]
                    if (itemLinesDiscountLocal > 0)
                    {
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = discountAccId,
                            DebitLocal = itemLinesDiscountLocal,
                            CreditLocal = 0m,
                            DebitForeign = doc.DiscountAmount - discountAdjustments.Sum(a => a.Amount),
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (خصم تجاري للأصناف)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // إلى مذكورين:
                    // 4. حـ/ إيرادات المبيعات [دائن]
                    je.Lines.Add(new JournalEntryLine
                    {
                        LineNumber = lineNum++,
                        AccountId = revenueAccId,
                        DebitLocal = 0m,
                        CreditLocal = beforeTaxLocal,
                        DebitForeign = 0m,
                        CreditForeign = doc.TotalAmountBeforeTax,
                        CurrencyId = doc.CurrencyId,
                        ExchangeRate = doc.ExchangeRate,
                        Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (إيرادات المبيعات)",
                        CreatedAt = DateTime.UtcNow
                    });

                    // 5. سطور الإضافات والمصاريف المخصصة لكل بند [دائن]
                    foreach (var aa in additionAdjustments)
                    {
                        decimal aaLocal = Math.Round(aa.Amount * rate, 4);
                        if (aaLocal <= 0) continue;
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = aa.GLAccountId,
                            DebitLocal = 0m,
                            CreditLocal = aaLocal,
                            DebitForeign = 0m,
                            CreditForeign = aa.Amount,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} ({aa.DocumentAdjustment?.LocalizedName ?? "إضافة / مصاريف"})",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // 6. حـ/ ضريبة القيمة المضافة [دائن]
                    if (doc.TaxAmount > 0)
                    {
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = taxAccId,
                            DebitLocal = 0m,
                            CreditLocal = taxLocal,
                            DebitForeign = 0m,
                            CreditForeign = doc.TaxAmount,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (ضريبة القيمة المضافة)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // 7. Automatic COGS Journal Entry: Debit COGS, Credit Inventory
                    if (tt.AffectsInventory && doc.Lines != null && doc.Lines.Any())
                    {
                        var inventoryLines = doc.Lines
                            .Where(l => (l.Item == null || !l.Item.IsService) && l.TotalCost > 0)
                            .ToList();

                        decimal totalCogsLocal = inventoryLines.Sum(l => l.TotalCost);
                        if (totalCogsLocal > 0)
                        {
                            int cogsAccId = tt.DefaultCogsAccountId ?? 0;
                            if (cogsAccId <= 0)
                            {
                                var cogsAcc = await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code == "510101" && a.IsActive && !a.IsGroup)
                                           ?? await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code.StartsWith("51") && a.IsActive && !a.IsGroup);
                                cogsAccId = cogsAcc?.Id ?? 0;
                            }

                            if (cogsAccId <= 0)
                            {
                                return (false, $"لا يمكن ترحيل المستند: لم يتم تحديد حساب تكلفة البضاعة المباعة في نوع الحركة ({tt.LocalizedName}) ولا يوجد حساب برمز 510101 في شجرة الحسابات.", null);
                            }

                            // Debit: Cost of Goods Sold
                            je.Lines.Add(new JournalEntryLine
                            {
                                LineNumber = lineNum++,
                                AccountId = cogsAccId,
                                DebitLocal = totalCogsLocal,
                                CreditLocal = 0m,
                                DebitForeign = 0m,
                                CreditForeign = 0m,
                                CurrencyId = doc.CurrencyId,
                                ExchangeRate = doc.ExchangeRate,
                                Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تكلفة البضاعة المباعة COGS)",
                                CreatedAt = DateTime.UtcNow
                            });

                            // Credit: Inventory by Warehouse
                            var invGroups = inventoryLines.GroupBy(l => l.WarehouseId ?? doc.WarehouseId ?? tt.DefaultWarehouseId ?? 0);
                            foreach (var grp in invGroups)
                            {
                                int whId = grp.Key;
                                var wh = whId > 0 ? await _context.Warehouses.FindAsync(whId) : null;
                                int invAccId = wh?.InventoryAccountId ?? tt.DefaultInventoryAccountId ?? 0;
                                if (invAccId <= 0)
                                {
                                    var invAcc = await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code == "110301" && a.IsActive && !a.IsGroup)
                                              ?? await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code.StartsWith("1103") && a.IsActive && !a.IsGroup)
                                              ?? await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code.StartsWith("11") && a.IsActive && !a.IsGroup);
                                    invAccId = invAcc?.Id ?? 0;
                                }

                                if (invAccId <= 0)
                                {
                                    return (false, $"لا يمكن ترحيل المستند: لم يتم تحديد حساب المخزون للمستودع ولا الحساب الافتراضي في نوع الحركة ({tt.LocalizedName}) ولا حساب برمز 110301 في شجرة الحسابات.", null);
                                }

                                decimal whCostLocal = grp.Sum(l => l.TotalCost);
                                je.Lines.Add(new JournalEntryLine
                                {
                                    LineNumber = lineNum++,
                                    AccountId = invAccId,
                                    DebitLocal = 0m,
                                    CreditLocal = whCostLocal,
                                    DebitForeign = 0m,
                                    CreditForeign = 0m,
                                    CurrencyId = doc.CurrencyId,
                                    ExchangeRate = doc.ExchangeRate,
                                    Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تخفيض المخزون بتكلفة البضاعة المباعة)",
                                    CreatedAt = DateTime.UtcNow
                                });
                            }
                        }
                    }
                }
                else
                {
                    // Sales Return:
                    // من مذكورين:
                    // حـ/ مردودات ومسموحات المبيعات [مدين]
                    je.Lines.Add(new JournalEntryLine
                    {
                        LineNumber = lineNum++,
                        AccountId = revenueAccId,
                        DebitLocal = beforeTaxLocal,
                        CreditLocal = 0m,
                        DebitForeign = doc.TotalAmountBeforeTax,
                        CreditForeign = 0m,
                        CurrencyId = doc.CurrencyId,
                        ExchangeRate = doc.ExchangeRate,
                        Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (مردودات مبيعات)",
                        CreatedAt = DateTime.UtcNow
                    });

                    // تسوية الإضافات في المردودات [مدين]
                    foreach (var aa in additionAdjustments)
                    {
                        decimal aaLocal = Math.Round(aa.Amount * rate, 4);
                        if (aaLocal <= 0) continue;
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = aa.GLAccountId,
                            DebitLocal = aaLocal,
                            CreditLocal = 0m,
                            DebitForeign = aa.Amount,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تسوية {aa.DocumentAdjustment?.LocalizedName ?? "إضافة"})",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // حـ/ ضريبة القيمة المضافة المستردة [مدين]
                    if (doc.TaxAmount > 0)
                    {
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = taxAccId,
                            DebitLocal = taxLocal,
                            CreditLocal = 0m,
                            DebitForeign = doc.TaxAmount,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تسوية ضريبة المبيعات)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // إلى مذكورين:
                    if (isCashWithCustomer)
                    {
                        // 1. حـ/ العميل (الصافي المستحق للعميل - مردودات) [دائن]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = customerAccId,
                            SubLedgerType = SubLedgerType.Customer,
                            SubLedgerId = doc.PartyId,
                            DebitLocal = 0m,
                            CreditLocal = netLocal,
                            DebitForeign = 0m,
                            CreditForeign = doc.TotalAmountNet,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (الصافي المستحق للعميل - مردودات)",
                            CreatedAt = DateTime.UtcNow
                        });

                        // 2. حـ/ العميل (صرف نقدي فوري للمردودات) [مدين]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = customerAccId,
                            SubLedgerType = SubLedgerType.Customer,
                            SubLedgerId = doc.PartyId,
                            DebitLocal = netLocal,
                            CreditLocal = 0m,
                            DebitForeign = doc.TotalAmountNet,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (صرف نقدي فوري للمردودات)",
                            CreatedAt = DateTime.UtcNow
                        });

                        // 3. حـ/ الصندوق أو الخزينة (صرف نقدي من الخزينة) [دائن]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = cashAccId,
                            SubLedgerType = SubLedgerType.None,
                            SubLedgerId = null,
                            DebitLocal = 0m,
                            CreditLocal = netLocal,
                            DebitForeign = 0m,
                            CreditForeign = doc.TotalAmountNet,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (صرف نقدي من الخزينة)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        // حـ/ الصندوق أو العميل [دائن]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = partyAccId,
                            SubLedgerType = (doc.PartyType == DocumentPartyType.Customer) ? SubLedgerType.Customer : SubLedgerType.None,
                            SubLedgerId = (doc.PartyType == DocumentPartyType.Customer) ? doc.PartyId : null,
                            DebitLocal = 0m,
                            CreditLocal = netLocal,
                            DebitForeign = 0m,
                            CreditForeign = doc.TotalAmountNet,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (الصافي المستحق للعميل)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // تسوية الخصومات في المردودات [دائن]
                    foreach (var da in discountAdjustments)
                    {
                        decimal daLocal = Math.Round(da.Amount * rate, 4);
                        if (daLocal <= 0) continue;
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = da.GLAccountId,
                            DebitLocal = 0m,
                            CreditLocal = daLocal,
                            DebitForeign = 0m,
                            CreditForeign = da.Amount,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تسوية {da.DocumentAdjustment?.LocalizedName ?? "خصم"})",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    if (itemLinesDiscountLocal > 0)
                    {
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = discountAccId,
                            DebitLocal = 0m,
                            CreditLocal = itemLinesDiscountLocal,
                            DebitForeign = 0m,
                            CreditForeign = doc.DiscountAmount - discountAdjustments.Sum(a => a.Amount),
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تسوية خصم الأصناف)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // Automatic Sales Return COGS reversal: Debit Inventory, Credit COGS
                    if (tt.AffectsInventory && doc.Lines != null && doc.Lines.Any())
                    {
                        var inventoryLines = doc.Lines
                            .Where(l => (l.Item == null || !l.Item.IsService) && l.TotalCost > 0)
                            .ToList();

                        decimal totalCogsLocal = inventoryLines.Sum(l => l.TotalCost);
                        if (totalCogsLocal > 0)
                        {
                            int cogsAccId = tt.DefaultCogsAccountId ?? 0;
                            if (cogsAccId <= 0)
                            {
                                var cogsAcc = await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code == "510101" && a.IsActive && !a.IsGroup)
                                           ?? await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code.StartsWith("51") && a.IsActive && !a.IsGroup);
                                cogsAccId = cogsAcc?.Id ?? 0;
                            }

                            if (cogsAccId <= 0)
                            {
                                return (false, $"لا يمكن ترحيل المستند: لم يتم تحديد حساب تكلفة البضاعة المباعة في نوع الحركة ({tt.LocalizedName}) ولا يوجد حساب برمز 510101 في شجرة الحسابات.", null);
                            }

                            // Debit: Inventory by Warehouse (restore historical inventory value)
                            var invGroups = inventoryLines.GroupBy(l => l.WarehouseId ?? doc.WarehouseId ?? tt.DefaultWarehouseId ?? 0);
                            foreach (var grp in invGroups)
                            {
                                int whId = grp.Key;
                                var wh = whId > 0 ? await _context.Warehouses.FindAsync(whId) : null;
                                int invAccId = wh?.InventoryAccountId ?? tt.DefaultInventoryAccountId ?? 0;
                                if (invAccId <= 0)
                                {
                                    var invAcc = await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code == "110301" && a.IsActive && !a.IsGroup)
                                              ?? await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code.StartsWith("1103") && a.IsActive && !a.IsGroup)
                                              ?? await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code.StartsWith("11") && a.IsActive && !a.IsGroup);
                                    invAccId = invAcc?.Id ?? 0;
                                }

                                if (invAccId <= 0)
                                {
                                    return (false, $"لا يمكن ترحيل المستند: لم يتم تحديد حساب المخزون للمستودع ولا الحساب الافتراضي في نوع الحركة ({tt.LocalizedName}) ولا حساب برمز 110301 في شجرة الحسابات.", null);
                                }

                                decimal whCostLocal = grp.Sum(l => l.TotalCost);
                                je.Lines.Add(new JournalEntryLine
                                {
                                    LineNumber = lineNum++,
                                    AccountId = invAccId,
                                    DebitLocal = whCostLocal,
                                    CreditLocal = 0m,
                                    DebitForeign = 0m,
                                    CreditForeign = 0m,
                                    CurrencyId = doc.CurrencyId,
                                    ExchangeRate = doc.ExchangeRate,
                                    Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (إعادة إثبات المخزون بالتكلفة التاريخية)",
                                    CreatedAt = DateTime.UtcNow
                                });
                            }

                            // Credit: Cost of Goods Sold (reduce COGS)
                            je.Lines.Add(new JournalEntryLine
                            {
                                LineNumber = lineNum++,
                                AccountId = cogsAccId,
                                DebitLocal = 0m,
                                CreditLocal = totalCogsLocal,
                                DebitForeign = 0m,
                                CreditForeign = 0m,
                                CurrencyId = doc.CurrencyId,
                                ExchangeRate = doc.ExchangeRate,
                                Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تسوية تكلفة البضاعة المباعة COGS)",
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                    }
                }
            }
            else if (tt.Module == TransactionModule.Purchase)
            {
                // Account Resolution for Purchase
                bool isCashWithVendor = (doc.PaymentMethod == VoucherPaymentMethod.Cash && doc.PartyType == DocumentPartyType.Vendor && doc.PartyId.HasValue);
                int vendorAccId = 0;
                int cashAccId = 0;
                int partyAccId = 0;

                if (isCashWithVendor)
                {
                    var vend = await _context.Vendors
                        .Include(v => v.VendorCategory)
                        .FirstOrDefaultAsync(v => v.Id == doc.PartyId!.Value);
                    vendorAccId = vend?.FinancialAccountId ?? vend?.VendorCategory?.ControlAccountId ?? tt.DefaultCreditAccountId ?? 0;
                    if (vendorAccId <= 0)
                    {
                        return (false, "لا يمكن ترحيل المستند: لم يتم تحديد الحساب المالي للمورد أو الفئة أو الحساب الدائن الافتراضي لنوع الحركة.", null);
                    }

                    if (doc.CashBankAccountId.HasValue && doc.CashBankAccountId.Value > 0)
                    {
                        cashAccId = doc.CashBankAccountId.Value;
                    }
                    else
                    {
                        cashAccId = tt.DefaultCreditAccountId ?? 0;
                    }
                    if (cashAccId <= 0)
                    {
                        return (false, "لا يمكن ترحيل المستند النقدي: لم يتم تحديد حساب الخزينة / البنك للمستند أو الحساب الدائن الافتراضي لنوع الحركة.", null);
                    }
                }
                else
                {
                    if (doc.PaymentMethod == VoucherPaymentMethod.Cash && doc.CashBankAccountId.HasValue)
                    {
                        partyAccId = doc.CashBankAccountId.Value;
                    }
                    else if (doc.PartyType == DocumentPartyType.Vendor && doc.PartyId.HasValue)
                    {
                        var vend = await _context.Vendors
                            .Include(v => v.VendorCategory)
                            .FirstOrDefaultAsync(v => v.Id == doc.PartyId.Value);
                        partyAccId = vend?.FinancialAccountId ?? vend?.VendorCategory?.ControlAccountId ?? 0;
                    }
                    if (partyAccId <= 0) partyAccId = tt.DefaultCreditAccountId ?? 0;
                    if (partyAccId <= 0)
                    {
                        return (false, "لا يمكن ترحيل المستند: لم يتم تحديد الحساب المالي للمورد أو الخزينة أو الحساب الدائن الافتراضي لنوع الحركة.", null);
                    }
                }

                int expenseAccId = tt.DefaultDebitAccountId ?? 0;
                if (expenseAccId <= 0)
                {
                    return (false, $"لا يمكن ترحيل المستند: لم يتم تحديد حساب المشتريات / المخزون (الحساب المدين الافتراضي) في إعدادات نوع الحركة '{tt.LocalizedName}'.", null);
                }

                int taxAccId = tt.DefaultTaxAccountId ?? 0;
                if (doc.TaxAmount > 0 && taxAccId <= 0)
                {
                    var vatAcc = await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code == "210201" && a.IsActive && !a.IsGroup);
                    taxAccId = vatAcc?.Id ?? 0;
                    if (taxAccId <= 0)
                    {
                        return (false, $"لا يمكن ترحيل المستند لوجود ضريبة بدون تحديد حساب الضريبة في إعدادات نوع الحركة '{tt.LocalizedName}'.", null);
                    }
                }

                int discountAccId = tt.DefaultDiscountAccountId ?? 0;
                if (itemLinesDiscountLocal > 0 && discountAccId <= 0)
                {
                    var discAcc = await _context.GLAccounts.FirstOrDefaultAsync(a => a.Code == "510201" && a.IsActive && !a.IsGroup);
                    discountAccId = discAcc?.Id ?? 0;
                    if (discountAccId <= 0)
                    {
                        return (false, $"لا يمكن ترحيل المستند لوجود خصم أصناف بدون تحديد حساب الخصم في إعدادات نوع الحركة '{tt.LocalizedName}'.", null);
                    }
                }

                bool isReturn = tt.BaseNature == DocumentBaseNature.Return;

                if (!isReturn)
                {
                    // Purchase Invoice:
                    // من مذكورين:
                    // 1. حـ/ المشتريات أو المخزون [مدين]
                    je.Lines.Add(new JournalEntryLine
                    {
                        LineNumber = lineNum++,
                        AccountId = expenseAccId,
                        DebitLocal = beforeTaxLocal,
                        CreditLocal = 0m,
                        DebitForeign = doc.TotalAmountBeforeTax,
                        CreditForeign = 0m,
                        CurrencyId = doc.CurrencyId,
                        ExchangeRate = doc.ExchangeRate,
                        Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (المشتريات / المخزون)",
                        CreatedAt = DateTime.UtcNow
                    });

                    // 2. سطور الإضافات ومصاريف الشحن/النولون [مدين]
                    foreach (var aa in additionAdjustments)
                    {
                        decimal aaLocal = Math.Round(aa.Amount * rate, 4);
                        if (aaLocal <= 0) continue;
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = aa.GLAccountId,
                            DebitLocal = aaLocal,
                            CreditLocal = 0m,
                            DebitForeign = aa.Amount,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} ({aa.DocumentAdjustment?.LocalizedName ?? "مصاريف إضافية"})",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // 3. حـ/ ضريبة القيمة المضافة (مدخلات) [مدين]
                    if (doc.TaxAmount > 0)
                    {
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = taxAccId,
                            DebitLocal = taxLocal,
                            CreditLocal = 0m,
                            DebitForeign = doc.TaxAmount,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (ضريبة القيمة المضافة - مدخلات)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // إلى مذكورين:
                    if (isCashWithVendor)
                    {
                        // 4.1 حـ/ المورد (الصافي المستحق للمورد) [دائن]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = vendorAccId,
                            SubLedgerType = SubLedgerType.Vendor,
                            SubLedgerId = doc.PartyId,
                            DebitLocal = 0m,
                            CreditLocal = netLocal,
                            DebitForeign = 0m,
                            CreditForeign = doc.TotalAmountNet,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (الصافي المستحق للمورد)",
                            CreatedAt = DateTime.UtcNow
                        });

                        // 4.2 حـ/ المورد (سداد نقدي فوري للفاتورة) [مدين]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = vendorAccId,
                            SubLedgerType = SubLedgerType.Vendor,
                            SubLedgerId = doc.PartyId,
                            DebitLocal = netLocal,
                            CreditLocal = 0m,
                            DebitForeign = doc.TotalAmountNet,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (سداد نقدي فوري للمورد)",
                            CreatedAt = DateTime.UtcNow
                        });

                        // 4.3 حـ/ الصندوق (صرف نقدي من الخزينة) [دائن]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = cashAccId,
                            SubLedgerType = SubLedgerType.None,
                            SubLedgerId = null,
                            DebitLocal = 0m,
                            CreditLocal = netLocal,
                            DebitForeign = 0m,
                            CreditForeign = doc.TotalAmountNet,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (صرف نقدي من الخزينة)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        // 4. حـ/ المورد أو الصندوق [دائن]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = partyAccId,
                            SubLedgerType = (doc.PaymentMethod != VoucherPaymentMethod.Cash && doc.PartyType == DocumentPartyType.Vendor) ? SubLedgerType.Vendor : SubLedgerType.None,
                            SubLedgerId = (doc.PaymentMethod != VoucherPaymentMethod.Cash && doc.PartyType == DocumentPartyType.Vendor) ? doc.PartyId : null,
                            DebitLocal = 0m,
                            CreditLocal = netLocal,
                            DebitForeign = 0m,
                            CreditForeign = doc.TotalAmountNet,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (الصافي المستحق للمورد / المسدد)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // 5. سطور الخصومات المكتسبة المخصصة [دائن]
                    foreach (var da in discountAdjustments)
                    {
                        decimal daLocal = Math.Round(da.Amount * rate, 4);
                        if (daLocal <= 0) continue;
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = da.GLAccountId,
                            DebitLocal = 0m,
                            CreditLocal = daLocal,
                            DebitForeign = 0m,
                            CreditForeign = da.Amount,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} ({da.DocumentAdjustment?.LocalizedName ?? "خصم مكتسب"})",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // 6. خصم الأصناف (إن وُجد) [دائن]
                    if (itemLinesDiscountLocal > 0)
                    {
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = discountAccId,
                            DebitLocal = 0m,
                            CreditLocal = itemLinesDiscountLocal,
                            DebitForeign = 0m,
                            CreditForeign = doc.DiscountAmount - discountAdjustments.Sum(a => a.Amount),
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (خصم الأصناف)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
                else
                {
                    // Purchase Return:
                    // من مذكورين:
                    if (isCashWithVendor)
                    {
                        // 1. حـ/ المورد (الصافي المسترد من المورد) [مدين]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = vendorAccId,
                            SubLedgerType = SubLedgerType.Vendor,
                            SubLedgerId = doc.PartyId,
                            DebitLocal = netLocal,
                            CreditLocal = 0m,
                            DebitForeign = doc.TotalAmountNet,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (الصافي المسترد من المورد)",
                            CreatedAt = DateTime.UtcNow
                        });

                        // 2. حـ/ الصندوق (تحصيل نقدي بالخزينة) [مدين]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = cashAccId,
                            SubLedgerType = SubLedgerType.None,
                            SubLedgerId = null,
                            DebitLocal = netLocal,
                            CreditLocal = 0m,
                            DebitForeign = doc.TotalAmountNet,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تحصيل نقدي بالخزينة)",
                            CreatedAt = DateTime.UtcNow
                        });

                        // 3. حـ/ المورد (تسوية المردودات نقداً) [دائن]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = vendorAccId,
                            SubLedgerType = SubLedgerType.Vendor,
                            SubLedgerId = doc.PartyId,
                            DebitLocal = 0m,
                            CreditLocal = netLocal,
                            DebitForeign = 0m,
                            CreditForeign = doc.TotalAmountNet,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تسوية المردودات نقداً)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        // حـ/ المورد أو الصندوق [مدين]
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = partyAccId,
                            SubLedgerType = (doc.PartyType == DocumentPartyType.Vendor) ? SubLedgerType.Vendor : SubLedgerType.None,
                            SubLedgerId = (doc.PartyType == DocumentPartyType.Vendor) ? doc.PartyId : null,
                            DebitLocal = netLocal,
                            CreditLocal = 0m,
                            DebitForeign = doc.TotalAmountNet,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (الصافي المسترد من المورد)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // تسوية الخصومات في المردودات [مدين]
                    foreach (var da in discountAdjustments)
                    {
                        decimal daLocal = Math.Round(da.Amount * rate, 4);
                        if (daLocal <= 0) continue;
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = da.GLAccountId,
                            DebitLocal = daLocal,
                            CreditLocal = 0m,
                            DebitForeign = da.Amount,
                            CreditForeign = 0m,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تسوية {da.DocumentAdjustment?.LocalizedName ?? "خصم"})",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    if (itemLinesDiscountLocal > 0)
                    {
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = discountAccId,
                            DebitLocal = itemLinesDiscountLocal,
                            CreditLocal = 0m,
                            DebitForeign = doc.DiscountAmount - discountAdjustments.Sum(a => a.Amount),
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تسوية خصم الأصناف)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // إلى مذكورين:
                    // حـ/ مردودات المشتريات أو المخزون [دائن]
                    je.Lines.Add(new JournalEntryLine
                    {
                        LineNumber = lineNum++,
                        AccountId = expenseAccId,
                        DebitLocal = 0m,
                        CreditLocal = beforeTaxLocal,
                        DebitForeign = 0m,
                        CreditForeign = doc.TotalAmountBeforeTax,
                        CurrencyId = doc.CurrencyId,
                        ExchangeRate = doc.ExchangeRate,
                        Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (مردودات مشتريات)",
                        CreatedAt = DateTime.UtcNow
                    });

                    // تسوية الإضافات في المردودات [دائن]
                    foreach (var aa in additionAdjustments)
                    {
                        decimal aaLocal = Math.Round(aa.Amount * rate, 4);
                        if (aaLocal <= 0) continue;
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = aa.GLAccountId,
                            DebitLocal = 0m,
                            CreditLocal = aaLocal,
                            DebitForeign = 0m,
                            CreditForeign = aa.Amount,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تسوية {aa.DocumentAdjustment?.LocalizedName ?? "إضافة"})",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    // حـ/ ضريبة القيمة المضافة [دائن]
                    if (doc.TaxAmount > 0)
                    {
                        je.Lines.Add(new JournalEntryLine
                        {
                            LineNumber = lineNum++,
                            AccountId = taxAccId,
                            DebitLocal = 0m,
                            CreditLocal = taxLocal,
                            DebitForeign = 0m,
                            CreditForeign = doc.TaxAmount,
                            CurrencyId = doc.CurrencyId,
                            ExchangeRate = doc.ExchangeRate,
                            Description = $"{tt.LocalizedName} - {doc.DocumentNumber} (تسوية ضريبة المدخلات)",
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
            }
            else
            {
                // Other transaction modules (Inventory Transfer, General, etc.)
                int debitAccId = tt.DefaultDebitAccountId ?? 0;
                int creditAccId = tt.DefaultCreditAccountId ?? 0;

                if (debitAccId <= 0 || creditAccId <= 0)
                {
                    return (false, $"لا يمكن ترحيل المستند: يرجى تحديد الحساب المدين والحساب الدائن الافتراضي في إعدادات نوع الحركة '{tt.LocalizedName}'.", null);
                }

                je.Lines.Add(new JournalEntryLine
                {
                    LineNumber = lineNum++,
                    AccountId = debitAccId,
                    DebitLocal = netLocal,
                    CreditLocal = 0m,
                    DebitForeign = doc.TotalAmountNet,
                    CreditForeign = 0m,
                    CurrencyId = doc.CurrencyId,
                    ExchangeRate = doc.ExchangeRate,
                    Description = $"{tt.LocalizedName} - {doc.DocumentNumber}",
                    CreatedAt = DateTime.UtcNow
                });

                je.Lines.Add(new JournalEntryLine
                {
                    LineNumber = lineNum++,
                    AccountId = creditAccId,
                    DebitLocal = 0m,
                    CreditLocal = netLocal,
                    DebitForeign = 0m,
                    CreditForeign = doc.TotalAmountNet,
                    CurrencyId = doc.CurrencyId,
                    ExchangeRate = doc.ExchangeRate,
                    Description = $"{tt.LocalizedName} - {doc.DocumentNumber}",
                    CreatedAt = DateTime.UtcNow
                });
            }

                // Recalculate totals from lines to ensure strict mathematical balancing
                decimal totalDebits = Math.Round(je.Lines.Sum(l => l.DebitLocal), 4);
                decimal totalCredits = Math.Round(je.Lines.Sum(l => l.CreditLocal), 4);

                if (Math.Abs(totalDebits - totalCredits) > 0.005m)
                {
                    return (false, $"القيد المحاسبي غير متوازن: إجمالي المدين ({totalDebits}) لا يساوي إجمالي الدائن ({totalCredits}).", null);
                }

                je.TotalDebitLocal = totalDebits;
                je.TotalCreditLocal = totalCredits;

                _context.JournalEntryHeaders.Add(je);
                await _context.SaveChangesAsync();
                return (true, "تم إنشاء القيد بنجاح.", je.Id);
            }
            catch (Exception ex)
            {
                string msg = ex.InnerException?.Message ?? ex.Message;
                return (false, $"فشل حفظ القيد المحاسبي في قاعدة البيانات: {msg}", null);
            }
            finally
            {
                jvLock.Release();
            }
        }

        private bool ResolveAllowNegativeStock(DocumentHeaderDto dto)
        {
            if (dto.AllowNegativeStock.HasValue)
                return dto.AllowNegativeStock.Value;

            if (_availabilityService != null)
                return !_availabilityService.PreventNegativeStock;

            return false;
        }
    }
}
