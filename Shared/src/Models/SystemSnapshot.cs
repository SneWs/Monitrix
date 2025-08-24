using System.Collections.Generic;

namespace Monitrix.System.Models;

public sealed class SystemSnapshotModel
{
    public CpuSnapshotModel Cpu { get; init; } = new();

    public GpuSnapshotModel Gpu { get; init; } = new();

    public RamUsageModel Ram { get; init; } = new();

    public IReadOnlyCollection<ProcessInfoModel> Processes { get; init; } = new List<ProcessInfoModel>();

    public IReadOnlyCollection<NetworkInfoModel> NetworkInterfaces { get; init; } = new List<NetworkInfoModel>();
}