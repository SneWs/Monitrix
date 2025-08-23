namespace Monitrix.System.Models;

public sealed class GpuUsage
{
    public string? Model { get; set; }

    public string? Vendor { get; set; }

    public float MemoryUsedInMB { get; set; } // in MB

    public float MemoryFreeInMB { get; set; } // in MB

    public float MemoryUsedPercentage => MemoryUsedInMB / (MemoryUsedInMB + MemoryFreeInMB) * 100;

    public float CoreUsedPercentage { get; set; } // in %
}
