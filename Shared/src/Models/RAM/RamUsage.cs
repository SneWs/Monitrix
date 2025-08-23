namespace Monitrix.System.Models;

public sealed class RamUsageModel
{
    public ulong TotalBytes { get; init; }

    public ulong UsedBytes { get; init; }

    public ulong FreeBytes { get; init; }

    public float UsedPercentage => TotalBytes > 0 ? (float)UsedBytes / TotalBytes * 100f : 0f;

    public float FreePercentage => TotalBytes > 0 ? (float)FreeBytes / TotalBytes * 100f : 0f;
}