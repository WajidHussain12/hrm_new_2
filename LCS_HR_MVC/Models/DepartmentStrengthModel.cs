using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class DepartmentStrengthModel
    {
        public string CityID { get; set; } = string.Empty;
        public string PDID { get; set; } = string.Empty;
        public string PDeptName { get; set; } = string.Empty;
        public string SDID { get; set; } = string.Empty;
        public string SubDeptName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Strength is required")]
        [Range(0, 10000, ErrorMessage = "Invalid strength value")]
        public int Strength { get; set; }
    }

    public class DepartmentStrengthViewModel
    {
        [Required(ErrorMessage = "City is required")]
        public string CityCode { get; set; } = "00";
        public List<DepartmentStrengthModel> Strengths { get; set; } = new List<DepartmentStrengthModel>();
    }
}