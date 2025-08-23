namespace Monitrix.System.Models;

public sealed class ProcessInfoModel
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public float CpuUsage { get; init; }

    public float MemoryUsage { get; init; }
}