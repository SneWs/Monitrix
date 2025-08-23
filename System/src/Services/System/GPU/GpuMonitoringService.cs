using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Monitrix.System.Models;

namespace Monitrix.System.Services.System.GPU;

public interface IGpuMonitoringService
{
    ValueTask<GpuSnapshotModel> GetGpuSnapshotAsync();

    ValueTask<IReadOnlyList<GpuInfo>> ListGpusAsync();

    ValueTask<IReadOnlyList<GpuUsage>> GetGpuUsageAsync();
}

public sealed class GpuMonitoringService : IGpuMonitoringService
{
    private readonly ILogger<GpuMonitoringService> _logger;

    public GpuMonitoringService(ILogger<GpuMonitoringService> logger)
    {
        _logger = logger;
    }

    public async ValueTask<GpuSnapshotModel> GetGpuSnapshotAsync()
    {
        var gpuInfo = await ListGpusAsync();
        var gpuUsage = await GetGpuUsageAsync();

        return new GpuSnapshotModel {
            GpuInfo = gpuInfo,
            GpuUsage = gpuUsage
        };
    }

    public async ValueTask<IReadOnlyList<GpuInfo>> ListGpusAsync()
    {
        var gpus = new List<GpuInfo>();

        try
        {
            // Try multiple methods to detect GPUs
            await TryAddNvidiaGpusAsync(gpus);
            await TryAddAmdGpusAsync(gpus);
            await TryAddIntelGpusAsync(gpus);
            await TryAddGenericPciGpusAsync(gpus);

            _logger.LogInformation("Found {Count} GPU(s)", gpus.Count);
            return gpus.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list GPUs");
            return gpus.AsReadOnly();
        }
    }

    public async ValueTask<IReadOnlyList<GpuUsage>> GetGpuUsageAsync()
    {
        var gpuUsages = new List<GpuUsage>();

        try
        {
            // Try to get usage from different GPU vendors
            await TryAddNvidiaUsageAsync(gpuUsages);
            await TryAddAmdUsageAsync(gpuUsages);
            await TryAddIntelUsageAsync(gpuUsages);

            _logger.LogDebug("Found usage data for {Count} GPU(s)", gpuUsages.Count);
            return gpuUsages.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get GPU usage");
            return gpuUsages.AsReadOnly();
        }
    }

    private async Task TryAddNvidiaGpusAsync(List<GpuInfo> gpus)
    {
        try
        {
            // Try nvidia-smi command
            var nvidiaResult = await RunCommandAsync("nvidia-smi", "--query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits");
            if (!string.IsNullOrEmpty(nvidiaResult))
            {
                var lines = nvidiaResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 2)
                    {
                        var model = parts[0].Trim();
                        var memoryStr = parts[1].Trim();

                        if (int.TryParse(memoryStr, out var memoryMB))
                        {
                            // Try to get core count from nvidia-ml-py or estimates
                            var coreCount = await GetNvidiaCoreCountAsync(model);

                            gpus.Add(new GpuInfo
                            {
                                Model = model,
                                Vendor = "NVIDIA",
                                MemorySizeInMB = memoryMB,
                                CoreCount = coreCount
                            });
                        }
                    }
                }
                return;
            }

