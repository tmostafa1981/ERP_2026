using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ERP.Web.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "RequiredField")]
        public int FiscalYearId { get; set; }

        [Required(ErrorMessage = "RequiredField")]
        public string EmployeeCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "RequiredField")]
        public int CompanyId { get; set; }

        [Required(ErrorMessage = "RequiredField")]
        public int BranchId { get; set; }

        [Required(ErrorMessage = "RequiredField")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public IEnumerable<SelectListItem> FiscalYears { get; set; } = new List<SelectListItem>();
    }
}

