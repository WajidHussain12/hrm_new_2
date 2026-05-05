using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models.Payroll
{
    public class FuelPricesViewModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Fuel type is required.")]
        public string TypeCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "From date is required.")]
        [DataType(DataType.Date)]
        public DateTime? FromDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ToDate { get; set; }

        [Required(ErrorMessage = "Price is required.")]
        [Range(typeof(decimal), "0.01", "999999999", ErrorMessage = "Invalid price.")]
        public decimal? Price { get; set; }

        public string Comments { get; set; } = string.Empty;

        public bool IsEditMode { get; set; }

        public string SearchField { get; set; } = "All";

        public string SearchText { get; set; } = string.Empty;

        public List<SelectListItem> FuelTypes { get; set; } = new();

        public List<SelectListItem> SearchFields { get; set; } = new();

        public List<FuelPriceRowViewModel> Rows { get; set; } = new();
    }

    public class FuelPriceRowViewModel
    {
        public string Code { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public DateTime FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public decimal Price { get; set; }
        public string Comments { get; set; } = string.Empty;
    }
}
