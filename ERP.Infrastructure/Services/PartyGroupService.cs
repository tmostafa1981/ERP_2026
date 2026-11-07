using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Application.Models;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERP.Infrastructure.Services
{
    public class PartyGroupService : IPartyGroupService
    {
        private readonly ErpDbContext _context;
        private readonly ICodeSequenceService _sequenceService;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditService _auditService;

        private static readonly Dictionary<string, (string Ar, string En)> CurrencySuffixMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["EGP"] = ("مصري", "EGP"),
                ["USD"] = ("دولار", "Dollar"),
                ["EUR"] = ("يورو", "Euro"),
                ["CNY"] = ("يوان صيني", "Chinese Yuan"),
                ["SAR"] = ("ريال سعودي", "SAR"),
                ["AED"] = ("درهم إماراتي", "AED"),
                ["GBP"] = ("جنيه إسترليني", "GBP")
            };

        public PartyGroupService(
            ErpDbContext context,
            ICodeSequenceService sequenceService,
            ICurrentUserContext currentUser,
            IAuditService auditService)
        {
            _context = context;
            _sequenceService = sequenceService;
            _currentUser = currentUser;
            _auditService = auditService;
        }

        public (string NameAr, string NameEn) FormatPartyCurrencyNames(
            string baseNameAr,
            string? baseNameEn,
            Currency? currency,
            bool appendCurrencySuffix = true)
        {
            var cleanAr = StripCurrencySuffix((baseNameAr ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(cleanAr) && !string.IsNullOrWhiteSpace(baseNameEn))
            {
                cleanAr = StripCurrencySuffix(baseNameEn.Trim());
            }

            // Critical Rule: If English name is empty, fallback to Arabic base name
            var cleanEn = !string.IsNullOrWhiteSpace(baseNameEn)
                ? StripCurrencySuffix(baseNameEn.Trim())
                : cleanAr;

            if (string.IsNullOrWhiteSpace(cleanEn))
            {
                cleanEn = cleanAr;
            }

            if (!appendCurrencySuffix || currency == null)
            {
                return (cleanAr, cleanEn);
            }

            string arSuffix;
            string enSuffix;

            if (!string.IsNullOrWhiteSpace(currency.Code) &&
                CurrencySuffixMap.TryGetValue(currency.Code.Trim(), out var mapped))
            {
                arSuffix = mapped.Ar;
                enSuffix = mapped.En;
            }
            else
            {
                arSuffix = !string.IsNullOrWhiteSpace(currency.NameAr) ? currency.NameAr.Trim() : currency.Code.Trim();
                enSuffix = !string.IsNullOrWhiteSpace(currency.NameEn) ? currency.NameEn.Trim() : currency.Code.Trim();
            }

            var finalAr = string.IsNullOrWhiteSpace(arSuffix) ? cleanAr : $"{cleanAr} - {arSuffix}";
            var finalEn = string.IsNullOrWhiteSpace(enSuffix) ? cleanEn : $"{cleanEn} - {enSuffix}";

            return (finalAr, finalEn);
        }

        public string StripCurrencySuffix(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var trimmed = name.Trim();
            var knownSuffixes = new[]
            {
                " - مصري", " - جنيه مصري", " - EGP", " - Egyptian Pound",
                " - دولار", " - دولار أمريكي", " - USD", " - Dollar", " - US Dollar",
                " - يورو", " - EUR", " - Euro",
                " - يوان صيني", " - يوان", " - CNY", " - Chinese Yuan",
                " - ريال سعودي", " - SAR", " - Saudi Riyal",
                " - درهم إماراتي", " - AED", " - UAE Dirham",
                " - جنيه إسترليني", " - GBP", " - British Pound"
            };

            foreach (var suffix in knownSuffixes)
            {
                if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(0, trimmed.Length - suffix.Length).Trim();
                }
            }

            return trimmed;
        }

        public async Task<CreateCustomerGroupResultDto> CreateCustomerGroupAsync(CreateCustomerGroupRequestDto request)
        {
            var baseCust = request.BaseCustomer;
            if (baseCust == null || string.IsNullOrWhiteSpace(baseCust.NameAr))
            {
                return new CreateCustomerGroupResultDto
                {
                    Success = false,
                    Message = "يرجى إدخال اسم العميل باللغة العربية."
                };
            }

            // 1. Permission & Branch Isolation Validation
            if (!_currentUser.HasPermission("MasterData.Customers.Add"))
            {
                return new CreateCustomerGroupResultDto
                {
                    Success = false,
                    Message = "ليس لديك صلاحية إضافة عملاء جدد (MasterData.Customers.Add)."
                };
            }

            if (!_currentUser.CanAccessBranch(baseCust.BranchId))
            {
                return new CreateCustomerGroupResultDto
                {
                    Success = false,
                    Message = "ليس لديك صلاحية الإضافة في هذا الفرع."
                };
            }

            bool createVendor = request.CreateLinkedVendor || (request.VendorCurrencyIds != null && request.VendorCurrencyIds.Any());
            if (createVendor && !_currentUser.HasPermission("MasterData.Vendors.Add"))
            {
                return new CreateCustomerGroupResultDto
                {
                    Success = false,
                    Message = "ليس لديك صلاحية إضافة موردين (MasterData.Vendors.Add) لإنشاء سجل مورد مرتبط."
                };
            }

            // 2. Validate Customer Currencies (Prevent duplicates within the same group)
            var custCurrIds = request.CustomerCurrencyIds ?? new List<int>();
            if (custCurrIds.Count > 0)
            {
                var distinctCustCurr = custCurrIds.Where(id => id > 0).Distinct().ToList();
                if (distinctCustCurr.Count != custCurrIds.Count(id => id > 0))
                {
                    return new CreateCustomerGroupResultDto
                    {
                        Success = false,
                        Message = "لا يمكن تكرار نفس العملة لنفس العميل داخل نفس المجموعة."
                    };
                }
                custCurrIds = distinctCustCurr;
            }
            else if (baseCust.CurrencyId.HasValue && baseCust.CurrencyId.Value > 0)
            {
                custCurrIds = new List<int> { baseCust.CurrencyId.Value };
            }

            // 3. Validate Vendor Currencies (Prevent duplicates within the same group)
            var vendCurrIds = new List<int>();
            if (createVendor)
            {
                if (request.VendorCurrencyIds != null && request.VendorCurrencyIds.Any(id => id > 0))
                {
                    var validVendIds = request.VendorCurrencyIds.Where(id => id > 0).ToList();
                    var distinctVendCurr = validVendIds.Distinct().ToList();
                    if (distinctVendCurr.Count != validVendIds.Count)
                    {
                        return new CreateCustomerGroupResultDto
                        {
                            Success = false,
                            Message = "لا يمكن تكرار نفس العملة لنفس المورد داخل نفس المجموعة."
                        };
                    }
                    vendCurrIds = distinctVendCurr;
                }
                else
                {
                    vendCurrIds = custCurrIds.ToList();
                }
            }

            // 4. Resolve Currencies from DB
            var allNeededCurrIds = custCurrIds.Union(vendCurrIds).Distinct().ToList();
            var currencyDict = await _context.Currencies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => allNeededCurrIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id);

            if (allNeededCurrIds.Any(id => !currencyDict.ContainsKey(id)))
            {
                return new CreateCustomerGroupResultDto
                {
                    Success = false,
                    Message = "إحدى العملات المحددة غير موجودة في النظام."
                };
            }

            // 5. Resolve Customer Category & Financial Account
            int custCategoryId = baseCust.CustomerCategoryId;
            if (custCategoryId <= 0)
            {
                var defCustCat = await _context.CustomerCategories
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(c => c.BranchId == baseCust.BranchId && c.IsActive)
                    .OrderByDescending(c => c.IsDefault)
                    .FirstOrDefaultAsync();
                if (defCustCat == null)
                {
                    return new CreateCustomerGroupResultDto
                    {
                        Success = false,
                        Message = "يرجى تحديد فئة العميل."
                    };
                }
                custCategoryId = defCustCat.Id;
                if (!baseCust.FinancialAccountId.HasValue || baseCust.FinancialAccountId <= 0)
                {
                    baseCust.FinancialAccountId = defCustCat.ControlAccountId;
                }
            }

            int? custFinancialAccId = baseCust.FinancialAccountId;
            if (!custFinancialAccId.HasValue || custFinancialAccId.Value <= 0)
            {
                var cat = await _context.CustomerCategories
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == custCategoryId);
                custFinancialAccId = cat?.ControlAccountId;
            }

            if (!custFinancialAccId.HasValue || custFinancialAccId.Value <= 0)
            {
                var fallbackArAcc = await _context.GLAccounts
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(a => !a.IsGroup && a.IsActive && a.SubLedgerType == SubLedgerType.Customer)
                    .Select(a => (int?)a.Id)
                    .FirstOrDefaultAsync();
                custFinancialAccId = fallbackArAcc;
            }

            if (!custFinancialAccId.HasValue || custFinancialAccId.Value <= 0)
            {
                return new CreateCustomerGroupResultDto
                {
                    Success = false,
                    Message = "يرجى تحديد الحساب المالي للعميل في دليل الحسابات."
                };
            }

            // 6. Resolve Vendor Category & Financial Account (if creating linked vendor)
            int vendCategoryId = 0;
            int? vendFinancialAccId = null;
            if (createVendor)
            {
                if (request.LinkedVendorCategoryId.HasValue && request.LinkedVendorCategoryId.Value > 0)
                {
                    vendCategoryId = request.LinkedVendorCategoryId.Value;
                }
                else
                {
                    var defVendCat = await _context.VendorCategories
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(v => v.BranchId == baseCust.BranchId && v.IsActive)
                        .OrderByDescending(v => v.IsDefault)
                        .FirstOrDefaultAsync();
                    if (defVendCat != null)
                    {
                        vendCategoryId = defVendCat.Id;
                        vendFinancialAccId = defVendCat.ControlAccountId;
                    }
                }

                if (request.LinkedVendorFinancialAccountId.HasValue && request.LinkedVendorFinancialAccountId.Value > 0)
                {
                    vendFinancialAccId = request.LinkedVendorFinancialAccountId.Value;
                }
                else if (!vendFinancialAccId.HasValue && vendCategoryId > 0)
                {
                    var vCat = await _context.VendorCategories
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(v => v.Id == vendCategoryId);
                    vendFinancialAccId = vCat?.ControlAccountId;
                }

                if (!vendFinancialAccId.HasValue || vendFinancialAccId.Value <= 0)
                {
                    vendFinancialAccId = await _context.GLAccounts
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(a => !a.IsGroup && a.IsActive && a.SubLedgerType == SubLedgerType.Vendor)
                        .Select(a => (int?)a.Id)
                        .FirstOrDefaultAsync();
                }

                if (vendCategoryId <= 0 || !vendFinancialAccId.HasValue || vendFinancialAccId.Value <= 0)
                {
                    return new CreateCustomerGroupResultDto
                    {
                        Success = false,
                        Message = "تعذر العثور على فئة مورد أو حساب مراقبة موردين افتراضي للفرع لإنشاء المورد المرتبط."
                    };
                }
            }

            // 7. Pre-generate Codes & Formatted Names (outside transaction to avoid nested transactions on SQL Server)
            var cleanBaseAr = StripCurrencySuffix(baseCust.NameAr);
            var cleanBaseEn = !string.IsNullOrWhiteSpace(baseCust.NameEn)
                ? StripCurrencySuffix(baseCust.NameEn)
                : cleanBaseAr;

            var effectiveCustCurrencies = custCurrIds.Any()
                ? custCurrIds.Select(id => (int?)id).ToList()
                : new List<int?> { null };

            var custConfigs = new List<(int? CurrencyId, string Code, string NameAr, string NameEn)>();
            for (int i = 0; i < effectiveCustCurrencies.Count; i++)
            {
                var currId = effectiveCustCurrencies[i];
                Currency? currObj = (currId.HasValue && currencyDict.TryGetValue(currId.Value, out var cObj)) ? cObj : null;

                string custCode;
                if (i == 0 && request.IsManualCode && !string.IsNullOrWhiteSpace(baseCust.Code) && effectiveCustCurrencies.Count == 1)
                {
                    custCode = baseCust.Code.Trim();
                    bool codeExists = await _context.Customers
                        .IgnoreQueryFilters()
                        .AnyAsync(c => c.BranchId == baseCust.BranchId && c.Code == custCode);
                    if (codeExists)
                    {
                        return new CreateCustomerGroupResultDto
                        {
                            Success = false,
                            Message = $"كود العميل ({custCode}) مستخدم بالفعل في هذا الفرع."
                        };
                    }
                }
                else
                {
                    custCode = await _sequenceService.GetNextCodeAsync("Customer", baseCust.BranchId);
                }

                var (formattedAr, formattedEn) = FormatPartyCurrencyNames(
                    baseCust.NameAr,
                    baseCust.NameEn,
                    currObj,
                    appendCurrencySuffix: currObj != null);

                custConfigs.Add((currId, custCode, formattedAr, formattedEn));
            }

            var vendConfigs = new List<(int? CurrencyId, string Code, string NameAr, string NameEn)>();
            if (createVendor)
            {
                var effectiveVendCurrencies = vendCurrIds.Any()
                    ? vendCurrIds.Select(id => (int?)id).ToList()
                    : new List<int?> { null };

                foreach (var currId in effectiveVendCurrencies)
                {
                    Currency? currObj = (currId.HasValue && currencyDict.TryGetValue(currId.Value, out var cObj)) ? cObj : null;
                    string vendCode = await _sequenceService.GetNextCodeAsync("Vendor", baseCust.BranchId);

                    var (formattedAr, formattedEn) = FormatPartyCurrencyNames(
                        baseCust.NameAr,
                        baseCust.NameEn,
                        currObj,
                        appendCurrencySuffix: currObj != null);

                    vendConfigs.Add((currId, vendCode, formattedAr, formattedEn));
                }
            }

            var groupCode = await GeneratePartyGroupCodeAsync();

            // 8. Execute Atomic Creation inside Execution Strategy & Database Transaction
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                IDbContextTransaction? ownTransaction = null;
                if (_context.Database.IsRelational() && _context.Database.CurrentTransaction == null)
                {
                    ownTransaction = await _context.Database.BeginTransactionAsync();
                }

                try
                {
                    var partyGroup = new PartyGroup
                    {
                        Code = groupCode,
                        NameAr = cleanBaseAr,
                        NameEn = cleanBaseEn,
                        BranchId = baseCust.BranchId,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.PartyGroups.Add(partyGroup);
                    await _context.SaveChangesAsync();

                    var createdCustomers = new List<Customer>();
                    foreach (var cfg in custConfigs)
                    {
                        var customerRecord = new Customer
                        {
                            BranchId = baseCust.BranchId,
                            Code = cfg.Code,
                            NameAr = cfg.NameAr,
                            NameEn = cfg.NameEn,
                            CustomerCategoryId = custCategoryId,
                            FinancialAccountId = custFinancialAccId,
                            SalesmanId = baseCust.SalesmanId,
                            CurrencyId = cfg.CurrencyId,
                            PartyGroupId = partyGroup.Id,
                            Address = baseCust.Address,
                            GovernorateId = baseCust.GovernorateId,
                            CityId = baseCust.CityId,
                            TaxNumber = baseCust.TaxNumber,
                            CommercialRegister = baseCust.CommercialRegister,
                            TaxPayerType = baseCust.TaxPayerType,
                            Phone = baseCust.Phone,
                            Mobile = baseCust.Mobile,
                            Mobile2 = baseCust.Mobile2,
                            Email = baseCust.Email,
                            Notes = baseCust.Notes,
                            IsActive = baseCust.IsActive,
                            CreatedAt = DateTime.UtcNow
                        };

                        _context.Customers.Add(customerRecord);
                        createdCustomers.Add(customerRecord);
                    }

                    var createdVendors = new List<Vendor>();
                    foreach (var cfg in vendConfigs)
                    {
                        var vendorRecord = new Vendor
                        {
                            BranchId = baseCust.BranchId,
                            Code = cfg.Code,
                            NameAr = cfg.NameAr,
                            NameEn = cfg.NameEn,
                            VendorCategoryId = vendCategoryId,
                            FinancialAccountId = vendFinancialAccId,
                            CurrencyId = cfg.CurrencyId,
                            PartyGroupId = partyGroup.Id,
                            Address = baseCust.Address,
                            GovernorateId = baseCust.GovernorateId,
                            CityId = baseCust.CityId,
                            TaxNumber = baseCust.TaxNumber,
                            CommercialRegister = baseCust.CommercialRegister,
                            TaxPayerType = baseCust.TaxPayerType,
                            Phone = baseCust.Phone,
                            Mobile = baseCust.Mobile,
                            Mobile2 = baseCust.Mobile2,
                            Email = baseCust.Email,
                            Notes = baseCust.Notes,
                            IsActive = baseCust.IsActive,
                            CreatedAt = DateTime.UtcNow
                        };

                        _context.Vendors.Add(vendorRecord);
                        createdVendors.Add(vendorRecord);
                    }

                    await _context.SaveChangesAsync();

                    if (ownTransaction != null)
                    {
                        await ownTransaction.CommitAsync();
                    }

                    // 9. Audit Logging
                    await _auditService.LogAsync(new AuditLogEntry
                    {
                        ScreenCode = "MD_CUST_GRP",
                        ScreenName = "Customers & Related Party Groups",
                        ModuleName = "MasterData",
                        EntityName = "PartyGroup",
                        EntityId = partyGroup.Id.ToString(),
                        Action = AuditAction.Create,
                        Description = $"تم إنشاء مجموعة الأطراف المرتبطة ({partyGroup.Code} - {partyGroup.NameAr}): {createdCustomers.Count} عميل، {createdVendors.Count} مورد.",
                        NewValues = new
                        {
                            GroupId = partyGroup.Id,
                            GroupCode = partyGroup.Code,
                            Customers = createdCustomers.Select(c => new { c.Id, c.Code, c.NameAr, c.NameEn, c.CurrencyId }).ToList(),
                            Vendors = createdVendors.Select(v => new { v.Id, v.Code, v.NameAr, v.NameEn, v.CurrencyId }).ToList()
                        },
                        Success = true
                    });

                    string summaryMsg = createdCustomers.Count == 1 && createdVendors.Count == 0
                        ? $"تم إضافة العميل ({createdCustomers[0].NameAr}) بنجاح بكود ({createdCustomers[0].Code})."
                        : $"تم إنشاء المجموعة ({partyGroup.Code}) بنجاح: ({createdCustomers.Count}) سجل عميل" +
                          (createdVendors.Count > 0 ? $" و({createdVendors.Count}) سجل مورد مرتبط." : ".");

                    return new CreateCustomerGroupResultDto
                    {
                        Success = true,
                        Message = summaryMsg,
                        PartyGroupId = partyGroup.Id,
                        PartyGroupCode = partyGroup.Code,
                        CreatedCustomers = createdCustomers,
                        CreatedVendors = createdVendors
                    };
                }
                catch (Exception ex)
                {
                    if (ownTransaction != null)
                    {
                        await ownTransaction.RollbackAsync();
                    }

                    return new CreateCustomerGroupResultDto
                    {
                        Success = false,
                        Message = $"حدث خطأ أثناء إنشاء السجلات: {ex.Message}"
                    };
                }
                finally
                {
                    if (ownTransaction != null)
                    {
                        await ownTransaction.DisposeAsync();
                    }
                }
            });
        }

        public async Task<(bool IsValid, string ErrorMessage)> ValidatePartyCurrencyChangeAsync(
            string partyType,
            int partyId,
            int? partyGroupId,
            int? newCurrencyId)
        {
            if (!partyGroupId.HasValue || !newCurrencyId.HasValue)
                return (true, string.Empty);

            if (string.Equals(partyType, "Vendor", StringComparison.OrdinalIgnoreCase))
            {
                bool exists = await _context.Vendors
                    .IgnoreQueryFilters()
                    .AnyAsync(v => v.PartyGroupId == partyGroupId.Value && v.Id != partyId && v.CurrencyId == newCurrencyId.Value);
                if (exists)
                {
                    return (false, "يوجد مورد آخر مرتبط بنفس المجموعة يستخدم نفس العملة بالفعل.");
                }
            }
            else
            {
                bool exists = await _context.Customers
                    .IgnoreQueryFilters()
                    .AnyAsync(c => c.PartyGroupId == partyGroupId.Value && c.Id != partyId && c.CurrencyId == newCurrencyId.Value);
                if (exists)
                {
                    return (false, "يوجد عميل آخر مرتبط بنفس المجموعة يستخدم نفس العملة بالفعل.");
                }
            }

            return (true, string.Empty);
        }

        public async Task<LinkPartyRecordResultDto> LinkRecordsAsync(LinkPartyRecordRequestDto request)
        {
            var srcType = NormalizePartyType(request.SourcePartyType);
            var tgtType = NormalizePartyType(request.TargetPartyType);

            // 1. Prevent Self-Linking
            if (srcType == tgtType && request.SourcePartyId == request.TargetPartyId)
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "لا يمكن ربط السجل بنفسه."
                };
            }

            // 2. Permission Checks
            bool involvesCustomer = srcType == "Customer" || tgtType == "Customer";
            bool involvesVendor = srcType == "Vendor" || tgtType == "Vendor";

            if (involvesCustomer && !_currentUser.HasPermission("MasterData.Customers.Edit"))
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "ليس لديك صلاحية تعديل وربط سجلات العملاء (MasterData.Customers.Edit)."
                };
            }

            if (involvesVendor && !_currentUser.HasPermission("MasterData.Vendors.Edit"))
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "ليس لديك صلاحية تعديل وربط سجلات الموردين (MasterData.Vendors.Edit)."
                };
            }

            // 3. Load Source & Target Records
            var srcInfo = await GetPartyEntityInfoAsync(srcType, request.SourcePartyId);
            var tgtInfo = await GetPartyEntityInfoAsync(tgtType, request.TargetPartyId);

            if (srcInfo == null || tgtInfo == null)
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "أحد السجلين المطلوب ربطهما غير موجود."
                };
            }

            // 4. Branch Access & Consistency Validation
            if (!_currentUser.CanAccessBranch(srcInfo.Value.BranchId) || !_currentUser.CanAccessBranch(tgtInfo.Value.BranchId))
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "ليس لديك صلاحية الوصول إلى فرع أحد السجلين."
                };
            }

            if (srcInfo.Value.BranchId != tgtInfo.Value.BranchId)
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "لا يمكن ربط سجلات تتبع فروعًا مختلفة (يجب أن يكون السجلان داخل نفس الفرع)."
                };
            }

            int? srcGroupId = srcInfo.Value.PartyGroupId;
            int? tgtGroupId = tgtInfo.Value.PartyGroupId;

            // 5. Prevent Duplicate Link
            if (srcGroupId.HasValue && tgtGroupId.HasValue && srcGroupId.Value == tgtGroupId.Value)
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "السجلان مرتبطان بالفعل داخل نفس المجموعة."
                };
            }

            // 6. Check if Target is already in a DIFFERENT group -> require user confirmation to move
            if (tgtGroupId.HasValue && srcGroupId.HasValue && tgtGroupId.Value != srcGroupId.Value && !request.ConfirmMove)
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    RequiresMoveConfirmation = true,
                    Message = $"السجل ({tgtInfo.Value.Code} - {tgtInfo.Value.NameAr}) مرتبط بمجموعة أخرى بالفعل. هل تريد تأكيد نقله إلى المجموعة الحالية؟"
                };
            }

            // Determine destination group ID
            int destinationGroupId;
            PartyGroup? createdGroup = null;

            if (srcGroupId.HasValue)
            {
                destinationGroupId = srcGroupId.Value;
            }
            else if (tgtGroupId.HasValue && !request.ConfirmMove)
            {
                destinationGroupId = tgtGroupId.Value;
            }
            else
            {
                // Auto-create a new PartyGroup for legacy unlinked records
                var cleanAr = StripCurrencySuffix(srcInfo.Value.NameAr);
                var cleanEn = !string.IsNullOrWhiteSpace(srcInfo.Value.NameEn)
                    ? StripCurrencySuffix(srcInfo.Value.NameEn)
                    : cleanAr;

                createdGroup = new PartyGroup
                {
                    Code = await GeneratePartyGroupCodeAsync(),
                    NameAr = cleanAr,
                    NameEn = cleanEn,
                    BranchId = srcInfo.Value.BranchId,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.PartyGroups.Add(createdGroup);
                await _context.SaveChangesAsync();
                destinationGroupId = createdGroup.Id;
            }

            // 7. Validate No Duplicate Currency for the same PartyType in the destination group
            var existingGroupCustomers = await _context.Customers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.PartyGroupId == destinationGroupId)
                .Select(c => new { c.Id, c.CurrencyId, c.Code, c.NameAr })
                .ToListAsync();

            var existingGroupVendors = await _context.Vendors
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(v => v.PartyGroupId == destinationGroupId)
                .Select(v => new { v.Id, v.CurrencyId, v.Code, v.NameAr })
                .ToListAsync();

            // Simulate the resulting Customer & Vendor currency sets after linking
            var simulatedCustomerCurrencies = existingGroupCustomers
                .ToDictionary(c => c.Id, c => c.CurrencyId);
            var simulatedVendorCurrencies = existingGroupVendors
                .ToDictionary(v => v.Id, v => v.CurrencyId);

            if (srcType == "Customer") simulatedCustomerCurrencies[srcInfo.Value.Id] = srcInfo.Value.CurrencyId;
            else simulatedVendorCurrencies[srcInfo.Value.Id] = srcInfo.Value.CurrencyId;

            if (tgtType == "Customer") simulatedCustomerCurrencies[tgtInfo.Value.Id] = tgtInfo.Value.CurrencyId;
            else simulatedVendorCurrencies[tgtInfo.Value.Id] = tgtInfo.Value.CurrencyId;

            var nonNullCustCurrencies = simulatedCustomerCurrencies.Values.Where(c => c.HasValue).Select(c => c!.Value).ToList();
            if (nonNullCustCurrencies.Count != nonNullCustCurrencies.Distinct().Count())
            {
                if (createdGroup != null)
                {
                    _context.PartyGroups.Remove(createdGroup);
                    await _context.SaveChangesAsync();
                }
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "لا يمكن إتمام الربط: يوجد سجل عميل آخر بنفس العملة داخل هذه المجموعة."
                };
            }

            var nonNullVendCurrencies = simulatedVendorCurrencies.Values.Where(c => c.HasValue).Select(c => c!.Value).ToList();
            if (nonNullVendCurrencies.Count != nonNullVendCurrencies.Distinct().Count())
            {
                if (createdGroup != null)
                {
                    _context.PartyGroups.Remove(createdGroup);
                    await _context.SaveChangesAsync();
                }
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "لا يمكن إتمام الربط: يوجد سجل مورد آخر بنفس العملة داخل هذه المجموعة."
                };
            }

            // 8. Apply PartyGroupId without modifying Code, Name, Currency, FinancialAccount, or Transactions
            await SetPartyGroupIdAsync(srcType, srcInfo.Value.Id, destinationGroupId);
            await SetPartyGroupIdAsync(tgtType, tgtInfo.Value.Id, destinationGroupId);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(new AuditLogEntry
            {
                ScreenCode = "MD_PARTY_LINK",
                ScreenName = "Link Related Customer/Vendor Records",
                ModuleName = "MasterData",
                EntityName = "PartyGroup",
                EntityId = destinationGroupId.ToString(),
                Action = AuditAction.Update,
                Description = $"تم ربط السجل ({tgtType}:{tgtInfo.Value.Code}) مع السجل ({srcType}:{srcInfo.Value.Code}) في المجموعة رقم {destinationGroupId}.",
                OldValues = new { SourceOldGroupId = srcGroupId, TargetOldGroupId = tgtGroupId },
                NewValues = new { PartyGroupId = destinationGroupId, SourceId = srcInfo.Value.Id, TargetId = tgtInfo.Value.Id },
                Success = true
            });

            var details = await GetPartyGroupDetailsAsync(srcType, srcInfo.Value.Id);

            return new LinkPartyRecordResultDto
            {
                Success = true,
                Message = "تم ربط السجل بالمجموعة بنجاح.",
                PartyGroupId = destinationGroupId,
                GroupDetails = details
            };
        }

        public async Task<LinkPartyRecordResultDto> UnlinkRecordAsync(string partyType, int partyId)
        {
            var normType = NormalizePartyType(partyType);

            if (normType == "Customer" && !_currentUser.HasPermission("MasterData.Customers.Edit"))
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "ليس لديك صلاحية فك ارتباط سجلات العملاء (MasterData.Customers.Edit)."
                };
            }

            if (normType == "Vendor" && !_currentUser.HasPermission("MasterData.Vendors.Edit"))
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "ليس لديك صلاحية فك ارتباط سجلات الموردين (MasterData.Vendors.Edit)."
                };
            }

            var info = await GetPartyEntityInfoAsync(normType, partyId);
            if (info == null)
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "السجل المطلوب فك ارتباطه غير موجود."
                };
            }

            if (!_currentUser.CanAccessBranch(info.Value.BranchId))
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "ليس لديك صلاحية الوصول إلى فرع هذا السجل."
                };
            }

            if (!info.Value.PartyGroupId.HasValue)
            {
                return new LinkPartyRecordResultDto
                {
                    Success = false,
                    Message = "هذا السجل غير مرتبط بأي مجموعة حالياً."
                };
            }

            int oldGroupId = info.Value.PartyGroupId.Value;
            await SetPartyGroupIdAsync(normType, partyId, null);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(new AuditLogEntry
            {
                ScreenCode = "MD_PARTY_UNLINK",
                ScreenName = "Unlink Related Customer/Vendor Record",
                ModuleName = "MasterData",
                EntityName = normType,
                EntityId = partyId.ToString(),
                Action = AuditAction.Update,
                Description = $"تم فك ارتباط السجل ({info.Value.Code} - {info.Value.NameAr}) من المجموعة رقم {oldGroupId} دون حذف السجل.",
                OldValues = new { PartyGroupId = oldGroupId },
                NewValues = new { PartyGroupId = (int?)null },
                Success = true
            });

            return new LinkPartyRecordResultDto
            {
                Success = true,
                Message = $"تم فك ارتباط السجل ({info.Value.Code} - {info.Value.NameAr}) من المجموعة بنجاح مع الاحتفاظ بالسجل وجميع حركاته.",
                PartyGroupId = oldGroupId
            };
        }

        public async Task<PartyGroupDetailsDto> GetPartyGroupDetailsAsync(
            string partyType,
            int partyId,
            int? unifiedTargetCurrencyId = null,
            DateTime? conversionDate = null,
            Dictionary<string, decimal>? customExchangeRates = null)
        {
            var normType = NormalizePartyType(partyType);
            int? groupId = null;

            if (normType == "PartyGroup")
            {
                groupId = partyId;
            }
            else
            {
                var info = await GetPartyEntityInfoAsync(normType, partyId);
                groupId = info?.PartyGroupId;

                if (!groupId.HasValue && info != null)
                {
                    // Single unlinked record: return its own standalone details
                    var singleDto = await BuildSingleUnlinkedPartyDetailsAsync(normType, partyId);
                    return singleDto;
                }
            }

            if (!groupId.HasValue)
            {
                return new PartyGroupDetailsDto();
            }

            var group = await _context.PartyGroups
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(pg => pg.Id == groupId.Value);

            var customers = await _context.Customers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(c => c.Currency)
                .Include(c => c.Branch)
                .Where(c => c.PartyGroupId == groupId.Value)
                .OrderBy(c => c.Code)
                .ToListAsync();

            var vendors = await _context.Vendors
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(v => v.Currency)
                .Include(v => v.Branch)
                .Where(v => v.PartyGroupId == groupId.Value)
                .OrderBy(v => v.Code)
                .ToListAsync();

            // Enforce Branch Isolation
            customers = customers.Where(c => _currentUser.CanAccessBranch(c.BranchId)).ToList();
            vendors = vendors.Where(v => _currentUser.CanAccessBranch(v.BranchId)).ToList();

            var custIds = customers.Select(c => c.Id).ToList();
            var vendIds = vendors.Select(v => v.Id).ToList();

            // Fetch independent ledger balances per Customer and Vendor
            var custBalances = await GetSubLedgerBalancesAsync(SubLedgerType.Customer, custIds);
            var vendBalances = await GetSubLedgerBalancesAsync(SubLedgerType.Vendor, vendIds);

            var custDocCounts = custIds.Any()
                ? await _context.DocumentHeaders
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(d => d.PartyType == DocumentPartyType.Customer && d.PartyId.HasValue && custIds.Contains(d.PartyId.Value))
                    .GroupBy(d => d.PartyId!.Value)
                    .Select(g => new { PartyId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.PartyId, x => x.Count)
                : new Dictionary<int, int>();

            var vendDocCounts = vendIds.Any()
                ? await _context.DocumentHeaders
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(d => d.PartyType == DocumentPartyType.Vendor && d.PartyId.HasValue && vendIds.Contains(d.PartyId.Value))
                    .GroupBy(d => d.PartyId!.Value)
                    .Select(g => new { PartyId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.PartyId, x => x.Count)
                : new Dictionary<int, int>();

            var records = new List<RelatedPartyRecordDto>();

            foreach (var c in customers)
            {
                custBalances.TryGetValue(c.Id, out var bal);
                custDocCounts.TryGetValue(c.Id, out int docCount);

                records.Add(new RelatedPartyRecordDto
                {
                    PartyType = "Customer",
                    PartyId = c.Id,
                    Code = c.Code,
                    NameAr = c.NameAr,
                    NameEn = c.NameEn,
                    CurrencyId = c.CurrencyId,
                    CurrencyCode = c.Currency?.Code ?? "EGP",
                    CurrencyNameAr = c.Currency?.NameAr ?? "جنيه مصري",
                    BranchId = c.BranchId,
                    BranchName = c.Branch?.NameAr ?? c.Branch?.Name ?? $"Branch {c.BranchId}",
                    PartyGroupId = c.PartyGroupId,
                    PartyGroupCode = group?.Code,
                    IsActive = c.IsActive,
                    DebitInCurrency = bal.Debit,
                    CreditInCurrency = bal.Credit,
                    BalanceInCurrency = bal.Debit - bal.Credit,
                    JournalEntriesCount = bal.LinesCount,
                    DocumentsCount = docCount
                });
            }

            foreach (var v in vendors)
            {
                vendBalances.TryGetValue(v.Id, out var bal);
                vendDocCounts.TryGetValue(v.Id, out int docCount);

                records.Add(new RelatedPartyRecordDto
                {
                    PartyType = "Vendor",
                    PartyId = v.Id,
                    Code = v.Code,
                    NameAr = v.NameAr,
                    NameEn = v.NameEn,
                    CurrencyId = v.CurrencyId,
                    CurrencyCode = v.Currency?.Code ?? "EGP",
                    CurrencyNameAr = v.Currency?.NameAr ?? "جنيه مصري",
                    BranchId = v.BranchId,
                    BranchName = v.Branch?.NameAr ?? v.Branch?.Name ?? $"Branch {v.BranchId}",
                    PartyGroupId = v.PartyGroupId,
                    PartyGroupCode = group?.Code,
                    IsActive = v.IsActive,
                    DebitInCurrency = bal.Debit,
                    CreditInCurrency = bal.Credit,
                    BalanceInCurrency = bal.Debit - bal.Credit,
                    JournalEntriesCount = bal.LinesCount,
                    DocumentsCount = docCount
                });
            }

            // Aggregate independent balances STRICTLY per currency (never mixing EGP + USD + EUR + CNY)
            var balancesByCurrency = records
                .GroupBy(r => new { r.CurrencyId, r.CurrencyCode, r.CurrencyNameAr })
                .Select(g =>
                {
                    var custs = g.Where(x => x.PartyType == "Customer").ToList();
                    var vends = g.Where(x => x.PartyType == "Vendor").ToList();

                    return new CurrencyBalanceSummaryDto
                    {
                        CurrencyId = g.Key.CurrencyId,
                        CurrencyCode = g.Key.CurrencyCode,
                        CurrencyNameAr = g.Key.CurrencyNameAr,
                        TotalCustomerDebit = custs.Sum(x => x.DebitInCurrency),
                        TotalCustomerCredit = custs.Sum(x => x.CreditInCurrency),
                        NetCustomerBalance = custs.Sum(x => x.BalanceInCurrency),
                        TotalVendorDebit = vends.Sum(x => x.DebitInCurrency),
                        TotalVendorCredit = vends.Sum(x => x.CreditInCurrency),
                        NetVendorBalance = vends.Sum(x => x.BalanceInCurrency)
                    };
                })
                .OrderBy(b => b.CurrencyCode)
                .ToList();

            var result = new PartyGroupDetailsDto
            {
                PartyGroupId = groupId,
                GroupCode = group?.Code ?? $"PG-{groupId}",
                GroupNameAr = group?.NameAr ?? records.FirstOrDefault()?.NameAr ?? string.Empty,
                GroupNameEn = group?.NameEn ?? records.FirstOrDefault()?.NameEn ?? string.Empty,
                BranchId = group?.BranchId ?? records.FirstOrDefault()?.BranchId,
                Records = records,
                BalancesByCurrency = balancesByCurrency
            };

            // Optional Unified Currency Evaluation (ONLY if explicitly requested with exchange rate & conversion date)
            if (unifiedTargetCurrencyId.HasValue && unifiedTargetCurrencyId.Value > 0)
            {
                var targetCurr = await _context.Currencies
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == unifiedTargetCurrencyId.Value);

                if (targetCurr != null)
                {
                    var allCurrencies = await _context.Currencies
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .ToDictionaryAsync(c => c.Code, c => c.ExchangeRate, StringComparer.OrdinalIgnoreCase);

                    decimal targetRateToLocal = customExchangeRates != null && customExchangeRates.TryGetValue(targetCurr.Code, out var crTarget)
                        ? crTarget
                        : (targetCurr.ExchangeRate > 0 ? targetCurr.ExchangeRate : 1m);

                    var evalDto = new UnifiedCurrencyEvaluationDto
                    {
                        TargetCurrencyCode = targetCurr.Code,
                        ConversionDate = conversionDate ?? DateTime.UtcNow
                    };

                    foreach (var currBal in balancesByCurrency)
                    {
                        decimal sourceRateToLocal = 1m;
                        if (customExchangeRates != null && customExchangeRates.TryGetValue(currBal.CurrencyCode, out var customRate))
                        {
                            sourceRateToLocal = customRate;
                        }
                        else if (allCurrencies.TryGetValue(currBal.CurrencyCode, out var dbRate) && dbRate > 0)
                        {
                            sourceRateToLocal = dbRate;
                        }

                        decimal effectiveFactor = targetRateToLocal > 0 ? (sourceRateToLocal / targetRateToLocal) : 1m;
                        evalDto.AppliedExchangeRates[currBal.CurrencyCode] = Math.Round(effectiveFactor, 6);

                        evalDto.TotalConvertedCustomerBalance += Math.Round(currBal.NetCustomerBalance * effectiveFactor, 2);
                        evalDto.TotalConvertedVendorBalance += Math.Round(currBal.NetVendorBalance * effectiveFactor, 2);
                    }

                    evalDto.TotalConvertedNetBalance = evalDto.TotalConvertedCustomerBalance + evalDto.TotalConvertedVendorBalance;
                    result.UnifiedEvaluation = evalDto;
                }
            }

            return result;
        }

        public async Task<List<RelatedPartyRecordDto>> SearchPartiesForLinkingAsync(
            string searchPartyType,
            string searchTerm,
            int branchId,
            string? excludePartyType = null,
            int? excludePartyId = null)
        {
            if (!_currentUser.CanAccessBranch(branchId))
                return new List<RelatedPartyRecordDto>();

            var normSearchType = NormalizePartyType(searchPartyType);
            var normExcludeType = excludePartyType != null ? NormalizePartyType(excludePartyType) : null;
            var term = (searchTerm ?? string.Empty).Trim();

            var results = new List<RelatedPartyRecordDto>();

            if (normSearchType == "Customer" || normSearchType == "All")
            {
                var q = _context.Customers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(c => c.Currency)
                    .Include(c => c.Branch)
                    .Include(c => c.PartyGroup)
                    .Where(c => c.BranchId == branchId);

                if (normExcludeType == "Customer" && excludePartyId.HasValue)
                {
                    q = q.Where(c => c.Id != excludePartyId.Value);
                }

                if (!string.IsNullOrWhiteSpace(term))
                {
                    q = q.Where(c => c.Code.Contains(term) || c.NameAr.Contains(term) || c.NameEn.Contains(term));
                }

                var custs = await q.OrderBy(c => c.Code).Take(30).ToListAsync();
                results.AddRange(custs.Select(c => new RelatedPartyRecordDto
                {
                    PartyType = "Customer",
                    PartyId = c.Id,
                    Code = c.Code,
                    NameAr = c.NameAr,
                    NameEn = c.NameEn,
                    CurrencyId = c.CurrencyId,
                    CurrencyCode = c.Currency?.Code ?? "EGP",
                    CurrencyNameAr = c.Currency?.NameAr ?? "جنيه مصري",
                    BranchId = c.BranchId,
                    BranchName = c.Branch?.NameAr ?? c.Branch?.Name ?? string.Empty,
                    PartyGroupId = c.PartyGroupId,
                    PartyGroupCode = c.PartyGroup?.Code,
                    IsActive = c.IsActive
                }));
            }

            if (normSearchType == "Vendor" || normSearchType == "All")
            {
                var q = _context.Vendors
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(v => v.Currency)
                    .Include(v => v.Branch)
                    .Include(v => v.PartyGroup)
                    .Where(v => v.BranchId == branchId);

                if (normExcludeType == "Vendor" && excludePartyId.HasValue)
                {
                    q = q.Where(v => v.Id != excludePartyId.Value);
                }

                if (!string.IsNullOrWhiteSpace(term))
                {
                    q = q.Where(v => v.Code.Contains(term) || v.NameAr.Contains(term) || v.NameEn.Contains(term));
                }

                var vends = await q.OrderBy(v => v.Code).Take(30).ToListAsync();
                results.AddRange(vends.Select(v => new RelatedPartyRecordDto
                {
                    PartyType = "Vendor",
                    PartyId = v.Id,
                    Code = v.Code,
                    NameAr = v.NameAr,
                    NameEn = v.NameEn,
                    CurrencyId = v.CurrencyId,
                    CurrencyCode = v.Currency?.Code ?? "EGP",
                    CurrencyNameAr = v.Currency?.NameAr ?? "جنيه مصري",
                    BranchId = v.BranchId,
                    BranchName = v.Branch?.NameAr ?? v.Branch?.Name ?? string.Empty,
                    PartyGroupId = v.PartyGroupId,
                    PartyGroupCode = v.PartyGroup?.Code,
                    IsActive = v.IsActive
                }));
            }

            return results;
        }

        private async Task<Dictionary<int, (decimal Debit, decimal Credit, int LinesCount)>> GetSubLedgerBalancesAsync(
            SubLedgerType subLedgerType,
            List<int> partyIds)
        {
            var dict = new Dictionary<int, (decimal Debit, decimal Credit, int LinesCount)>();
            if (partyIds == null || !partyIds.Any())
                return dict;

            var lines = await _context.JournalEntryLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(l => l.SubLedgerType == subLedgerType
                            && l.SubLedgerId.HasValue
                            && partyIds.Contains(l.SubLedgerId.Value)
                            && l.JournalEntryHeader != null
                            && l.JournalEntryHeader.Status == JournalEntryStatus.Posted)
                .Select(l => new
                {
                    PartyId = l.SubLedgerId!.Value,
                    Debit = (l.DebitForeign != 0 || l.CreditForeign != 0) ? l.DebitForeign : l.DebitLocal,
                    Credit = (l.DebitForeign != 0 || l.CreditForeign != 0) ? l.CreditForeign : l.CreditLocal
                })
                .ToListAsync();

            foreach (var grp in lines.GroupBy(x => x.PartyId))
            {
                dict[grp.Key] = (grp.Sum(x => x.Debit), grp.Sum(x => x.Credit), grp.Count());
            }

            return dict;
        }

        private async Task<PartyGroupDetailsDto> BuildSingleUnlinkedPartyDetailsAsync(string normType, int partyId)
        {
            var dto = new PartyGroupDetailsDto();
            if (normType == "Customer")
            {
                var c = await _context.Customers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(x => x.Currency)
                    .Include(x => x.Branch)
                    .FirstOrDefaultAsync(x => x.Id == partyId);

                if (c != null && _currentUser.CanAccessBranch(c.BranchId))
                {
                    var balMap = await GetSubLedgerBalancesAsync(SubLedgerType.Customer, new List<int> { c.Id });
                    balMap.TryGetValue(c.Id, out var bal);

                    var rec = new RelatedPartyRecordDto
                    {
                        PartyType = "Customer",
                        PartyId = c.Id,
                        Code = c.Code,
                        NameAr = c.NameAr,
                        NameEn = c.NameEn,
                        CurrencyId = c.CurrencyId,
                        CurrencyCode = c.Currency?.Code ?? "EGP",
                        CurrencyNameAr = c.Currency?.NameAr ?? "جنيه مصري",
                        BranchId = c.BranchId,
                        BranchName = c.Branch?.NameAr ?? c.Branch?.Name ?? string.Empty,
                        IsActive = c.IsActive,
                        DebitInCurrency = bal.Debit,
                        CreditInCurrency = bal.Credit,
                        BalanceInCurrency = bal.Debit - bal.Credit,
                        JournalEntriesCount = bal.LinesCount
                    };
                    dto.GroupNameAr = StripCurrencySuffix(c.NameAr);
                    dto.GroupNameEn = StripCurrencySuffix(c.NameEn);
                    dto.BranchId = c.BranchId;
                    dto.Records.Add(rec);
                    dto.BalancesByCurrency.Add(new CurrencyBalanceSummaryDto
                    {
                        CurrencyId = rec.CurrencyId,
                        CurrencyCode = rec.CurrencyCode,
                        CurrencyNameAr = rec.CurrencyNameAr,
                        TotalCustomerDebit = rec.DebitInCurrency,
                        TotalCustomerCredit = rec.CreditInCurrency,
                        NetCustomerBalance = rec.BalanceInCurrency
                    });
                }
            }
            else if (normType == "Vendor")
            {
                var v = await _context.Vendors
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(x => x.Currency)
                    .Include(x => x.Branch)
                    .FirstOrDefaultAsync(x => x.Id == partyId);

                if (v != null && _currentUser.CanAccessBranch(v.BranchId))
                {
                    var balMap = await GetSubLedgerBalancesAsync(SubLedgerType.Vendor, new List<int> { v.Id });
                    balMap.TryGetValue(v.Id, out var bal);

                    var rec = new RelatedPartyRecordDto
                    {
                        PartyType = "Vendor",
                        PartyId = v.Id,
                        Code = v.Code,
                        NameAr = v.NameAr,
                        NameEn = v.NameEn,
                        CurrencyId = v.CurrencyId,
                        CurrencyCode = v.Currency?.Code ?? "EGP",
                        CurrencyNameAr = v.Currency?.NameAr ?? "جنيه مصري",
                        BranchId = v.BranchId,
                        BranchName = v.Branch?.NameAr ?? v.Branch?.Name ?? string.Empty,
                        IsActive = v.IsActive,
                        DebitInCurrency = bal.Debit,
                        CreditInCurrency = bal.Credit,
                        BalanceInCurrency = bal.Debit - bal.Credit,
                        JournalEntriesCount = bal.LinesCount
                    };
                    dto.GroupNameAr = StripCurrencySuffix(v.NameAr);
                    dto.GroupNameEn = StripCurrencySuffix(v.NameEn);
                    dto.BranchId = v.BranchId;
                    dto.Records.Add(rec);
                    dto.BalancesByCurrency.Add(new CurrencyBalanceSummaryDto
                    {
                        CurrencyId = rec.CurrencyId,
                        CurrencyCode = rec.CurrencyCode,
                        CurrencyNameAr = rec.CurrencyNameAr,
                        TotalVendorDebit = rec.DebitInCurrency,
                        TotalVendorCredit = rec.CreditInCurrency,
                        NetVendorBalance = rec.BalanceInCurrency
                    });
                }
            }
            return dto;
        }

        private async Task<(int Id, string Code, string NameAr, string NameEn, int BranchId, int? CurrencyId, int? PartyGroupId)?> GetPartyEntityInfoAsync(
            string normType, int id)
        {
            if (normType == "Vendor")
            {
                var v = await _context.Vendors
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id);
                if (v == null) return null;
                return (v.Id, v.Code, v.NameAr, v.NameEn, v.BranchId, v.CurrencyId, v.PartyGroupId);
            }
            else
            {
                var c = await _context.Customers
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id);
                if (c == null) return null;
                return (c.Id, c.Code, c.NameAr, c.NameEn, c.BranchId, c.CurrencyId, c.PartyGroupId);
            }
        }

        private async Task SetPartyGroupIdAsync(string normType, int id, int? partyGroupId)
        {
            if (normType == "Vendor")
            {
                var v = await _context.Vendors.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
                if (v != null)
                {
                    v.PartyGroupId = partyGroupId;
                    v.UpdatedAt = DateTime.UtcNow;
                }
            }
            else
            {
                var c = await _context.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
                if (c != null)
                {
                    c.PartyGroupId = partyGroupId;
                    c.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        private async Task<string> GeneratePartyGroupCodeAsync()
        {
            int count = await _context.PartyGroups.IgnoreQueryFilters().CountAsync();
            string candidate;
            do
            {
                count++;
                candidate = $"PG-{count:D6}";
            }
            while (await _context.PartyGroups.IgnoreQueryFilters().AnyAsync(pg => pg.Code == candidate));

            return candidate;
        }

        private static string NormalizePartyType(string? partyType)
        {
            if (string.IsNullOrWhiteSpace(partyType))
                return "Customer";
            if (partyType.Trim().Equals("Vendor", StringComparison.OrdinalIgnoreCase))
                return "Vendor";
            if (partyType.Trim().Equals("PartyGroup", StringComparison.OrdinalIgnoreCase))
                return "PartyGroup";
            if (partyType.Trim().Equals("All", StringComparison.OrdinalIgnoreCase))
                return "All";
            return "Customer";
        }
    }
}
