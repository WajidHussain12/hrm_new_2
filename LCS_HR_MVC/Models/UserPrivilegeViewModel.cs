namespace LCS_HR_MVC.Models
{
    public class UserPrivilegeViewModel
    {
        public string RoleID { get; set; } = string.Empty;
        public List<PrivilegeItem> Privileges { get; set; } = new List<PrivilegeItem>();
    }

    public class PrivilegeItem
    {
        public string PrivilegesID { get; set; } = string.Empty;
        public int ShouldInsert { get; set; }
        public string MenuID { get; set; } = string.Empty;
        public string SubMenuID { get; set; } = string.Empty;
        public string SubmenudetID { get; set; } = string.Empty;
        public string MenuLocation { get; set; } = string.Empty;
        public string FormName { get; set; } = string.Empty;
        public bool CanView { get; set; }
        public bool CanInsert { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanDelete { get; set; }
    }
}
