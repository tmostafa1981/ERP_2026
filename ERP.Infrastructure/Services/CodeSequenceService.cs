using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERP.Infrastructure.Services
{
    public class CodeSequenceService : ICodeSequenceService
    {
        private readonly ErpDbContext _context;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _memoryLocks = new(StringComparer.OrdinalIgnoreCase);

        private static SemaphoreSlim GetMemoryLock(string entityName, int? branchId) =>
            _memoryLocks.GetOrAdd($"{entityName}:{branchId ?? 0}", _ => new SemaphoreSlim(1, 1));

        public static readonly IReadOnlyDictionary<string, string> DefaultPrefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Governorate",      "GOV"  },
            { "City",             "CITY" },
            { "Currency",         "CUR"  },
            { "Unit",             "UNT"  },
            { "ItemGroup",        "GRP"  },
            { "Item",             "ITM"  },
            { "CustomerCategory", "CCAT" },
            { "VendorCategory",   "VCAT" },
            { "Customer",         "CUS"  },
            { "Vendor",           "VEN"  },
            { "Salesman",         "SLM"  },
            { "Warehouse",        "WH"   },
            { "ReceiptVoucher",   "RV"   },
            { "PaymentVoucher",   "PV"   }
        };

        public CodeSequenceService(ErpDbContext context)
        {
            _context = context;
        }

        public async Task<string> GetNextCodeAsync(string entityName, int? branchId = null)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name cannot be null or empty.", nameof(entityName));

            entityName = entityName.Trim();

            // Handle concurrency safely
            if (_context.Database.IsSqlServer())
            {
                return await GetNextCodeSqlServerAsync(entityName, branchId);
            }
            else
            {
                // In-Memory or SQLite database (for unit tests / development)
                return await GetNextCodeInMemoryAsync(entityName, branchId);
            }
        }

        private async Task<string> GetNextCodeSqlServerAsync(string entityName, int? branchId)
        {
            // Ensure sequence record exists first
            await EnsureSequenceRecordExistsAsync(entityName, branchId);

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                IDbContextTransaction? transaction = null;
                if (_context.Database.IsRelational() && _context.Database.CurrentTransaction == null)
                {
                    transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
                }

                try
                {
                    // Query using raw SQL with UPDLOCK, ROWLOCK to guarantee atomic exclusive lock on the sequence row
                    object[] sqlParams = new object[] { entityName, (object?)branchId ?? DBNull.Value };
                    var query = _context.CodeSequences
                        .FromSqlRaw(
                            "SELECT * FROM CodeSequences WITH (UPDLOCK, ROWLOCK) WHERE EntityName = {0} AND (BranchId = {1} OR (BranchId IS NULL AND {1} IS NULL))",
                            sqlParams);

                    var seq = await query.FirstOrDefaultAsync();
                    if (seq == null)
                    {
                        // Fallback in case it wasn't caught
                        seq = await EnsureSequenceRecordExistsAsync(entityName, branchId);
                    }

                    await SyncWithExistingDataAsync(seq, entityName, branchId);

                    long candidate = seq.LastNumber + 1;
                    while (await CodeExistsInDbAsync(entityName, branchId, FormatCode(seq.Prefix, candidate, seq.PaddingLength)))
                    {
                        candidate++;
                    }

                    seq.LastNumber = candidate;
                    seq.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();

                    if (transaction != null)
                    {
                        await transaction.CommitAsync();
                    }

                    return FormatCode(seq.Prefix, seq.LastNumber, seq.PaddingLength);
                }
                catch
                {
                    if (transaction != null)
                    {
                        await transaction.RollbackAsync();
                    }
                    throw;
                }
                finally
                {
                    if (transaction != null)
                    {
                        await transaction.DisposeAsync();
                    }
                }
            });
        }

        private async Task<string> GetNextCodeInMemoryAsync(string entityName, int? branchId)
        {
            var memLock = GetMemoryLock(entityName, branchId);
            await memLock.WaitAsync();
            try
            {
                var seq = await EnsureSequenceRecordExistsAsync(entityName, branchId);

                await SyncWithExistingDataAsync(seq, entityName, branchId);

                long candidate = seq.LastNumber + 1;
                while (await CodeExistsInDbAsync(entityName, branchId, FormatCode(seq.Prefix, candidate, seq.PaddingLength)))
                {
                    candidate++;
                }

                seq.LastNumber = candidate;
                seq.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return FormatCode(seq.Prefix, seq.LastNumber, seq.PaddingLength);
            }
            finally
            {
                memLock.Release();
            }
        }

        public async Task<string> PeekNextCodeAsync(string entityName, int? branchId = null)
        {
            var config = await GetConfigAsync(entityName, branchId);
            return config.PreviewCode;
        }

        public async Task<bool> IsAutoEnabledAsync(string entityName, int? branchId = null)
        {
            var config = await GetConfigAsync(entityName, branchId);
            return config.IsAuto;
        }

        public async Task<bool> IsManualAllowedAsync(string entityName, int? branchId = null)
        {
            var config = await GetConfigAsync(entityName, branchId);
            return config.IsManualAllowed;
        }

        public async Task<CodeSequenceConfigDto> GetConfigAsync(string entityName, int? branchId = null)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name cannot be null or empty.", nameof(entityName));

            entityName = entityName.Trim();

            var seq = await EnsureSequenceRecordExistsAsync(entityName, branchId);
            await SyncWithExistingDataAsync(seq, entityName, branchId);

            long nextNum = seq.LastNumber + 1;
            while (await CodeExistsInDbAsync(entityName, branchId, FormatCode(seq.Prefix, nextNum, seq.PaddingLength)))
            {
                nextNum++;
            }

            return new CodeSequenceConfigDto
            {
                EntityName      = seq.EntityName,
                BranchId        = seq.BranchId,
                Prefix          = seq.Prefix,
                NextNumber      = nextNum,
                PaddingLength   = seq.PaddingLength,
                IsAuto          = seq.IsAuto,
                IsManualAllowed = seq.IsManualAllowed,
                PreviewCode     = FormatCode(seq.Prefix, nextNum, seq.PaddingLength)
            };
        }

        private async Task<string> ResolvePrefixAsync(string entityName, int? branchId)
        {
            if (branchId.HasValue && branchId.Value > 0)
            {
                if (string.Equals(entityName, "Customer", StringComparison.OrdinalIgnoreCase))
                {
                    var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId.Value);
                    if (branch != null)
                    {
                        var bCode = !string.IsNullOrWhiteSpace(branch.BranchCode) ? branch.BranchCode.Trim() : branch.Code.Trim();
                        return $"{bCode}-C";
                    }
                }
                else if (string.Equals(entityName, "Vendor", StringComparison.OrdinalIgnoreCase))
                {
                    var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId.Value);
                    if (branch != null)
                    {
                        var bCode = !string.IsNullOrWhiteSpace(branch.BranchCode) ? branch.BranchCode.Trim() : branch.Code.Trim();
                        return $"{bCode}-V";
                    }
                }
            }

            return DefaultPrefixes.TryGetValue(entityName, out var p) ? p : entityName.ToUpperInvariant();
        }

        private async Task<CodeSequence> EnsureSequenceRecordExistsAsync(string entityName, int? branchId)
        {
            var seq = await _context.CodeSequences
                .FirstOrDefaultAsync(s => s.EntityName == entityName && s.BranchId == branchId);

            if (seq == null)
            {
                return await CreateDefaultSequenceAsync(entityName, branchId);
            }

            string expectedPrefix = await ResolvePrefixAsync(entityName, branchId);
            if (branchId.HasValue && !string.Equals(seq.Prefix, expectedPrefix, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(entityName, "Customer", StringComparison.OrdinalIgnoreCase) || string.Equals(entityName, "Vendor", StringComparison.OrdinalIgnoreCase)))
            {
                seq.Prefix = expectedPrefix;
                await _context.SaveChangesAsync();
            }

            return seq;
        }

        private async Task<CodeSequence> CreateDefaultSequenceAsync(string entityName, int? branchId)
        {
            string prefix = await ResolvePrefixAsync(entityName, branchId);

            var newSeq = new CodeSequence
            {
                EntityName      = entityName,
                BranchId        = branchId,
                Prefix          = prefix,
                LastNumber      = 0,
                PaddingLength   = 6,
                IsAuto          = true,
                IsManualAllowed = true,
                CreatedAt       = DateTime.UtcNow
            };

            _context.CodeSequences.Add(newSeq);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // In case of concurrency race during initial insert, re-query existing
                _context.Entry(newSeq).State = EntityState.Detached;
                var existing = await _context.CodeSequences
                    .FirstOrDefaultAsync(s => s.EntityName == entityName && s.BranchId == branchId);
                if (existing != null) return existing;
                throw;
            }

            return newSeq;
        }

        private async Task SyncWithExistingDataAsync(CodeSequence seq, string entityName, int? branchId)
        {
            var prefix = seq.Prefix;
            List<string> codes = entityName.ToLowerInvariant() switch
            {
                "item" => await _context.Items.AsNoTracking().Where(x => x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync(),
                "customer" => branchId.HasValue && branchId.Value > 0
                    ? await _context.Customers.AsNoTracking().Where(x => x.BranchId == branchId.Value && x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync()
                    : await _context.Customers.AsNoTracking().Where(x => x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync(),
                "vendor" => branchId.HasValue && branchId.Value > 0
                    ? await _context.Vendors.AsNoTracking().Where(x => x.BranchId == branchId.Value && x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync()
                    : await _context.Vendors.AsNoTracking().Where(x => x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync(),
                "salesman" => branchId.HasValue && branchId.Value > 0
                    ? await _context.Salesmen.AsNoTracking().Where(x => x.BranchId == branchId.Value && x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync()
                    : await _context.Salesmen.AsNoTracking().Where(x => x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync(),
                "warehouse" => branchId.HasValue && branchId.Value > 0
                    ? await _context.Warehouses.AsNoTracking().Where(x => x.BranchId == branchId.Value && x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync()
                    : await _context.Warehouses.AsNoTracking().Where(x => x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync(),
                "customercategory" => branchId.HasValue && branchId.Value > 0
                    ? await _context.CustomerCategories.AsNoTracking().Where(x => x.BranchId == branchId.Value && x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync()
                    : await _context.CustomerCategories.AsNoTracking().Where(x => x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync(),
                "vendorcategory" => branchId.HasValue && branchId.Value > 0
                    ? await _context.VendorCategories.AsNoTracking().Where(x => x.BranchId == branchId.Value && x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync()
                    : await _context.VendorCategories.AsNoTracking().Where(x => x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync(),
                "unit" => await _context.Units.AsNoTracking().Where(x => x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync(),
                "itemgroup" => await _context.ItemGroups.AsNoTracking().Where(x => x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync(),
                "currency" => await _context.Currencies.AsNoTracking().Where(x => x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync(),
                "city" => await _context.Cities.AsNoTracking().Where(x => x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync(),
                "governorate" => await _context.Governorates.AsNoTracking().Where(x => x.Code.StartsWith(prefix)).OrderByDescending(x => x.Id).Select(x => x.Code).Take(100).ToListAsync(),
                _ => new List<string>()
            };

            long maxNum = seq.LastNumber;
            foreach (var code in codes)
            {
                if (code != null && code.Length > prefix.Length)
                {
                    var suffix = code.Substring(prefix.Length);
                    if (long.TryParse(suffix, out long parsedNum))
                    {
                        if (parsedNum > maxNum)
                        {
                            maxNum = parsedNum;
                        }
                    }
                }
            }

            if (maxNum > seq.LastNumber)
            {
                seq.LastNumber = maxNum;
                await _context.SaveChangesAsync();
            }
        }

        private async Task<bool> CodeExistsInDbAsync(string entityName, int? branchId, string code)
        {
            var normalized = code.Trim();
            return entityName.ToLowerInvariant() switch
            {
                "item" => await _context.Items.AnyAsync(x => x.Code == normalized),
                "customer" => branchId.HasValue && branchId.Value > 0
                    ? await _context.Customers.AnyAsync(x => x.BranchId == branchId.Value && x.Code == normalized)
                    : await _context.Customers.AnyAsync(x => x.Code == normalized),
                "vendor" => branchId.HasValue && branchId.Value > 0
                    ? await _context.Vendors.AnyAsync(x => x.BranchId == branchId.Value && x.Code == normalized)
                    : await _context.Vendors.AnyAsync(x => x.Code == normalized),
                "salesman" => branchId.HasValue && branchId.Value > 0
                    ? await _context.Salesmen.AnyAsync(x => x.BranchId == branchId.Value && x.Code == normalized)
                    : await _context.Salesmen.AnyAsync(x => x.Code == normalized),
                "warehouse" => branchId.HasValue && branchId.Value > 0
                    ? await _context.Warehouses.AnyAsync(x => x.BranchId == branchId.Value && x.Code == normalized)
                    : await _context.Warehouses.AnyAsync(x => x.Code == normalized),
                "customercategory" => branchId.HasValue && branchId.Value > 0
                    ? await _context.CustomerCategories.AnyAsync(x => x.BranchId == branchId.Value && x.Code == normalized)
                    : await _context.CustomerCategories.AnyAsync(x => x.Code == normalized),
                "vendorcategory" => branchId.HasValue && branchId.Value > 0
                    ? await _context.VendorCategories.AnyAsync(x => x.BranchId == branchId.Value && x.Code == normalized)
                    : await _context.VendorCategories.AnyAsync(x => x.Code == normalized),
                "unit" => await _context.Units.AnyAsync(x => x.Code == normalized),
                "itemgroup" => await _context.ItemGroups.AnyAsync(x => x.Code == normalized),
                "currency" => await _context.Currencies.AnyAsync(x => x.Code == normalized),
                "city" => await _context.Cities.AnyAsync(x => x.Code == normalized),
                "governorate" => await _context.Governorates.AnyAsync(x => x.Code == normalized),
                _ => false
            };
        }

        private static string FormatCode(string prefix, long number, int paddingLength)
        {
            return $"{prefix}{number.ToString("D" + paddingLength)}";
        }
    }
}
