using LCS_HR_MVC.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LCS_HR_MVC.Services
{
    public interface ITaxSetupService
    {
        Task<IEnumerable<TaxHeadModel>> GetAllTaxesAsync();
        Task<TaxHeadModel?> GetTaxByCodeAsync(string code);
        Task<bool> IsTaxYearExistsAsync(int taxYear, string? excludeCode = null);
        Task<bool> SaveTaxAsync(TaxHeadModel model, string currentUserId);
        Task<bool> UpdateTaxAsync(TaxHeadModel model, string currentUserId);
        Task<bool> DeleteTaxAsync(string code);
    }
}
