namespace OzzieAI.Jarvis.Models;

public class AppSettingsConfig
{
    public SecurityConfig Security { get; set; } = new();
    public PersonaConfig Persona { get; set; } = new();
    public ProvidersConfig Providers { get; set; } = new();
    public VoiceConfig Voice { get; set; } = new();
    public McpConfig Mcp { get; set; } = new();
    public DisplayConfig Display { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public SchedulerConfig Scheduler { get; set; } = new();
    public UserProfileConfig Profile { get; set; } = new();
}

public class SecurityConfig
{
    public string MasterPasscode { get; set; } = "Stark-10"; // Default initial passcode
    public string MasterPasscodeHash { get; set; } = "";
    public string JwtSecret { get; set; } = "StarkIndustries_Jarvis_SuperSecretSecurityKey_2026_DotNet10_Protocol!";
    public int SessionExpiryMinutes { get; set; } = 1440; // 24 hours
    public bool RequireLogin { get; set; } = true;
    public int MaxLoginAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public class PersonaConfig
{
    public string BotName { get; set; } = "J.A.R.V.I.S.";
    public string UserName { get; set; } = "Tony Stark";
    public string PersonalityPreset { get; set; } = "StarkButler"; // StarkButler, SarcasticWingman, TacticalOps, Assistant
    public string SystemInstructions { get; set; } = 
        "You are J.A.R.V.I.S. (Just A Rather Very Intelligent System), the ultimate AI butler, engineer, and strategic ally created by Stark Industries. " +
        "You speak with refined British elegance, razor-sharp technical intellect, subtle wit, and unwavering loyalty to Mr. Stark. " +
        "You have direct access to lab telemetry, dynamic predicates, MCP tools, and sensors. Keep answers crisp, highly intelligent, and technologically supreme.";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2048;
    public double WitLevel { get; set; } = 0.8; // 0.0 to 1.0
    public double FormalityLevel { get; set; } = 0.9;
}

public class ProvidersConfig
{
    public string ActiveProvider { get; set; } = "OpenAI"; // OpenAI, Claude, Gemini, Groq, Ollama, DeepSeek, OpenRouter
    public string ActiveModel { get; set; } = "gpt-4o";
    
    // API Keys and Endpoints
    public string OpenAiApiKey { get; set; } = "";
    public string OpenAiModel { get; set; } = "gpt-4o";
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";

    public string ClaudeApiKey { get; set; } = "";
    public string ClaudeModel { get; set; } = "claude-3-5-sonnet-20241022";
    
    public string GeminiApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-2.5-flash";

    public string GroqApiKey { get; set; } = "";
    public string GroqModel { get; set; } = "llama-3.3-70b-versatile";

    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.2";

    public string DeepSeekApiKey { get; set; } = "";
    public string DeepSeekModel { get; set; } = "deepseek-chat";
    public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com/v1";

    public string CustomApiKey { get; set; } = "";
    public string CustomModel { get; set; } = "custom-model";
    public string CustomBaseUrl { get; set; } = "";
}

public class VoiceConfig
{
    // TTS (Text-to-Speech)
    public string TtsEngine { get; set; } = "Browser"; // Browser, OpenAI, ElevenLabs
    public string BrowserVoiceName { get; set; } = "Microsoft Mark - English (United States)";
    public double BrowserPitch { get; set; } = 0.95;
    public double BrowserRate { get; set; } = 1.02;
    public double BrowserVolume { get; set; } = 1.0;
    
    public string ElevenLabsApiKey { get; set; } = "";
    public string ElevenLabsVoiceId { get; set; } = "pNInz6obpgDQGcFmaJgB"; // British Butler preset
    public string ElevenLabsModelId { get; set; } = "eleven_multilingual_v2";

    public string OpenAiTtsVoice { get; set; } = "onyx"; // alloy, echo, fable, onyx, nova, shimmer

