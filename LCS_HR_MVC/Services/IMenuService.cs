namespace LCS_HR_MVC.Services
{
    public interface IMenuService
    {
        Task<string> GetMenuHtmlAsync(string userRole);
    }
}
