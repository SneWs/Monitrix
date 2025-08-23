using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Monitrix.System.Models;

namespace Monitrix.System.Services.System.Network;

public interface INetworkMonitoringService
{
    ValueTask<IReadOnlyCollection<NetworkInfoModel>> ListNetworkInterfacesAsync();
}

public sealed class NetworkMonitoringService : INetworkMonitoringService
{
    private readonly ILogger<NetworkMonitoringService> _logger;

    public NetworkMonitoringService(ILogger<NetworkMonitoringService> logger)
    {
        _logger = logger;
    }

    public async ValueTask<IReadOnlyCollection<NetworkInfoModel>> ListNetworkInterfacesAsync()
    {
        try
        {
            var networkInterfaces = new List<NetworkInfoModel>();

            // Get network interfaces from /sys/class/net
            var netDir = "/sys/class/net";
            if (!Directory.Exists(netDir))
            {
                _logger.LogWarning("Network interfaces directory not found at {Path}", netDir);
                return networkInterfaces.AsReadOnly();
            }

            var interfaceDirs = Directory.GetDirectories(netDir);
            
            foreach (var interfaceDir in interfaceDirs)
            {
                try
                {
                    var interfaceName = Path.GetFileName(interfaceDir);
                    
                    // Skip loopback and virtual interfaces if desired
                    if (interfaceName == "lo" || interfaceName.StartsWith("docker") || interfaceName.StartsWith("veth"))
                        continue;

                    var networkInfo = await ReadNetworkInterfaceInfoAsync(interfaceName);
                    if (networkInfo != null)
                    {
                        networkInterfaces.Add(networkInfo);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to read network interface info for {Interface}", Path.GetFileName(interfaceDir));
                }
            }

            _logger.LogInformation("Found {Count} network interface(s)", networkInterfaces.Count);
            return networkInterfaces.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list network interfaces");
            return new List<NetworkInfoModel>().AsReadOnly();
        }
    }

    private async Task<NetworkInfoModel?> ReadNetworkInterfaceInfoAsync(string interfaceName)
    {
        try
        {
            var interfaceDir = $"/sys/class/net/{interfaceName}";
            
            // Read interface status
            var status = await ReadInterfaceStatusAsync(interfaceDir);
            
            // Read MAC address
            var macAddress = await ReadMacAddressAsync(interfaceDir);
            
            // Read speed
            var speedInMBs = await ReadInterfaceSpeedAsync(interfaceDir);
            
            // Read all IP addresses
            var ipAddresses = await ReadIpAddressesAsync(interfaceName);

            return new NetworkInfoModel
            {
                Name = interfaceName,
                IpAddress = ipAddresses,
                MacAddress = macAddress,
                Status = status,
                SpeedInMBs = speedInMBs
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read network interface info for {Interface}", interfaceName);
            return null;
        }
    }

    private async Task<string> ReadInterfaceStatusAsync(string interfaceDir)
    {
        try
        {
            var operstatePath = Path.Combine(interfaceDir, "operstate");
            var carrierPath = Path.Combine(interfaceDir, "carrier");

            if (File.Exists(operstatePath))
            {
                var operstate = (await File.ReadAllTextAsync(operstatePath)).Trim();
                
                // Check carrier status for more detailed info
                if (File.Exists(carrierPath))
                {
                    var carrier = (await File.ReadAllTextAsync(carrierPath)).Trim();
                    if (operstate == "up" && carrier == "1")
                        return "Connected";
                    else if (operstate == "up" && carrier == "0")
                        return "Disconnected";
                    else if (operstate == "down")
                        return "Down";
                }

                return operstate switch
                {
                    "up" => "Up",
                    "down" => "Down",
                    "dormant" => "Dormant",
                    "unknown" => "Unknown",
                    _ => operstate
                };
            }

            return "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private async Task<string> ReadMacAddressAsync(string interfaceDir)
    {
        try
        {
            var addressPath = Path.Combine(interfaceDir, "address");
            if (File.Exists(addressPath))
            {
                var address = (await File.ReadAllTextAsync(addressPath)).Trim();
                // Format MAC address consistently
                if (address.Length == 17 && address.Count(c => c == ':') == 5)
                {
                    return address.ToUpperInvariant();
                }
            }
            return "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private async Task<long> ReadInterfaceSpeedAsync(string interfaceDir)
    {
        try
        {
            var speedPath = Path.Combine(interfaceDir, "speed");
            if (File.Exists(speedPath))
            {
                var speedText = (await File.ReadAllTextAsync(speedPath)).Trim();
                if (int.TryParse(speedText, out var speedMbps))
                {
                    // Convert Mbps to MB/s (divide by 8)
                    return speedMbps / 8;
                }
            }

            // Fallback: try to determine speed from interface type
            var typePath = Path.Combine(interfaceDir, "type");
            if (File.Exists(typePath))
            {
                var type = (await File.ReadAllTextAsync(typePath)).Trim();
                // Type 1 is typically Ethernet
                if (type == "1")
                {
                    // Default to 100 MB/s for unknown Ethernet
                    return 12; // 100 Mbps / 8 = 12.5 MB/s, rounded down
                }
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<IReadOnlyCollection<string>> ReadIpAddressesAsync(string interfaceName)
    {
        var ipAddresses = new List<string>();
        
        try
        {
            // Use .NET's NetworkInterface to get all IP addresses
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            var targetInterface = networkInterfaces.FirstOrDefault(ni => ni.Name == interfaceName);

            if (targetInterface != null)
            {
                var ipProperties = targetInterface.GetIPProperties();
                var unicastAddresses = ipProperties.UnicastAddresses;

                // Get all IPv4 addresses (excluding loopback)
                var ipv4Addresses = unicastAddresses
                    .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork
                                  && !IPAddress.IsLoopback(addr.Address))
                    .Select(addr => addr.Address.ToString());

                ipAddresses.AddRange(ipv4Addresses);

                // Get all IPv6 addresses (excluding loopback and link-local)
                var ipv6Addresses = unicastAddresses
                    .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetworkV6
                                  && !IPAddress.IsLoopback(addr.Address)
                                  && !addr.Address.IsIPv6LinkLocal)
                    .Select(addr => addr.Address.ToString());

                ipAddresses.AddRange(ipv6Addresses);
            }

            // If no addresses found via .NET, try alternative method
            if (ipAddresses.Count == 0)
            {
                var alternativeAddresses = await ReadIpAddressesFromProcAsync(interfaceName);
                ipAddresses.AddRange(alternativeAddresses);
            }

            return ipAddresses.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read IP addresses for interface {Interface}", interfaceName);
            return ipAddresses.AsReadOnly();
        }
    }

    private async Task<List<string>> ReadIpAddressesFromProcAsync(string interfaceName)
    {
        var ipAddresses = new List<string>();
        
        try
        {
            // Try using ip command as fallback
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ip",
                    Arguments = $"addr show {interfaceName}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                // Parse all IP addresses from ip command output
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    // Look for both inet (IPv4) and inet6 (IPv6) addresses
                    if ((line.Contains("inet ") || line.Contains("inet6 ")) && !line.Contains("127.0.0.1") && !line.Contains("::1"))
                    {
                        var parts = line.Trim().Split(' ');
                        
                        // Find inet or inet6 keyword
                        var inetIndex = -1;
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (parts[i] == "inet" || parts[i] == "inet6")
                            {
                                inetIndex = i;
                                break;
                            }
                        }
                        
                        if (inetIndex >= 0 && inetIndex + 1 < parts.Length)
                        {
                            var addressWithCidr = parts[inetIndex + 1];
                            var slashIndex = addressWithCidr.IndexOf('/');
                            if (slashIndex > 0)
                            {
                                var address = addressWithCidr.Substring(0, slashIndex);
                                
                                // Skip link-local IPv6 addresses
                                if (!address.StartsWith("fe80:"))
                                {
                                    ipAddresses.Add(address);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read IP addresses from ip command for interface {Interface}", interfaceName);
        }

        return ipAddresses;
    }
}
