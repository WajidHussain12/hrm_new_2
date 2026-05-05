namespace LCS_HR_MVC.Models.ViewModels
{
    public class DashboardViewModel
    {
        public bool ShowDashboard { get; set; }
        public string ExtrasChartData { get; set; } = "[]";
        public string DeductionChartData { get; set; } = "[]";
        public string FiscalRange { get; set; } = string.Empty;
    }
}
