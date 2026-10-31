using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using OzzieAI.Jarvis.Hubs;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Config;

namespace OzzieAI.Jarvis.Services.Sensors;

public class McpSensorService : BackgroundService, IMcpSensorService
{
    private readonly IHubContext<JarvisHub> _hubContext;
    private readonly IJarvisConfigService _configService;
    private readonly ILogger<McpSensorService> _logger;

    private readonly Random _rand = new();
    private double _currentCpu = 12.5;
    private double _currentMem = 32.0;
    private double _arcPower = 3.14;
    private double _coreTemp = 48.6;
    private double _inputRms = 0.0;
    private double _outputRms = 0.0;
    private string _activeProtocol = "CleanSlate-Standby";

    public McpSensorService(
        IHubContext<JarvisHub> hubContext,
        IJarvisConfigService configService,
        ILogger<McpSensorService> logger)
    {
        _hubContext = hubContext;
        _configService = configService;
        _logger = logger;
    }

    public SensorTelemetry GetCurrentTelemetry()
    {
        UpdateTelemetryInternal();

        return new SensorTelemetry
        {
            Timestamp = DateTime.UtcNow,
            CpuUsagePercent = Math.Round(_currentCpu, 1),
            MemoryUsagePercent = Math.Round(_currentMem, 1),
            MemoryUsedMb = Math.Round(Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0), 1),
            TotalMemoryMb = 16384.0,
            NetworkBandwidthKbps = Math.Round(120.0 + _rand.NextDouble() * 35.0, 1),
            ActiveProcesses = Process.GetProcesses().Length,
            BatteryLevel = 100.0,
            IsCharging = true,
            ArcReactorOutputGigawatts = Math.Round(_arcPower, 2),
            CoreTemperatureCelsius = Math.Round(_coreTemp, 1),
            ThermalFluxDelta = Math.Round(0.01 + _rand.NextDouble() * 0.03, 3),
            MagneticContainmentField = 99.8,
            CoreStatus = _coreTemp > 85.0 ? "CRITICAL_OVERHEAT" : "NOMINAL",
            AmbientNoiseDb = Math.Round(30.0 + _inputRms * 40.0, 1),
            AudioInputRms = Math.Round(_inputRms, 2),
            AudioOutputRms = Math.Round(_outputRms, 2),
            VisionTarget = "Tony Stark (Owner Clearance L10)",
            VisionConfidence = 0.99
        };
    }

    public void SetAudioRms(double inputRms, double outputRms)
    {
        _inputRms = inputRms;
        _outputRms = outputRms;
    }

    public async Task TriggerProtocolPulseAsync(string protocol)
    {
        _activeProtocol = protocol;
        _arcPower = 3.85; // Power spike during protocol deployment
        _coreTemp = 58.2;
        await _hubContext.Clients.All.SendAsync("OnProtocolAlert", protocol);
    }

    private void UpdateTelemetryInternal()
    {
        try
        {
            var memInfo = GC.GetGCMemoryInfo();
            if (memInfo.TotalAvailableMemoryBytes > 0)
            {
                var totalMb = memInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0);
                var memoryLoadPercent = ((double)Process.GetCurrentProcess().WorkingSet64 / memInfo.TotalAvailableMemoryBytes) * 100.0;
                _currentMem = Math.Clamp(Math.Round(memoryLoadPercent * 4.0 + 20.0, 1), 10.0, 95.0);
            }

            // Real Host Process CPU sampling
            var proc = Process.GetCurrentProcess();
            var cpuEstimate = Math.Clamp(proc.TotalProcessorTime.TotalMilliseconds / (Environment.TickCount64 + 1.0) * 100.0, 5.0, 95.0);
            var variance = (_rand.NextDouble() - 0.5) * 3.0;
            _currentCpu = Math.Clamp(Math.Round(cpuEstimate + variance, 1), 5.0, 95.0);

            // Arc reactor harmonic fluctuation correlated to system load
            var powerBase = 3.10 + (_currentCpu / 100.0) * 0.45;
            _arcPower = Math.Clamp(Math.Round(powerBase + (_rand.NextDouble() - 0.5) * 0.04, 2), 2.95, 3.85);

            // Core temp tracks CPU + power
            var targetTemp = 42.0 + (_currentCpu * 0.22);
            _coreTemp += (targetTemp - _coreTemp) * 0.15;
        }
        catch { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("McpSensorService telemetry background engine started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = _configService.GetConfig();
            var pollInterval = Math.Max(200, config.Mcp.SensorPollIntervalMs);

            try
            {
                var telemetry = GetCurrentTelemetry();
                await _hubContext.Clients.All.SendAsync("OnSensorTelemetry", telemetry, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogTrace("Error streaming sensor telemetry: {Message}", ex.Message);
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }
}
