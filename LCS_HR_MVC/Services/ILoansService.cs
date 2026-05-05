using LCS_HR_MVC.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LCS_HR_MVC.Services
{
    public interface ILoansService
    {
        Task<IEnumerable<LoanRequestModel>> GetAllLoanRequestsAsync(string currentUserId);
        Task<LoanRequestModel?> GetLoanRequestByIdAsync(string id);
        Task<bool> AddLoanRequestAsync(LoanRequestModel model, string currentUserId, string currentUserName);
        Task<bool> UpdateLoanRequestAsync(LoanRequestModel model, string currentUserId, string currentUserName);
        Task<bool> DeleteLoanRequestAsync(string id);
        Task<(int successCount, string message)> BulkUploadLoanRequestsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);
        
        Task<IEnumerable<dynamic>> SearchLoansAsync(string term);

        Task<IEnumerable<LoanRequestModel>> GetPendingLoanRequestsAsync(string currentUserId);
        Task<(int processed, int failed)> ProcessLoanRequestsAsync(List<string> requestCodes, string status, string currentUserId, string currentUserName);

        Task<IEnumerable<LoanDisbursedModel>> GetAllLoanDisbursedAsync(string currentUserId);
        Task<LoanDisbursedModel?> GetLoanDisbursedByIdAsync(string id);
        Task<bool> AddLoanDisbursedAsync(LoanDisbursedModel model, string currentUserId, string currentUserName);
        Task<bool> UpdateLoanDisbursedAsync(LoanDisbursedModel model, string currentUserId);
        Task<bool> DeleteLoanDisbursedAsync(string id);
        Task<(int successCount, string message)> BulkUploadLoanDisbursedAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);
        
        Task<dynamic?> GetApprovedLoanRequestDataAsync(string lrNo);

        Task<IEnumerable<LoanDeductionModel>> GetAllLoanDeductionsAsync(string currentUserId);
        Task<LoanDeductionModel?> GetLoanDeductionByIdAsync(string id);
        Task<bool> AddLoanDeductionAsync(LoanDeductionModel model, string currentUserId);
        Task<bool> UpdateLoanDeductionAsync(LoanDeductionModel model, string currentUserId);
        Task<bool> DeleteLoanDeductionAsync(string id);
        Task<(int successCount, string message)> BulkUploadLoanDeductionsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);

        Task<dynamic?> GetLoanDisbursedDataAsync(string ldNo);
    }
}