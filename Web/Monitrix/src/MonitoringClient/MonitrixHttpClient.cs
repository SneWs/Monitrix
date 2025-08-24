using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Monitrix.System.Models;

namespace Monitrix.MonitoringClient;

public sealed class MonitrixHttpClient
{
    private readonly HttpClient _httpClient;

    public MonitrixHttpClient(HttpClient client)
    {
        _httpClient = client;
    }

    public Task<CpuSnapshotModel?> GetCpuSnapshotAsync()
    {
        try
        {
            return _httpClient.GetFromJsonAsync<CpuSnapshotModel>("/cpu");
        }
        catch { }

        return Task.FromResult<CpuSnapshotModel?>(new());
    }

    public Task<RamUsageModel?> GetRamUsageAsync()
    {
        try
        {
            return _httpClient.GetFromJsonAsync<RamUsageModel>("/ram");
        }
        catch { }

        return Task.FromResult<RamUsageModel?>(new());
    }

    public Task<GpuSnapshotModel?> GetGpuUsageAsync()
    {
        try
        {
            return _httpClient.GetFromJsonAsync<GpuSnapshotModel>("/gpu");
        }
        catch { }

        return Task.FromResult<GpuSnapshotModel?>(new());
    }
}
