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
        return _httpClient.GetFromJsonAsync<CpuSnapshotModel>("/cpu");
    }
}
