namespace OzzieAI.Jarvis.Models;

public class SensorTelemetry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    // System Host Sensors
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public double MemoryUsedMb { get; set; }
    public double TotalMemoryMb { get; set; }
    public double NetworkBandwidthKbps { get; set; }
    public int ActiveProcesses { get; set; }
    public double BatteryLevel { get; set; } = 100.0;
    public bool IsCharging { get; set; } = true;

    // Stark Arc Reactor & Lab Sensors
    public double ArcReactorOutputGigawatts { get; set; } = 3.14; // Peak 3.14 GW
    public double CoreTemperatureCelsius { get; set; } = 48.6;
    public double ThermalFluxDelta { get; set; } = 0.02;
    public double MagneticContainmentField { get; set; } = 99.8; // Percent
    public string CoreStatus { get; set; } = "NOMINAL"; // NOMINAL, CHARGING, DISCHARGING, CRITICAL

    // Audio & Environmental
    public double AmbientNoiseDb { get; set; } = 32.5;
    public double AudioInputRms { get; set; } = 0.0;
    public double AudioOutputRms { get; set; } = 0.0;

    // Vision Analysis
    public string VisionTarget { get; set; } = "User Detected: Tony Stark";
    public double VisionConfidence { get; set; } = 0.99;
}