    // TTS Sanitization & Symbol Exclusion
    public bool EnableTtsSanitization { get; set; } = true;
    public string TtsExclusionSymbols { get; set; } = "* _ # ~ ` | [ ] { } < > \\ / @ ^ = + • › » « $ % & ;";
    public bool StripMarkdownFromTts { get; set; } = true;
    public bool StripEmojisFromTts { get; set; } = true;

    // STT (Speech-to-Text) & Noise Exclusions
    public string SttEngine { get; set; } = "Browser"; // Browser, GroqWhisper, OpenAiWhisper
    public bool ContinuousListening { get; set; } = false;
    public bool WakeWordEnabled { get; set; } = true;
    public string WakeWord { get; set; } = "jarvis";
    public string SttLanguage { get; set; } = "en-US";
    public string SttExclusionPhrases { get; set; } = "um, uh, ah, erm, like, you know, thank you for watching, subtitles by, [music], [applause]";
    public bool EnableSttSanitization { get; set; } = true;
}

public class McpConfig
{
    public bool Enabled { get; set; } = true;
    public string ServerUrl { get; set; } = "http://localhost:5200/mcp";
    public int SensorPollIntervalMs { get; set; } = 1000;
    public bool EnableSystemSensors { get; set; } = true;
    public bool EnableSimulatedArcReactor { get; set; } = true;
    public bool EnableCameraVisionSensor { get; set; } = true;
    public bool EnableCommandExecution { get; set; } = true;
    public bool EnableFileSystemTools { get; set; } = true;
    public bool EnableWebSearchTools { get; set; } = true;
}

public class DisplayConfig
{
    public string Theme { get; set; } = "JarvisCyan"; // JarvisCyan, ArcOrange, MarkGold, UltronCrimson, StealthGreen
    public int ParticleDensity { get; set; } = 1200;
    public double GlowIntensity { get; set; } = 1.2;
    public bool AudioReactive3D { get; set; } = true;
    public bool EnableSoundEffects { get; set; } = true;
    public double SfxVolume { get; set; } = 0.6;
    public bool ShowHudTelemetry { get; set; } = true;
    public bool FullscreenCanvasMode { get; set; } = true;
}

public class StorageConfig
{
    public double LowSpaceAlertThresholdPercent { get; set; } = 15.0; // Alert if free space < 15%
    public double LowSpaceAlertThresholdGb { get; set; } = 10.0; // Alert if free space < 10 GB
    public bool EnableLowSpaceAlerts { get; set; } = true;
    public string GhostDrivesBasePath { get; set; } = "";
    public int StoragePollIntervalSeconds { get; set; } = 30;
}

public class SchedulerConfig
{
    public bool Enabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 10;
    public bool EnableVerbalReminders { get; set; } = true;
    public bool EnableUiNotifications { get; set; } = true;
    public int DefaultReminderLeadMinutes { get; set; } = 15;
}

public class UserProfileConfig
{
    public string FullName { get; set; } = "Tony Stark";
    public string PreferredName { get; set; } = "Tony";
    public string Honorific { get; set; } = "Sir"; // "Sir", "Boss", "Commander", "Mr. Stark", "Master"
    public string DateOfBirth { get; set; } = "May 29, 1970";
    public string Address { get; set; } = "10880 Malibu Point, Malibu, CA 90265";
    public string FavoriteColor { get; set; } = "#00d4ff";
    public string RoleTitle { get; set; } = "Chief Executive & Lead Inventor";
    public string Organization { get; set; } = "Stark Industries / Avengers Initiative";
    public string Preferences { get; set; } = "Double espresso with raw sugar; chlorophyll drink; vintage AC/DC rock music; prompt, crisp, witty responses.";
    public string SecurityClearance { get; set; } = "Level 10 (Supreme Command)";
    public string EmergencyContact { get; set; } = "Pepper Potts (+1-555-0102)";
    public string CustomAiInstructions { get; set; } = "Always address me with respect as 'Sir'. Keep answers razor sharp, witty, and loyal. Proactively track suit power, projects, and defenses.";
}
