using System;
using System.Collections.Generic;
using ERP.Domain.Enums;

namespace ERP.Application.DTOs
{
    public class DocumentAdjustmentDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string LocalizedName { get; set; } = string.Empty;

        public AdjustmentType AdjustmentType { get; set; }
        public AdjustmentCalculationMethod CalculationMethod { get; set; }

        public decimal? DefaultValue { get; set; }

        public int GLAccountId { get; set; }
        public string? GLAccountCode { get; set; }
        public string? GLAccountName { get; set; }

        public bool IsTaxable { get; set; }
        public bool IsActive { get; set; }

        public bool IsDefault { get; set; }
        public bool IsMandatory { get; set; }
    }

    public class CreateDocumentAdjustmentDto
    {
        public int? Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;

        public AdjustmentType AdjustmentType { get; set; }
        public AdjustmentCalculationMethod CalculationMethod { get; set; }

        public decimal? DefaultValue { get; set; }
        public int GLAccountId { get; set; }

        public bool IsTaxable { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class TransactionTypeAdjustmentConfigDto
    {
        public int TransactionTypeId { get; set; }
        public string TransactionTypeName { get; set; } = string.Empty;
        public List<DocumentAdjustmentSelectionDto> Adjustments { get; set; } = new();
    }

    public class DocumentAdjustmentSelectionDto
    {
        public int DocumentAdjustmentId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public AdjustmentType AdjustmentType { get; set; }
        public AdjustmentCalculationMethod CalculationMethod { get; set; }
        public decimal? DefaultValue { get; set; }
        public int GLAccountId { get; set; }
        public string? GLAccountName { get; set; }
        public bool IsAllowed { get; set; }
        public bool IsDefault { get; set; }
        public bool IsMandatory { get; set; }
    }

    public class DocumentAdjustmentLineDto
    {
        public int Id { get; set; }
        public int DocumentHeaderId { get; set; }
        public int DocumentAdjustmentId { get; set; }
        public string AdjustmentCode { get; set; } = string.Empty;
        public string AdjustmentName { get; set; } = string.Empty;

        public int LineNumber { get; set; }
        public AdjustmentType AdjustmentType { get; set; }
        public AdjustmentCalculationMethod CalculationMethod { get; set; }

        public decimal? Percentage { get; set; }
        public decimal Amount { get; set; }
        public decimal AmountLocal { get; set; }

        public int GLAccountId { get; set; }
        public string? GLAccountCode { get; set; }
        public string? GLAccountName { get; set; }

        public string? Notes { get; set; }
    }

    public class DocumentAdjustmentLineCreateDto
    {
        public int DocumentAdjustmentId { get; set; }
        public AdjustmentType AdjustmentType { get; set; }
        public AdjustmentCalculationMethod CalculationMethod { get; set; }
        public decimal? Percentage { get; set; }
        public decimal Amount { get; set; }
        public int? GLAccountId { get; set; }
        public string? Notes { get; set; }
    }

    public class AdjustmentTransactionTypeLookupDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public TransactionModule Module { get; set; }
        public string ModuleName { get; set; } = string.Empty;
    }

    public class AdjustmentGLAccountLookupDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string LocalizedName { get; set; } = string.Empty;
        public AccountCategory AccountCategory { get; set; }
    }
}
