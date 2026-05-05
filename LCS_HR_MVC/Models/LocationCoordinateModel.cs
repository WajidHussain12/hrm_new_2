namespace LCS_HR_MVC.Models
{
    public class LocationCoordinateModel
    {
        public string ZoneName { get; set; } = string.Empty;
        public string ZoneCode { get; set; } = string.Empty;
        public string CityCode { get; set; } = string.Empty;
        public string CityName { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public string LandLine { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string Latitude { get; set; } = string.Empty;
        public string Longitude { get; set; } = string.Empty;
        public string LocationCode { get; set; } = string.Empty;
        public int LocationId { get; set; }
    }

    public class LocationCoordinateViewModel
    {
        public List<LocationCoordinateModel> Locations { get; set; } = new();
        public int SelectedLocationId { get; set; }
        public string SelectedCityCode { get; set; } = string.Empty;
        public string Latitude { get; set; } = string.Empty;
        public string Longitude { get; set; } = string.Empty;
    }
}
