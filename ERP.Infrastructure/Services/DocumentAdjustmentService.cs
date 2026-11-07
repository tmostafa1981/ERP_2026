using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Data;

namespace ERP.Infrastructure.Services
{
    public class DocumentAdjustmentService : IDocumentAdjustmentService
    {
        private readonly ErpDbContext _context;

        public DocumentAdjustmentService(ErpDbContext context)
        {
            _context = context;
        }

        public async Task<List<DocumentAdjustmentDto>> GetAllAdjustmentsAsync(bool activeOnly = false)
        {
            var query = _context.DocumentAdjustments
                .AsNoTracking()
                .Include(a => a.GLAccount)
                .AsQueryable();

            if (activeOnly)
            {
                query = query.Where(a => a.IsActive);
            }

            var list = await query.OrderBy(a => a.AdjustmentType).ThenBy(a => a.Code).ToListAsync();

            return list.Select(a => new DocumentAdjustmentDto
            {
                Id = a.Id,
                Code = a.Code,
                NameAr = a.NameAr,
                NameEn = a.NameEn,
                LocalizedName = a.LocalizedName,
                AdjustmentType = a.AdjustmentType,
                CalculationMethod = a.CalculationMethod,
                DefaultValue = a.DefaultValue,
                GLAccountId = a.GLAccountId,
                GLAccountCode = a.GLAccount?.Code,
                GLAccountName = a.GLAccount?.LocalizedName,
                IsTaxable = a.IsTaxable,
                IsActive = a.IsActive
            }).ToList();
        }

        public async Task<DocumentAdjustmentDto?> GetAdjustmentByIdAsync(int id)
        {
            var a = await _context.DocumentAdjustments
                .AsNoTracking()
                .Include(x => x.GLAccount)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (a == null) return null;

            return new DocumentAdjustmentDto
            {
                Id = a.Id,
                Code = a.Code,
                NameAr = a.NameAr,
                NameEn = a.NameEn,
                LocalizedName = a.LocalizedName,
                AdjustmentType = a.AdjustmentType,
                CalculationMethod = a.CalculationMethod,
                DefaultValue = a.DefaultValue,
                GLAccountId = a.GLAccountId,
                GLAccountCode = a.GLAccount?.Code,
                GLAccountName = a.GLAccount?.LocalizedName,
                IsTaxable = a.IsTaxable,
                IsActive = a.IsActive
            };
        }

        public async Task<DocumentAdjustmentDto> CreateAdjustmentAsync(CreateDocumentAdjustmentDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.NameAr))
                throw new ArgumentException("الكود والاسم العربي مطلوبان.");

            var exists = await _context.DocumentAdjustments.AnyAsync(a => a.Code == dto.Code.Trim().ToUpper());
            if (exists)
                throw new InvalidOperationException($"كود البند '{dto.Code}' موجود بالفعل.");

            var entity = new DocumentAdjustment
            {
                Code = dto.Code.Trim().ToUpper(),
                NameAr = dto.NameAr.Trim(),
                NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? dto.NameAr.Trim() : dto.NameEn.Trim(),
                AdjustmentType = dto.AdjustmentType,
                CalculationMethod = dto.CalculationMethod,
                DefaultValue = dto.DefaultValue,
                GLAccountId = dto.GLAccountId,
                IsTaxable = dto.IsTaxable,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.DocumentAdjustments.Add(entity);
            await _context.SaveChangesAsync();

            return (await GetAdjustmentByIdAsync(entity.Id))!;
        }

        public async Task<DocumentAdjustmentDto> UpdateAdjustmentAsync(CreateDocumentAdjustmentDto dto)
        {
            if (!dto.Id.HasValue)
                throw new ArgumentException("معرف البند مطلوب للتعديل.");

            var entity = await _context.DocumentAdjustments.FindAsync(dto.Id.Value);
            if (entity == null)
                throw new KeyNotFoundException("بند المصاريف / الخصم غير موجود.");

            entity.NameAr = dto.NameAr.Trim();
            entity.NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? dto.NameAr.Trim() : dto.NameEn.Trim();
            entity.AdjustmentType = dto.AdjustmentType;
            entity.CalculationMethod = dto.CalculationMethod;
            entity.DefaultValue = dto.DefaultValue;
            entity.GLAccountId = dto.GLAccountId;
            entity.IsTaxable = dto.IsTaxable;
            entity.IsActive = dto.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return (await GetAdjustmentByIdAsync(entity.Id))!;
        }

