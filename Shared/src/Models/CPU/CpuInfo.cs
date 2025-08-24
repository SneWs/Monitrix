namespace Monitrix.System.Models;

public sealed class CpuInfoModel
{
    public string Architecture { get; init; } = string.Empty;
    
    public string ModelName { get; init; } = string.Empty;

    public string VendorId { get; init; } = string.Empty;

    public int CoreCount { get; init; }

    public int ThreadCount { get; init; }

    public bool IsHyperThreadingEnabled { get; init; }
    
    public float CpuSpeedMhz { get; init; }
}
