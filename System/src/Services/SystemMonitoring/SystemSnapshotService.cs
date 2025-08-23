using System.Threading.Tasks;
using Monitrix.System.Models;
using Monitrix.SystemMonitoring.Services.SystemMonitoring.CPU;
using Monitrix.SystemMonitoring.Services.SystemMonitoring.GPU;
using Monitrix.SystemMonitoring.Services.SystemMonitoring.Network;
using Monitrix.SystemMonitoring.Services.SystemMonitoring.ProcessInfo;
using Monitrix.SystemMonitoring.Services.SystemMonitoring.RAM;

namespace Monitrix.SystemMonitoring.Services.SystemMonitoring;

public interface ISystemSnapshotService
{
    ValueTask<SystemSnapshotModel> TakeSnapshotAsync();
}

public class SystemSnapshotService : ISystemSnapshotService
{
    private readonly ICpuMonitoringService _cpuMonitoringService;
    private readonly IRamMonitoringService _ramMonitoringService;
    private readonly IProcessMonitoringService _processMonitoringService;
    private readonly IGpuMonitoringService _gpuMonitoringService;
    private readonly INetworkMonitoringService _networkMonitoringService;

    public SystemSnapshotService(
        ICpuMonitoringService cpuMonitoringService,
        IRamMonitoringService ramMonitoringService,
        IProcessMonitoringService processMonitoringService,
        IGpuMonitoringService gpuMonitoringService,
        INetworkMonitoringService networkMonitoringService)
    {
        _cpuMonitoringService = cpuMonitoringService;
        _ramMonitoringService = ramMonitoringService;
        _processMonitoringService = processMonitoringService;
        _gpuMonitoringService = gpuMonitoringService;
        _networkMonitoringService = networkMonitoringService;
    }

    public async ValueTask<SystemSnapshotModel> TakeSnapshotAsync()
    {
        var cpuSnapshot = await _cpuMonitoringService.ReadCpuSnapshotAsync();
        var ramUsage = await _ramMonitoringService.ReadRamUsageAsync();
        var processes = await _processMonitoringService.ListProcessesAsync();
        var gpuSnapshot = await _gpuMonitoringService.GetGpuSnapshotAsync();
        var networkInterfaces = await _networkMonitoringService.ListNetworkInterfacesAsync();

        return new SystemSnapshotModel {
            Cpu = cpuSnapshot,
            Gpu = gpuSnapshot,
            Ram = ramUsage,
            Processes = processes,
            NetworkInterfaces = networkInterfaces
        };
    }
}
