using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class UserLocationViewModel
    {
        [Required(ErrorMessage = "User is required")]
        public string UserID { get; set; } = string.Empty;
        public string UserDescription { get; set; } = string.Empty;
        
        public List<LocationItem> Locations { get; set; } = new List<LocationItem>();
        
        public string Action { get; set; } = string.Empty;
    }

    public class LocationItem
    {
        public string Code { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }
}
