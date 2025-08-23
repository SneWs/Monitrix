using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Monitrix.System.Models;

namespace Monitrix.System.Services.System.CPU;

public interface ICpuMonitoringService
{
    ValueTask<CpuSnapshotModel> ReadCpuSnapshotAsync();

    ValueTask<CpuInfoModel> ReadCpuInfoAsync();

    ValueTask<CpuUsageModel> ReadCpuUsageAsync();
}

public sealed class CpuMonitoringService : ICpuMonitoringService
{
    private readonly ILogger<CpuMonitoringService> _logger;
    private Dictionary<string, long[]>? _previousCpuStats;

    public CpuMonitoringService(ILogger<CpuMonitoringService> logger)
    {
        _logger = logger;
    }

    public async ValueTask<CpuSnapshotModel> ReadCpuSnapshotAsync()
    {
        var cpuInfo = await ReadCpuInfoAsync();
        var cpuUsage = await ReadCpuUsageAsync();

        return new CpuSnapshotModel {
            CpuInfo = cpuInfo,
            CpuUsage = cpuUsage
        };
    }

    public async ValueTask<CpuInfoModel> ReadCpuInfoAsync()
    {
        try
        {
            var cpuInfoPath = "/proc/cpuinfo";
            if (!File.Exists(cpuInfoPath))
            {
                _logger.LogWarning("CPU info file not found at {Path}", cpuInfoPath);
                return new CpuInfoModel();
            }

            var lines = await File.ReadAllLinesAsync(cpuInfoPath);

            string architecture = string.Empty;
            string modelName = string.Empty;
            string vendorId = string.Empty;
            int coreCount = 0;
            int threadCount = 0;
            var physicalIds = new HashSet<int>();
            var coreIds = new HashSet<string>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(':', 2);
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                switch (key)
                {
                    case "model name":
                        if (string.IsNullOrEmpty(modelName))
                            modelName = value;
                        break;

                    case "vendor_id":
                        if (string.IsNullOrEmpty(vendorId))
                            vendorId = value;
                        break;

                    case "cpu family":
                        if (string.IsNullOrEmpty(architecture))
                        {
                            architecture = vendorId switch
                            {
                                "GenuineIntel" => $"Intel Family {value}",
                                "AuthenticAMD" => $"AMD Family {value}",
                                _ => $"Family {value}"
                            };
                        }
                        break;

                    case "processor":
                        if (int.TryParse(value, out var processorId))
                            threadCount = Math.Max(threadCount, processorId + 1);
                        break;

                    case "physical id":
                        if (int.TryParse(value, out var physicalId))
                            physicalIds.Add(physicalId);
                        break;

                    case "core id":
                        if (int.TryParse(value, out var coreId) && physicalIds.Count > 0)
                        {
                            // Create unique core identifier combining physical ID and core ID
                            var lastPhysicalId = physicalIds.Max();
                            coreIds.Add($"{lastPhysicalId}:{coreId}");
                        }
                        break;
                }
            }

            // If we couldn't get core count from core id, try alternative methods
            if (coreIds.Count == 0)
            {
                coreCount = await GetCoreCountFromSysAsync();
                if (coreCount == 0)
                    coreCount = threadCount; // Fallback to thread count
            }
            else
            {
                coreCount = coreIds.Count;
            }

            // If we still don't have thread count, use processor count from /sys
            if (threadCount == 0)
            {
                threadCount = GetProcessorCountFromSysAsync();
            }

            // Determine architecture if not found
            if (string.IsNullOrEmpty(architecture))
            {
                architecture = await GetArchitectureFromUnameAsync();
            }

            bool isHyperThreadingEnabled = threadCount > coreCount;

            return new CpuInfoModel
            {
                Architecture = architecture,
                ModelName = modelName,
                VendorId = vendorId,
                CoreCount = coreCount,
                ThreadCount = threadCount,
                IsHyperThreadingEnabled = isHyperThreadingEnabled
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read CPU information");
            throw;
        }
    }

