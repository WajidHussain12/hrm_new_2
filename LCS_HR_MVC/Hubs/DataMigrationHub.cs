using Microsoft.AspNetCore.SignalR;

namespace LCS_HR_MVC.Hubs
{
    public class DataMigrationHub : Hub
    {
        public async Task JoinMigration(string groupKey) =>
            await Groups.AddToGroupAsync(Context.ConnectionId, groupKey);

        public async Task LeaveMigration(string groupKey) =>
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupKey);
    }
}
