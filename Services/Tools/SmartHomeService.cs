using System.Text.Json;
using System.Text.Json.Nodes;

namespace OzzieAI.Jarvis.Services.Tools;

public interface ISmartHomeService
{
    Task<string> SetAmbientSceneAsync(string sceneName, int brightness = 100);
    Task<string> ToggleDeviceAsync(string deviceId, bool turnOn);
    Task<List<SmartDeviceState>> GetLabDevicesAsync();
}

public record SmartDeviceState(string Id, string Name, string Type, bool IsOn, int Brightness, string ColorHex);

public class SmartHomeService : ISmartHomeService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SmartHomeService> _logger;
    private readonly List<SmartDeviceState> _simulatedDevices = new()
    {
        new("light-lab-ceiling", "Workshop Overhead Arc Luminaires", "Light", true, 85, "#00d4ff"),
        new("light-bench-led", "Workbench Hologram Strips", "Light", true, 100, "#00d4ff"),
        new("plug-solder-fan", "Fume Extraction & Turbine Vents", "Switch", false, 0, "#39ff14"),
        new("light-ambient-perimeter", "Perimeter Blast Wall Backlight", "Light", true, 40, "#ff9800")
    };

    public SmartHomeService(HttpClient httpClient, ILogger<SmartHomeService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<string> SetAmbientSceneAsync(string sceneName, int brightness = 100)
    {
        var s = sceneName.ToLowerInvariant();
        string color = "#00d4ff";
        string label = "Stark Laboratory Standard";

        if (s.Contains("arc") || s.Contains("orange"))
        {
            color = "#ff9800";
            label = "Arc Reactor Overdrive (Orange Ambient)";
        }
        else if (s.Contains("clean slate") || s.Contains("red") || s.Contains("emergency"))
        {
            color = "#ff3333";
            label = "Emergency Defense Lockdown (Crimson Alert)";
        }
        else if (s.Contains("stealth") || s.Contains("green"))
        {
            color = "#39ff14";
            label = "Stealth Recon (Tactical Green)";
        }
        else if (s.Contains("night") || s.Contains("dim"))
        {
            color = "#0b2038";
            brightness = 15;
            label = "Malibu Coastal Night Mode";
        }

        foreach (var dev in _simulatedDevices)
        {
            if (dev.Type == "Light")
            {
                var idx = _simulatedDevices.IndexOf(dev);
                _simulatedDevices[idx] = dev with { IsOn = true, Brightness = brightness, ColorHex = color };
            }
        }

        _logger.LogInformation("Smart Home ambient scene set to: {Label}", label);
        return Task.FromResult($"Ambient workshop scene switched to **{label}** (Brightness: {brightness}%, Color: `{color}`).");
    }

    public Task<string> ToggleDeviceAsync(string deviceId, bool turnOn)
    {
        var dev = _simulatedDevices.FirstOrDefault(d => d.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase) || d.Name.Contains(deviceId, StringComparison.OrdinalIgnoreCase));
        if (dev != null)
        {
            var idx = _simulatedDevices.IndexOf(dev);
            _simulatedDevices[idx] = dev with { IsOn = turnOn };
            return Task.FromResult($"Device **{dev.Name}** powered {(turnOn ? "ON" : "OFF")}.");
        }
        return Task.FromResult($"Device '{deviceId}' not located on Stark IoT mesh.");
    }

    public Task<List<SmartDeviceState>> GetLabDevicesAsync()
    {
        return Task.FromResult(_simulatedDevices.ToList());
    }
}
