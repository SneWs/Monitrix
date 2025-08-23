using System.Collections.Generic;

namespace Monitrix.System.Models;

public sealed class SystemSnapshotModel
{
    public CpuSnapshotModel Cpu { get; init; }

    public GpuSnapshotModel Gpu { get; init; }

    public RamUsageModel Ram { get; init; }

    public IReadOnlyCollection<ProcessInfoModel> Processes { get; init; }

    public IReadOnlyCollection<NetworkInfoModel> NetworkInterfaces { get; init; }
}