using Hangfire.Dashboard;

namespace LCS_HR_MVC.Data
{
    /// <summary>
    /// Allows access to the Hangfire dashboard for any authenticated application user.
    /// Replace or extend the body to restrict to a specific role when needed.
    /// </summary>
    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();
            // Allow if authenticated — can be tightened to specific role if needed
            return httpContext.User.Identity?.IsAuthenticated == true;
        }
    }
}
