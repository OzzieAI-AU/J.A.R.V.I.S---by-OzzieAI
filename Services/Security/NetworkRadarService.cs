using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace OzzieAI.Jarvis.Services.Security;

public record NetworkDevice(string IpAddress, string MacAddress, string Type, bool IsActive);
public record PortScanResult(string Host, int Port, bool IsOpen, string ServiceName);

public interface INetworkRadarService
{
    Task<List<NetworkDevice>> ScanArpTableAsync();
    Task<List<PortScanResult>> ScanHostPortsAsync(string host, int[]? ports = null);
}

public class NetworkRadarService : INetworkRadarService
{
    private readonly ILogger<NetworkRadarService> _logger;

    public NetworkRadarService(ILogger<NetworkRadarService> logger)
    {
        _logger = logger;
    }

    public async Task<List<NetworkDevice>> ScanArpTableAsync()
    {
        var devices = new List<NetworkDevice>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "arp",
                Arguments = "-a",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return devices;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var matches = Regex.Matches(output, @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s+([0-9a-fA-F-]+)\s+(\w+)");
            foreach (Match m in matches)
            {
                var ip = m.Groups[1].Value;
                var mac = m.Groups[2].Value;
                var type = m.Groups[3].Value;
                if (!ip.EndsWith(".255") && !ip.StartsWith("224."))
                {
                    devices.Add(new NetworkDevice(ip, mac, type, true));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan ARP perimeter table");
        }
        return devices;
    }

    public async Task<List<PortScanResult>> ScanHostPortsAsync(string host, int[]? ports = null)
    {
        ports ??= new[] { 22, 80, 443, 3306, 5432, 6379, 8080, 11434 };
        var results = new List<PortScanResult>();

        var serviceNames = new Dictionary<int, string>
        {
            { 22, "SSH / Secure Shell" },
            { 80, "HTTP Web" },
            { 443, "HTTPS Web" },
            { 3306, "MySQL Database" },
            { 5432, "PostgreSQL Database" },
            { 6379, "Redis Memory Cache" },
            { 8080, "Stark API Gateway" },
            { 11434, "Ollama Local LLM Daemon" }
        };

        foreach (var port in ports)
        {
            try
            {
                using var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync(host, port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(200));
                var isOpen = completed == connectTask && tcp.Connected;
                results.Add(new PortScanResult(host, port, isOpen, serviceNames.GetValueOrDefault(port, "Custom Service")));
            }
            catch
            {
                results.Add(new PortScanResult(host, port, false, serviceNames.GetValueOrDefault(port, "Custom Service")));
            }
        }

        return results;
    }
}
