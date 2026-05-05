using System;
using System.Collections.Generic;

namespace LCS_HR_MVC.Models.Report
{
    public class ReportViewModel
    {
        public string ReportType { get; set; } = "PaySlips";
        
        public int Year { get; set; } = DateTime.Now.Year;
        public int Month { get; set; } = DateTime.Now.Month;
        
        public string ZoneCode { get; set; } = string.Empty;
        public string CityCode { get; set; } = string.Empty;
        public string DepartmentId { get; set; } = string.Empty;
        
        public string EmpNo { get; set; } = string.Empty;
        public string EmployeeDescription { get; set; } = string.Empty;

        public DateTime? FromDate { get; set; } = DateTime.Now.Date;
        public DateTime? ToDate { get; set; } = DateTime.Now.Date;

        public string BankName { get; set; } = string.Empty;
        public string ReportMode { get; set; } = string.Empty;

        // Generic result container for rendering table
        public List<Dictionary<string, object>> ReportData { get; set; } = new List<Dictionary<string, object>>();
        public List<string> Columns { get; set; } = new List<string>();
    }
}
