using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface IAttendanceManagementService
    {
        /// <summary>
        /// Returns the fully populated view-model (dropdown lists + paged records + summary stats)
        /// for the Employee Attendance Management page.
        /// </summary>
        Task<AttendanceManagementViewModel> GetAttendancePageAsync(
            AttendanceManagementFilter filter,
            string userId,
            CancellationToken cancellationToken = default);

        /// <summary>Autocomplete search for employees accessible to this user.</summary>
        Task<IEnumerable<dynamic>> SearchEmployeesAsync(
            string term,
            string userId,
            CancellationToken cancellationToken = default);

        /// <summary>Add a new attendance adjustment record.</summary>
        Task<(bool success, string message)> AddAdjustmentAsync(
            AttendanceAdjustmentModel model,
            string userId,
            CancellationToken cancellationToken = default);

        /// <summary>Update an existing attendance adjustment record.</summary>
        Task<(bool success, string message)> UpdateAdjustmentAsync(
            AttendanceAdjustmentModel model,
            string userId,
            CancellationToken cancellationToken = default);

        /// <summary>Delete an attendance adjustment record.</summary>
        Task<(bool success, string message)> DeleteAdjustmentAsync(
            string empNo,
            DateTime date,
            CancellationToken cancellationToken = default);

        /// <summary>Load a single adjustment for the edit form.</summary>
        Task<AttendanceAdjustmentModel?> GetAdjustmentAsync(
            string empNo,
            DateTime date,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Update check-in or check-out time for a given employee + date.
        /// Works for both Biometric (hr_employeeattendance) and Mobile App (hr_mobileappattendence).
        /// </summary>
        Task<(bool success, string message)> UpdateCheckTimeAsync(
            string empNo,
            DateTime date,
            string checkType,   // "I" or "O"
            TimeSpan newTime,
            string source,      // "Biometric" or "Mobile App"
            string userId,
            CancellationToken cancellationToken = default);
    }
}