            // Fallback: check /proc/driver/nvidia/gpus
            await TryAddNvidiaFromProcAsync(gpus);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not detect NVIDIA GPUs");
        }
    }

    private async Task TryAddNvidiaFromProcAsync(List<GpuInfo> gpus)
    {
        try
        {
            var nvidiaDir = "/proc/driver/nvidia/gpus";
            if (Directory.Exists(nvidiaDir))
            {
                var gpuDirs = Directory.GetDirectories(nvidiaDir);
                foreach (var gpuDir in gpuDirs)
                {
                    var informationPath = Path.Combine(gpuDir, "information");
                    if (File.Exists(informationPath))
                    {
                        var content = await File.ReadAllTextAsync(informationPath);
                        var model = ExtractValueFromNvidiaInfo(content, "Model:");
                        var memoryStr = ExtractValueFromNvidiaInfo(content, "Video BIOS:");
                        
                        if (!string.IsNullOrEmpty(model))
                        {
                            gpus.Add(new GpuInfo
                            {
                                Model = model,
                                Vendor = "NVIDIA",
                                MemorySizeInMB = 0, // Cannot easily determine from proc
                                CoreCount = 0
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read NVIDIA GPUs from /proc");
        }
    }

    private async Task TryAddAmdGpusAsync(List<GpuInfo> gpus)
    {
        try
        {
            // Try rocm-smi command
            var rocmResult = await RunCommandAsync("rocm-smi", "--showproductname --showmeminfo vram");
            if (!string.IsNullOrEmpty(rocmResult))
            {
                // Parse rocm-smi output
                var lines = rocmResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("Card series:") || line.Contains("GPU"))
                    {
                        // Extract AMD GPU information
                        // This is a simplified parser - AMD output format varies
                        gpus.Add(new GpuInfo
                        {
                            Model = "AMD GPU (detected via rocm-smi)",
                            Vendor = "AMD",
                            MemorySizeInMB = 0,
                            CoreCount = 0
                        });
                    }
                }
                return;
            }

            // Fallback: check sysfs for AMD GPUs
            await TryAddAmdFromSysfsAsync(gpus);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not detect AMD GPUs");
        }
    }

    private async Task TryAddAmdFromSysfsAsync(List<GpuInfo> gpus)
    {
        try
        {
            var drmDir = "/sys/class/drm";
            if (Directory.Exists(drmDir))
            {
                var cardDirs = Directory.GetDirectories(drmDir, "card*")
                    .Where(d => !Path.GetFileName(d).Contains("-"));

                foreach (var cardDir in cardDirs)
                {
                    var deviceDir = Path.Combine(cardDir, "device");
                    var vendorPath = Path.Combine(deviceDir, "vendor");
                    var devicePath = Path.Combine(deviceDir, "device");

                    if (File.Exists(vendorPath) && File.Exists(devicePath))
                    {
                        var vendor = await File.ReadAllTextAsync(vendorPath);
                        if (vendor.Trim().Equals("0x1002", StringComparison.OrdinalIgnoreCase)) // AMD vendor ID
                        {
                            var deviceId = await File.ReadAllTextAsync(devicePath);
                            var model = GetAmdModelFromDeviceId(deviceId.Trim());

                            gpus.Add(new GpuInfo
                            {
                                Model = model,
                                Vendor = "AMD",
                                MemorySizeInMB = 0,
                                CoreCount = 0
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read AMD GPUs from sysfs");
        }
    }

    private async Task TryAddIntelGpusAsync(List<GpuInfo> gpus)
    {
        try
        {
            var drmDir = "/sys/class/drm";
            if (Directory.Exists(drmDir))
            {
                var cardDirs = Directory.GetDirectories(drmDir, "card*")
                    .Where(d => !Path.GetFileName(d).Contains("-"));

                foreach (var cardDir in cardDirs)
                {
                    var deviceDir = Path.Combine(cardDir, "device");
                    var vendorPath = Path.Combine(deviceDir, "vendor");

                    if (File.Exists(vendorPath))
                    {
                        var vendor = await File.ReadAllTextAsync(vendorPath);
                        if (vendor.Trim().Equals("0x8086", StringComparison.OrdinalIgnoreCase)) // Intel vendor ID
                        {
                            gpus.Add(new GpuInfo
                            {
                                Model = "Intel Integrated Graphics",
                                Vendor = "Intel",
                                MemorySizeInMB = 0, // Shared memory
                                CoreCount = 0
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not detect Intel GPUs");
        }
    }

    private async Task TryAddGenericPciGpusAsync(List<GpuInfo> gpus)
    {
        try
        {
            // Use lspci as fallback
            var lspciResult = await RunCommandAsync("lspci", "-nn | grep -i vga");
            if (!string.IsNullOrEmpty(lspciResult))
            {
                var lines = lspciResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("VGA") && !gpus.Any(g => line.Contains(g.Vendor ?? "")))
                    {
                        var vendor = "Unknown";
                        var model = "Unknown GPU";

                        if (line.Contains("NVIDIA") || line.Contains("GeForce"))
                        {
                            vendor = "NVIDIA";
                            model = ExtractModelFromLspci(line);
                        }
                        else if (line.Contains("AMD") || line.Contains("Radeon"))
                        {
                            vendor = "AMD";
                            model = ExtractModelFromLspci(line);
                        }
                        else if (line.Contains("Intel"))
                        {
                            vendor = "Intel";
                            model = ExtractModelFromLspci(line);
                        }

                        // Only add if we don't already have a GPU from this vendor
                        if (!gpus.Any(g => g.Vendor == vendor))
                        {
                            gpus.Add(new GpuInfo
                            {
                                Model = model,
                                Vendor = vendor,
                                MemorySizeInMB = 0,
                                CoreCount = 0
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not detect GPUs via lspci");
        }
    }

    private async Task<string> RunCommandAsync(string command, string arguments)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private ValueTask<int> GetNvidiaCoreCountAsync(string model)
    {
        // This would typically require more sophisticated detection
        // For now, return 0 or implement lookup table based on model
        return ValueTask.FromResult(0);
    }

    private string ExtractValueFromNvidiaInfo(string content, string key)
    {
        var lines = content.Split('\n');
        var line = lines.FirstOrDefault(l => l.Trim().StartsWith(key));
        if (line != null)
        {
            var parts = line.Split(':', 2);
            if (parts.Length > 1)
                return parts[1].Trim();
        }
        return string.Empty;
    }

    private string GetAmdModelFromDeviceId(string deviceId)
    {
        // Simple lookup - in production you'd want a comprehensive database
        return deviceId switch
        {
            "0x73ff" => "AMD Radeon RX 6900 XT",
            "0x73df" => "AMD Radeon RX 6800 XT",
            "0x73bf" => "AMD Radeon RX 6800",
            _ => $"AMD GPU (Device ID: {deviceId})"
        };
    }

    private string ExtractModelFromLspci(string line)
    {
        // Extract GPU model from lspci output
        var match = Regex.Match(line, @"VGA compatible controller: (.+?) \[");
        if (match.Success)
            return match.Groups[1].Value.Trim();

        // Fallback: take everything after "controller: "
        var controllerIndex = line.IndexOf("controller: ");
        if (controllerIndex >= 0)
        {
            var start = controllerIndex + "controller: ".Length;
            var end = line.IndexOf(" [", start);
            if (end > start)
                return line.Substring(start, end - start).Trim();
        }

        return "Unknown GPU";
    }

    private async Task TryAddNvidiaUsageAsync(List<GpuUsage> gpuUsages)
    {
        try
        {
            // Try nvidia-smi for usage data
            var nvidiaResult = await RunCommandAsync("nvidia-smi", "--query-gpu=name,memory.used,memory.free,memory.total,utilization.gpu --format=csv,noheader,nounits");
            if (!string.IsNullOrEmpty(nvidiaResult))
            {
                var lines = nvidiaResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 5)
                    {
                        var model = parts[0].Trim();
                        var memoryUsedStr = parts[1].Trim();
                        var memoryFreeStr = parts[2].Trim();
                        var memoryTotalStr = parts[3].Trim();
                        var gpuUtilStr = parts[4].Trim();

                        if (float.TryParse(memoryUsedStr, out var memoryUsed) &&
                            float.TryParse(memoryFreeStr, out var memoryFree) &&
                            float.TryParse(gpuUtilStr, out var gpuUtil))
                        {
                            gpuUsages.Add(new GpuUsage
                            {
                                Model = model,
                                Vendor = "NVIDIA",
                                MemoryUsedInMB = memoryUsed,
                                MemoryFreeInMB = memoryFree,
                                CoreUsedPercentage = gpuUtil
                            });
                        }
                    }
                }
                return;
            }

            // Fallback: try to read from nvidia proc files
            await TryAddNvidiaUsageFromProcAsync(gpuUsages);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get NVIDIA GPU usage");
        }
    }

    private async Task TryAddNvidiaUsageFromProcAsync(List<GpuUsage> gpuUsages)
    {
        try
        {
            var nvidiaDir = "/proc/driver/nvidia/gpus";
            if (Directory.Exists(nvidiaDir))
            {
                var gpuDirs = Directory.GetDirectories(nvidiaDir);
                foreach (var gpuDir in gpuDirs)
                {
                    var informationPath = Path.Combine(gpuDir, "information");
                    if (File.Exists(informationPath))
                    {
                        var content = await File.ReadAllTextAsync(informationPath);
                        var model = ExtractValueFromNvidiaInfo(content, "Model:");
                        
                        if (!string.IsNullOrEmpty(model))
                        {
                            // Basic fallback with no real usage data
                            gpuUsages.Add(new GpuUsage
                            {
                                Model = model,
                                Vendor = "NVIDIA",
                                MemoryUsedInMB = 0,
                                MemoryFreeInMB = 0,
                                CoreUsedPercentage = 0
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read NVIDIA GPU usage from /proc");
        }
    }

    private async Task TryAddAmdUsageAsync(List<GpuUsage> gpuUsages)
    {
        try
        {
            // Try rocm-smi for usage data
            var rocmResult = await RunCommandAsync("rocm-smi", "--showuse --showmemuse --showmeminfo vram");
            if (!string.IsNullOrEmpty(rocmResult))
            {
                var lines = rocmResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                string? currentGpu = null;
                float gpuUsage = 0;
                float memoryUsed = 0;
                float memoryTotal = 0;

                foreach (var line in lines)
                {
                    if (line.Contains("GPU") && line.Contains(":"))
                    {
                        currentGpu = "AMD GPU";
                    }
                    else if (line.Contains("GPU use (%)") && float.TryParse(ExtractNumberFromLine(line), out var usage))
                    {
                        gpuUsage = usage;
                    }
                    else if (line.Contains("GPU memory use") && float.TryParse(ExtractNumberFromLine(line), out var memUse))
                    {
                        memoryUsed = memUse;
                    }
                    else if (line.Contains("GPU memory total") && float.TryParse(ExtractNumberFromLine(line), out var memTotal))
                    {
                        memoryTotal = memTotal;
                    }
                }

                if (!string.IsNullOrEmpty(currentGpu))
                {
                    gpuUsages.Add(new GpuUsage
                    {
                        Model = currentGpu,
                        Vendor = "AMD",
                        MemoryUsedInMB = memoryUsed,
                        MemoryFreeInMB = memoryTotal - memoryUsed,
                        CoreUsedPercentage = gpuUsage
                    });
                }
                return;
            }

            // Fallback: try to read from sysfs
            await TryAddAmdUsageFromSysfsAsync(gpuUsages);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get AMD GPU usage");
        }
    }

    private async Task TryAddAmdUsageFromSysfsAsync(List<GpuUsage> gpuUsages)
    {
        try
        {
            var drmDir = "/sys/class/drm";
            if (Directory.Exists(drmDir))
            {
                var cardDirs = Directory.GetDirectories(drmDir, "card*")
                    .Where(d => !Path.GetFileName(d).Contains("-"));

                foreach (var cardDir in cardDirs)
                {
                    var deviceDir = Path.Combine(cardDir, "device");
                    var vendorPath = Path.Combine(deviceDir, "vendor");

                    if (File.Exists(vendorPath))
                    {
                        var vendor = await File.ReadAllTextAsync(vendorPath);
                        if (vendor.Trim().Equals("0x1002", StringComparison.OrdinalIgnoreCase)) // AMD vendor ID
                        {
                            // Try to read GPU usage from hwmon
                            var hwmonUsage = await ReadAmdHwmonUsageAsync(deviceDir);
                            
                            gpuUsages.Add(new GpuUsage
                            {
                                Model = "AMD GPU",
                                Vendor = "AMD",
                                MemoryUsedInMB = 0, // Not easily available from sysfs
                                MemoryFreeInMB = 0,
                                CoreUsedPercentage = hwmonUsage
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read AMD GPU usage from sysfs");
        }
    }

    private async Task<float> ReadAmdHwmonUsageAsync(string deviceDir)
    {
        try
        {
            var hwmonDir = Path.Combine(deviceDir, "hwmon");
            if (Directory.Exists(hwmonDir))
            {
                var hwmonSubDirs = Directory.GetDirectories(hwmonDir);
                foreach (var hwmonSubDir in hwmonSubDirs)
                {
                    // Look for GPU usage files
                    var files = Directory.GetFiles(hwmonSubDir, "*busy*");
                    foreach (var file in files)
                    {
                        var content = await File.ReadAllTextAsync(file);
                        if (float.TryParse(content.Trim(), out var usage))
                        {
                            return usage;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read AMD hwmon usage");
        }
        return 0;
    }

    private async Task TryAddIntelUsageAsync(List<GpuUsage> gpuUsages)
    {
        try
        {
            // Intel GPU usage is typically available through i915 debugfs or intel_gpu_top
            var intelGpuTopResult = await RunCommandAsync("intel_gpu_top", "-s 100 -n 1");
            if (!string.IsNullOrEmpty(intelGpuTopResult))
            {
                // Parse intel_gpu_top output
                var lines = intelGpuTopResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("Render/3D") && line.Contains("%"))
                    {
                        var usage = ExtractPercentageFromLine(line);
                        gpuUsages.Add(new GpuUsage
                        {
                            Model = "Intel Integrated Graphics",
                            Vendor = "Intel",
                            MemoryUsedInMB = 0, // Shared memory, hard to determine
                            MemoryFreeInMB = 0,
                            CoreUsedPercentage = usage
                        });
                        break;
                    }
                }
                return;
            }

            // Fallback: try to read from sysfs/debugfs
            await TryAddIntelUsageFromSysfsAsync(gpuUsages);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get Intel GPU usage");
        }
    }

    private async Task TryAddIntelUsageFromSysfsAsync(List<GpuUsage> gpuUsages)
    {
        try
        {
            var drmDir = "/sys/class/drm";
            if (Directory.Exists(drmDir))
            {
                var cardDirs = Directory.GetDirectories(drmDir, "card*")
                    .Where(d => !Path.GetFileName(d).Contains("-"));

                foreach (var cardDir in cardDirs)
                {
                    var deviceDir = Path.Combine(cardDir, "device");
                    var vendorPath = Path.Combine(deviceDir, "vendor");

                    if (File.Exists(vendorPath))
                    {
                        var vendor = await File.ReadAllTextAsync(vendorPath);
                        if (vendor.Trim().Equals("0x8086", StringComparison.OrdinalIgnoreCase)) // Intel vendor ID
                        {
                            // Basic fallback for Intel
                            gpuUsages.Add(new GpuUsage
                            {
                                Model = "Intel Integrated Graphics",
                                Vendor = "Intel",
                                MemoryUsedInMB = 0,
                                MemoryFreeInMB = 0,
                                CoreUsedPercentage = 0
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read Intel GPU usage from sysfs");
        }
    }

    private string ExtractNumberFromLine(string line)
    {
        var match = Regex.Match(line, @"(\d+(?:\.\d+)?)");
        return match.Success ? match.Groups[1].Value : "0";
    }

    private float ExtractPercentageFromLine(string line)
    {
        var match = Regex.Match(line, @"(\d+(?:\.\d+)?)\s*%");
        if (match.Success && float.TryParse(match.Groups[1].Value, out var percentage))
        {
            return percentage;
        }
        return 0;
    }
}