namespace Monitrix.System.Models;

public sealed class GpuInfo
{
    public string? Model { get; set; }

    public string? Vendor { get; set; }

    public int MemorySizeInMB { get; set; } // in MB

    public int CoreCount { get; set; }
}