        public async Task<bool> DeleteAdjustmentAsync(int id)
        {
            var entity = await _context.DocumentAdjustments.FindAsync(id);
            if (entity == null) return false;

            // Check if used in document lines
            var inUse = await _context.DocumentAdjustmentLines.AnyAsync(l => l.DocumentAdjustmentId == id);
            if (inUse)
            {
                // Soft delete
                entity.IsActive = false;
                entity.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return true;
            }

            _context.DocumentAdjustments.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<DocumentAdjustmentDto>> GetAllowedAdjustmentsByTransactionTypeAsync(int transactionTypeId)
        {
            var allowed = await _context.TransactionTypeAdjustments
                .AsNoTracking()
                .Include(ta => ta.DocumentAdjustment)
                    .ThenInclude(da => da!.GLAccount)
                .Where(ta => ta.TransactionTypeId == transactionTypeId && ta.IsActive && ta.DocumentAdjustment!.IsActive)
                .OrderBy(ta => ta.DocumentAdjustment!.AdjustmentType)
                .ThenBy(ta => ta.DocumentAdjustment!.Code)
                .ToListAsync();

            return allowed.Select(ta => new DocumentAdjustmentDto
            {
                Id = ta.DocumentAdjustment!.Id,
                Code = ta.DocumentAdjustment.Code,
                NameAr = ta.DocumentAdjustment.NameAr,
                NameEn = ta.DocumentAdjustment.NameEn,
                LocalizedName = ta.DocumentAdjustment.LocalizedName,
                AdjustmentType = ta.DocumentAdjustment.AdjustmentType,
                CalculationMethod = ta.DocumentAdjustment.CalculationMethod,
                DefaultValue = ta.DocumentAdjustment.DefaultValue,
                GLAccountId = ta.DocumentAdjustment.GLAccountId,
                GLAccountCode = ta.DocumentAdjustment.GLAccount?.Code,
                GLAccountName = ta.DocumentAdjustment.GLAccount?.LocalizedName,
                IsTaxable = ta.DocumentAdjustment.IsTaxable,
                IsActive = ta.DocumentAdjustment.IsActive,
                IsDefault = ta.IsDefault,
                IsMandatory = ta.IsMandatory
            }).ToList();
        }

        public async Task<TransactionTypeAdjustmentConfigDto> GetTransactionTypeAdjustmentConfigAsync(int transactionTypeId)
        {
            var tt = await _context.TransactionTypes.FindAsync(transactionTypeId);
            if (tt == null) throw new KeyNotFoundException("نوع الحركة غير موجود.");

            var allAdjustments = await _context.DocumentAdjustments
                .AsNoTracking()
                .Include(a => a.GLAccount)
                .Where(a => a.IsActive)
                .OrderBy(a => a.AdjustmentType)
                .ThenBy(a => a.Code)
                .ToListAsync();

            var currentLinks = await _context.TransactionTypeAdjustments
                .AsNoTracking()
                .Where(ta => ta.TransactionTypeId == transactionTypeId)
                .ToDictionaryAsync(ta => ta.DocumentAdjustmentId);

            var result = new TransactionTypeAdjustmentConfigDto
            {
                TransactionTypeId = tt.Id,
                TransactionTypeName = tt.LocalizedName,
                Adjustments = allAdjustments.Select(a =>
                {
                    bool exists = currentLinks.TryGetValue(a.Id, out var link);
                    return new DocumentAdjustmentSelectionDto
                    {
                        DocumentAdjustmentId = a.Id,
                        Code = a.Code,
                        Name = a.LocalizedName,
                        AdjustmentType = a.AdjustmentType,
                        CalculationMethod = a.CalculationMethod,
                        DefaultValue = a.DefaultValue,
                        GLAccountId = a.GLAccountId,
                        GLAccountName = a.GLAccount != null ? $"{a.GLAccount.Code} - {a.GLAccount.LocalizedName}" : null,
                        IsAllowed = exists && link!.IsActive,
                        IsDefault = exists && link!.IsDefault,
                        IsMandatory = exists && link!.IsMandatory
                    };
                }).ToList()
            };

            return result;
        }

        public async Task SaveTransactionTypeAdjustmentConfigAsync(TransactionTypeAdjustmentConfigDto config)
        {
            var existing = await _context.TransactionTypeAdjustments
                .Where(ta => ta.TransactionTypeId == config.TransactionTypeId)
                .ToListAsync();

            _context.TransactionTypeAdjustments.RemoveRange(existing);

            foreach (var item in config.Adjustments.Where(x => x.IsAllowed))
            {
                _context.TransactionTypeAdjustments.Add(new TransactionTypeAdjustment
                {
                    TransactionTypeId = config.TransactionTypeId,
                    DocumentAdjustmentId = item.DocumentAdjustmentId,
                    IsDefault = item.IsDefault,
                    IsMandatory = item.IsMandatory,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }
    }
}
