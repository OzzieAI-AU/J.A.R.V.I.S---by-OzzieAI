namespace OzzieAI.Jarvis.Services.Voice;

public interface IVoiceService
{
    Task<byte[]?> SynthesizeSpeechAsync(string text, string? engine = null, string? voice = null);
    Task<string?> TranscribeAudioAsync(byte[] audioData, string filename, string? engine = null);
    Task<List<string>> GetAvailableProviderVoicesAsync(string provider);
    string SanitizeTextForSpeech(string text);
}
