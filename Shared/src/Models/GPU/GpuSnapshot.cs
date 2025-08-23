using System.Collections.Generic;

namespace Monitrix.System.Models;

public sealed class GpuSnapshotModel
{
    public IReadOnlyList<GpuInfo> GpuInfo { get; init; } = new List<GpuInfo>();

    public IReadOnlyList<GpuUsage> GpuUsage { get; init; } = new List<GpuUsage>();
}