using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface IRouteSetupService
    {
        Task<IEnumerable<RouteModel>> GetAllRoutesAsync(string currentUserId);
        Task<RouteModel?> GetRouteByCodeAndCityAsync(string routeCode, string cityCode);
        Task<bool> IsRouteCodeExistsAsync(string routeCode, string cityCode);
        Task<bool> IsRouteDescriptionExistsAsync(string description, string cityCode, string? excludeRouteCode = null);
        Task<bool> SaveRouteAsync(RouteModel model, string currentUserId);
        Task<bool> UpdateRouteAsync(RouteModel model, string oldCityCode, string currentUserId);
        Task<bool> DeleteRouteAsync(string routeCode, string cityCode);
    }
}
