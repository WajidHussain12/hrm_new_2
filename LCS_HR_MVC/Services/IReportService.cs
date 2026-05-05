using System.Collections.Generic;
using System.Threading.Tasks;
using LCS_HR_MVC.Models.Report;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Services
{
    public interface IReportService
    {
        Task<List<Dictionary<string, object>>> GetReportDataAsync(ReportViewModel model, string currentUserId);
        byte[] GenerateExcelReport(List<Dictionary<string, object>> data, string reportName);
        Task<List<SelectListItem>> GetHrDepartmentsAsync();
    }
}