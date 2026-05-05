namespace LCS_HR_MVC.Models
{
    public class UserModel
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string UserRole { get; set; } = string.Empty;
        public string RoleDescription { get; set; } = string.Empty;
        public string LocCode { get; set; } = string.Empty;
        public List<string> UserCities { get; set; } = new List<string>();
        public DateTime WorkingDate { get; set; }
    }
}
