using System;
using System.Collections.Generic;

namespace Monitrix.System.Models;

public sealed class CpuUsageModel
{
    public double CpuSpeedMhz { get; init; }

    public double CurrentSpeedMhz { get; init; }

    public double TotalUsagePercentage { get; init; }

    public IReadOnlyList<float> PerCoreUsagePercentage { get; init; } = Array.Empty<float>();
}