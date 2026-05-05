namespace LCS_HR_MVC.Models
{
    public class SetupLocationModel
    {
        public int LocationId { get; set; }
        public string LocationName { get; set; } = string.Empty;
        public string CityName { get; set; } = string.Empty;
        public string ZoneName { get; set; } = string.Empty;
        public string CountryName { get; set; } = string.Empty;
    }

    public class AssignMultipleLocationsViewModel
    {
        public string EmpNo { get; set; } = string.Empty;
        public string EmpName { get; set; } = string.Empty;
        public List<SetupLocationModel> AllLocations { get; set; } = new();
        public List<int> AssignedLocationIds { get; set; } = new();
    }
}
