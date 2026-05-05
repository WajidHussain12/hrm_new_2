using System.Collections.Generic;

namespace LCS_HR_MVC.Models
{
    public class LeaveRequestApprovalViewModel
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public List<LeaveRequestModel> Requests { get; set; } = new List<LeaveRequestModel>();
        public List<string> SelectedRequestCodes { get; set; } = new List<string>();
    }
}