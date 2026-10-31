using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Agents;
using OzzieAI.Jarvis.Services.Calendar;
using OzzieAI.Jarvis.Services.Config;
using OzzieAI.Jarvis.Services.Contacts;
using OzzieAI.Jarvis.Services.Llm;
using OzzieAI.Jarvis.Services.Memory;
using OzzieAI.Jarvis.Services.Scheduler;
using OzzieAI.Jarvis.Services.Projects;
using OzzieAI.Jarvis.Services.Sensors;
using OzzieAI.Jarvis.Services.Skills;
using OzzieAI.Jarvis.Services.Storage;
using OzzieAI.Jarvis.Services.Tools;
using OzzieAI.Jarvis.Services.Voice;
using OzzieAI.Jarvis.Services.Security;
using OzzieAI.Jarvis.Services.Media;

namespace OzzieAI.Jarvis.Hubs;

public class JarvisHub : Hub
{
    private readonly IAgentService _agentService;
    private readonly LlmProviderFactory _llmFactory;
    private readonly IMcpSensorService _sensorService;
    private readonly IToolService _toolService;
    private readonly IVoiceService _voiceService;
    private readonly IJarvisConfigService _configService;
    private readonly IPredicateService _predicateService;
    private readonly ISkillService _skillService;
    private readonly ICalendarService _calendarService;
    private readonly IJarvisSchedulerService _schedulerService;
    private readonly IGhostDriveService _ghostDriveService;
    private readonly IContactService _contactService;
    private readonly IProjectService _projectService;
    private readonly IVectorMemoryService _vectorMemoryService;
    private readonly ISmartHomeService _smartHomeService;
    private readonly IGitOpsService _gitOpsService;
    private readonly ICodeSandboxService _codeSandboxService;
    private readonly IBriefingService _briefingService;
    private readonly IBackupService _backupService;
    private readonly IStarkVaultService _starkVaultService;
    private readonly ITerminalSentinelService _sentinelService;
    private readonly IStarkRadioService _starkRadioService;
    private readonly IArtistVerificationService _artistVerifier;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JarvisHub> _logger;

    public JarvisHub(
        IAgentService agentService,
        LlmProviderFactory llmFactory,
        IMcpSensorService sensorService,
        IToolService toolService,
        IVoiceService voiceService,
        IJarvisConfigService configService,
        IPredicateService predicateService,
        ISkillService skillService,
        ICalendarService calendarService,
        IJarvisSchedulerService schedulerService,
        IGhostDriveService ghostDriveService,
        IContactService contactService,
        IProjectService projectService,
        IVectorMemoryService vectorMemoryService,
        ISmartHomeService smartHomeService,
        IGitOpsService gitOpsService,
        ICodeSandboxService codeSandboxService,
        IBriefingService briefingService,
        IBackupService backupService,
        IStarkVaultService starkVaultService,
        ITerminalSentinelService sentinelService,
        IStarkRadioService starkRadioService,
        IArtistVerificationService artistVerifier,
        IServiceProvider serviceProvider,
        ILogger<JarvisHub> logger)
    {
        _agentService = agentService;
        _llmFactory = llmFactory;
        _sensorService = sensorService;
        _toolService = toolService;
        _voiceService = voiceService;
        _configService = configService;
        _predicateService = predicateService;
        _skillService = skillService;
        _calendarService = calendarService;
        _schedulerService = schedulerService;
        _ghostDriveService = ghostDriveService;
        _contactService = contactService;
        _projectService = projectService;
        _vectorMemoryService = vectorMemoryService;
        _smartHomeService = smartHomeService;
        _gitOpsService = gitOpsService;
        _codeSandboxService = codeSandboxService;
        _briefingService = briefingService;
        _backupService = backupService;
        _starkVaultService = starkVaultService;
        _sentinelService = sentinelService;
        _starkRadioService = starkRadioService;
        _artistVerifier = artistVerifier;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        var activeAgent = await _agentService.GetActiveAgentAsync();
        var telemetry = _sensorService.GetCurrentTelemetry();
        var config = _configService.GetConfig();
        var appointments = await _calendarService.GetAllAppointmentsAsync();
        var tasks = await _schedulerService.GetAllTasksAsync();
        var ghostDrives = await _ghostDriveService.GetAllGhostDrivesAsync();
        var storage = await _ghostDriveService.GetStorageTelemetryAsync();
        var contacts = await _contactService.GetAllContactsAsync();
        var projects = await _projectService.GetAllProjectsAsync();
        var devices = await _smartHomeService.GetLabDevicesAsync();
        var snapshots = await _backupService.GetSnapshotsAsync();

        await Clients.Caller.SendAsync("OnInitState", new
        {
            Agent = activeAgent,
            Telemetry = telemetry,
            Config = config,
            Appointments = appointments,
            ScheduledTasks = tasks,
            GhostDrives = ghostDrives,
            Storage = storage,
            Contacts = contacts,
            Projects = projects,
            Devices = devices,
            Snapshots = snapshots
        });

        await base.OnConnectedAsync();
    }

    public async Task SendMessage(ChatRequest request)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("Processing message from {ConnectionId}: {Message}", connectionId, request.Message);

        var activeAgent = await _agentService.GetActiveAgentAsync();
        var systemPrompt = await _agentService.BuildAgentSystemPromptAsync(activeAgent.Id);
        var config = _configService.GetConfig();

