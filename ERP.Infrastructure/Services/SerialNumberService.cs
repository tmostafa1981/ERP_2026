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
    public class SerialNumberService : ISerialNumberService
    {
        private readonly ErpDbContext _context;
        private static readonly SemaphoreSlim _serialLock = new(1, 1);

        public SerialNumberService(ErpDbContext context)
        {
            _context = context;
        }

        public async Task<List<SerialLookupDto>> GetAvailableSerialsAsync(int itemId, int warehouseId, int branchId)
        {
            var now = DateTime.UtcNow;
            return await _context.ItemSerialNumbers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(s => s.Item)
                .Include(s => s.Warehouse)
                .Include(s => s.Branch)
                .Where(s => s.ItemId == itemId &&
                            s.WarehouseId == warehouseId &&
                            s.BranchId == branchId &&
                            (s.Status == ItemSerialStatus.Available || s.Status == ItemSerialStatus.InStock) &&
                            (!s.ReservedDocId.HasValue || (s.ReservedUntil.HasValue && s.ReservedUntil.Value < now)))
                .OrderBy(s => s.SerialNumber)
                .Select(s => new SerialLookupDto
                {
                    Id = s.Id,
                    ItemId = s.ItemId,
                    ItemCode = s.Item != null ? s.Item.Code : "",
                    ItemName = s.Item != null ? s.Item.LocalizedName : "",
                    SerialNumber = s.SerialNumber,
                    BranchId = s.BranchId,
                    BranchName = s.Branch != null ? s.Branch.NameAr : "",
                    WarehouseId = s.WarehouseId,
                    WarehouseName = s.Warehouse != null ? s.Warehouse.LocalizedName : "",
                    Status = s.Status,
                    StatusNameAr = GetStatusNameAr(s.Status),
                    InDate = s.InDate,
                    Notes = s.Notes
                })
                .ToListAsync();
        }

        public async Task<PagedSerialLookupResultDto> GetAvailableSerialsPagedAsync(AvailableSerialsQueryDto query)
        {
            int page = query.PageNumber <= 0 ? 1 : query.PageNumber;
            int pageSize = query.PageSize <= 0 ? 50 : Math.Min(query.PageSize, 500);
            var now = DateTime.UtcNow;

            var itemInfo = await _context.Items
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(i => i.Id == query.ItemId)
                .Select(i => new { i.Code, i.NameAr, i.NameEn })
                .FirstOrDefaultAsync();

            var warehouseInfo = await _context.Warehouses
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(w => w.Id == query.WarehouseId)
                .Select(w => new { w.NameAr, w.NameEn })
                .FirstOrDefaultAsync();

            var baseQuery = _context.ItemSerialNumbers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(s => s.Item)
                .Include(s => s.Warehouse)
                .Include(s => s.Branch)
                .Where(s => s.ItemId == query.ItemId &&
                            s.WarehouseId == query.WarehouseId &&
                            s.BranchId == query.BranchId &&
                            (((s.Status == ItemSerialStatus.Available || s.Status == ItemSerialStatus.InStock) &&
                              (!s.ReservedDocId.HasValue || (s.ReservedUntil.HasValue && s.ReservedUntil.Value < now))) ||
                             (query.ExcludeDocumentId.HasValue && s.OutDocId == query.ExcludeDocumentId.Value)));

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim();
                baseQuery = baseQuery.Where(s => s.SerialNumber.Contains(term));
            }

            var totalCount = await baseQuery.CountAsync();

            var rows = await baseQuery
                .OrderBy(s => s.SerialNumber)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new SerialLookupDto
                {
                    Id = s.Id,
                    ItemId = s.ItemId,
                    ItemCode = s.Item != null ? s.Item.Code : (itemInfo != null ? itemInfo.Code : ""),
                    ItemName = s.Item != null ? s.Item.NameAr : (itemInfo != null ? itemInfo.NameAr : ""),
                    SerialNumber = s.SerialNumber,
                    BranchId = s.BranchId,
                    BranchName = s.Branch != null ? s.Branch.NameAr : "",
                    WarehouseId = s.WarehouseId,
                    WarehouseName = s.Warehouse != null ? s.Warehouse.NameAr : (warehouseInfo != null ? warehouseInfo.NameAr : ""),
                    Status = (query.ExcludeDocumentId.HasValue && s.OutDocId == query.ExcludeDocumentId.Value) ? ItemSerialStatus.Available : s.Status,
                    StatusNameAr = GetStatusNameAr((query.ExcludeDocumentId.HasValue && s.OutDocId == query.ExcludeDocumentId.Value) ? ItemSerialStatus.Available : s.Status),
                    InDate = s.InDate,
                    Notes = s.Notes
                })
                .ToListAsync();

            return new PagedSerialLookupResultDto
            {
                Items = rows,
                TotalCount = totalCount,
                PageNumber = page,
                PageSize = pageSize,
                ItemCode = itemInfo?.Code ?? "",
                ItemName = itemInfo?.NameAr ?? "",
                WarehouseName = warehouseInfo?.NameAr ?? ""
            };
        }

        public async Task<SerialBatchValidationResponseDto> ValidateBatchForReceiveAsync(SerialBatchValidationRequestDto request)
        {
            var response = new SerialBatchValidationResponseDto();
            if (request == null || request.RawSerials == null || !request.RawSerials.Any())
                return response;

            // Extract tokens supporting Excel Tab (\t), Enter (\r, \n), Comma (,), Semicolon (;)
            // Never parse as numbers; preserve original strings and leading zeros.
            var tokens = new List<string>();
            var separators = new[] { '\t', '\r', '\n', ',', ';' };
            foreach (var rawEntry in request.RawSerials)
            {
                if (string.IsNullOrWhiteSpace(rawEntry))
                    continue;

                var parts = rawEntry.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        tokens.Add(trimmed);
                    }
                }
            }

            if (!tokens.Any())
                return response;

            var existingInLineSet = new HashSet<string>(
                (request.ExistingLineSerials ?? new List<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim()),
                StringComparer.Ordinal);

            var distinctCandidateSerials = tokens
                .Where(t => t.Length <= 100)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var dbSerialsDict = new Dictionary<string, ItemSerialNumber>(StringComparer.Ordinal);
            if (request.ItemId > 0 && distinctCandidateSerials.Any())
            {
                var dbMatches = await _context.ItemSerialNumbers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(s => s.ItemId == request.ItemId && distinctCandidateSerials.Contains(s.SerialNumber))
                    .ToListAsync();

                foreach (var dbS in dbMatches)
                {
                    dbSerialsDict[dbS.SerialNumber] = dbS;
                }
            }

            var seenInPaste = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sn in tokens)
            {
                // 1. Validate format / length / control chars
                if (string.IsNullOrWhiteSpace(sn) || sn.Length > 100 || sn.Any(c => char.IsControl(c)))
                {
                    response.Results.Add(new SerialBatchValidationItemDto
                    {
                        SerialNumber = sn,
                        Status = SerialPasteValidationStatus.Invalid,
                        StatusKey = "Invalid",
                        StatusTextAr = "غير صالح",
                        StatusTextEn = "Invalid",
                        ErrorMessage = sn.Length > 100
                            ? "طول السيريال يتجاوز الحد المسموح (100 حرف) / Serial exceeds max length (100)"
                            : "صيغة السيريال غير صالحة / Invalid serial format",
                        IsValid = false
                    });
                    continue;
                }

                // 2. Check duplicate within pasted batch
                if (!seenInPaste.Add(sn))
                {
                    response.Results.Add(new SerialBatchValidationItemDto
                    {
                        SerialNumber = sn,
                        Status = SerialPasteValidationStatus.DuplicateInPaste,
                        StatusKey = "DuplicateInPaste",
                        StatusTextAr = "مكرر في البيانات الملصقة",
                        StatusTextEn = "Duplicate in pasted data",
                        ErrorMessage = "السيريال مكرر داخل البيانات الملصقة / Duplicate in pasted batch",
                        IsValid = false
                    });
                    continue;
                }

                // 3. Check if already in current line's added serials
                if (existingInLineSet.Contains(sn))
                {
                    response.Results.Add(new SerialBatchValidationItemDto
                    {
                        SerialNumber = sn,
                        Status = SerialPasteValidationStatus.DuplicateInPaste,
                        StatusKey = "DuplicateInPaste",
                        StatusTextAr = "مكرر في البيانات الملصقة",
                        StatusTextEn = "Duplicate in current list",
                        ErrorMessage = "السيريال مضاف بالفعل في قائمة السطر الحالي / Already in current line",
                        IsValid = false
                    });
                    continue;
                }

                // 4. Check database rules
                if (request.IsSalesReturn)
                {
                    if (!dbSerialsDict.TryGetValue(sn, out var dbSerial))
                    {
                        response.Results.Add(new SerialBatchValidationItemDto
                        {
                            SerialNumber = sn,
                            Status = SerialPasteValidationStatus.Invalid,
                            StatusKey = "Invalid",
                            StatusTextAr = "غير صالح",
                            StatusTextEn = "Invalid",
                            ErrorMessage = $"السيريال ({sn}) غير مسجل في النظام لهذا الصنف، ولا يمكن إرجاع سيريال لم يتم بيعه.",
                            IsValid = false
                        });
                        continue;
                    }

                    if (dbSerial.Status != ItemSerialStatus.Sold && dbSerial.Status != ItemSerialStatus.Issued)
                    {
                        response.Results.Add(new SerialBatchValidationItemDto
                        {
                            SerialNumber = sn,
                            Status = SerialPasteValidationStatus.AlreadyExists,
                            StatusKey = "AlreadyExists",
                            StatusTextAr = "موجود مسبقًا",
                            StatusTextEn = "Already in stock",
                            ErrorMessage = $"السيريال ({sn}) ليس بحالة مباع (حالته: {GetStatusNameAr(dbSerial.Status)}).",
                            IsValid = false
                        });
                        continue;
                    }
                }
                else
                {
                    if (dbSerialsDict.TryGetValue(sn, out var dbSerial))
                    {
                        bool isSameEditedDoc = request.ExcludeDocumentId.HasValue &&
                                               dbSerial.SourceDocId == request.ExcludeDocumentId.Value;
                        if (!isSameEditedDoc)
                        {
                            response.Results.Add(new SerialBatchValidationItemDto
                            {
                                SerialNumber = sn,
                                Status = SerialPasteValidationStatus.AlreadyExists,
                                StatusKey = "AlreadyExists",
                                StatusTextAr = "موجود مسبقًا",
                                StatusTextEn = "Already exists",
                                ErrorMessage = $"رقم السيريال ({sn}) مسجل مسبقاً لهذا الصنف في النظام (حالته: {GetStatusNameAr(dbSerial.Status)}).",
                                IsValid = false
                            });
                            continue;
                        }
                    }
                }

                // 5. Valid
                response.Results.Add(new SerialBatchValidationItemDto
                {
                    SerialNumber = sn,
                    Status = SerialPasteValidationStatus.Valid,
                    StatusKey = "Valid",
                    StatusTextAr = "صالح للإضافة",
                    StatusTextEn = "Valid to add",
                    ErrorMessage = null,
                    IsValid = true
                });
            }

            response.TotalCount = response.Results.Count;
            response.ValidCount = response.Results.Count(r => r.Status == SerialPasteValidationStatus.Valid);
            response.DuplicateCount = response.Results.Count(r => r.Status == SerialPasteValidationStatus.DuplicateInPaste);
            response.ExistingCount = response.Results.Count(r => r.Status == SerialPasteValidationStatus.AlreadyExists);
            response.InvalidCount = response.Results.Count(r => r.Status == SerialPasteValidationStatus.Invalid);

            return response;
        }

        public async Task<SerialValidationResultDto> ValidateSerialForIssueAsync(int itemId, int warehouseId, int branchId, string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
                return new SerialValidationResultDto { IsValid = false, Message = "رقم السيريال فارغ." };

            var trimmedSerial = serialNumber.Trim();
            var serial = await _context.ItemSerialNumbers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(s => s.Branch)
                .Include(s => s.Warehouse)
                .FirstOrDefaultAsync(s => s.ItemId == itemId && s.SerialNumber == trimmedSerial);

            // Guard 1: Existence
            if (serial == null)
            {
                return new SerialValidationResultDto
                {
                    IsValid = false,
                    Message = $"السيريال ({trimmedSerial}) غير مسجل في النظام لهذا الصنف."
                };
            }

            // Guard 2: Branch Ownership
            if (serial.BranchId != branchId)
            {
                var brName = serial.Branch != null ? serial.Branch.NameAr : $"فرع {serial.BranchId}";
                return new SerialValidationResultDto
                {
                    IsValid = false,
                    Message = $"السيريال ({trimmedSerial}) مسجل في ({brName}) ولا يتبع الفرع المحدد."
                };
            }

            // Guard 3: Warehouse Location
            if (serial.WarehouseId != warehouseId)
            {
                var whName = serial.Warehouse != null ? serial.Warehouse.LocalizedName : $"مستودع {serial.WarehouseId}";
                return new SerialValidationResultDto
                {
                    IsValid = false,
                    Message = $"السيريال ({trimmedSerial}) متواجد في مستودع ({whName}) ولا يتبع المستودع المحدد للسطر."
                };
            }

            // Guard 4: Reservation Check
            var now = DateTime.UtcNow;
            if (serial.Status == ItemSerialStatus.Reserved ||
                (serial.ReservedDocId.HasValue && (!serial.ReservedUntil.HasValue || serial.ReservedUntil.Value >= now)))
            {
                return new SerialValidationResultDto
                {
                    IsValid = false,
                    Message = $"السيريال ({trimmedSerial}) محجوز ولا يمكن صرفه حالياً."
                };
            }

            // Guard 5: Status (Only Available can be sold)
            if (serial.Status != ItemSerialStatus.Available && serial.Status != ItemSerialStatus.InStock)
            {
                if (serial.Status == ItemSerialStatus.Sold || serial.Status == ItemSerialStatus.Issued)
                {
                    return new SerialValidationResultDto
                    {
                        IsValid = false,
                        Message = $"السيريال ({trimmedSerial}) تم بيعه مسبقاً ولا يمكن بيعه أكثر من مرة."
                    };
                }

                return new SerialValidationResultDto
                {
                    IsValid = false,
                    Message = $"السيريال ({trimmedSerial}) غير متاح للبيع (حالته الراهنة: {GetStatusNameAr(serial.Status)})."
                };
            }

            return new SerialValidationResultDto
            {
                IsValid = true,
                SerialId = serial.Id
            };
        }

        public async Task<SerialValidationResultDto> ValidateSerialForReceiveAsync(int itemId, string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
                return new SerialValidationResultDto { IsValid = false, Message = "رقم السيريال فارغ." };

            var trimmedSerial = serialNumber.Trim();
            var existing = await _context.ItemSerialNumbers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ItemId == itemId && s.SerialNumber == trimmedSerial);

            // User Rule: The combination (ItemId + SerialNumber) must always be unique.
            // The same Serial Number cannot be purchased twice for the same item.
            if (existing != null)
            {
                return new SerialValidationResultDto
                {
                    IsValid = false,
                    Message = $"رقم السيريال ({trimmedSerial}) مسجل مسبقاً لهذا الصنف في النظام (حالته: {GetStatusNameAr(existing.Status)}). لا يمكن تكرار شراء نفس السيريال لنفس الصنف."
                };
            }

            return new SerialValidationResultDto
            {
                IsValid = true
            };
        }

        public async Task RegisterReceivedSerialsAsync(
            int documentId,
            string docType,
            int itemId,
            int warehouseId,
            int branchId,
            IEnumerable<string> serialNumbers,
            int? partyId = null,
            string? partyName = null,
            string? docNumber = null,
            string? userId = null)
        {
            var rawList = serialNumbers.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            if (!rawList.Any()) return;

            var dupInBatch = rawList
                .GroupBy(s => s, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (dupInBatch.Any())
            {
                throw new InvalidOperationException($"تم تكرار السيريال داخل نفس عملية الإضافة: {string.Join(", ", dupInBatch)}");
            }

            var list = rawList.Distinct(StringComparer.Ordinal).ToList();

            await _serialLock.WaitAsync();
            try
            {
                // Check uniqueness across system
                var existing = await _context.ItemSerialNumbers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(s => s.ItemId == itemId && list.Contains(s.SerialNumber))
                    .Select(s => s.SerialNumber)
                    .ToListAsync();

                if (existing.Any())
                {
                    throw new InvalidOperationException($"أرقام السيريال التالية مسجلة مسبقاً في النظام لهذا الصنف ولا يمكن شراؤها مجدداً: {string.Join(", ", existing)}");
                }

                var newSerials = new List<ItemSerialNumber>();
                var now = DateTime.UtcNow;

                foreach (var sn in list)
                {
                    var record = new ItemSerialNumber
                    {
                        ItemId = itemId,
                        SerialNumber = sn,
                        BranchId = branchId,
                        WarehouseId = warehouseId,
                        Status = ItemSerialStatus.Available,
                        InDate = now,
                        SourceDocType = docType,
                        SourceDocId = documentId,
                        CurrentPartyType = "Vendor",
                        CurrentPartyId = partyId,
                        CurrentPartyName = partyName,
                        CreatedAt = now
                    };
                    newSerials.Add(record);
                    _context.ItemSerialNumbers.Add(record);
                }

                await _context.SaveChangesAsync();

                // Create Movements
                foreach (var record in newSerials)
                {
                    _context.ItemSerialMovements.Add(new ItemSerialMovement
                    {
                        ItemSerialNumberId = record.Id,
                        ItemId = itemId,
                        SerialNumber = record.SerialNumber,
                        MovementType = SerialMovementType.PurchaseReceipt,
                        ToBranchId = branchId,
                        ToWarehouseId = warehouseId,
                        PreviousStatus = 0,
                        NewStatus = ItemSerialStatus.Available,
                        DocumentType = docType,
                        DocumentHeaderId = documentId,
                        DocumentNumber = docNumber,
                        PartyId = partyId,
                        PartyName = partyName,
                        Notes = $"استلام مشتريات للمستودع {warehouseId}",
                        UserId = userId,
                        MovementDate = now
                    });
                }

                // Link newly generated ItemSerialNumber IDs to DocumentLineSerials
                var savedSerials = newSerials.ToDictionary(s => s.SerialNumber, StringComparer.Ordinal);
                var docLineSerials = await _context.DocumentLineSerials
                    .Include(dls => dls.DocumentLine)
                    .Where(dls => dls.DocumentLine != null &&
                                  dls.DocumentLine.DocumentHeaderId == documentId &&
                                  dls.DocumentLine.ItemId == itemId &&
                                  list.Contains(dls.SerialNumber))
                    .ToListAsync();

                foreach (var dls in docLineSerials)
                {
                    if (savedSerials.TryGetValue(dls.SerialNumber, out var snRec))
                    {
                        dls.ItemSerialNumberId = snRec.Id;
                    }
                }

                await _context.SaveChangesAsync();
            }
            finally
            {
                _serialLock.Release();
            }
        }

        public async Task MarkSerialsAsIssuedAsync(
            int documentId,
            string docType,
            int itemId,
            int warehouseId,
            int branchId,
            IEnumerable<string> serialNumbers,
            int? partyId = null,
            string? partyName = null,
            string? docNumber = null,
            string? userId = null,
            int? warrantyMonths = null)
        {
            var rawList = serialNumbers.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            if (!rawList.Any()) return;

            var dupInBatch = rawList
                .GroupBy(s => s, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (dupInBatch.Any())
            {
                throw new InvalidOperationException($"لا يمكن صرف نفس السيريال أكثر من مرة في نفس المستند: {string.Join(", ", dupInBatch)}");
            }

            var list = rawList.Distinct(StringComparer.Ordinal).ToList();

            await _serialLock.WaitAsync();
            try
            {
                // Detach any cached ItemSerialNumber entries for this item/serial list so we always read fresh DB state
                var trackedEntries = _context.ChangeTracker.Entries<ItemSerialNumber>()
                    .Where(e => e.Entity.ItemId == itemId && list.Contains(e.Entity.SerialNumber))
                    .ToList();
                foreach (var entry in trackedEntries)
                {
                    entry.State = EntityState.Detached;
                }

                var serials = await _context.ItemSerialNumbers
                    .IgnoreQueryFilters()
                    .Include(s => s.Branch)
                    .Include(s => s.Warehouse)
                    .Where(s => s.ItemId == itemId && list.Contains(s.SerialNumber))
                    .ToListAsync();

                var serialByCode = serials.ToDictionary(s => s.SerialNumber, StringComparer.Ordinal);
                var now = DateTime.UtcNow;

                foreach (var sn in list)
                {
                    if (!serialByCode.TryGetValue(sn, out var s))
                    {
                        throw new InvalidOperationException($"السيريال ({sn}) غير مسجل في النظام لهذا الصنف.");
                    }

                    if (s.BranchId != branchId)
                    {
                        var brName = s.Branch != null ? s.Branch.NameAr : $"فرع {s.BranchId}";
                        throw new InvalidOperationException($"السيريال ({sn}) مسجل في ({brName}) ولا يتبع الفرع المحدد للصرف.");
                    }

                    if (s.WarehouseId != warehouseId)
                    {
                        var whName = s.Warehouse != null ? s.Warehouse.LocalizedName : $"مستودع {s.WarehouseId}";
                        throw new InvalidOperationException($"السيريال ({sn}) متواجد في مستودع ({whName}) ولا يتبع المستودع المحدد للصرف.");
                    }

                    if (s.Status == ItemSerialStatus.Reserved ||
                        (s.ReservedDocId.HasValue && s.ReservedDocId.Value != documentId && (!s.ReservedUntil.HasValue || s.ReservedUntil.Value >= now)))
                    {
                        throw new InvalidOperationException($"السيريال ({sn}) محجوز ولا يمكن صرفه حالياً.");
                    }

                    if (s.Status != ItemSerialStatus.Available && s.Status != ItemSerialStatus.InStock)
                    {
                        if (s.Status == ItemSerialStatus.Sold || s.Status == ItemSerialStatus.Issued)
                        {
                            throw new InvalidOperationException($"السيريال ({sn}) تم صرفه/بيعه مسبقاً ولا يمكن صرفه مرة أخرى.");
                        }

                        throw new InvalidOperationException($"السيريال ({sn}) غير متاح للصرف (حالته: {GetStatusNameAr(s.Status)}).");
                    }

                    var prevStatus = s.Status;
                    s.Status = ItemSerialStatus.Sold;
                    s.OutDate = now;
                    s.OutDocType = docType;
                    s.OutDocId = documentId;
                    s.CurrentPartyType = "Customer";
                    s.CurrentPartyId = partyId;
                    s.CurrentPartyName = partyName;
                    s.UpdatedAt = now;

                    if (warrantyMonths.HasValue && warrantyMonths.Value > 0)
                    {
                        s.WarrantyMonths = warrantyMonths.Value;
                        s.WarrantyStartDate = now;
                        s.WarrantyEndDate = now.AddMonths(warrantyMonths.Value);
                    }

                    _context.ItemSerialMovements.Add(new ItemSerialMovement
                    {
                        ItemSerialNumberId = s.Id,
                        ItemId = itemId,
                        SerialNumber = s.SerialNumber,
                        MovementType = SerialMovementType.SalesDelivery,
                        FromBranchId = branchId,
                        FromWarehouseId = warehouseId,
                        PreviousStatus = prevStatus,
                        NewStatus = ItemSerialStatus.Sold,
                        DocumentType = docType,
                        DocumentHeaderId = documentId,
                        DocumentNumber = docNumber,
                        PartyId = partyId,
                        PartyName = partyName,
                        Notes = $"صرف مبيعات للعميل {partyName}",
                        UserId = userId,
                        MovementDate = now
                    });
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException($"تعذر حفظ حركة الصرف لأن أحد السيريالات ({string.Join(", ", list)}) تم صرفه أو تعديله بواسطة مستخدم آخر في نفس الوقت.");
                }

                var docLineSerials = await _context.DocumentLineSerials
                    .Include(dls => dls.DocumentLine)
                    .Where(dls => dls.DocumentLine != null &&
                                  dls.DocumentLine.DocumentHeaderId == documentId &&
                                  dls.DocumentLine.ItemId == itemId &&
                                  list.Contains(dls.SerialNumber))
                    .ToListAsync();

                foreach (var dls in docLineSerials)
                {
                    if (serialByCode.TryGetValue(dls.SerialNumber, out var sRec))
                    {
                        dls.ItemSerialNumberId = sRec.Id;
                    }
                }

                await _context.SaveChangesAsync();
            }
            finally
            {
                _serialLock.Release();
            }
        }

        public async Task RegisterSalesReturnSerialsAsync(
            int documentId,
            string docType,
            int itemId,
            int warehouseId,
            int branchId,
            IEnumerable<string> serialNumbers,
            int? partyId = null,
            string? partyName = null,
            string? docNumber = null,
            string? userId = null)
        {
            var list = serialNumbers.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (!list.Any()) return;

            await _serialLock.WaitAsync();
            try
            {
                var trackedEntries = _context.ChangeTracker.Entries<ItemSerialNumber>()
                    .Where(e => e.Entity.ItemId == itemId && list.Contains(e.Entity.SerialNumber))
                    .ToList();
                foreach (var entry in trackedEntries)
                {
                    entry.State = EntityState.Detached;
                }

                var serials = await _context.ItemSerialNumbers
                    .IgnoreQueryFilters()
                    .Where(s => s.ItemId == itemId && list.Contains(s.SerialNumber))
                    .ToListAsync();

                var now = DateTime.UtcNow;

                foreach (var sn in list)
                {
                    var s = serials.FirstOrDefault(x => x.SerialNumber == sn);
                    if (s == null)
                    {
                        throw new InvalidOperationException($"السيريال ({sn}) غير موجود بالنظام لهذا الصنف، لا يمكن إرجاع سيريال غير مباع.");
                    }

                    if (s.Status != ItemSerialStatus.Sold && s.Status != ItemSerialStatus.Issued)
                    {
                        throw new InvalidOperationException($"السيريال ({sn}) ليس بحالة مباع (حالته الحالية: {GetStatusNameAr(s.Status)}). لا يمكن إرجاع سيريال لم يتم بيعه.");
                    }

                    var prevStatus = s.Status;
                    var prevBranch = s.BranchId;
                    var prevWarehouse = s.WarehouseId;

                    // Return to inventory: Reactivate to Available, assign to receiving Branch & Warehouse
                    s.Status = ItemSerialStatus.Available;
                    s.BranchId = branchId;
                    s.WarehouseId = warehouseId;
                    s.OutDate = null;
                    s.OutDocType = null;
                    s.OutDocId = null;
                    s.UpdatedAt = now;

                    _context.ItemSerialMovements.Add(new ItemSerialMovement
                    {
                        ItemSerialNumberId = s.Id,
                        ItemId = itemId,
                        SerialNumber = s.SerialNumber,
                        MovementType = SerialMovementType.SalesReturn,
                        FromBranchId = prevBranch,
                        FromWarehouseId = prevWarehouse,
                        ToBranchId = branchId,
                        ToWarehouseId = warehouseId,
                        PreviousStatus = prevStatus,
                        NewStatus = ItemSerialStatus.Available,
                        DocumentType = docType,
                        DocumentHeaderId = documentId,
                        DocumentNumber = docNumber,
                        PartyId = partyId,
                        PartyName = partyName,
                        Notes = $"مردود مبيعات واستلام بالمستودع {warehouseId}",
                        UserId = userId,
                        MovementDate = now
                    });
                }

                await _context.SaveChangesAsync();

                var serialsDict = serials.ToDictionary(s => s.SerialNumber, StringComparer.Ordinal);
                var docLineSerials = await _context.DocumentLineSerials
                    .Include(dls => dls.DocumentLine)
                    .Where(dls => dls.DocumentLine != null &&
                                  dls.DocumentLine.DocumentHeaderId == documentId &&
                                  dls.DocumentLine.ItemId == itemId &&
                                  list.Contains(dls.SerialNumber))
                    .ToListAsync();

                foreach (var dls in docLineSerials)
                {
                    if (serialsDict.TryGetValue(dls.SerialNumber, out var sRec))
                    {
                        dls.ItemSerialNumberId = sRec.Id;
                    }
                }

                await _context.SaveChangesAsync();
            }
            finally
            {
                _serialLock.Release();
            }
        }

        public async Task TransferSerialsDirectAsync(
            int documentId,
            string docType,
            int itemId,
            int fromWarehouseId,
            int fromBranchId,
            int toWarehouseId,
            int toBranchId,
            IEnumerable<string> serialNumbers,
            string? docNumber = null,
            string? userId = null)
        {
            var list = serialNumbers.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();
            if (!list.Any()) return;

            var serials = await _context.ItemSerialNumbers
                .IgnoreQueryFilters()
                .Where(s => s.ItemId == itemId && list.Contains(s.SerialNumber) && s.WarehouseId == fromWarehouseId && s.BranchId == fromBranchId)
                .ToListAsync();

            if (serials.Count < list.Count)
            {
                throw new InvalidOperationException("بعض السيريالات المحددة للتحويل غير متوفرة في المستودع المصدر.");
            }

            var now = DateTime.UtcNow;

            foreach (var s in serials)
            {
                if (s.Status != ItemSerialStatus.Available && s.Status != ItemSerialStatus.InStock)
                {
                    throw new InvalidOperationException($"السيريال ({s.SerialNumber}) غير متاح للتحويل (حالته: {GetStatusNameAr(s.Status)}).");
                }

                var prevStatus = s.Status;
                s.BranchId = toBranchId;
                s.WarehouseId = toWarehouseId;
                s.UpdatedAt = now;

                _context.ItemSerialMovements.Add(new ItemSerialMovement
                {
                    ItemSerialNumberId = s.Id,
                    ItemId = itemId,
                    SerialNumber = s.SerialNumber,
                    MovementType = SerialMovementType.TransferDirect,
                    FromBranchId = fromBranchId,
                    FromWarehouseId = fromWarehouseId,
                    ToBranchId = toBranchId,
                    ToWarehouseId = toWarehouseId,
                    PreviousStatus = prevStatus,
                    NewStatus = s.Status,
                    DocumentType = docType,
                    DocumentHeaderId = documentId,
                    DocumentNumber = docNumber,
                    Notes = $"تحويل مخزني من مستودع {fromWarehouseId} إلى {toWarehouseId}",
                    UserId = userId,
                    MovementDate = now
                });
            }

            await _context.SaveChangesAsync();
        }

        public async Task RevertIssuedSerialsAsync(int documentId, string docType)
        {
            var serials = await _context.ItemSerialNumbers
                .IgnoreQueryFilters()
                .Where(s => s.OutDocId == documentId && s.OutDocType == docType)
                .ToListAsync();

            var now = DateTime.UtcNow;

            foreach (var s in serials)
            {
                var prevStatus = s.Status;
                s.Status = ItemSerialStatus.Available;
                s.OutDate = null;
                s.OutDocType = null;
                s.OutDocId = null;
                s.UpdatedAt = now;

                _context.ItemSerialMovements.Add(new ItemSerialMovement
                {
                    ItemSerialNumberId = s.Id,
                    ItemId = s.ItemId,
                    SerialNumber = s.SerialNumber,
                    MovementType = SerialMovementType.AdjustmentIn,
                    FromBranchId = s.BranchId,
                    FromWarehouseId = s.WarehouseId,
                    ToBranchId = s.BranchId,
                    ToWarehouseId = s.WarehouseId,
                    PreviousStatus = prevStatus,
                    NewStatus = ItemSerialStatus.Available,
                    DocumentType = docType,
                    DocumentHeaderId = documentId,
                    Notes = "إلغاء اعتماد مستند الصرف وإعادة السيريال للرصيد المتاح",
                    MovementDate = now
                });
            }

            await _context.SaveChangesAsync();
        }

        public async Task RevertReceivedSerialsAsync(int documentId, string docType)
        {
            var serials = await _context.ItemSerialNumbers
                .IgnoreQueryFilters()
                .Where(s => s.SourceDocId == documentId && s.SourceDocType == docType)
                .ToListAsync();

            var soldOrUsed = serials
                .Where(s => s.Status != ItemSerialStatus.Available || s.OutDocId.HasValue)
                .Select(s => s.SerialNumber)
                .ToList();

            if (soldOrUsed.Any())
            {
                throw new InvalidOperationException($"لا يمكن إلغاء سيريالات تم بيعها أو صرفها في حركات لاحقة: {string.Join(", ", soldOrUsed)}. يجب إلغاء حركات البيع أو الصرف أولاً.");
            }

            var serialIds = serials.Select(s => s.Id).ToList();

            // Clear movements for these serials
            var movements = await _context.ItemSerialMovements
                .Where(m => serialIds.Contains(m.ItemSerialNumberId))
                .ToListAsync();
            _context.ItemSerialMovements.RemoveRange(movements);

            // Clear foreign keys in DocumentLineSerials before removal
            var docLineSerials = await _context.DocumentLineSerials
                .Where(dls => dls.ItemSerialNumberId.HasValue && serialIds.Contains(dls.ItemSerialNumberId.Value))
                .ToListAsync();

            foreach (var dls in docLineSerials)
            {
                dls.ItemSerialNumberId = null;
            }

            _context.ItemSerialNumbers.RemoveRange(serials);
            await _context.SaveChangesAsync();
        }

        public async Task<SerialInquiryDto?> GetSerialDetailsAsync(string serialNumber, int? itemId = null)
        {
            if (string.IsNullOrWhiteSpace(serialNumber)) return null;
            var trimmed = serialNumber.Trim();

            var query = _context.ItemSerialNumbers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(s => s.Item)
                .Include(s => s.Branch)
                .Include(s => s.Warehouse)
                .Include(s => s.Movements)
                    .ThenInclude(m => m.FromBranch)
                .Include(s => s.Movements)
                    .ThenInclude(m => m.FromWarehouse)
                .Include(s => s.Movements)
                    .ThenInclude(m => m.ToBranch)
                .Include(s => s.Movements)
                    .ThenInclude(m => m.ToWarehouse)
                .Where(s => s.SerialNumber == trimmed);

            if (itemId.HasValue && itemId.Value > 0)
            {
                query = query.Where(s => s.ItemId == itemId.Value);
            }

            var s = await query.FirstOrDefaultAsync();
            if (s == null) return null;

            return MapToInquiryDto(s);
        }

        public async Task<List<SerialInquiryDto>> SearchSerialsAsync(SerialInquiryFilterDto filter)
        {
            var query = _context.ItemSerialNumbers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(s => s.Item)
                .Include(s => s.Branch)
                .Include(s => s.Warehouse)
                .Include(s => s.Movements)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.SerialNumber))
            {
                var sn = filter.SerialNumber.Trim();
                query = query.Where(s => s.SerialNumber.Contains(sn));
            }

            if (!string.IsNullOrWhiteSpace(filter.ItemCode))
            {
                var code = filter.ItemCode.Trim();
                query = query.Where(s => s.Item != null && s.Item.Code.Contains(code));
            }

            if (!string.IsNullOrWhiteSpace(filter.ItemName))
            {
                var name = filter.ItemName.Trim();
                query = query.Where(s => s.Item != null && (s.Item.NameAr.Contains(name) || s.Item.NameEn.Contains(name)));
            }

            if (filter.BranchId.HasValue && filter.BranchId.Value > 0)
            {
                query = query.Where(s => s.BranchId == filter.BranchId.Value);
            }

            if (filter.WarehouseId.HasValue && filter.WarehouseId.Value > 0)
            {
                query = query.Where(s => s.WarehouseId == filter.WarehouseId.Value);
            }

            if (filter.Status.HasValue)
            {
                query = query.Where(s => s.Status == filter.Status.Value);
            }

            var results = await query
                .OrderByDescending(s => s.CreatedAt)
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            return results.Select(MapToInquiryDto).ToList();
        }

        private static SerialInquiryDto MapToInquiryDto(ItemSerialNumber s)
        {
            return new SerialInquiryDto
            {
                Id = s.Id,
                ItemId = s.ItemId,
                ItemCode = s.Item != null ? s.Item.Code : "",
                ItemNameAr = s.Item != null ? s.Item.NameAr : "",
                ItemNameEn = s.Item != null ? s.Item.NameEn : "",
                SerialNumber = s.SerialNumber,
                BranchId = s.BranchId,
                BranchName = s.Branch != null ? s.Branch.NameAr : "",
                WarehouseId = s.WarehouseId,
                WarehouseName = s.Warehouse != null ? s.Warehouse.LocalizedName : "",
                Status = s.Status,
                StatusNameAr = GetStatusNameAr(s.Status),
                StatusBadgeClass = GetStatusBadgeClass(s.Status),
                InDate = s.InDate,
                OutDate = s.OutDate,
                SourceDocType = s.SourceDocType,
                SourceDocId = s.SourceDocId,
                OutDocType = s.OutDocType,
                OutDocId = s.OutDocId,
                CurrentPartyType = s.CurrentPartyType,
                CurrentPartyId = s.CurrentPartyId,
                CurrentPartyName = s.CurrentPartyName,
                VendorName = s.CurrentPartyType == "Vendor" ? s.CurrentPartyName : null,
                CustomerName = s.CurrentPartyType == "Customer" ? s.CurrentPartyName : null,
                WarrantyMonths = s.WarrantyMonths,
                WarrantyStartDate = s.WarrantyStartDate,
                WarrantyEndDate = s.WarrantyEndDate,
                AssetTag = s.AssetTag,
                IsAsset = s.IsAsset,
                LastServiceDate = s.LastServiceDate,
                ServiceCount = s.ServiceCount,
                Notes = s.Notes,
                Movements = (s.Movements ?? new List<ItemSerialMovement>())
                    .OrderByDescending(m => m.MovementDate)
                    .Select(m => new SerialMovementDto
                    {
                        Id = m.Id,
                        ItemSerialNumberId = m.ItemSerialNumberId,
                        MovementType = m.MovementType,
                        MovementTypeNameAr = GetMovementTypeNameAr(m.MovementType),
                        MovementTypeNameEn = m.MovementType.ToString(),
                        FromBranchId = m.FromBranchId,
                        FromBranchName = m.FromBranch?.NameAr,
                        FromWarehouseId = m.FromWarehouseId,
                        FromWarehouseName = m.FromWarehouse?.LocalizedName,
                        ToBranchId = m.ToBranchId,
                        ToBranchName = m.ToBranch?.NameAr,
                        ToWarehouseId = m.ToWarehouseId,
                        ToWarehouseName = m.ToWarehouse?.LocalizedName,
                        PreviousStatus = m.PreviousStatus,
                        NewStatus = m.NewStatus,
                        NewStatusNameAr = GetStatusNameAr(m.NewStatus),
                        DocumentType = m.DocumentType,
                        DocumentHeaderId = m.DocumentHeaderId,
                        DocumentNumber = m.DocumentNumber,
                        PartyId = m.PartyId,
                        PartyName = m.PartyName,
                        Notes = m.Notes,
                        UserId = m.UserId,
                        MovementDate = m.MovementDate
                    }).ToList()
            };
        }

        private static string GetStatusNameAr(ItemSerialStatus status) => status switch
        {
            ItemSerialStatus.Available => "متاح بالمخزن",
            ItemSerialStatus.Reserved => "محجوز",
            ItemSerialStatus.Sold => "مباع",
            ItemSerialStatus.Returned => "مرتجع",
            ItemSerialStatus.Transferred => "قيد النقل (In-Transit)",
            ItemSerialStatus.Damaged => "تالف",
            ItemSerialStatus.Scrapped => "مستبعد / تكهين",
            _ => status.ToString()
        };

        private static string GetStatusBadgeClass(ItemSerialStatus status) => status switch
        {
            ItemSerialStatus.Available => "badge-success",
            ItemSerialStatus.Reserved => "badge-warning",
            ItemSerialStatus.Sold => "badge-info",
            ItemSerialStatus.Returned => "badge-primary",
            ItemSerialStatus.Transferred => "badge-secondary",
            ItemSerialStatus.Damaged => "badge-danger",
            ItemSerialStatus.Scrapped => "badge-dark",
            _ => "badge-light"
        };

        private static string GetMovementTypeNameAr(SerialMovementType type) => type switch
        {
            SerialMovementType.PurchaseReceipt => "استلام مشتريات",
            SerialMovementType.SalesDelivery => "صرف مبيعات",
            SerialMovementType.SalesReturn => "مردود مبيعات",
            SerialMovementType.PurchaseReturn => "مردود مشتريات",
            SerialMovementType.TransferOut => "تحويل صادر (قيد النقل)",
            SerialMovementType.TransferIn => "تحويل وارد (استلام)",
            SerialMovementType.TransferDirect => "تحويل مباشر بين مستودعين",
            SerialMovementType.AdjustmentIn => "تسوية جردية - إضافة",
            SerialMovementType.AdjustmentOut => "تسوية جردية - صرف",
            SerialMovementType.Damaged => "إثبات تلف",
            SerialMovementType.Scrapped => "تكهين / استبعاد",
            SerialMovementType.Reserved => "حجز",
            SerialMovementType.Unreserved => "إلغاء حجز",
            _ => type.ToString()
        };
    }
}
