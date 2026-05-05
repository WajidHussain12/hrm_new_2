using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface IDataSyncService
    {
        Task<DataSyncResult> RunSyncAsync(
            CancellationToken ct = default);
    }
}
