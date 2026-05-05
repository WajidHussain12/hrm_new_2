using Microsoft.AspNetCore.SignalR;

namespace LCS_HR_MVC.Hubs
{
    public class CommissionProgressHub : Hub<ICommissionProgressClient>
    {
        public async Task JoinRun(string jobRunId) =>
            await Groups.AddToGroupAsync(Context.ConnectionId, jobRunId);

        public async Task LeaveRun(string jobRunId) =>
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, jobRunId);
    }
}