        // Vector RAG memory augmentation
        try
        {
            var ragContext = await _vectorMemoryService.BuildAugmentedContextAsync(request.Message);
            if (!string.IsNullOrEmpty(ragContext))
            {
                systemPrompt += "\n" + ragContext;
            }
            _ = _vectorMemoryService.IndexDocumentAsync($"user_{Guid.NewGuid():N}", request.Message, "UserQuery");
        }
        catch { }

        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = request.Message }
        };

        var provider = _llmFactory.GetActiveProvider(request.OverrideProvider);
        var responseSb = new System.Text.StringBuilder();

        try
        {
            var raw = request.Message.Trim();
            var lower = raw.ToLowerInvariant();

            // 0. RADIO STARK STATION & MEDIA HARVESTER DIRECTIVES
            // Handles: "Jarvis, play all AC/DC songs, download all their albums", "stop music", "pause music", "resume music", "next track", "shuffle music", "repeat music", "play rock", etc.
            var cleanMusic = Regex.Replace(raw, @"^(?:hey|hi|hello|ok|okay)?\s*(?:jarvis|javis|travis|javus|yavus)[\s,.:;!?]*", "", RegexOptions.IgnoreCase).Trim();
            var lowerM = cleanMusic.ToLowerInvariant();

            var radioStatus = await _starkRadioService.GetStatusAsync();
            var currentPlayingArtist = radioStatus.CurrentTrack?.Artist;

            var harvestIntent = await _artistVerifier.ParseUserHarvestIntentAsync(cleanMusic, currentPlayingArtist);
            if (harvestIntent.IsHarvestRequest)
            {
                if (!string.IsNullOrWhiteSpace(harvestIntent.ClarificationNeeded))
                {
                    await StreamTextToCallerAsync(harvestIntent.ClarificationNeeded, responseSb);
                    return;
                }

                var artistTarget = harvestIntent.Artist ?? "AC/DC";
                var specificAlbum = harvestIntent.Album;
                var targetDesc = specificAlbum ?? (harvestIntent.IncludeSingles ? "Complete Discography & Master Singles" : "Complete Discography / Master Albums");
                var initialReply = $"Right away, Sir. Initiating autonomous background harvest of **{artistTarget}**'s master albums and singles from YouTube.\n\n" +
                                   $"• **Artist:** `{artistTarget}`\n" +
                                   $"• **Target:** `{targetDesc}`\n" +
                                   $"• **Archive Hierarchy:** `Music/JarvisRadio/{artistTarget}/`\n" +
                                   $"• **Radio Broadcast:** Engaging high-fidelity streaming to your holographic deck now.";

                await StreamTextToCallerAsync(initialReply, responseSb);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _starkRadioService.DownloadAlbumAsync(artistTarget, specificAlbum, 15, null, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background album harvest error for {Artist}", artistTarget);
                    }
                });
                return;
            }
            else if (lowerM == "stop music" || lowerM == "stop radio" || lowerM == "pause music" || lowerM == "pause radio" || lowerM == "pause" || lowerM == "stop the music")
            {
                await _starkRadioService.ControlPlaybackAsync("pause");
                var reply = "Stark Radio broadcast paused, Sir. Standby mode engaged.";
                await StreamTextToCallerAsync(reply, responseSb);
            }
            else if (lowerM == "resume music" || lowerM == "resume radio" || lowerM == "continue music" || lowerM == "unpause music" || lowerM == "play radio" || lowerM == "play music")
            {
                var status = await _starkRadioService.ControlPlaybackAsync("play");
                var trackName = status.CurrentTrack?.Title ?? "Stark Radio";
                var reply = $"Resuming broadcast for **'{trackName}'**, Sir.";
                await StreamTextToCallerAsync(reply, responseSb);
            }
            else if (lowerM == "next track" || lowerM == "next song" || lowerM == "skip track" || lowerM == "skip song" || lowerM == "skip" || lowerM == "next")
            {
                var status = await _starkRadioService.ControlPlaybackAsync("skip");
                var trackName = status.CurrentTrack?.Title ?? "Next Track";
                var reply = $"Advancing to next track: **'{trackName}'**, Sir.";
                await StreamTextToCallerAsync(reply, responseSb);
            }
            else if (lowerM == "previous track" || lowerM == "prev track" || lowerM == "previous song" || lowerM == "last song" || lowerM == "back track")
            {
                var status = await _starkRadioService.ControlPlaybackAsync("previous");
                var trackName = status.CurrentTrack?.Title ?? "Previous Track";
                var reply = $"Replaying previous track: **'{trackName}'**, Sir.";
                await StreamTextToCallerAsync(reply, responseSb);
            }
            else if (lowerM == "shuffle music" || lowerM == "shuffle songs" || lowerM == "toggle shuffle" || lowerM == "shuffle" || lowerM == "randomize music")
            {
                var status = await _starkRadioService.ControlPlaybackAsync("shuffle");
                var modeStr = status.IsShuffle ? "**ENABLED** (Randomized Rotation)" : "**DISABLED** (Sequential)";
                var reply = $"Radio Stark shuffle mode is now {modeStr}, Sir.";
                await StreamTextToCallerAsync(reply, responseSb);
            }
            else if (lowerM == "repeat music" || lowerM == "repeat song" || lowerM == "toggle repeat" || lowerM == "repeat" || lowerM == "loop music" || lowerM == "loop song")
            {
                var status = await _starkRadioService.ControlPlaybackAsync("repeat");
                var modeStr = status.RepeatMode switch {
                    "one" => "**REPEAT SINGLE TRACK** (🔁¹)",
                    "all" => "**REPEAT ENTIRE ARCHIVE** (🔁)",
                    _ => "**OFF**"
                };
                var reply = $"Radio Stark repeat mode set to {modeStr}, Sir.";
                await StreamTextToCallerAsync(reply, responseSb);
            }
            else if (lowerM.StartsWith("play rock") || lowerM.StartsWith("play jazz") || lowerM.StartsWith("play classical") ||
                     lowerM.StartsWith("play pop") || lowerM.StartsWith("play hip-hop") || lowerM.StartsWith("play hip hop") ||
                     lowerM.StartsWith("play rap") || lowerM.StartsWith("play electronic") || lowerM.StartsWith("play edm") ||
                     lowerM.StartsWith("play blues") || lowerM.StartsWith("play country") || lowerM.StartsWith("play folk") ||
                     lowerM.StartsWith("play reggae") || lowerM.StartsWith("play latin") || lowerM.StartsWith("play r&b") || lowerM.StartsWith("play soul") ||
                     lowerM.Contains("rock music") || lowerM.Contains("jazz music") || lowerM.Contains("classical music"))
            {
                string targetGenre = "Rock";
                if (lowerM.Contains("classical")) targetGenre = "Classical";
                else if (lowerM.Contains("jazz")) targetGenre = "Jazz";
                else if (lowerM.Contains("blues")) targetGenre = "Blues";
                else if (lowerM.Contains("hip-hop") || lowerM.Contains("hip hop") || lowerM.Contains("rap")) targetGenre = "Hip-Hop / Rap";
                else if (lowerM.Contains("electronic") || lowerM.Contains("edm")) targetGenre = "Electronic / EDM";
                else if (lowerM.Contains("r&b") || lowerM.Contains("soul")) targetGenre = "R&B & Soul";
                else if (lowerM.Contains("country") || lowerM.Contains("folk")) targetGenre = "Country & Folk";
                else if (lowerM.Contains("reggae") || lowerM.Contains("caribbean")) targetGenre = "Reggae & Caribbean";
                else if (lowerM.Contains("latin")) targetGenre = "Latin";
                else if (lowerM.Contains("pop")) targetGenre = "Pop";

                var status = await _starkRadioService.ControlPlaybackAsync("play_genre", genre: targetGenre);
                var reply = $"Tuning Stark Radio Station to the **{targetGenre}** umbrella channel, Sir. Spinning curated tracks from local archive and YouTube.";
                await StreamTextToCallerAsync(reply, responseSb);
            }
            else if (lowerM.Contains("corrupted") || lowerM.Contains("bad song") || lowerM.Contains("broken song") || lowerM.Contains("replace this song") || lowerM.Contains("download a new one and replace"))
            {
                await StreamTextToCallerAsync("Acknowledged, Sir. Track integrity warning received. Purging corrupted file and re-harvesting pristine audio master from YouTube...", responseSb);
                var fixedTrack = await _starkRadioService.ReplaceCorruptedTrackAsync();
                if (fixedTrack != null)
                {
                    var confirm = $"Replacement complete, Sir. Pristine master acquired for **'{fixedTrack.Title}'** by **'{fixedTrack.Artist}'** ({fixedTrack.FileSizeBytes / (1024 * 1024):F1} MB). Streaming now.";
                    await StreamTextToCallerAsync(confirm, responseSb);
                }
                else
                {
                    await StreamTextToCallerAsync("Could not isolate active corrupted track to replace, Sir.", responseSb);
                }
            }
            else if (lowerM.StartsWith("play ") && !lowerM.StartsWith("play with ") && !lowerM.StartsWith("play video"))
            {
                var songQuery = cleanMusic.Substring(5).Trim('?', '.', '!', ' ', '"', '\'');
                if (!string.IsNullOrWhiteSpace(songQuery))
                {
                    await StreamTextToCallerAsync($"Right away, Sir. Querying local library and YouTube for **\"{songQuery}\"**...", responseSb);
                    try
                    {
                        var track = await _starkRadioService.RequestSongAsync(songQuery, "mp3", true);
                        var reply = $"Now broadcasting: **'{track.Title}'** by **'{track.Artist}'** ({track.DurationSeconds / 60}:{track.DurationSeconds % 60:D2}) • `{track.Genre}`.\nArchived to: `{track.FilePath}`";
                        await StreamTextToCallerAsync(reply, responseSb);
                    }
                    catch (Exception ex)
                    {
                        await StreamTextToCallerAsync($"Radio harvest encountered an issue: {ex.Message}, Sir.", responseSb);
                    }
                }
            }
            else if (lowerM.Contains("music volume") || lowerM.Contains("radio volume"))
            {
                var numMatch = Regex.Match(lowerM, @"\d+");
                if (numMatch.Success && double.TryParse(numMatch.Value, out var pctVal))
                {
                    var targetVol = Math.Clamp(pctVal / 100.0, 0.0, 1.0);
                    await _starkRadioService.ControlPlaybackAsync("volume", volume: targetVol);
                    var reply = $"Stark Radio broadcast volume calibrated to {pctVal:F0}%, Sir.";
                    await StreamTextToCallerAsync(reply, responseSb);
                }
            }
            // 1. CLEAR SCREEN / CLS DIRECTIVE
            else if (lower == "clear" || lower == "cls" || (lower.Contains("clear") && (lower.Contains("screen") || lower.Contains("chat") || lower.Contains("display") || lower.Contains("terminal"))) || lower.Contains("clean screen") || lower.Contains("wipe screen"))
            {
                await Clients.Caller.SendAsync("OnClearScreen");
                var clearMsg = "Holographic display buffer purged and recalibrated, Sir.";
                await StreamTextToCallerAsync(clearMsg, responseSb);
            }
            // 2. HELP & COMMANDS DIRECTIVE
            else if (lower == "help" || lower == "commands" || lower == "?" || lower.Contains("what can you do") || lower == "menu")
            {
                var helpText = "### J.A.R.V.I.S. Core Tactical Directives & Commands:\n\n" +
                               "| Directive | Example | Action |\n" +
                               "|---|---|---|\n" +
                               "| **weather [city]** | `weather Tokyo` or `weather` | Real-time atmospheric scan (temp, humidity, barometer, wind) |\n" +
                               "| **news / headlines** | `news` or `latest news` | Live global intelligence news briefing from real RSS feeds |\n" +
                               "| **diagnostics / hardware** | `diagnostics` or `specs` | Real host hardware stats (CPU, RAM, disk space, .NET 10, uptime) |\n" +
                               "| **time / world clock** | `time` or `world clock` | Exact local time, UTC, and global facility timezones |\n" +
                               "| **appointment [meeting]** | `Create an Appointment for tomorrow at 10AM with Renee to discuss Global Domination` | Schedules meeting & alarms until dismissed |\n" +
                               "| **schedule task / agent** | `Create an Agent to research AI and run it 7 times a day` | Autonomous agent execution matrix |\n" +
                               "| **ghost drives / storage** | `ghost drives` or `storage` | Scans physical & virtual mounts + low space alerts |\n" +
                               "| **calendar / my schedule** | `calendar` or `appointments` | Lists all scheduled meetings & tactical agenda |\n" +
                               "| **dismiss [id/title]** | `dismiss appointment` | Acknowledges & silences appointment alarms |\n" +
                               "| **who is [X] / what is [X]** | `who is Tony Stark` | Instant encyclopedic dossier & reference summary |\n" +
                               "| **calculate [expression]** | `calc 2^8 * 4.5` or `144 / 12` | High-precision scientific & math calculation engine |\n" +
                               "| **predicates / memory** | `predicates` or `facts` | Displays active persistent semantic facts & memory |\n" +
                               "| **remember [key] = [val]** | `remember Suit = Mark 85` | Stores new persistent fact in SQLite database |\n" +
                               "| **agents** | `agents` or `list agents` | Inspect all available persona engines |\n" +
                               "| **skills** | `skills` or `list skills` | Inspect all tactical execution playbooks |\n" +
                               "| **tools** | `tools` or `list tools` | Inspect active tool calling registry |\n" +
                               "| **clean slate protocol** | `clean slate` | Arms emergency defense matrix & peaks Arc Reactor output |\n" +
                               "| **house party protocol** | `house party` | Mobilizes auxiliary armors and logs to persistent predicates |\n" +
                               "| **sentry mode** | `sentry mode` | Engages perimeter defenses and monitoring subroutines |\n" +
                               "| **rea analyze [path]** | `rea analyze` | Reverse Engineer Anything: decompiles assemblies & maps symbols |\n" +
                               "| **ping / latency** | `ping` | Verifies SignalR neural duplex latency & server heartbeat |\n" +
                               "| **clear / cls** | `clear screen` | Instantly purges the holographic chat screen |\n" +
                               "| **theme [name]** | `theme orange` / `theme gold` | Hot-swaps 3D WebGL neural core & HUD color palette |\n" +
                               "| **settings** | `settings` | Opens the 9-tab Advanced Configuration Studio |";

                await StreamTextToCallerAsync(helpText, responseSb);
            }
            // 3. WEATHER / ATMOSPHERIC DIRECTIVE
            else if (lower.Contains("weather") || lower.Contains("atmospheric") || lower.Contains("temperature") || lower.Contains("forecast"))
            {
                var city = ExtractWeatherCity(raw);
                var toolRes = await _toolService.ExecuteToolAsync(new ToolExecutionRequest 
                { 
                    ToolNameOrId = "weather_atmospheric_scan", 
                    Arguments = new() { ["location"] = city } 
                });
                var outputText = !string.IsNullOrEmpty(toolRes.Output) ? toolRes.Output : toolRes.Error ?? "Atmospheric scan returned no data.";
                await StreamTextToCallerAsync(outputText, responseSb);
            }
            // 4. NEWS & CURRENT EVENTS DIRECTIVE
            else if (lower.Contains("news") || lower.Contains("headline") || lower.Contains("current events") || lower.Contains("briefing"))
            {
                var query = ExtractNewsTopic(raw);
                var toolRes = await _toolService.ExecuteToolAsync(new ToolExecutionRequest 
                { 
                    ToolNameOrId = "search_web", 
                    Arguments = new() { ["query"] = query } 
                });
                var outputText = !string.IsNullOrEmpty(toolRes.Output) ? toolRes.Output : toolRes.Error ?? "Intelligence briefing currently offline.";
                await StreamTextToCallerAsync(outputText, responseSb);
            }
            // 5. HARDWARE & SYSTEM DIAGNOSTICS DIRECTIVE
            else if (lower.Contains("hardware") || lower.Contains("diagnostics") || lower.Contains("system info") || lower.Contains("specs") || lower.Contains("disk space") || lower == "status" || lower == "sysinfo")
            {
                var toolRes = await _toolService.ExecuteToolAsync(new ToolExecutionRequest { ToolNameOrId = "system_hardware_diagnostics" });
                var outputText = !string.IsNullOrEmpty(toolRes.Output) ? toolRes.Output : toolRes.Error ?? "Diagnostics subsystem unreachable.";
                await StreamTextToCallerAsync(outputText, responseSb);
            }
            // 6. TIME & WORLD CLOCK DIRECTIVE
            else if (lower.Contains("what time") || lower == "time" || lower == "date" || lower.Contains("world clock") || lower == "clock")
            {
                var toolRes = await _toolService.ExecuteToolAsync(new ToolExecutionRequest { ToolNameOrId = "world_time_clock" });
                var outputText = !string.IsNullOrEmpty(toolRes.Output) ? toolRes.Output : toolRes.Error ?? "Chronometer offline.";
                await StreamTextToCallerAsync(outputText, responseSb);
            }
            // 7. CALCULATION & MATHEMATICS DIRECTIVE
            else if (lower.StartsWith("calculate ") || lower.StartsWith("calc ") || lower.StartsWith("math ") || 
                    (lower.StartsWith("what is ") && (lower.Contains('+') || lower.Contains('-') || lower.Contains('*') || lower.Contains('/') || lower.Contains('^'))))
            {
                var expr = raw.Replace("calculate", "", StringComparison.OrdinalIgnoreCase)
                              .Replace("calc", "", StringComparison.OrdinalIgnoreCase)
                              .Replace("what is", "", StringComparison.OrdinalIgnoreCase)
                              .Replace("math", "", StringComparison.OrdinalIgnoreCase)
                              .Trim('?', ' ', '=');
                var toolRes = await _toolService.ExecuteToolAsync(new ToolExecutionRequest { ToolNameOrId = "calculate_scientific", Arguments = new() { ["expression"] = expr } });
                var outputText = !string.IsNullOrEmpty(toolRes.Output) ? toolRes.Output : toolRes.Error ?? "Calculation failed.";
                await StreamTextToCallerAsync(outputText, responseSb);
            }
            // 8. WIKIPEDIA & ENCYCLOPEDIC DOSSIER DIRECTIVE
            else if (lower.StartsWith("who is ") || lower.StartsWith("what is ") || lower.StartsWith("wiki ") || lower.StartsWith("tell me about ") || lower.StartsWith("lookup "))
            {
                var topic = raw.Replace("who is", "", StringComparison.OrdinalIgnoreCase)
                               .Replace("what is", "", StringComparison.OrdinalIgnoreCase)
                               .Replace("wiki", "", StringComparison.OrdinalIgnoreCase)
                               .Replace("tell me about", "", StringComparison.OrdinalIgnoreCase)
                               .Replace("lookup", "", StringComparison.OrdinalIgnoreCase)
                               .Trim('?', ' ', '.');
                var toolRes = await _toolService.ExecuteToolAsync(new ToolExecutionRequest { ToolNameOrId = "knowledge_lookup", Arguments = new() { ["query"] = topic } });
                var outputText = !string.IsNullOrEmpty(toolRes.Output) ? toolRes.Output : toolRes.Error ?? $"No dossier found for {topic}.";
                await StreamTextToCallerAsync(outputText, responseSb);
            }
            // 9. PREDICATES & PERSISTENT MEMORY DIRECTIVES
            else if (lower == "predicates" || lower == "facts" || lower == "memory" || lower == "list predicates")
            {
                var predicates = await _predicateService.GetAllPredicatesAsync();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("### Active Persistent Memory Predicates (SQLite Core):\n");
                sb.AppendLine("| Key | Value | Category |");
                sb.AppendLine("|---|---|---|");
                foreach (var p in predicates)
                {
                    sb.AppendLine($"| **{p.Key}** | `{p.Value}` | *{p.Category}* |");
                }
                sb.AppendLine("\n*Tip: Store a new fact anytime by typing:* `remember Key = Value`");
                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            else if (lower.StartsWith("remember ") || lower.StartsWith("save fact "))
            {
                var statement = raw.Replace("remember", "", StringComparison.OrdinalIgnoreCase)
                                   .Replace("save fact", "", StringComparison.OrdinalIgnoreCase).Trim();
                var parts = statement.Split('=', 2);
                if (parts.Length == 2)
                {
                    var k = parts[0].Trim();
                    var v = parts[1].Trim();
                    await _predicateService.SavePredicateAsync(new PredicateItem
                    {
                        Key = k,
                        Value = v,
                        Category = "Custom",
                        Description = "User-defined persistent memory predicate"
                    });
                    var confirm = $"Acknowledged, Sir. Fact **{k}** = `{v}` stored in persistent Stark neural memory.";
                    await StreamTextToCallerAsync(confirm, responseSb);
                }
                else
                {
                    await StreamTextToCallerAsync("Format invalid, Sir. Please use: `remember [Key] = [Value]`", responseSb);
                }
            }
            // 10. AGENTS & PERSONAS DIRECTIVE
            else if (lower == "agents" || lower == "list agents")
            {
                var agents = await _agentService.GetAllAgentsAsync();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("### Registered Autonomous Persona Agents:\n");
                sb.AppendLine("| ID | Name | Role | Status | Theme |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var a in agents)
                {
                    var activeBadge = a.IsActive ? "**[ACTIVE]**" : "Standby";
                    sb.AppendLine($"| `{a.Id}` | **{a.Name}** | {a.Role} | {activeBadge} | `{a.ColorTheme}` |");
                }
                sb.AppendLine("\n*Tip: Switch agent in the top header selector or via Settings Studio.*");
                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 11. SKILLS DIRECTIVE
            else if (lower == "skills" || lower == "list skills")
            {
                var skills = await _skillService.GetAllSkillsAsync();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("### Registered Tactical Skills & Playbooks:\n");
                sb.AppendLine("| Skill Name | Category | Description | Built-in |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var s in skills)
                {
                    sb.AppendLine($"| **{s.Name}** | *{s.Category}* | {s.Description} | {(s.IsBuiltIn ? "Yes" : "Custom")} |");
                }
                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 12. TOOLS DIRECTIVE
            else if (lower == "tools" || lower == "list tools")
            {
                var tools = await _toolService.GetAllToolsAsync();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("### Registered Capabilities & Tool Calling Matrix:\n");
                sb.AppendLine("| Tool Name | Type | Description |");
                sb.AppendLine("|---|---|---|");
                foreach (var t in tools)
                {
                    sb.AppendLine($"| **{t.Name}** | `{t.ExecutionType}` | {t.Description} |");
                }
                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 13. PING & NETWORK HEARTBEAT
            else if (lower == "ping" || lower == "heartbeat")
            {
                var tel = _sensorService.GetCurrentTelemetry();
                var pingMsg = $"**PONG.** Duplex SignalR Neural Link: `NOMINAL`\n" +
                              $"• **Arc Reactor Output:** `{tel.ArcReactorOutputGigawatts:F2} GW`\n" +
                              $"• **Core Temperature:** `{tel.CoreTemperatureCelsius:F1}°C`\n" +
                              $"• **Host Uptime:** `{TimeSpan.FromMilliseconds(Environment.TickCount64):d'd 'hh'h 'mm'm 'ss's'}`\n" +
                              $"• **Active Persona:** `{activeAgent.Name}` ({activeAgent.Role})";
                await StreamTextToCallerAsync(pingMsg, responseSb);
            }
            // 14. SUIT PROTOCOLS DIRECTIVE
            else if (lower.Contains("clean slate") || lower.Contains("house party") || lower.Contains("sentry mode") || lower.Contains("protocol"))
            {
                string protocol = "CleanSlate";
                if (lower.Contains("house party")) protocol = "HouseParty";
                else if (lower.Contains("sentry")) protocol = "SentryMode";

                var toolRes = await _toolService.ExecuteToolAsync(new ToolExecutionRequest 
                { 
                    ToolNameOrId = "suit_protocol_control", 
                    Arguments = new() { ["protocol"] = protocol } 
                });
                var outputText = !string.IsNullOrEmpty(toolRes.Output) ? toolRes.Output : toolRes.Error ?? "Protocol execution error.";
                await StreamTextToCallerAsync(outputText, responseSb);
            }
            // 15. REVERSE ENGINEER ANYTHING (REA) DIRECTIVE
            else if (lower.Contains("rea") || lower.Contains("reverse engineer") || lower.Contains("decompile"))
            {
                var toolRes = await _toolService.ExecuteToolAsync(new ToolExecutionRequest { 
                    ToolNameOrId = "rea_analyze_target", 
                    Arguments = new() { ["target"] = @"bin\Debug\net10.0\OzzieAI.Jarvis.dll" } 
                });
                var outputText = !string.IsNullOrEmpty(toolRes.Output) ? toolRes.Output : toolRes.Error ?? "REA Engine encountered an error.";
                await StreamTextToCallerAsync(outputText, responseSb);
            }
            // 16. APPOINTMENT & CALENDAR DIRECTIVE
            else if (lower.Contains("appointment") || lower.Contains("schedule a meeting") || lower.Contains("create an appointment") || (lower.Contains("meeting") && lower.Contains("with")))
            {
                var appt = _calendarService.ParseAppointmentFromNaturalLanguage(raw);

                // Cross-reference attendee with Stark Contacts Directory
                ContactItem? contactMatch = null;
                if (!string.IsNullOrWhiteSpace(appt.Attendees))
                {
                    contactMatch = await _contactService.FindContactByNameOrCallSignAsync(appt.Attendees);
                    if (contactMatch != null)
                    {
                        appt.Description += $" [Contact: {contactMatch.FullName} ({contactMatch.Role}, {contactMatch.Organization}) | Comms: {contactMatch.PrimaryPhone} | Email: {contactMatch.PrimaryEmail} | Clearance: LVL {contactMatch.ClearanceLevel}]";
                    }
                }

                await _calendarService.CreateAppointmentAsync(appt);
                await Clients.All.SendAsync("OnCalendarUpdated", appt);

                var localStart = appt.StartTime.ToLocalTime();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("### Tactical Calendar Appointment Scheduled, Sir:\n");
                sb.AppendLine($"• **Event Title:** **{appt.Title}**");
                if (contactMatch != null)
                {
                    sb.AppendLine($"• **Confirmed Attendee:** **{contactMatch.FullName}** ({contactMatch.Role})");
                    sb.AppendLine($"• **Clearance Level:** `LEVEL {contactMatch.ClearanceLevel}` ({contactMatch.Organization})");
                    sb.AppendLine($"• **Primary Comms:** `{contactMatch.PrimaryPhone}` | `{contactMatch.PrimaryEmail}`");
                }
                else if (!string.IsNullOrEmpty(appt.Attendees))
                {
                    sb.AppendLine($"• **Attendees:** `{appt.Attendees}`");
                }
                sb.AppendLine($"• **Scheduled Time:** `{localStart:yyyy-MM-dd h:mm tt}` ({TimeZoneInfo.Local.DisplayName})");
                sb.AppendLine($"• **Reminders:** Arming verbal & UI notifications ({appt.RemindBeforeMinutes} min lead window; recurring until dismissed)");
                sb.AppendLine($"• **Appointment ID:** `{appt.Id}`");
                sb.AppendLine($"• **Status:** Active in Stark Chronometer Subsystem");

                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 17. AUTONOMOUS AGENT & RECURRING SCHEDULER DIRECTIVE
            else if (((lower.Contains("create an agent") || lower.Contains("create agent") || lower.Contains("make an agent") || lower.Contains("deploy agent")) && (lower.Contains("run") || lower.Contains("day") || lower.Contains("research"))) || 
                     lower.Contains("times a day") || lower.Contains("schedule task") || lower.Contains("scheduled task"))
            {
                var agentName = "AI Research Specialist";
                var agentId = "ai_research_agent";
                var existing = await _agentService.GetAgentAsync(agentId);
                if (existing == null)
                {
                    await _agentService.SaveAgentAsync(new AgentItem
                    {
                        Id = agentId,
                        Name = agentName,
                        Role = "Autonomous AI & Intelligence Researcher",
                        Description = "Autonomous agent assigned to scan, analyze, and synthesize emerging AI breakthroughs.",
                        SystemPrompt = "You are the AI Research Specialist at Stark Industries. Perform deep-dive research into AI, LLMs, and autonomous agent systems.",
                        Tone = "Analytical, precise, forward-looking",
                        ColorTheme = "#ff9800",
                        IsActive = false,
                        IsBuiltIn = false
                    });
                }

                var task = _schedulerService.ParseTaskFromNaturalLanguage(raw, agentId);
                await _schedulerService.CreateTaskAsync(task);
                await Clients.All.SendAsync("OnScheduledTaskUpdated", task);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("### Autonomous Agent & Scheduled Task Deployed, Sir:\n");
                sb.AppendLine($"• **Agent Unit:** **{agentName}** (`{agentId}`)");
                sb.AppendLine($"• **Task Directive:** `{task.PromptOrDirective}`");
                sb.AppendLine($"• **Frequency:** `{task.RunsPerDay} times per day` (~every {task.IntervalMinutes} minutes)");
                sb.AppendLine($"• **Next Run:** `{task.NextRun.ToLocalTime():yyyy-MM-dd h:mm:ss tt}`");
                sb.AppendLine($"• **Execution Engine:** Autonomous Background Task Runner");
                sb.AppendLine($"• **Memory Sync:** Findings will persist into SQLite Predicates & HUD alerts");

                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 18. GHOST DRIVES & STORAGE DIRECTIVE
            else if (lower.Contains("ghost drive") || lower == "drives" || lower == "storage" || lower.Contains("check storage") || lower.Contains("check drives") || lower.Contains("disk space") || lower.Contains("storage telemetry"))
            {
                var telemetry = await _ghostDriveService.GetStorageTelemetryAsync();
                var alerts = await _ghostDriveService.CheckLowSpaceAlertsAsync();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("### Stark Storage Matrix & Ghost Drives Telemetry:\n\n");
                sb.AppendLine("| Drive / Partition | Type | Total | Free | Used | Free % | Status |");
                sb.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var d in telemetry)
                {
                    var flag = d.IsLowSpace ? "**ALERT: LOW**" : d.Status;
                    sb.AppendLine($"| `{d.DriveName}` | {d.DriveFormat} | `{d.TotalGb:F1} GB` | `{d.FreeGb:F1} GB` | `{d.UsedGb:F1} GB` | `{d.PercentFree:F1}%` | {flag} |");
                }

                if (alerts.Count > 0)
                {
                    sb.AppendLine("\n> [!WARNING]");
                    foreach (var a in alerts)
                    {
                        sb.AppendLine($"> **{a.DriveName}**: {a.AlertMessage}");
                    }
                }
                else
                {
                    sb.AppendLine("\n*All physical drive volumes and Ghost Drive virtual partitions are nominal.*");
                }

                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 19. CALENDAR DISMISS DIRECTIVE
            else if (lower.StartsWith("dismiss appointment") || (lower.StartsWith("dismiss") && (lower.Contains("meeting") || lower.Contains("alert"))))
            {
                var idOrTitle = raw.Replace("dismiss appointment", "", StringComparison.OrdinalIgnoreCase)
                                   .Replace("dismiss", "", StringComparison.OrdinalIgnoreCase).Trim();
                var appts = await _calendarService.GetAllAppointmentsAsync();
                var match = appts.FirstOrDefault(a => a.Id.Equals(idOrTitle, StringComparison.OrdinalIgnoreCase) || a.Title.Contains(idOrTitle, StringComparison.OrdinalIgnoreCase) || a.Status == "Active");
                if (match != null)
                {
                    await _calendarService.DismissAppointmentAsync(match.Id);
                    await Clients.All.SendAsync("OnAppointmentDismissed", match.Id);
                    await StreamTextToCallerAsync($"Calendar appointment **{match.Title}** dismissed, Sir. Alarms ceased.", responseSb);
                }
                else
                {
                    await StreamTextToCallerAsync("All active calendar alarms dismissed, Sir.", responseSb);
                }
            }
            // 20. DIRECTIVE: ADD CONTACT VIA NATURAL LANGUAGE / AGENT AUTOMATION
            else if (lower.StartsWith("add contact") || lower.StartsWith("add a contact") || 
                     lower.StartsWith("create contact") || lower.StartsWith("create a contact") || 
                     lower.StartsWith("new contact") || 
                     (lower.Contains("contact") && (lower.Contains("add ") || lower.Contains("create ")) && lower.Contains("phone")))
            {
                // Example: "Add a contact for Merry Evans, phone 0416566989"
                string extractedName = "";
                string extractedPhone = "";
                string extractedEmail = "";

                // Parse phone
                var phoneMatch = System.Text.RegularExpressions.Regex.Match(raw, @"(?:phone|mobile|tel|cell|number)\s*(?:is|:)?\s*([+0-9\s-]{6,20})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (phoneMatch.Success)
                {
                    extractedPhone = phoneMatch.Groups[1].Value.Trim();
                }

                // Parse email
                var emailMatch = System.Text.RegularExpressions.Regex.Match(raw, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (emailMatch.Success)
                {
                    extractedEmail = emailMatch.Value.Trim();
                }

                // Parse name
                var nameMatch = System.Text.RegularExpressions.Regex.Match(raw, @"(?:add|create|new)\s+(?:a\s+)?contact\s+(?:for\s+|named\s+|called\s+)?(?<name>[^,\n]+?)(?:,\s*phone|\s+phone|\s+with\s+phone|\s+email|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (nameMatch.Success)
                {
                    extractedName = nameMatch.Groups["name"].Value.Trim(' ', ':', ',');
                }

                // Fallback clean-up if regex didn't isolate name cleanly
                if (string.IsNullOrWhiteSpace(extractedName))
                {
                    var clean = raw;
                    clean = System.Text.RegularExpressions.Regex.Replace(clean, @"^(?:add|create|new)\s+(?:a\s+)?contact\s+(?:for\s+)?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!string.IsNullOrEmpty(extractedPhone)) clean = clean.Replace(extractedPhone, "");
                    clean = System.Text.RegularExpressions.Regex.Replace(clean, @"(?:phone|mobile|tel|cell|number|email)\s*[:=]?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    extractedName = clean.Split(',')[0].Trim(' ', '?', '.', '!');
                }

                if (string.IsNullOrWhiteSpace(extractedName)) extractedName = "Tactical Operative";
                if (string.IsNullOrWhiteSpace(extractedPhone)) extractedPhone = "Unassigned";

                var newContact = new ContactItem
                {
                    FullName = extractedName,
                    PrimaryPhone = extractedPhone,
                    PrimaryEmail = !string.IsNullOrWhiteSpace(extractedEmail) ? extractedEmail : $"{extractedName.ToLowerInvariant().Replace(" ", ".")}@stark-external.net",
                    Role = "External Specialist",
                    Organization = "Stark Tactical Network",
                    ClearanceLevel = 5,
                    Tags = "VIP, Operative, External Network",
                    Notes = $"Registered via J.A.R.V.I.S. neural voice directive. Primary comms: {extractedPhone}.",
                    AvatarColor = "#00d4ff",
                    AvatarUrl = "https://images.unsplash.com/photo-1544005313-94ddf0286df2?w=300&auto=format&fit=crop&q=80"
                };

                var created = await _contactService.CreateContactAsync(newContact);
                await Clients.All.SendAsync("OnContactUpdated", created);
                await Clients.Caller.SendAsync("OnOpenContactDossier", created);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"### Stark Contact Registered & Persisted: **{created.FullName}**\n");
                sb.AppendLine($"• **Full Name:** **{created.FullName}**");
                sb.AppendLine($"• **Primary Phone:** `{created.PrimaryPhone}`");
                if (!string.IsNullOrWhiteSpace(created.PrimaryEmail)) sb.AppendLine($"• **Primary Email:** `{created.PrimaryEmail}`");
                sb.AppendLine($"• **Clearance Status:** `LEVEL {created.ClearanceLevel}` ({created.Tags})");
                sb.AppendLine($"• **Designation:** {created.Role} — *{created.Organization}*");
                sb.AppendLine($"• **Storage Status:** Encrypted in Stark SQLite Contact Database (`jarvis_data.db`)");
                sb.AppendLine($"\n*Holographic Biometric Dossier pulled up and active on your main display, Sir.*");

                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 21. DIRECTIVE: ADD PROJECT VIA NATURAL LANGUAGE / AGENT AUTOMATION
            else if (lower.StartsWith("add a new project") || lower.StartsWith("add new project") || 
                     lower.StartsWith("add a project") || lower.StartsWith("add project") || 
                     lower.StartsWith("create a new project") || lower.StartsWith("create new project") || 
                     lower.StartsWith("create a project") || lower.StartsWith("create project") || 
                     lower.StartsWith("new project") ||
                     (lower.Contains("project") && (lower.Contains("add ") || lower.Contains("create ") || lower.Contains("start ")) && (lower.Contains("for ") || lower.Contains("called ") || lower.Contains("named "))))
            {
                // Example: "Add a new project for Ai Model Build"
                string projName = "";
                var m = System.Text.RegularExpressions.Regex.Match(raw, @"(?:add|create|new|start)\s+(?:a\s+)?(?:new\s+)?project\s+(?:for|called|named|titled)?\s*(?<name>.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    projName = m.Groups["name"].Value.Trim(' ', ':', '"', '\'', '.', '?', '!');
                }

                if (string.IsNullOrWhiteSpace(projName))
                {
                    projName = raw.Replace("add a new project for", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("add new project for", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("add project for", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("create a new project for", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("create project for", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("new project for", "", StringComparison.OrdinalIgnoreCase)
                                  .Trim(' ', ':', '"', '\'', '.', '?', '!');
                }

                if (string.IsNullOrWhiteSpace(projName)) projName = "Advanced AI System Architecture";

                var category = (projName.Contains("ai", StringComparison.OrdinalIgnoreCase) || 
                                projName.Contains("model", StringComparison.OrdinalIgnoreCase) || 
                                projName.Contains("neural", StringComparison.OrdinalIgnoreCase) || 
                                projName.Contains("learning", StringComparison.OrdinalIgnoreCase))
                                ? "AI/Research"
                                : (projName.Contains("armor", StringComparison.OrdinalIgnoreCase) || projName.Contains("nano", StringComparison.OrdinalIgnoreCase) ? "Engineering" : "Tactical");

                var slug = GenerateProjectSlug(projName);
                var newProj = new JarvisProject
                {
                    Name = projName,
                    Category = category,
                    Description = $"Autonomous tactical project initiative for {projName}. Managed by J.A.R.V.I.S. Core with full agent automation and Ghost Drive integration.",
                    Priority = "High",
                    Status = "Active",
                    ProgressPercent = 20.0,
                    Color = "#00d4ff",
                    Tags = category == "AI/Research" ? "AI, Neural Core, Deep Learning, Stark Labs" : "Engineering, Nanotech, Stark Labs",
                    GhostDriveTag = "PROJECTS",
                    GhostDrivePath = $"Projects/{slug}",
                    LeadAgentId = activeAgent.Id,
                    LeadAgentName = activeAgent.Name,
                    AutomationEnabled = true
                };

                var createdProj = await _projectService.CreateProjectAsync(newProj);

                // Add 3 initial tactical milestone tasks
                await _projectService.AddTaskAsync(createdProj.Id, new ProjectTaskItem
                {
                    ProjectId = createdProj.Id,
                    Title = "Pipeline & Architecture Specification",
                    Description = $"Compile blueprint and system schemas for {projName} on Ghost Drive.",
                    Priority = "Critical",
                    Status = "Completed",
                    CanAutoExecute = true,
                    AutomationResult = "Initial architecture validated and blueprints saved to Ghost Drive."
                });
                await _projectService.AddTaskAsync(createdProj.Id, new ProjectTaskItem
                {
                    ProjectId = createdProj.Id,
                    Title = "Autonomous Execution & Core Training Matrix",
                    Description = $"Deploy background worker agents to process tasks for {projName}.",
                    Priority = "High",
                    Status = "InProgress",
                    CanAutoExecute = true
                });
                await _projectService.AddTaskAsync(createdProj.Id, new ProjectTaskItem
                {
                    ProjectId = createdProj.Id,
                    Title = "Holographic Interface & Telemetry Verification",
                    Description = "Link live metrics to 3D Project Studio and HUD reticles.",
                    Priority = "Medium",
                    Status = "Pending",
                    CanAutoExecute = true
                });

                await Clients.All.SendAsync("OnProjectUpdated", createdProj);
                await Clients.Caller.SendAsync("OnOpenProjectStudio", createdProj.Id);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"### Tactical Project Initialized: **{createdProj.Name}**\n");
                sb.AppendLine($"• **Project Name:** **{createdProj.Name}** (`{createdProj.Slug}`)");
                sb.AppendLine($"• **Strategic Category:** `{createdProj.Category}` | Priority: `{createdProj.Priority}`");
                sb.AppendLine($"• **Ghost Drive Mount:** `{createdProj.GhostDrivePath}/` (Blueprint initialized)");
                sb.AppendLine($"• **Lead Automation Agent:** **{createdProj.LeadAgentName}** (Autonomous Matrix Armed)");
                sb.AppendLine($"• **Milestones:** 3 initial tactical tasks generated (Architecture, Training, Holographic Reticle)");
                sb.AppendLine($"• **Workspace Action:** Opening Full-Screen 3D Project Studio on your display.");
                sb.AppendLine($"\n*Displaying 3D Project Studio workspace now, Sir.*");

                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 22. DIRECTIVE: CONTACTS, ADDRESS & BIOMETRIC DOSSIER QUERY
            else if (lower.Contains("address for") || lower.Contains("address of") || lower.Contains("where is") || 
                     (lower.Contains("do we have") && (lower.Contains("address") || lower.Contains("phone") || lower.Contains("contact") || lower.Contains("number") || lower.Contains("details") || lower.Contains("info") || lower.Contains("clearance"))) ||
                     lower.StartsWith("phone for") || lower.StartsWith("phone number of") || lower.StartsWith("phone number for") ||
                     lower.StartsWith("pull up contact") || lower.StartsWith("pull up ") || lower.StartsWith("dossier for") ||
                     lower.StartsWith("find contact") || lower.StartsWith("contact info for") ||
                     lower == "contacts" || lower == "show contacts" || lower == "list contacts" || lower == "all contacts")
            {
                // Extract target contact name
                var target = "";
                if (lower.Contains("address for")) target = ExtractAfterPhrase(raw, "address for");
                else if (lower.Contains("address of")) target = ExtractAfterPhrase(raw, "address of");
                else if (lower.Contains("where is")) target = ExtractAfterPhrase(raw, "where is");
                else if (lower.Contains("phone for")) target = ExtractAfterPhrase(raw, "phone for");
                else if (lower.Contains("phone number of")) target = ExtractAfterPhrase(raw, "phone number of");
                else if (lower.Contains("phone number for")) target = ExtractAfterPhrase(raw, "phone number for");
                else if (lower.Contains("do we have an address for")) target = ExtractAfterPhrase(raw, "do we have an address for");
                else if (lower.Contains("do we have address for")) target = ExtractAfterPhrase(raw, "do we have address for");
                else if (lower.Contains("do we have a contact for")) target = ExtractAfterPhrase(raw, "do we have a contact for");
                else if (lower.Contains("do we have contact for")) target = ExtractAfterPhrase(raw, "do we have contact for");
                else if (lower.Contains("do we have a phone for")) target = ExtractAfterPhrase(raw, "do we have a phone for");
                else if (lower.Contains("do we have phone for")) target = ExtractAfterPhrase(raw, "do we have phone for");
                else if (lower.Contains("do we have info for")) target = ExtractAfterPhrase(raw, "do we have info for");
                else if (lower.Contains("do we have details for")) target = ExtractAfterPhrase(raw, "do we have details for");
                else if (lower.StartsWith("find contact ")) target = raw.Substring(13).Trim('?', '.', ' ');
                else if (lower.StartsWith("contact info for ")) target = raw.Substring(17).Trim('?', '.', ' ');
                else if (lower.StartsWith("pull up contact ")) target = raw.Substring(16).Trim('?', '.', ' ');
                else if (lower.StartsWith("pull up ")) target = raw.Substring(8).Trim('?', '.', ' ');
                else if (lower.StartsWith("dossier for ")) target = raw.Substring(12).Trim('?', '.', ' ');

                target = target.Trim('?', '.', '!', ' ', ';', '"', '\'');

                if (!string.IsNullOrWhiteSpace(target))
                {
                    var match = await _contactService.FindContactByNameOrCallSignAsync(target);
                    if (match != null)
                    {
                        await Clients.Caller.SendAsync("OnOpenContactDossier", match);

                        var address = ExtractContactAddress(match);
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"### Stark Intelligence Dossier: **{match.FullName}**\n");
                        sb.AppendLine($"• **Operational Address / Headquarters:** `{address}`");
                        sb.AppendLine($"• **Call Sign / Alias:** `{match.CallSign}`");
                        sb.AppendLine($"• **Role & Division:** {match.Role} — *{match.Organization}*");
                        sb.AppendLine($"• **Clearance Status:** `LEVEL {match.ClearanceLevel}` ({match.Tags})");
                        sb.AppendLine($"• **Primary Secure Comms:** `{match.PrimaryPhone}`");
                        if (!string.IsNullOrWhiteSpace(match.SecondaryPhone)) sb.AppendLine($"• **Secondary Comms:** `{match.SecondaryPhone}`");
                        sb.AppendLine($"• **Primary Email:** `{match.PrimaryEmail}`");
                        if (!string.IsNullOrWhiteSpace(match.WorkEmail)) sb.AppendLine($"• **Secure Dispatch:** `{match.WorkEmail}`");
                        if (!string.IsNullOrWhiteSpace(match.Notes)) sb.AppendLine($"• **Intelligence Brief:** {match.Notes}");
                        sb.AppendLine($"\n*Tactical biometric dossier pulled up and projected to your holographic HUD, Sir.*");

                        await StreamTextToCallerAsync(sb.ToString(), responseSb);
                    }
                    else
                    {
                        var list = await _contactService.SearchContactsAsync(target);
                        if (list.Count > 0)
                        {
                            var sb = new System.Text.StringBuilder($"### Found {list.Count} Contact Matches for '{target}':\n\n");
                            sb.AppendLine("| Clearance | Name | Call Sign | Role | Organization | Primary Comms | Email |");
                            sb.AppendLine("|---|---|---|---|---|---|---|");
                            foreach (var c in list)
                            {
                                sb.AppendLine($"| `LVL {c.ClearanceLevel}` | **{c.FullName}** | `{c.CallSign}` | {c.Role} | {c.Organization} | `{c.PrimaryPhone}` | `{c.PrimaryEmail}` |");
                            }
                            await StreamTextToCallerAsync(sb.ToString(), responseSb);
                        }
                        else
                        {
                            await StreamTextToCallerAsync($"I searched the Stark Intelligence Directory, but found no active record matching '**{target}**', Sir. Would you like me to register a new contact?", responseSb);
                        }
                    }
                }
                else
                {
                    var all = await _contactService.GetAllContactsAsync();
                    var sb = new System.Text.StringBuilder("### Stark Industries Encrypted Contacts Directory:\n\n");
                    sb.AppendLine("| Clearance | Full Name | Call Sign | Role | Organization | Primary Comms | Primary Email |");
                    sb.AppendLine("|---|---|---|---|---|---|---|");
                    foreach (var c in all)
                    {
                        sb.AppendLine($"| `LVL {c.ClearanceLevel}` | **{c.FullName}** | `{c.CallSign}` | {c.Role} | {c.Organization} | `{c.PrimaryPhone}` | `{c.PrimaryEmail}` |");
                    }
                    sb.AppendLine($"\n*Showing {all.Count} authorized contacts across Stark Industries, S.H.I.E.L.D., and Avengers networks.*");
                    await StreamTextToCallerAsync(sb.ToString(), responseSb);
                }
            }
            // 23. PROJECTS, RESEARCH AGENTS & TASK AUTOMATION DIRECTIVE
            else if (lower == "projects" || lower == "list projects" || lower == "project status" || lower.StartsWith("run research agent") || lower.StartsWith("research agent") || lower.StartsWith("execute task ") || lower.StartsWith("auto task ") || lower.StartsWith("automate task "))
            {
                if (lower.StartsWith("run research agent") || lower.StartsWith("research agent"))
                {
                    var topic = raw.Replace("run research agent on", "", StringComparison.OrdinalIgnoreCase)
                                   .Replace("run research agent", "", StringComparison.OrdinalIgnoreCase)
                                   .Replace("research agent on", "", StringComparison.OrdinalIgnoreCase)
                                   .Replace("research agent", "", StringComparison.OrdinalIgnoreCase).Trim(' ', ':', '"');
                    if (string.IsNullOrWhiteSpace(topic)) topic = "Quantum Arc Reactor Stabilization & Sub-Atomic Flux Control";

                    await StreamTextToCallerAsync($"*Deploying Autonomous Stark Deep Research Agent for: \"{topic}\"...*\n\n", responseSb);

                    var allProjs = await _projectService.GetAllProjectsAsync();
                    var targetProj = allProjs.FirstOrDefault() ?? new JarvisProject { Id = "mark-85-armor", Name = "Mark LXXXV Nanotech Armor" };

                    var report = await _projectService.RunResearchAgentAsync(new ProjectResearchRequest
                    {
                        ProjectId = targetProj.Id,
                        ResearchTopic = topic,
                        SpecificDifficulty = "Deep engineering, material stress analysis, and tactical intelligence breakdown with Ghost Drive persistence"
                    });

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"### Autonomous Research Agent Completed, Sir!\n");
                    sb.AppendLine($"• **Target Project:** **{targetProj.Name}** (`{targetProj.Id}`)");
                    sb.AppendLine($"• **Research Topic:** `{topic}`");
                    sb.AppendLine($"• **Stored Ghost Drive Artifact:** `{targetProj.GhostDrivePath}/RESEARCH_*.md`");
                    sb.AppendLine($"• **Synthesis Summary:**\n{report}\n");
                    sb.AppendLine($"\n*Blueprint and synthesis data successfully archived to Ghost Drive under Projects/{targetProj.Name}/*");

                    await StreamTextToCallerAsync(sb.ToString(), responseSb);
                    await Clients.All.SendAsync("OnProjectUpdated", await _projectService.GetProjectByIdAsync(targetProj.Id));
                }
                else if (lower.StartsWith("execute task ") || lower.StartsWith("auto task ") || lower.StartsWith("automate task "))
                {
                    var taskIdOrTitle = raw.Replace("execute task", "", StringComparison.OrdinalIgnoreCase)
                                           .Replace("automate task", "", StringComparison.OrdinalIgnoreCase)
                                           .Replace("auto task", "", StringComparison.OrdinalIgnoreCase).Trim();
                    
                    var allProjs = await _projectService.GetAllProjectsAsync();
                    ProjectTaskItem? foundTask = null;
                    JarvisProject? parentProj = null;

                    foreach (var p in allProjs)
                    {
                        var t = p.Tasks.FirstOrDefault(x => x.Id.Equals(taskIdOrTitle, StringComparison.OrdinalIgnoreCase) || x.Title.Contains(taskIdOrTitle, StringComparison.OrdinalIgnoreCase));
                        if (t != null)
                        {
                            foundTask = t;
                            parentProj = p;
                            break;
                        }
                    }

                    if (foundTask != null && parentProj != null)
                    {
                        await StreamTextToCallerAsync($"*Autonomous LLM Agent engaged. Executing project task: **{foundTask.Title}**...*\n\n", responseSb);
                        var executed = await _projectService.ExecuteAutonomousTaskAsync(parentProj.Id, foundTask.Id);
                        
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"### Autonomous Task Execution Finalized, Sir:\n");
                        sb.AppendLine($"• **Project:** **{parentProj.Name}**");
                        sb.AppendLine($"• **Task:** **{executed.Title}**");
                        sb.AppendLine($"• **Status:** `{executed.Status}` (Progress: 100%)");
                        sb.AppendLine($"• **Assigned Agent:** `{executed.AssignedAgentName}`");
                        sb.AppendLine($"• **Autonomous Agent Output:**\n{executed.AutomationResult}");

                        await StreamTextToCallerAsync(sb.ToString(), responseSb);
                        await Clients.All.SendAsync("OnProjectUpdated", await _projectService.GetProjectByIdAsync(parentProj.Id));
                    }
                    else
                    {
                        await StreamTextToCallerAsync($"Unable to locate project task '{taskIdOrTitle}'. Displaying active projects matrix.", responseSb);
                    }
                }
                else
                {
                    var projs = await _projectService.GetAllProjectsAsync();
                    var sb = new System.Text.StringBuilder("### Stark Industries Strategic Projects & 3D Blueprints:\n\n");
                    sb.AppendLine("| Project Name | Category | Progress | Status | Priority | Tasks | Artifacts | Ghost Drive Path |");
                    sb.AppendLine("|---|---|---|---|---|---|---|---|");
                    foreach (var p in projs)
                    {
                        var completedCount = p.Tasks.Count(t => t.Status == "Completed");
                        sb.AppendLine($"| **{p.Name}** | `{p.Category}` | `{p.ProgressPercent:F0}%` | {p.Status} | {p.Priority} | {completedCount}/{p.Tasks.Count} | {p.Artifacts.Count} | `{p.GhostDrivePath}` |");
                    }
                    sb.AppendLine($"\n*Click **PROJECTS** in the top navigation or type `research agent [topic]` to run autonomous research.*");
                    await StreamTextToCallerAsync(sb.ToString(), responseSb);
                }
            }
            // 24. PROACTIVE MORNING EXECUTIVE BRIEFING DIRECTIVE
            else if (lower.Contains("morning briefing") || lower.Contains("executive briefing") || lower == "briefing" || lower == "daily brief" || lower.Contains("daily briefing"))
            {
                var briefingText = await _briefingService.GenerateMorningBriefingAsync();
                await StreamTextToCallerAsync(briefingText, responseSb);
            }
            // 25. SMART HOME & AMBIENT SCENES DIRECTIVE
            else if (lower.Contains("ambient scene") || lower.Contains("lab lights") || lower.StartsWith("set scene") || lower.Contains("turn on light") || lower.Contains("dim lights") || lower.StartsWith("iot "))
            {
                var sceneName = raw.Replace("set scene", "", StringComparison.OrdinalIgnoreCase)
                                   .Replace("ambient scene", "", StringComparison.OrdinalIgnoreCase)
                                   .Replace("lab lights", "", StringComparison.OrdinalIgnoreCase)
                                   .Replace("iot", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (string.IsNullOrWhiteSpace(sceneName)) sceneName = "Stark Laboratory Standard";
                var res = await _smartHomeService.SetAmbientSceneAsync(sceneName);
                await Clients.All.SendAsync("OnSmartHomeUpdated", await _smartHomeService.GetLabDevicesAsync());
                await StreamTextToCallerAsync(res, responseSb);
            }
            // 26. CODE SANDBOX EXECUTION DIRECTIVE
            else if (lower.StartsWith("sandbox ") || lower.StartsWith("run python ") || lower.StartsWith("run script ") || lower.StartsWith("execute script "))
            {
                var lang = "python";
                var code = raw;
                if (lower.StartsWith("run python "))
                {
                    lang = "python";
                    code = raw.Substring(11).Trim();
                }
                else if (lower.StartsWith("sandbox "))
                {
                    code = raw.Substring(8).Trim();
                }
                
                await Clients.Caller.SendAsync("OnReActStep", new { Step = "Code Sandbox", Status = "Executing", Language = lang });
                var res = await _codeSandboxService.ExecuteScriptAsync(lang, code);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"### Polyglot Code Sandbox Execution (`{res.Language}` | {res.ExecutionMs}ms):\n");
                if (res.Success)
                {
                    sb.AppendLine("```\n" + (string.IsNullOrWhiteSpace(res.Output) ? "(Script executed with return code 0)" : res.Output) + "\n```");
                }
                else
                {
                    sb.AppendLine("> [!WARNING]\n> **Execution Error:**\n```\n" + res.Error + "\n```");
                }
                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 27. AUTONOMOUS GIT & GITHUB DIRECTIVE
            else if (lower.StartsWith("git ") || lower == "git status" || lower == "git diff" || lower == "git log")
            {
                string gitOutput = "";
                if (lower.Contains("status")) gitOutput = await _gitOpsService.GetStatusAsync();
                else if (lower.Contains("diff")) gitOutput = await _gitOpsService.GetDiffAsync();
                else if (lower.Contains("log")) gitOutput = await _gitOpsService.GetRecentCommitsAsync(5);
                else if (lower.StartsWith("git commit"))
                {
                    var msg = raw.Replace("git commit", "", StringComparison.OrdinalIgnoreCase).Trim(' ', '"', '\'');
                    gitOutput = await _gitOpsService.CommitChangesAsync(msg);
                }
                else gitOutput = await _gitOpsService.GetStatusAsync();

                var sb = new System.Text.StringBuilder("### Stark Autonomous Git Sentinel:\n\n```\n" + gitOutput + "\n```");
                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 28. STARK VAULT HARDWARE ENCRYPTED SECRETS DIRECTIVE
            else if (lower.StartsWith("vault ") || lower == "vault" || lower.Contains("stark vault"))
            {
                var secrets = await _starkVaultService.ListSecretMetadataAsync();
                var sb = new System.Text.StringBuilder("### Stark Industries Hardware-Encrypted Vault (AES-256):\n\n");
                sb.AppendLine("| Key Identifier | Encryption Status | Last Modified |");
                sb.AppendLine("|---|---|---|");
                foreach (var s in secrets)
                {
                    sb.AppendLine($"| **{s.Key}** | `{s.Value}` | `{s.LastModified:yyyy-MM-dd HH:mm}` |");
                }
                sb.AppendLine("\n*Secrets are hardware-protected and restricted to Level 10 Passcode clearance.*");
                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 29. AUTOMATED DATABASE & GHOST DRIVE SNAPSHOTS DIRECTIVE
            else if (lower.Contains("create snapshot") || lower.Contains("backup database") || lower == "snapshots" || lower == "backup")
            {
                var snap = await _backupService.CreateSnapshotAsync("User Directed J.A.R.V.I.S. Snapshot");
                await Clients.All.SendAsync("OnSnapshotsUpdated", await _backupService.GetSnapshotsAsync());
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"### Recovery Snapshot Created, Sir:\n");
                sb.AppendLine($"• **Snapshot File:** `{snap.FileName}`");
                sb.AppendLine($"• **Archive Size:** `{snap.SizeBytes / 1024} KB`");
                sb.AppendLine($"• **Contains:** SQLite database (`jarvis_data.db`) & all Ghost Drive virtual partitions");
                sb.AppendLine($"• **Snapshot ID:** `{snap.Id}`");
                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 30. VECTOR RAG & SEMANTIC MEMORY SEARCH DIRECTIVE
            else if (lower.StartsWith("rag ") || lower.StartsWith("search memory ") || lower.StartsWith("semantic search "))
            {
                var q = raw.Replace("rag", "", StringComparison.OrdinalIgnoreCase)
                           .Replace("search memory", "", StringComparison.OrdinalIgnoreCase)
                           .Replace("semantic search", "", StringComparison.OrdinalIgnoreCase).Trim();
                var matches = await _vectorMemoryService.SearchSimilarAsync(q, 5);
                var sb = new System.Text.StringBuilder($"### Vector RAG Semantic Memory Matches for '{q}':\n\n");
                sb.AppendLine("| Relevance | Category | Snippet |");
                sb.AppendLine("|---|---|---|");
                foreach (var m in matches)
                {
                    sb.AppendLine($"| `{m.Score:P0}` | *{m.Category}* | {m.Content} |");
                }
                await StreamTextToCallerAsync(sb.ToString(), responseSb);
            }
            // 31. GENERAL RESILIENT LLM / ZERO-CLOUD STREAM WITH RE-ACT PULSES
            else
            {
                var (activeP, isFallback, fallbackReason) = await _llmFactory.GetResilientProviderAsync(request.OverrideProvider);
                IAsyncEnumerable<string> tokenStream;

                if (!isFallback)
                {
                    tokenStream = activeP.StreamResponseAsync(history, systemPrompt);
                }
                else if (activeP.ProviderName == "Ollama")
                {
                    await Clients.Caller.SendAsync("OnTokenReceived", "*[Zero-Cloud Local Ollama Engaged]*\n\n");
                    tokenStream = activeP.StreamResponseAsync(history, systemPrompt);
                }
                else
                {
                    var telemetry = _sensorService.GetCurrentTelemetry();
                    tokenStream = _llmFactory.StreamBuiltinJarvisSimulationAsync(request.Message, systemPrompt, telemetry);
                }

                await foreach (var token in tokenStream)
                {
                    responseSb.Append(token);
                    await Clients.Caller.SendAsync("OnTokenReceived", token);
                }
            }

            var fullText = responseSb.ToString();

            // Index assistant message into Vector RAG memory
            try
            {
                _ = _vectorMemoryService.IndexDocumentAsync($"asst_{Guid.NewGuid():N}", fullText, "JarvisSpeech");
            }
            catch { }

            // Handle optional speech synthesis
            string? audioBase64 = null;
            if (request.SpeakResponse && config.Voice.TtsEngine != "Browser")
            {
                var audioBytes = await _voiceService.SynthesizeSpeechAsync(fullText);
                if (audioBytes != null && audioBytes.Length > 0)
                {
                    audioBase64 = Convert.ToBase64String(audioBytes);
                }
            }

            var assistantMessage = new ChatMessage
            {
                Role = "assistant",
                Content = fullText,
                Provider = provider.ProviderName,
                Model = config.Providers.ActiveModel,
                IsVoiceGenerated = audioBase64 != null,
                AudioUrl = audioBase64 != null ? $"data:audio/mp3;base64,{audioBase64}" : null
            };

            await Clients.Caller.SendAsync("OnStreamComplete", assistantMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat stream");
            await Clients.Caller.SendAsync("OnStreamError", $"[Jarvis Core Error]: {ex.Message}");
        }
    }

    private async Task StreamTextToCallerAsync(string text, System.Text.StringBuilder sb)
    {
        if (string.IsNullOrEmpty(text)) text = "Awaiting input, Sir.";
        var words = text.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            var w = words[i] + (i < words.Length - 1 ? " " : "");
            sb.Append(w);
            await Clients.Caller.SendAsync("OnTokenReceived", w);
            await Task.Delay(14);
        }
    }

    private static string ExtractWeatherCity(string message)
    {
        var trimmed = message.Trim().Trim('?', '.', '!', ';');
        var lower = trimmed.ToLowerInvariant();

        var prepositions = new[] { " in ", " at ", " for ", " of ", " around " };
        foreach (var prep in prepositions)
        {
            var idx = lower.IndexOf(prep);
            if (idx >= 0 && idx + prep.Length < trimmed.Length)
            {
                var candidate = trimmed[(idx + prep.Length)..].Trim();
                candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"\b(tomorrow|today|this week|now|please)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim('?', ' ', '.', '!');
                if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length > 1) return candidate;
            }
        }

        var prefixes = new[] { "weather ", "forecast ", "temperature ", "temp ", "atmospheric scan for ", "atmospheric " };
        foreach (var prefix in prefixes)
        {
            if (lower.StartsWith(prefix))
            {
                var candidate = trimmed[prefix.Length..].Trim();
                candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"\b(tomorrow|today|this week|now|please)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim('?', ' ', '.', '!');
                if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length > 1) return candidate;
            }
        }

        var suffixes = new[] { " weather", " forecast", " temperature" };
        foreach (var suffix in suffixes)
        {
            if (lower.EndsWith(suffix))
            {
                var candidate = trimmed[..^suffix.Length].Trim();
                candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"\b(tomorrow|today|this week|now|please)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim('?', ' ', '.', '!');
                if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length > 1) return candidate;
            }
        }

        return "New York";
    }

    private static string ExtractNewsTopic(string message)
    {
        var lower = message.ToLowerInvariant().Trim();
        if (lower.Contains("about "))
        {
            var idx = lower.IndexOf("about ");
            return message[(idx + 6)..].Trim('?', '.', '!');
        }
        if (lower.Contains("on "))
        {
            var idx = lower.IndexOf("on ");
            return message[(idx + 3)..].Trim('?', '.', '!');
        }
        return "technology news";
    }

    private static string ExtractAfterPhrase(string text, string phrase)
    {
        var idx = text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0 && idx + phrase.Length < text.Length)
        {
            return text[(idx + phrase.Length)..].Trim('?', '.', '!', ' ');
        }
        return "";
    }

    private static string GenerateProjectSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "project-" + Guid.NewGuid().ToString("N")[..8];
        var clean = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9\s-]", "");
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(clean) ? "project-" + Guid.NewGuid().ToString("N")[..8] : clean;
    }

    private static string ExtractContactAddress(ContactItem contact)
    {
        if (!string.IsNullOrWhiteSpace(contact.MetadataJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(contact.MetadataJson);
                if (doc.RootElement.TryGetProperty("address", out var addrProp) && !string.IsNullOrWhiteSpace(addrProp.GetString()))
                {
                    return addrProp.GetString()!;
                }
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(contact.Notes))
        {
            var match = System.Text.RegularExpressions.Regex.Match(contact.Notes, @"(?:HQ|Stationed at|Address|Location)[:\s]+([^.\n]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Trim();
        }

        if (!string.IsNullOrWhiteSpace(contact.Organization))
        {
            if (contact.Organization.Contains("S.H.I.E.L.D.", StringComparison.OrdinalIgnoreCase))
                return "S.H.I.E.L.D. Triskelion Command & Helicarrier Alpha, Potomac River, Washington D.C. 20001";
            if (contact.Organization.Contains("Stark", StringComparison.OrdinalIgnoreCase))
                return "Stark Tower, 890 Fifth Avenue, Manhattan, New York, NY 10021";
            if (contact.Organization.Contains("Avengers", StringComparison.OrdinalIgnoreCase))
                return "Avengers Compound, Upstate New York, NY 10994";
        }

        return "Classified Tactical Facility / Coordinates Encrypted";
    }

    public async Task ExecuteTool(ToolExecutionRequest request)
    {
        var result = await _toolService.ExecuteToolAsync(request);
        await Clients.Caller.SendAsync("OnToolExecuted", result);
    }

    public void ReportAudioFft(double inputRms, double outputRms)
    {
        _sensorService.SetAudioRms(inputRms, outputRms);
    }

    public async Task DismissAppointment(string id)
    {
        await _calendarService.DismissAppointmentAsync(id);
        await Clients.All.SendAsync("OnAppointmentDismissed", id);
    }

    public async Task CancelAppointment(string id)
    {
        await _calendarService.CancelAppointmentAsync(id);
        await Clients.All.SendAsync("OnAppointmentCancelled", id);
    }

    public async Task CreateAppointment(CalendarAppointment appointment)
    {
        var created = await _calendarService.CreateAppointmentAsync(appointment);
        await Clients.All.SendAsync("OnCalendarUpdated", created);
    }

    public async Task CreateScheduledTask(ScheduledTaskItem task)
    {
        var created = await _schedulerService.CreateTaskAsync(task);
        await Clients.All.SendAsync("OnScheduledTaskUpdated", created);
    }

    public async Task<string> ExecuteTaskNow(string id)
    {
        return await _schedulerService.ExecuteTaskNowAsync(id);
    }

    public async Task<List<DriveTelemetry>> GetStorageTelemetry()
    {
        return await _ghostDriveService.GetStorageTelemetryAsync();
    }

    public async Task<List<GhostDriveInfo>> GetGhostDrives()
    {
        return await _ghostDriveService.GetAllGhostDrivesAsync();
    }

    public async Task<GhostDriveInfo> CreateGhostDrive(GhostDriveInfo drive)
    {
        var created = await _ghostDriveService.CreateGhostDriveAsync(drive);
        await Clients.All.SendAsync("OnGhostDrivesUpdated", await _ghostDriveService.GetAllGhostDrivesAsync());
        return created;
    }

    public async Task<bool> DeleteGhostDrive(string id)
    {
        var ok = await _ghostDriveService.DeleteGhostDriveAsync(id);
        await Clients.All.SendAsync("OnGhostDrivesUpdated", await _ghostDriveService.GetAllGhostDrivesAsync());
        return ok;
    }

    public async Task<List<FileItemInfo>> ListGhostDriveFiles(string pathOrId, string? subfolder = null)
    {
        return await _ghostDriveService.ListFilesAsync(pathOrId, subfolder);
    }

    public async Task<string> ReadGhostDriveFile(string pathOrId, string? relativePath = null)
    {
        return await _ghostDriveService.ReadFileContentAsync(pathOrId, relativePath);
    }

    public async Task<bool> WriteGhostDriveFile(string pathOrId, string relativePath, string content)
    {
        return await _ghostDriveService.WriteFileContentAsync(pathOrId, relativePath, content);
    }

    // ==================== CONTACTS RPC DUPLICATE PIPELINE ====================
    public async Task<List<ContactItem>> GetContacts(string? search = null, string? tag = null)
    {
        return await _contactService.GetAllContactsAsync(search, tag);
    }

    public async Task<ContactItem> CreateContact(ContactItem contact)
    {
        var created = await _contactService.CreateContactAsync(contact);
        await Clients.All.SendAsync("OnContactUpdated", created);
        return created;
    }

    public async Task<ContactItem> UpdateContact(ContactItem contact)
    {
        var updated = await _contactService.UpdateContactAsync(contact);
        await Clients.All.SendAsync("OnContactUpdated", updated);
        return updated;
    }

    public async Task<bool> DeleteContact(string id)
    {
        var ok = await _contactService.DeleteContactAsync(id);
        if (ok)
        {
            await Clients.All.SendAsync("OnContactDeleted", id);
        }
        return ok;
    }

    // ==================== PROJECTS & AUTONOMOUS AGENTS RPC PIPELINE ====================
    public async Task<List<JarvisProject>> GetProjects()
    {
        return await _projectService.GetAllProjectsAsync();
    }

    public async Task<JarvisProject?> GetProject(string id)
    {
        return await _projectService.GetProjectByIdAsync(id);
    }

    public async Task<JarvisProject> CreateProject(JarvisProject project)
    {
        var created = await _projectService.CreateProjectAsync(project);
        await Clients.All.SendAsync("OnProjectUpdated", created);
        return created;
    }

    public async Task<JarvisProject> UpdateProject(JarvisProject project)
    {
        var updated = await _projectService.UpdateProjectAsync(project);
        await Clients.All.SendAsync("OnProjectUpdated", updated);
        return updated;
    }

    public async Task<bool> DeleteProject(string id)
    {
        var ok = await _projectService.DeleteProjectAsync(id);
        if (ok)
        {
            await Clients.All.SendAsync("OnProjectDeleted", id);
        }
        return ok;
    }

    public async Task<ProjectTaskItem> ExecuteProjectTask(string projectId, string taskId)
    {
        var res = await _projectService.ExecuteAutonomousTaskAsync(projectId, taskId);
        var proj = await _projectService.GetProjectByIdAsync(projectId);
        if (proj != null)
        {
            await Clients.All.SendAsync("OnProjectUpdated", proj);
        }
        return res;
    }

    public async Task<string> RunProjectResearch(ProjectResearchRequest request)
    {
        var res = await _projectService.RunResearchAgentAsync(request);
        var proj = await _projectService.GetProjectByIdAsync(request.ProjectId);
        if (proj != null)
        {
            await Clients.All.SendAsync("OnProjectUpdated", proj);
        }
        return res;
    }

    public async Task<List<ProjectArtifactItem>> ListProjectFiles(string projectId)
    {
        return await _projectService.GetProjectFilesAsync(projectId);
    }

    public async Task<bool> SaveProjectFile(string projectId, string fileName, string content)
    {
        return await _projectService.SaveProjectFileAsync(projectId, fileName, content);
    }

    public async Task<string> ReadProjectFile(string projectId, string fileName)
    {
        return await _projectService.ReadProjectFileAsync(projectId, fileName);
    }

    // ==================== NEW 20-FEATURE EXTENSION RPC PIPELINE ====================
    public async Task<SandboxExecutionResult> ExecuteSandboxScript(string language, string code)
    {
        return await _codeSandboxService.ExecuteScriptAsync(language, code);
    }

    public async Task<string> SetAmbientScene(string scene, int brightness = 100)
    {
        var msg = await _smartHomeService.SetAmbientSceneAsync(scene, brightness);
        await Clients.All.SendAsync("OnSmartHomeUpdated", await _smartHomeService.GetLabDevicesAsync());
        return msg;
    }

    public async Task<string> GetMorningBriefing()
    {
        return await _briefingService.GenerateMorningBriefingAsync();
    }

    public async Task<SnapshotItem> CreateBackupSnapshot(string? description)
    {
        var snap = await _backupService.CreateSnapshotAsync(description ?? "Manual HUD Snapshot");
        await Clients.All.SendAsync("OnSnapshotsUpdated", await _backupService.GetSnapshotsAsync());
        return snap;
    }

    public async Task<bool> RestoreBackupSnapshot(string snapshotId)
    {
        return await _backupService.RestoreSnapshotAsync(snapshotId);
    }

    public async Task<bool> AuthorizeSecurityChallenge(string challengeToken, string passcode)
    {
        var valid = _sentinelService.VerifyChallenge(challengeToken, passcode);
        if (valid)
        {
            await Clients.Caller.SendAsync("OnSecurityChallengeApproved", challengeToken);
        }
        return valid;
    }

    public async Task InterruptSpeech()
    {
        await Clients.All.SendAsync("OnSpeechInterrupted");
    }

    public async Task<List<VectorMemoryItem>> SearchSemanticMemory(string query)
    {
        return await _vectorMemoryService.SearchSimilarAsync(query, 5);
    }

    public async Task<SwarmExecutionReport> DeployAgentSwarm(string goal)
    {
        var swarm = _serviceProvider.GetRequiredService<IAgentSwarmService>();
        return await swarm.ExecuteSwarmDirectiveAsync(goal, step =>
        {
            Clients.Caller.SendAsync("OnSwarmStep", step);
        });
    }

    public async Task<TerminalExecResult> ExecuteInteractiveTerminal(string command)
    {
        var term = _serviceProvider.GetRequiredService<IInteractiveTerminalService>();
        return await term.ExecuteTerminalCommandAsync(command);
    }

    public WindowContext GetActiveWindowContext()
    {
        var tracker = _serviceProvider.GetRequiredService<IActiveWindowTrackerService>();
        return tracker.GetCurrentForegroundContext();
    }

    public ClipboardAnalysisResult AnalyzeClipboard(string text)
    {
        var clip = _serviceProvider.GetRequiredService<IClipboardSentinelService>();
        return clip.AnalyzeSnippet(text);
    }

    public async Task<List<DockerContainerInfo>> GetDockerContainers()
    {
        var docker = _serviceProvider.GetRequiredService<IDockerManagerService>();
        return await docker.GetContainersAsync();
    }

    public async Task<List<NetworkDevice>> ScanNetworkPerimeter()
    {
        var radar = _serviceProvider.GetRequiredService<INetworkRadarService>();
        return await radar.ScanArpTableAsync();
    }

    public List<StarkAchievement> GetAchievements()
    {
        var ach = _serviceProvider.GetRequiredService<IAchievementService>();
        return ach.GetAchievements();
    }

    public StarkAchievement? UnlockAchievement(string id)
    {
        var ach = _serviceProvider.GetRequiredService<IAchievementService>();
        var result = ach.UnlockAchievement(id);
        if (result != null && result.Unlocked)
        {
            Clients.All.SendAsync("OnAchievementUnlocked", result);
        }
        return result;
    }
}