    public async ValueTask<CpuUsageModel> ReadCpuUsageAsync()
    {
        try
        {
            // Get CPU frequency information
            var cpuSpeedMhz = await GetCpuMaxFrequencyAsync();
            var currentSpeedMhz = await GetCurrentCpuFrequencyAsync();

            // Get CPU usage statistics
            var currentStats = await ReadCpuStatsAsync();

            // Calculate usage percentages
            var (totalUsage, perCoreUsage) = await CalculateCpuUsage(currentStats);

            // Store current stats for next calculation
            _previousCpuStats = currentStats;

            return new CpuUsageModel
            {
                CpuSpeedMhz = cpuSpeedMhz,
                CurrentSpeedMhz = currentSpeedMhz,
                TotalUsagePercentage = totalUsage,
                PerCoreUsagePercentage = perCoreUsage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read CPU usage");
            throw;
        }
    }

    private async Task<double> GetCpuMaxFrequencyAsync()
    {
        try
        {
            var cpuInfoPath = "/proc/cpuinfo";
            if (!File.Exists(cpuInfoPath))
                return 0;

            var lines = await File.ReadAllLinesAsync(cpuInfoPath);
            foreach (var line in lines)
            {
                if (line.StartsWith("cpu MHz"))
                {
                    var parts = line.Split(':');
                    if (parts.Length == 2 && double.TryParse(parts[1].Trim(), out var mhz))
                    {
                        return mhz;
                    }
                }
            }

            // Fallback: try to read from cpufreq
            var scalingMaxFreqPath = "/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq";
            if (File.Exists(scalingMaxFreqPath))
            {
                var freqText = await File.ReadAllTextAsync(scalingMaxFreqPath);
                if (long.TryParse(freqText.Trim(), out var freqKhz))
                {
                    return freqKhz / 1000.0; // Convert from KHz to MHz
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve CPU max frequency");
        }
        return 0;
    }

    private async Task<double> GetCurrentCpuFrequencyAsync()
    {
        try
        {
            var scalingCurFreqPath = "/sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq";
            if (File.Exists(scalingCurFreqPath))
            {
                var freqText = await File.ReadAllTextAsync(scalingCurFreqPath);
                if (long.TryParse(freqText.Trim(), out var freqKhz))
                {
                    return freqKhz / 1000.0; // Convert from KHz to MHz
                }
            }

            // Fallback to max frequency if current is not available
            return await GetCpuMaxFrequencyAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve current CPU frequency");
            return await GetCpuMaxFrequencyAsync();
        }
    }

    private async Task<Dictionary<string, long[]>> ReadCpuStatsAsync()
    {
        var stats = new Dictionary<string, long[]>();
        
        try
        {
            var statLines = await File.ReadAllLinesAsync("/proc/stat");
            
            foreach (var line in statLines)
            {
                if (line.StartsWith("cpu"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 8)
                    {
                        var cpuName = parts[0];
                        var values = new long[7];
                        
                        for (int i = 1; i <= 7; i++)
                        {
                            if (long.TryParse(parts[i], out var value))
                            {
                                values[i - 1] = value;
                            }
                        }
                        
                        stats[cpuName] = values;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read CPU statistics from /proc/stat");
            throw;
        }

        return stats;
    }

    private async ValueTask<(float totalUsage, IReadOnlyList<float> perCoreUsage)> CalculateCpuUsage(Dictionary<string, long[]> currentStats)
    {
        if (_previousCpuStats == null)
        {
            // First run, store current stats and take another reading
            _previousCpuStats = currentStats;
            await Task.Delay(1000); // Wait 1 second for meaningful difference
            
            // Read stats again
            var newCurrentStats = await ReadCpuStatsAsync();
            return CalculateCpuUsageInternal(newCurrentStats, _previousCpuStats);
        }

        return CalculateCpuUsageInternal(currentStats, _previousCpuStats);
    }

    private (float totalUsage, IReadOnlyList<float> perCoreUsage) CalculateCpuUsageInternal(
        Dictionary<string, long[]> current,
        Dictionary<string, long[]> previous)
    {
        var totalUsage = CalculateUsageForCpu("cpu", current, previous);
        var perCoreUsage = new List<float>();

        // Calculate per-core usage
        int coreIndex = 0;
        while (current.ContainsKey($"cpu{coreIndex}"))
        {
            var coreUsage = CalculateUsageForCpu($"cpu{coreIndex}", current, previous);
            perCoreUsage.Add(coreUsage);
            coreIndex++;
        }

        return (totalUsage, perCoreUsage);
    }

    private float CalculateUsageForCpu(string cpuName, Dictionary<string, long[]> current, Dictionary<string, long[]> previous)
    {
        if (!current.ContainsKey(cpuName) || !previous.ContainsKey(cpuName))
            return 0.0f;

        var currentValues = current[cpuName];
        var previousValues = previous[cpuName];

        // CPU stats: user, nice, system, idle, iowait, irq, softirq
        var currentIdle = currentValues[3] + currentValues[4]; // idle + iowait
        var previousIdle = previousValues[3] + previousValues[4];

        var currentTotal = currentValues.Sum();
        var previousTotal = previousValues.Sum();

        var totalDiff = currentTotal - previousTotal;
        var idleDiff = currentIdle - previousIdle;

        if (totalDiff == 0)
            return 0.0f;

        var usage = (float)(totalDiff - idleDiff) / totalDiff * 100.0f;
        return MathF.Max(0.0f, MathF.Min(100.0f, usage));
    }

    private async Task<int> GetCoreCountFromSysAsync()
    {
        try
        {
            var cpuPath = "/sys/devices/system/cpu";
            if (!Directory.Exists(cpuPath))
                return 0;

            // Count physical cores by looking for core_id files
            var coreIds = new HashSet<string>();
            var cpuDirs = Directory.GetDirectories(cpuPath, "cpu[0-9]*");
            
            foreach (var cpuDir in cpuDirs)
            {
                var topologyPath = Path.Combine(cpuDir, "topology");
                var coreIdPath = Path.Combine(topologyPath, "core_id");
                var physicalPackageIdPath = Path.Combine(topologyPath, "physical_package_id");
                
                if (File.Exists(coreIdPath) && File.Exists(physicalPackageIdPath))
                {
                    var coreId = await File.ReadAllTextAsync(coreIdPath);
                    var packageId = await File.ReadAllTextAsync(physicalPackageIdPath);
                    coreIds.Add($"{packageId.Trim()}:{coreId.Trim()}");
                }
            }
            
            return coreIds.Count > 0 ? coreIds.Count : cpuDirs.Length;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get core count from /sys");
            return 0;
        }
    }

    private int GetProcessorCountFromSysAsync()
    {
        try
        {
            var cpuPath = "/sys/devices/system/cpu";
            if (!Directory.Exists(cpuPath))
                return Environment.ProcessorCount;

            var cpuDirs = Directory.GetDirectories(cpuPath, "cpu[0-9]*");
            return cpuDirs.Length;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get processor count from /sys");
            return Environment.ProcessorCount;
        }
    }

    private async Task<string> GetArchitectureFromUnameAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "uname",
                    Arguments = "-m",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var result = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return result.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get architecture from uname");
            return "Unknown";
        }
    }
}