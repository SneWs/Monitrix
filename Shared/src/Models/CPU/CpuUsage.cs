using System;
using System.Collections.Generic;

namespace Monitrix.System.Models;

public sealed class CpuUsageModel
{
    public float CpuSpeedMhz { get; init; }

    public float CurrentSpeedMhz { get; init; }

    public float TotalUsagePercentage { get; init; }

    public IReadOnlyList<float> PerCoreUsagePercentage { get; init; } = Array.Empty<float>();
}