namespace Monitrix.System.Models;

public sealed class CpuSnapshotModel
{
    public CpuInfoModel CpuInfo { get; init; } = new CpuInfoModel();

    public CpuUsageModel CpuUsage { get; init; } = new CpuUsageModel();
}
