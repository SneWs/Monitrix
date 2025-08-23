using System;
using System.Collections.Generic;

namespace Monitrix.System.Models;

public sealed class CpuUsage
{
    public double CpuSpeedMhz { get; init; }

    public double CurrentSpeedMhz { get; init; }

    public double TotalUsagePercentage { get; init; }

    public IReadOnlyList<double> PerCoreUsagePercentage { get; init; } = Array.Empty<double>();
}