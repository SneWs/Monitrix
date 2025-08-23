using System.Collections.Generic;

namespace Monitrix.System.Models;

public sealed class NetworkInfoModel
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyCollection<string> IpAddress { get; init; } = new List<string>();

    public string MacAddress { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public long SpeedInMBs { get; init; } // in MB/s
}