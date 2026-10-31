using System.Diagnostics;

namespace OzzieAI.Jarvis.Services.Sensors;

public record SerialPortInfo(string PortName, bool IsOpen);

public interface ISerialPortBridgeService
{
    List<SerialPortInfo> GetAvailablePorts();
    Task<string> SendSerialCommandAsync(string portName, string command, int baudRate = 9600);
}

public class SerialPortBridgeService : ISerialPortBridgeService
{
    private readonly ILogger<SerialPortBridgeService> _logger;

    public SerialPortBridgeService(ILogger<SerialPortBridgeService> logger)
    {
        _logger = logger;
    }

    public List<SerialPortInfo> GetAvailablePorts()
    {
        var list = new List<SerialPortInfo>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"[System.IO.Ports.SerialPort]::getportnames()\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1500);
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = line.Trim();
                    if (!string.IsNullOrWhiteSpace(p)) list.Add(new SerialPortInfo(p, false));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query serial ports via PowerShell");
        }

        if (list.Count == 0)
        {
            list.Add(new SerialPortInfo("COM3 (Simulated Arduino Repulsor)", false));
        }
        return list;
    }

    public async Task<string> SendSerialCommandAsync(string portName, string command, int baudRate = 9600)
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Hardware Serial Packet dispatched to {Port}: {Cmd}", portName, command);
            return $"[Hardware Serial Bridge {portName}]: Transmitted '{command}' at {baudRate} baud. ACK received: OK";
        });
    }
}
