using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Sensors;

public interface IMcpSensorService
{
    SensorTelemetry GetCurrentTelemetry();
    void SetAudioRms(double inputRms, double outputRms);
    Task TriggerProtocolPulseAsync(string protocol);
}
