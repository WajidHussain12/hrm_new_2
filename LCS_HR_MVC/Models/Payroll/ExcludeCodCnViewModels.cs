using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models.Payroll
{
    public class ExcludeCodCnViewModel
    {
        [Display(Name = "CSV File")]
        public IFormFile? UploadFile { get; set; }

        public int InsertedCount { get; set; }

        public int SkippedCount { get; set; }

        public List<ExcludeCodCnRowViewModel> DuplicateRows { get; set; } = new();
    }

    public class ExcludeCodCnRowViewModel
    {
        public int RowNumber { get; set; }
        public string CnNumber { get; set; } = string.Empty;
        public string CourierId { get; set; } = string.Empty;
        public string ArrivalDestination { get; set; } = string.Empty;
    }
}
