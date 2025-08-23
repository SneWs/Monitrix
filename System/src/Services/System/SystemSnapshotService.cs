using System.Threading.Tasks;
using Monitrix.System.Models;
using Monitrix.System.Services.System.CPU;
using Monitrix.System.Services.System.GPU;
using Monitrix.System.Services.System.Network;
using Monitrix.System.Services.System.ProcessInfo;
using Monitrix.System.Services.System.RAM;

namespace Monitrix.System.Services.System;

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
