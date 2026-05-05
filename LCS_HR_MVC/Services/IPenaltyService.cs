using System.Collections.Generic;
using System.Threading.Tasks;
using LCS_HR_MVC.Models.Penalty;

namespace LCS_HR_MVC.Services
{
    public interface IPenaltyService
    {
        Task<IEnumerable<PenaltyFineModel>> GetAllPenaltyFinesAsync(string currentUserId);
        Task<PenaltyFineModel?> GetPenaltyFineByIdAsync(string id);
        Task<bool> AddPenaltyFineAsync(PenaltyFineModel model, string currentUserId);
        Task<bool> UpdatePenaltyFineAsync(PenaltyFineModel model, string currentUserId);
        Task<bool> DeletePenaltyFineAsync(string id);

        Task<(int successCount, string message)> BulkUploadPenaltyFineAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);
        
        Task<IEnumerable<BulkPenaltyDeleteModel>> GetBulkPenaltyBatchesAsync();
        Task<bool> DeleteBulkPenaltyBatchAsync(string createdBy, string createdDate);

        Task<IEnumerable<dynamic>> GetPenaltyTypesAsync();
        
        Task<IEnumerable<dynamic>> GetDivisionsAsync();
        Task<IEnumerable<dynamic>> GetDepartmentsByDivisionAsync(int divisionId);
        Task<IEnumerable<dynamic>> GetSubDepartmentsByDepartmentAsync(int departmentId);
    }
}