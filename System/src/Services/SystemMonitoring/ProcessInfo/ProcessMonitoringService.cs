using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Monitrix.System.Models;

namespace Monitrix.SystemMonitoring.Services.SystemMonitoring.ProcessInfo;

public interface IProcessMonitoringService
{
    ValueTask<IReadOnlyCollection<ProcessInfoModel>> ListProcessesAsync();
}

public sealed class ProcessMonitoringService : IProcessMonitoringService
{
    private readonly ILogger<ProcessMonitoringService> _logger;
    private Dictionary<int, (ulong userTime, ulong systemTime, DateTime timestamp)>? _previousCpuStats;

    public ProcessMonitoringService(ILogger<ProcessMonitoringService> logger)
    {
        _logger = logger;
    }

    public async ValueTask<IReadOnlyCollection<ProcessInfoModel>> ListProcessesAsync()
    {
        try
        {
            var processes = new List<ProcessInfoModel>();
            var procPath = "/proc";
            
            if (!Directory.Exists(procPath))
            {
                _logger.LogWarning("Proc filesystem not found at {Path}", procPath);
                return processes.AsReadOnly();
            }

            // Get all process directories (numeric folder names)
            var processDirs = Directory.GetDirectories(procPath)
                .Where(dir => int.TryParse(Path.GetFileName(dir), out _))
                .ToList();

            var currentCpuStats = new Dictionary<int, (ulong userTime, ulong systemTime, DateTime timestamp)>();
            var totalSystemMemoryKb = await GetTotalSystemMemoryAsync();

            foreach (var processDir in processDirs)
            {
                try
                {
                    var pidStr = Path.GetFileName(processDir);
                    if (!int.TryParse(pidStr, out var pid))
                        continue;

                    var processInfo = await ReadProcessInfoAsync(pid, totalSystemMemoryKb, currentCpuStats);
                    if (processInfo != null)
                    {
                        processes.Add(processInfo);
                    }
                }
                catch (Exception ex)
                {
                    // Process might have disappeared, or we don't have permission
                    _logger.LogDebug(ex, "Failed to read process info for directory {Dir}", processDir);
                }
            }

            // Store current stats for next CPU calculation
            _previousCpuStats = currentCpuStats;

            return processes.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list processes");
            throw;
        }
    }

    private async Task<ProcessInfoModel?> ReadProcessInfoAsync(int pid, ulong totalSystemMemoryKb, Dictionary<int, (ulong, ulong, DateTime)> currentCpuStats)
    {
        try
        {
            var statPath = $"/proc/{pid}/stat";
            var statusPath = $"/proc/{pid}/status";
            var cmdlinePath = $"/proc/{pid}/cmdline";

            if (!File.Exists(statPath))
                return null;

            // Read process name and basic info
            var name = await ReadProcessNameAsync(cmdlinePath, statusPath, pid);
            
            // Read memory usage
            var memoryUsageMb = await ReadProcessMemoryUsageAsync(statusPath);
            
            // Read CPU usage
            var cpuUsage = await ReadProcessCpuUsageAsync(statPath, pid, currentCpuStats);

            return new ProcessInfoModel
            {
                Id = pid,
                Name = name,
                CpuUsage = cpuUsage,
                MemoryUsage = memoryUsageMb
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read process info for PID {Pid}", pid);
            return null;
        }
    }

    private async Task<string> ReadProcessNameAsync(string cmdlinePath, string statusPath, int pid)
    {
        try
        {
            // Try to get command line first (more descriptive)
            if (File.Exists(cmdlinePath))
            {
                var cmdline = await File.ReadAllTextAsync(cmdlinePath);
                if (!string.IsNullOrEmpty(cmdline))
                {
                    // Command line arguments are null-separated
                    var args = cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                    if (args.Length > 0)
                    {
                        var command = Path.GetFileName(args[0]);
                        return !string.IsNullOrEmpty(command) ? command : $"Process {pid}";
                    }
                }
            }

            // Fallback to status file
            if (File.Exists(statusPath))
            {
                var statusLines = await File.ReadAllLinesAsync(statusPath);
                var nameLine = statusLines.FirstOrDefault(l => l.StartsWith("Name:"));
                if (nameLine != null)
                {
                    var parts = nameLine.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                        return parts[1].Trim();
                }
            }

            return $"Process {pid}";
        }
        catch
        {
            return $"Process {pid}";
        }
    }

    private async Task<float> ReadProcessMemoryUsageAsync(string statusPath)
    {
        try
        {
            if (!File.Exists(statusPath))
                return 0f;

            var statusLines = await File.ReadAllLinesAsync(statusPath);
            
            // Look for VmRSS (Resident Set Size - actual memory usage)
            var vmRssLine = statusLines.FirstOrDefault(l => l.StartsWith("VmRSS:"));
            if (vmRssLine != null)
            {
                var parts = vmRssLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && ulong.TryParse(parts[1], out var memoryKb))
                {
                    return memoryKb / 1024f; // Convert KB to MB
                }
            }

            return 0f;
        }
        catch
        {
            return 0f;
        }
    }

    private async Task<float> ReadProcessCpuUsageAsync(string statPath, int pid, Dictionary<int, (ulong, ulong, DateTime)> currentCpuStats)
    {
        try
        {
            if (!File.Exists(statPath))
                return 0f;

            var statContent = await File.ReadAllTextAsync(statPath);
            var parts = statContent.Split(' ');
            
            if (parts.Length < 17)
                return 0f;

            // Parse CPU times (in clock ticks)
            var userTime = ulong.Parse(parts[13]);   // utime
            var systemTime = ulong.Parse(parts[14]); // stime
            var currentTime = DateTime.UtcNow;

            currentCpuStats[pid] = (userTime, systemTime, currentTime);

            // Calculate CPU usage if we have previous data
            if (_previousCpuStats?.ContainsKey(pid) == true)
            {
                var (prevUserTime, prevSystemTime, prevTimestamp) = _previousCpuStats[pid];
                
                var userTimeDiff = userTime - prevUserTime;
                var systemTimeDiff = systemTime - prevSystemTime;
                var totalCpuTimeDiff = userTimeDiff + systemTimeDiff;
                
                var timeDiff = (currentTime - prevTimestamp).TotalSeconds;
                
                if (timeDiff > 0)
                {
                    // Convert clock ticks to seconds (assuming 100 ticks per second - sysconf(_SC_CLK_TCK))
                    var cpuTimeSeconds = totalCpuTimeDiff / 100.0;
                    var cpuUsage = (cpuTimeSeconds / timeDiff) * 100.0;
                    
                    return (float)Math.Min(100.0, Math.Max(0.0, cpuUsage));
                }
            }

            return 0f;
        }
        catch
        {
            return 0f;
        }
    }

    private async Task<ulong> GetTotalSystemMemoryAsync()
    {
        try
        {
            var memInfoPath = "/proc/meminfo";
            if (!File.Exists(memInfoPath))
                return 0;

            var lines = await File.ReadAllLinesAsync(memInfoPath);
            var totalLine = lines.FirstOrDefault(l => l.StartsWith("MemTotal:"));
            
            if (totalLine != null)
            {
                var parts = totalLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && ulong.TryParse(parts[1], out var totalKb))
                {
                    return totalKb;
                }
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }
}
