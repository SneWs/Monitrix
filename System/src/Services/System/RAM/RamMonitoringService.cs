using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Monitrix.System.Models;

namespace Monitrix.System.Services.System.RAM;

public interface IRamMonitoringService
{
    ValueTask<RamUsageModel> ReadRamUsageAsync();
}

public sealed class RamMonitoringService : IRamMonitoringService
{
    private readonly ILogger<RamMonitoringService> _logger;

    public RamMonitoringService(ILogger<RamMonitoringService> logger)
    {
        _logger = logger;
    }

    public async ValueTask<RamUsageModel> ReadRamUsageAsync()
    {
        try
        {
            var memInfoPath = "/proc/meminfo";
            if (!File.Exists(memInfoPath))
            {
                _logger.LogWarning("Memory info file not found at {Path}", memInfoPath);
                return new RamUsageModel();
            }

            var lines = await File.ReadAllLinesAsync(memInfoPath);
            
            ulong totalKb = 0;
            ulong freeKb = 0;
            ulong availableKb = 0;
            ulong buffersKb = 0;
            ulong cachedKb = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var valueStr = parts[1].Trim().Replace(" kB", "").Trim();

                if (!ulong.TryParse(valueStr, out var value))
                    continue;

                switch (key)
                {
                    case "MemTotal":
                        totalKb = value;
                        break;
                    case "MemFree":
                        freeKb = value;
                        break;
                    case "MemAvailable":
                        availableKb = value;
                        break;
                    case "Buffers":
                        buffersKb = value;
                        break;
                    case "Cached":
                        cachedKb = value;
                        break;
                }
            }

            // Convert from KB to bytes
            var totalBytes = totalKb * 1024;
            
            // Use MemAvailable if available (more accurate), otherwise calculate
            var freeBytes = availableKb > 0 
                ? availableKb * 1024
                : (freeKb + buffersKb + cachedKb) * 1024;
            
            var usedBytes = totalBytes - freeBytes;
            return new RamUsageModel {
                TotalBytes = totalBytes,
                UsedBytes = usedBytes,
                FreeBytes = freeBytes,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read RAM usage information");
            throw;
        }
    }
}