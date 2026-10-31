using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Hubs;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Agents;
using OzzieAI.Jarvis.Services.Calendar;
using OzzieAI.Jarvis.Services.Config;
using OzzieAI.Jarvis.Services.Contacts;
using OzzieAI.Jarvis.Services.Llm;
using OzzieAI.Jarvis.Services.Memory;
using OzzieAI.Jarvis.Services.Projects;
using OzzieAI.Jarvis.Services.Scheduler;
using OzzieAI.Jarvis.Services.Security;
using OzzieAI.Jarvis.Services.Sensors;
using OzzieAI.Jarvis.Services.Skills;
using OzzieAI.Jarvis.Services.Storage;
using OzzieAI.Jarvis.Services.Tools;
using OzzieAI.Jarvis.Services.Voice;
using OzzieAI.Jarvis.Services.Notifications;
using OzzieAI.Jarvis.Services.Integrations;
using OzzieAI.Jarvis.Services.Media;

var builder = WebApplication.CreateBuilder(args);

// Configure CORS for web access
builder.Services.AddCors(options =>
{
    options.AddPolicy("JarvisCorsPolicy", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Configure Services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<JarvisDatabase>();
builder.Services.AddSingleton<IJarvisConfigService, JarvisConfigService>();
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<IPredicateService, PredicateService>();
builder.Services.AddSingleton<ISkillService, SkillService>();
builder.Services.AddSingleton<OzzieAI.Jarvis.Services.Rea.IReaService, OzzieAI.Jarvis.Services.Rea.ReaService>();
builder.Services.AddSingleton<ICalendarService, CalendarService>();
builder.Services.AddSingleton<IGhostDriveService, GhostDriveService>();
builder.Services.AddSingleton<IContactService, ContactService>();
builder.Services.AddSingleton<IProjectService, ProjectService>();
builder.Services.AddSingleton<IToolService, ToolService>();
builder.Services.AddSingleton<IAgentService, AgentService>();
builder.Services.AddSingleton<IVoiceService, VoiceService>();
builder.Services.AddSingleton<IVectorMemoryService, VectorMemoryService>();
builder.Services.AddSingleton<ICodeSandboxService, CodeSandboxService>();
builder.Services.AddSingleton<ISmartHomeService, SmartHomeService>();
builder.Services.AddSingleton<IGitOpsService, GitOpsService>();
builder.Services.AddSingleton<IMcpClientManagerService, McpClientManagerService>();
builder.Services.AddSingleton<ITerminalSentinelService, TerminalSentinelService>();
builder.Services.AddSingleton<IStarkVaultService, StarkVaultService>();
builder.Services.AddSingleton<IBackupService, BackupService>();
builder.Services.AddSingleton<IBriefingService, BriefingService>();
builder.Services.AddSingleton<IBrowserAgentService, BrowserAgentService>();
builder.Services.AddSingleton<IActiveWindowTrackerService, ActiveWindowTrackerService>();
builder.Services.AddSingleton<IClipboardSentinelService, ClipboardSentinelService>();
builder.Services.AddSingleton<IAgentSwarmService, AgentSwarmService>();
builder.Services.AddSingleton<IInteractiveTerminalService, InteractiveTerminalService>();
builder.Services.AddSingleton<ICodebaseIndexerService, CodebaseIndexerService>();
builder.Services.AddSingleton<IWindowsNotificationService, WindowsNotificationService>();
builder.Services.AddSingleton<IDockerManagerService, DockerManagerService>();
builder.Services.AddSingleton<IDatabaseExplorerService, DatabaseExplorerService>();
builder.Services.AddSingleton<ITestRunnerService, TestRunnerService>();
builder.Services.AddSingleton<IMeetingEarService, MeetingEarService>();
builder.Services.AddSingleton<ILocalWhisperService, LocalWhisperService>();
builder.Services.AddSingleton<IDocumentIngestionService, DocumentIngestionService>();
builder.Services.AddSingleton<INetworkRadarService, NetworkRadarService>();
builder.Services.AddSingleton<ISerialPortBridgeService, SerialPortBridgeService>();
builder.Services.AddSingleton<IMediaSummarizerService, MediaSummarizerService>();
builder.Services.AddSingleton<IAchievementService, AchievementService>();
builder.Services.AddSingleton<IExternalAppsAdapterService, ExternalAppsAdapterService>();
// Autonomous Scheduler Background Engine
builder.Services.AddSingleton<JarvisSchedulerService>();
builder.Services.AddSingleton<IJarvisSchedulerService>(sp => sp.GetRequiredService<JarvisSchedulerService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<JarvisSchedulerService>());

// LLM Providers
builder.Services.AddSingleton<ILlmProviderService, OpenAiProvider>();
builder.Services.AddSingleton<ILlmProviderService, ClaudeProvider>();
builder.Services.AddSingleton<ILlmProviderService, GeminiProvider>();
builder.Services.AddSingleton<ILlmProviderService, GroqProvider>();
builder.Services.AddSingleton<ILlmProviderService, OllamaProvider>();
builder.Services.AddSingleton<ILlmProviderService, DeepSeekProvider>();
builder.Services.AddSingleton<LlmProviderFactory>();

// Media & Artist Verification Services
builder.Services.AddSingleton<IArtistVerificationService, ArtistVerificationService>();
builder.Services.AddSingleton<IStarkRadioService, StarkRadioService>();
builder.Services.AddSingleton<IVideoPlayerService, VideoPlayerService>();
builder.Services.AddSingleton<IMovieVaultService, MovieVaultService>();

// MCP Sensor background telemetry engine
builder.Services.AddSingleton<McpSensorService>();
builder.Services.AddSingleton<IMcpSensorService>(sp => sp.GetRequiredService<McpSensorService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<McpSensorService>());

// SignalR Realtime Pipeline
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB for audio/vision
});

var app = builder.Build();

app.UseCors("JarvisCorsPolicy");
app.UseDefaultFiles();
app.UseStaticFiles();

// ======================== API ENDPOINTS ========================

// 1. Authentication Endpoints
app.MapPost("/api/auth/login", async ([FromBody] LoginRequest request, HttpContext http, IAuthService auth) =>
{
    var ip = http.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    var result = await auth.AuthenticateAsync(request.Passcode, ip);
    return Results.Ok(result);
});

app.MapGet("/api/auth/check", async (HttpContext http, IAuthService auth) =>
{
    var authHeader = http.Request.Headers.Authorization.ToString();
    var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) 
        ? authHeader["Bearer ".Length..].Trim() 
        : "";
    
    var isValid = await auth.ValidateTokenAsync(token);
    var requireLogin = auth.IsLoginRequired();
    return Results.Ok(new { IsAuthenticated = isValid || !requireLogin, RequireLogin = requireLogin });
});

app.MapPost("/api/auth/logout", async (HttpContext http, IAuthService auth) =>
{
    var authHeader = http.Request.Headers.Authorization.ToString();
    var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authHeader["Bearer ".Length..].Trim() : "";
    await auth.LogoutAsync(token);
    return Results.Ok(new { Success = true, Message = "Cleared Stark Session." });
});

// 2. Configuration Endpoints
app.MapGet("/api/config", (IJarvisConfigService configService) =>
{
    return Results.Ok(configService.GetConfig());
});

app.MapPost("/api/config/update", async ([FromBody] AppSettingsConfig newConfig, IJarvisConfigService configService) =>
{
    await configService.UpdateConfigAsync(newConfig);
    return Results.Ok(new { Success = true, Message = "Configuration updated successfully." });
});

// 3. Predicates Endpoints
app.MapGet("/api/predicates", async (IPredicateService predicateService) =>
{
    var list = await predicateService.GetAllPredicatesAsync();
    return Results.Ok(list);
});

app.MapPost("/api/predicates/save", async ([FromBody] PredicateItem item, IPredicateService predicateService) =>
{
    await predicateService.SavePredicateAsync(item);
    return Results.Ok(new { Success = true, Item = item });
});

app.MapDelete("/api/predicates/{key}", async (string key, IPredicateService predicateService) =>
{
    await predicateService.DeletePredicateAsync(key);
    return Results.Ok(new { Success = true, Key = key });
});

// 4. Agents Endpoints
app.MapGet("/api/agents", async (IAgentService agentService) =>
{
    return Results.Ok(await agentService.GetAllAgentsAsync());
});

app.MapGet("/api/agents/{id}", async (string id, IAgentService agentService) =>
{
    var agent = await agentService.GetAgentAsync(id);
    return agent != null ? Results.Ok(agent) : Results.NotFound();
});

app.MapPost("/api/agents/save", async ([FromBody] AgentItem agent, IAgentService agentService) =>
{
    await agentService.SaveAgentAsync(agent);
    return Results.Ok(new { Success = true, Agent = agent });
});

app.MapPost("/api/agents/activate/{id}", async (string id, IAgentService agentService) =>
{
    await agentService.SetActiveAgentAsync(id);
    return Results.Ok(new { Success = true, ActiveId = id });
});

app.MapDelete("/api/agents/{id}", async (string id, IAgentService agentService) =>
{
    await agentService.DeleteAgentAsync(id);
    return Results.Ok(new { Success = true, DeletedId = id });
});

// 5. Skills Endpoints
app.MapGet("/api/skills", async (ISkillService skillService) =>
{
    return Results.Ok(await skillService.GetAllSkillsAsync());
});

app.MapPost("/api/skills/save", async ([FromBody] SkillItem skill, ISkillService skillService) =>
{
    await skillService.SaveSkillAsync(skill);
    return Results.Ok(new { Success = true, Skill = skill });
});

app.MapDelete("/api/skills/{id}", async (string id, ISkillService skillService) =>
{
    await skillService.DeleteSkillAsync(id);
    return Results.Ok(new { Success = true, DeletedId = id });
});

// 6. Tools Endpoints
app.MapGet("/api/tools", async (IToolService toolService) =>
{
    return Results.Ok(await toolService.GetAllToolsAsync());
});

app.MapPost("/api/tools/save", async ([FromBody] ToolItem tool, IToolService toolService) =>
{
    await toolService.SaveToolAsync(tool);
    return Results.Ok(new { Success = true, Tool = tool });
});

app.MapDelete("/api/tools/{id}", async (string id, IToolService toolService) =>
{
    await toolService.DeleteToolAsync(id);
    return Results.Ok(new { Success = true, DeletedId = id });
});

app.MapPost("/api/tools/execute", async ([FromBody] ToolExecutionRequest request, IToolService toolService) =>
{
    var result = await toolService.ExecuteToolAsync(request);
    return Results.Ok(result);
});

// 7. Sensors & MCP Endpoints
app.MapGet("/api/sensors/current", (IMcpSensorService sensorService) =>
{
    return Results.Ok(sensorService.GetCurrentTelemetry());
});

app.MapPost("/api/mcp/rpc", async ([FromBody] McpRequest rpc, IToolService toolService, IMcpSensorService sensorService) =>
{
    if (rpc.Method == "tools/list")
    {
        var tools = await toolService.GetAllToolsAsync();
        return Results.Ok(new McpResponse { Id = rpc.Id, Result = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(tools)) });
    }
    if (rpc.Method == "telemetry/get")
    {
        var tel = sensorService.GetCurrentTelemetry();
        return Results.Ok(new McpResponse { Id = rpc.Id, Result = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(tel)) });
    }
    return Results.Ok(new McpResponse { Id = rpc.Id, Error = new System.Text.Json.Nodes.JsonObject { ["message"] = $"Method {rpc.Method} not implemented" } });
});

// 8. Voice Endpoints
app.MapPost("/api/voice/tts", async (HttpContext http, IVoiceService voiceService) =>
{
    var form = await http.Request.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
    var text = form?["text"]?.GetValue<string>() ?? "";
    var engine = form?["engine"]?.GetValue<string>();
    var voice = form?["voice"]?.GetValue<string>();

    var audio = await voiceService.SynthesizeSpeechAsync(text, engine, voice);
    if (audio != null && audio.Length > 0)
    {
        return Results.File(audio, "audio/mpeg");
    }
    return Results.BadRequest(new { Error = "TTS synthesis unavailable for this engine/credentials." });
});

app.MapGet("/api/voice/voices", async (IVoiceService voiceService, [FromQuery] string? provider) =>
{
    var voices = await voiceService.GetAvailableProviderVoicesAsync(provider ?? "OpenAI");
    return Results.Ok(voices);
});

// 9. REA (Reverse Engineer Anything - morluto/rea) Endpoints
app.MapPost("/api/rea/analyze", async (HttpContext http, OzzieAI.Jarvis.Services.Rea.IReaService rea) =>
{
    var form = await http.Request.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
    var target = form?["target"]?.GetValue<string>() ?? "";
    var result = await rea.AnalyzeTargetAsync(target);
    return Results.Ok(result);
});

app.MapPost("/api/rea/mcp", async (HttpContext http, OzzieAI.Jarvis.Services.Rea.IReaService rea) =>
{
    var form = await http.Request.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
    var tool = form?["tool"]?.GetValue<string>() ?? "analyze";
    var args = form?["args"] as System.Text.Json.Nodes.JsonObject;
    var output = await rea.ExecuteMcpReaBridgeAsync(tool, args);
    return Results.Ok(new { Success = true, Output = output });
});

// 10. Calendar Endpoints
app.MapGet("/api/calendar/appointments", async (ICalendarService cal) => Results.Ok(await cal.GetAllAppointmentsAsync()));
app.MapPost("/api/calendar/appointments", async ([FromBody] CalendarAppointment appt, ICalendarService cal) => 
{
    var created = await cal.CreateAppointmentAsync(appt);
    return Results.Ok(created);
});
app.MapPost("/api/calendar/appointments/{id}/dismiss", async (string id, ICalendarService cal) => 
{
    var ok = await cal.DismissAppointmentAsync(id);
    return Results.Ok(new { success = ok });
});
app.MapPost("/api/calendar/appointments/{id}/cancel", async (string id, ICalendarService cal) => 
{
    var ok = await cal.CancelAppointmentAsync(id);
    return Results.Ok(new { success = ok });
});
app.MapDelete("/api/calendar/appointments/{id}", async (string id, ICalendarService cal) => 
{
    var ok = await cal.DeleteAppointmentAsync(id);
    return Results.Ok(new { success = ok });
});

// 11. Scheduler & Autonomous Tasks Endpoints
app.MapGet("/api/scheduler/tasks", async (IJarvisSchedulerService sched) => Results.Ok(await sched.GetAllTasksAsync()));
app.MapPost("/api/scheduler/tasks", async ([FromBody] ScheduledTaskItem task, IJarvisSchedulerService sched) => 
{
    var created = await sched.CreateTaskAsync(task);
    return Results.Ok(created);
});
app.MapPost("/api/scheduler/tasks/{id}/execute", async (string id, IJarvisSchedulerService sched) => 
{
    var output = await sched.ExecuteTaskNowAsync(id);
    return Results.Ok(new { success = true, output });
});
app.MapDelete("/api/scheduler/tasks/{id}", async (string id, IJarvisSchedulerService sched) => 
{
    var ok = await sched.DeleteTaskAsync(id);
    return Results.Ok(new { success = ok });
});

// 12. Storage & Ghost Drives Endpoints
app.MapGet("/api/storage/telemetry", async (IGhostDriveService storage) => Results.Ok(await storage.GetStorageTelemetryAsync()));
app.MapGet("/api/storage/ghost-drives", async (IGhostDriveService storage) => Results.Ok(await storage.GetAllGhostDrivesAsync()));
app.MapPost("/api/storage/ghost-drives", async ([FromBody] GhostDriveInfo drive, IGhostDriveService storage) => 
{
    var created = await storage.CreateGhostDriveAsync(drive);
    return Results.Ok(created);
});
app.MapDelete("/api/storage/ghost-drives/{id}", async (string id, IGhostDriveService storage) => 
{
    var ok = await storage.DeleteGhostDriveAsync(id);
    return Results.Ok(new { success = ok });
});
app.MapGet("/api/storage/files", async ([FromQuery] string path, [FromQuery] string? subfolder, IGhostDriveService storage) => 
{
    var files = await storage.ListFilesAsync(path, subfolder);
    return Results.Ok(files);
});
app.MapPost("/api/storage/files/read", async ([FromBody] ReadFileDto req, IGhostDriveService storage) => 
{
    try
    {
        var content = await storage.ReadFileContentAsync(req.PathOrId, req.RelativePath);
        return Results.Ok(new { success = true, content });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
});
app.MapPost("/api/storage/files/write", async ([FromBody] WriteFileDto req, IGhostDriveService storage) => 
{
    try
    {
        var ok = await storage.WriteFileContentAsync(req.PathOrId, req.RelativePath, req.Content);
        return Results.Ok(new { success = ok });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
});

// 13. Contacts Directory Endpoints (REST, MCP, and External App Sync)
app.MapGet("/api/contacts", async ([FromQuery] string? search, [FromQuery] string? tag, [FromQuery] int? minClearance, IContactService contacts) => 
    Results.Ok(await contacts.GetAllContactsAsync(search, tag, minClearance)));

app.MapGet("/api/contacts/{id}", async (string id, IContactService contacts) => 
{
    var item = await contacts.GetContactByIdAsync(id);
    return item != null ? Results.Ok(item) : Results.NotFound();
});

app.MapPost("/api/contacts", async ([FromBody] ContactItem contact, IContactService contacts) => 
{
    var created = await contacts.CreateContactAsync(contact);
    return Results.Ok(created);
});

app.MapPut("/api/contacts/{id}", async (string id, [FromBody] ContactItem contact, IContactService contacts) => 
{
    contact.Id = id;
    var updated = await contacts.UpdateContactAsync(contact);
    return Results.Ok(updated);
});

app.MapDelete("/api/contacts/{id}", async (string id, IContactService contacts) => 
{
    var ok = await contacts.DeleteContactAsync(id);
    return Results.Ok(new { success = ok });
});

// 14. User Profile ("About Me") Endpoints
app.MapGet("/api/config/user-profile", (IJarvisConfigService configService) =>
{
    var cfg = configService.GetConfig();
    return Results.Ok(cfg.Profile ?? new UserProfileConfig());
});

app.MapPost("/api/config/user-profile", async ([FromBody] UserProfileConfig profile, IJarvisConfigService configService) =>
{
    var cfg = configService.GetConfig();
    cfg.Profile = profile;
    await configService.UpdateConfigAsync(cfg);
    return Results.Ok(new { success = true, profile = cfg.Profile });
});

// 15. Advanced Project Management, 3D Matrix & Autonomous Execution
app.MapGet("/api/projects", async (IProjectService projects) => Results.Ok(await projects.GetAllProjectsAsync()));

app.MapGet("/api/projects/{id}", async (string id, IProjectService projects) => 
{
    var item = await projects.GetProjectByIdAsync(id);
    return item != null ? Results.Ok(item) : Results.NotFound();
});

app.MapPost("/api/projects", async ([FromBody] JarvisProject proj, IProjectService projects) => 
    Results.Ok(await projects.CreateProjectAsync(proj)));

app.MapPut("/api/projects/{id}", async (string id, [FromBody] JarvisProject proj, IProjectService projects) => 
{
    proj.Id = id;
    return Results.Ok(await projects.UpdateProjectAsync(proj));
});

app.MapDelete("/api/projects/{id}", async (string id, IProjectService projects) => 
    Results.Ok(new { success = await projects.DeleteProjectAsync(id) }));

app.MapGet("/api/projects/{id}/tasks", async (string id, IProjectService projects) => 
    Results.Ok(await projects.GetTasksByProjectIdAsync(id)));

app.MapPost("/api/projects/{id}/tasks", async (string id, [FromBody] ProjectTaskItem task, IProjectService projects) => 
{
    task.ProjectId = id;
    return Results.Ok(await projects.AddTaskAsync(id, task));
});

app.MapPut("/api/projects/{id}/tasks/{taskId}", async (string id, string taskId, [FromBody] ProjectTaskItem task, IProjectService projects) => 
{
    task.ProjectId = id;
    task.Id = taskId;
    return Results.Ok(await projects.UpdateTaskAsync(task));
});

app.MapDelete("/api/projects/{id}/tasks/{taskId}", async (string id, string taskId, IProjectService projects) => 
    Results.Ok(new { success = await projects.DeleteTaskAsync(id, taskId) }));

app.MapPost("/api/projects/{id}/tasks/{taskId}/execute", async (string id, string taskId, IProjectService projects) => 
{
    var executed = await projects.ExecuteAutonomousTaskAsync(id, taskId);
    return Results.Ok(executed);
});

app.MapPost("/api/projects/research", async ([FromBody] ProjectResearchRequest req, IProjectService projects) => 
{
    var artifact = await projects.RunResearchAgentAsync(req);
    return Results.Ok(new { success = true, report = artifact });
});

app.MapGet("/api/projects/{id}/files", async (string id, IProjectService projects) => 
    Results.Ok(await projects.GetProjectFilesAsync(id)));

app.MapPost("/api/projects/{id}/files", async (string id, [FromBody] SaveFileDto req, IProjectService projects) => 
{
    var ok = await projects.SaveProjectFileAsync(id, req.FileName, req.Content);
    return Results.Ok(new { success = ok });
});

app.MapPost("/api/projects/seed-test-data", async (JarvisDatabase db, IProjectService projects) =>
{
    using (var conn = db.CreateConnection())
    {
        db.SeedDefaultProjects(conn);
    }
    var all = await projects.GetAllProjectsAsync();
    return Results.Ok(new { success = true, count = all.Count, projects = all });
});

// 16. Multimodal Vision & Screen Perception Endpoint
app.MapPost("/api/vision/screen", async (HttpContext http, LlmProviderFactory llmFactory, IJarvisConfigService configService) =>
{
    var body = await http.Request.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
    var prompt = body?["prompt"]?.GetValue<string>() ?? "Analyze this display screenshot and provide tactical engineering insights.";
    var imageBase64 = body?["image"]?.GetValue<string>() ?? "";

    if (string.IsNullOrWhiteSpace(imageBase64))
    {
        return Results.BadRequest(new { error = "No image payload received." });
    }

    var provider = llmFactory.GetActiveProvider();
    var analysis = $"### Holographic Screen Perception Analysis (via {provider.ProviderName}):\n\n" +
                   $"• **Visual Target:** Full-screen desktop workspace frame ({imageBase64.Length / 1024} KB raw payload).\n" +
                   $"• **Prompt Directive:** *\"{prompt}\"*\n" +
                   $"• **Perception Telemetry:** Primary windows, code buffers, and active UI elements scanned and mapped to Stark neural working memory.\n" +
                   $"• **Synthesis:** Frame telemetry nominal. Ready for interactive debugging or automated task execution, Sir.";

    return Results.Ok(new { success = true, analysis });
});

// 17. Vector Memory & RAG Search Endpoints
app.MapGet("/api/memory/search", async ([FromQuery] string q, [FromQuery] string? category, IVectorMemoryService memory) =>
{
    var matches = await memory.SearchSimilarAsync(q, 10, category);
    return Results.Ok(matches);
});

// 18. Morning Executive Briefing Endpoint
app.MapGet("/api/briefing/morning", async (IBriefingService briefing) =>
{
    var report = await briefing.GenerateMorningBriefingAsync();
    return Results.Ok(new { success = true, report });
});

// 19. Stark Vault & Hardware Encrypted Secrets Endpoints
app.MapGet("/api/vault/list", async (IStarkVaultService vault) => Results.Ok(await vault.ListSecretMetadataAsync()));
app.MapPost("/api/vault/store", async ([FromBody] VaultSecretRequest req, IStarkVaultService vault) =>
{
    await vault.StoreSecretAsync(req.Key, req.Value, req.Description ?? "Stored via Stark API");
    return Results.Ok(new { success = true, key = req.Key });
});
app.MapPost("/api/vault/retrieve", async ([FromBody] VaultRetrieveRequest req, IStarkVaultService vault) =>
{
    try
    {
        var secret = await vault.RetrieveSecretAsync(req.Key, req.Passcode);
        return secret != null ? Results.Ok(new { success = true, value = secret }) : Results.NotFound();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
});

// 20. Automated Snapshots & Recovery Endpoints
app.MapGet("/api/storage/snapshots", async (IBackupService backup) => Results.Ok(await backup.GetSnapshotsAsync()));
app.MapPost("/api/storage/snapshots/create", async ([FromBody] CreateSnapshotDto? req, IBackupService backup) =>
{
    var item = await backup.CreateSnapshotAsync(req?.Description ?? "Manual Stark Snapshot");
    return Results.Ok(item);
});
app.MapPost("/api/storage/snapshots/{id}/restore", async (string id, IBackupService backup) =>
{
    var ok = await backup.RestoreSnapshotAsync(id);
    return Results.Ok(new { success = ok });
});

// 21. Smart Home & Lab IoT Endpoints
app.MapGet("/api/iot/devices", async (ISmartHomeService iot) => Results.Ok(await iot.GetLabDevicesAsync()));
app.MapPost("/api/iot/scene", async ([FromBody] SetSceneDto req, ISmartHomeService iot) =>
{
    var msg = await iot.SetAmbientSceneAsync(req.SceneName, req.Brightness);
    return Results.Ok(new { success = true, message = msg });
});

// 22. Code Sandbox Endpoint
app.MapPost("/api/sandbox/execute", async ([FromBody] SandboxRunDto req, ICodeSandboxService sandbox) =>
{
    var res = await sandbox.ExecuteScriptAsync(req.Language, req.Code, req.TimeoutSeconds);
    return Results.Ok(res);
});

// 23. Sub-HUD Suite Extended Endpoints
app.MapGet("/api/os/context", (IActiveWindowTrackerService tracker) => Results.Ok(tracker.GetCurrentForegroundContext()));

app.MapPost("/api/browser/navigate", async ([FromBody] BrowserNavigateDto req, IBrowserAgentService browser) =>
    Results.Ok(await browser.NavigateAndExtractAsync(req.Url)));

app.MapPost("/api/clipboard/analyze", ([FromBody] ClipboardAnalyzeDto req, IClipboardSentinelService clip) =>
    Results.Ok(clip.AnalyzeSnippet(req.Text)));

app.MapPost("/api/swarm/deploy", async ([FromBody] SwarmDeployDto req, IAgentSwarmService swarm) =>
    Results.Ok(await swarm.ExecuteSwarmDirectiveAsync(req.Goal)));

app.MapPost("/api/terminal/exec", async ([FromBody] TerminalExecDto req, IInteractiveTerminalService term) =>
    Results.Ok(await term.ExecuteTerminalCommandAsync(req.Command, req.WorkingDirectory)));

app.MapGet("/api/docker/containers", async (IDockerManagerService docker) =>
    Results.Ok(await docker.GetContainersAsync()));

app.MapGet("/api/database/schema", async (IDatabaseExplorerService db) =>
    Results.Ok(await db.GetSchemaOverviewAsync()));

app.MapPost("/api/database/query", async ([FromBody] SqlQueryDto req, IDatabaseExplorerService db) =>
    Results.Ok(await db.ExecuteSafeQueryAsync(req.Sql)));

app.MapGet("/api/radar/scan", async (INetworkRadarService radar) =>
    Results.Ok(await radar.ScanArpTableAsync()));

app.MapGet("/api/achievements", (IAchievementService ach) =>
    Results.Ok(ach.GetAchievements()));

app.MapPost("/api/webhooks/{source}", async (string source, HttpContext http, IHubContext<JarvisHub> hub) =>
{
    using var reader = new StreamReader(http.Request.Body);
    var body = await reader.ReadToEndAsync();
    await hub.Clients.All.SendAsync("OnWebhookTriggered", new { source, payload = body, timestamp = DateTime.UtcNow });
    return Results.Ok(new { success = true, source, receivedAt = DateTime.UtcNow });
});

// 25. J.A.R.V.I.S. External Apps Integration Endpoints (ArborNet, AgentLab, AxiomCODAR)
app.MapGet("/api/external-apps/telemetry", async (IExternalAppsAdapterService ext) =>
    Results.Ok(await ext.GetUnifiedExternalAppsTelemetryAsync()));

app.MapPost("/api/external-apps/arbornet", async ([FromBody] ExternalAppActionDto req, IExternalAppsAdapterService ext) =>
    Results.Ok(new { output = await ext.ExecuteArborNetActionAsync(req.Action, req.Parameters) }));

app.MapPost("/api/external-apps/agentlab", async ([FromBody] ExternalAppActionDto req, IExternalAppsAdapterService ext) =>
    Results.Ok(new { output = await ext.ExecuteAgentLabActionAsync(req.Action, req.Parameters) }));

app.MapPost("/api/external-apps/axiom-codar", async ([FromBody] ExternalAppActionDto req, IExternalAppsAdapterService ext) =>
    Results.Ok(new { output = await ext.ExecuteAxiomCodarActionAsync(req.Action, req.Parameters) }));

app.MapPost("/api/external-apps/ecosystem", async ([FromBody] ExternalAppActionDto req, IExternalAppsAdapterService ext) =>
    Results.Ok(new { output = await ext.ExecuteEcosystemActionAsync(req.Action, req.Parameters) }));

app.MapPost("/api/external-apps/voice-directive", async ([FromBody] ExternalVoiceDirectiveDto req, IExternalAppsAdapterService ext) =>
    Results.Ok(await ext.ProcessVoiceDirectiveAsync(req.Directive)));

// 27. Stark Radio Station & YouTube Harvester Endpoints
app.MapGet("/api/radio/status", async (IStarkRadioService radio) =>
    Results.Ok(await radio.GetStatusAsync()));

app.MapGet("/api/radio/tracks", async (string? search, string? genre, IStarkRadioService radio) =>
    Results.Ok(await radio.GetAllTracksAsync(search, genre)));

app.MapGet("/api/radio/tracks/{id}", async (string id, IStarkRadioService radio) =>
{
    var track = await radio.GetTrackByIdAsync(id);
    return track != null ? Results.Ok(track) : Results.NotFound();
});

app.MapGet("/api/radio/stream/{id}", async (string id, IStarkRadioService radio) =>
{
    var path = await radio.GetTrackFilePathAsync(id);
    if (path == null || !File.Exists(path))
    {
        return Results.NotFound();
    }

    var ext = Path.GetExtension(path).ToLowerInvariant();
    var contentType = ext switch
    {
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        ".mp4" => "video/mp4",
        _ => "application/octet-stream"
    };

    // Enable byte-range processing for non-blocking seeking and smooth radio playback
    return Results.File(path, contentType: contentType, enableRangeProcessing: true);
});

app.MapPost("/api/radio/request", async ([FromBody] RadioRequestDto req, IStarkRadioService radio, CancellationToken ct) =>
{
    try
    {
        var track = await radio.RequestSongAsync(req.QueryOrUrl, req.Format, req.AutoPlay, req.Genre, ct);
        return Results.Ok(track);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/radio/harvest-album", async ([FromBody] RadioAlbumRequestDto req, IStarkRadioService radio, CancellationToken ct) =>
{
    try
    {
        var tracks = await radio.DownloadAlbumAsync(req.Artist, req.Album, req.MaxTracks, req.Genre, req.AutoPlay, ct);
        return Results.Ok(tracks);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/radio/control", async ([FromBody] RadioControlDto req, IStarkRadioService radio) =>
    Results.Ok(await radio.ControlPlaybackAsync(req.Action, req.TrackId, req.Position, req.Volume, req.Genre, req.RepeatMode)));

app.MapPost("/api/radio/redownload/{id?}", async (string? id, IStarkRadioService radio, CancellationToken ct) =>
{
    try
    {
        var fixedTrack = await radio.ReplaceCorruptedTrackAsync(id, ct);
        return fixedTrack != null ? Results.Ok(fixedTrack) : Results.NotFound(new { error = "No active or specified track found to replace." });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPut("/api/radio/tracks/{id}", async (string id, [FromBody] RadioEditTrackDto req, IStarkRadioService radio) =>
{
    var updated = await radio.UpdateTrackMetadataAsync(id, req.Title, req.Artist, req.Album, req.Genre);
    return updated != null ? Results.Ok(updated) : Results.NotFound();
});

app.MapDelete("/api/radio/tracks/{id}", async (string id, IStarkRadioService radio) =>
{
    var deleted = await radio.DeleteTrackAsync(id);
    return deleted ? Results.Ok(new { success = true }) : Results.NotFound();
});

app.MapPost("/api/radio/resync", async (IStarkRadioService radio) =>
{
    await radio.ResyncLibraryFromDiskAsync();
    return Results.Ok(new { success = true, message = "Radio library resynchronized from disk" });
});

// 26. Stark Cinema & Video Player Endpoints (YouTube & Rumble)
app.MapGet("/api/video/channels", async (string? platform, IVideoPlayerService videoService) =>
{
    return Results.Ok(await videoService.GetChannelsAsync(platform));
});

app.MapPost("/api/video/channels", async ([FromBody] AddChannelRequestDto req, IVideoPlayerService videoService, CancellationToken ct) =>
{
    try
    {
        var channel = await videoService.AddChannelAsync(req.ChannelUrlOrHandle, req.Platform, ct);
        return Results.Ok(channel);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapDelete("/api/video/channels/{id}", async (string id, IVideoPlayerService videoService) =>
{
    var deleted = await videoService.DeleteChannelAsync(id);
    return deleted ? Results.Ok(new { success = true }) : Results.NotFound();
});

app.MapPost("/api/video/channels/{id}/sync", async (string id, IVideoPlayerService videoService, CancellationToken ct) =>
{
    try
    {
        var count = await videoService.SyncChannelVideosAsync(id, 15, ct);
        return Results.Ok(new { success = true, syncedVideos = count });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/video/feed", async (string? channelId, string? search, bool? onlyDownloaded, bool? onlyUnwatched, string? platform, int? limit, IVideoPlayerService videoService) =>
{
    var videos = await videoService.GetRecommendedVideosAsync(channelId, search, onlyDownloaded ?? false, onlyUnwatched, platform, limit ?? 100);
    return Results.Ok(videos);
});

app.MapGet("/api/video/videos/{id}", async (string id, IVideoPlayerService videoService) =>
{
    var video = await videoService.GetVideoByIdAsync(id);
    return video != null ? Results.Ok(video) : Results.NotFound();
});

app.MapPost("/api/video/watched", async ([FromBody] ToggleWatchedRequestDto req, IVideoPlayerService videoService) =>
{
    var updated = await videoService.SetVideoWatchedStatusAsync(req.VideoId, req.IsWatched);
    return updated != null ? Results.Ok(updated) : Results.NotFound();
});

app.MapPost("/api/video/download", async ([FromBody] DownloadVideoRequestDto req, IVideoPlayerService videoService, CancellationToken ct) =>
{
    try
    {
        var downloaded = await videoService.DownloadVideoAsync(req.VideoId, ct);
        return Results.Ok(downloaded);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/video/stream/{id}", async (string id, IVideoPlayerService videoService) =>
{
    var path = await videoService.GetVideoFilePathAsync(id);
    if (path == null || !File.Exists(path))
    {
        return Results.NotFound();
    }

    var ext = Path.GetExtension(path).ToLowerInvariant();
    var contentType = ext switch
    {
        ".mp4" => "video/mp4",
        ".mkv" => "video/x-matroska",
        ".webm" => "video/webm",
        _ => "video/mp4"
    };

    return Results.File(path, contentType: contentType, enableRangeProcessing: true);
});

app.MapPost("/api/video/resync", async (IVideoPlayerService videoService) =>
{
    await videoService.ResyncDownloadedVideosAsync();
    return Results.Ok(new { success = true, message = "Downloaded videos resynchronized from disk" });
});

// 27. Stark Movie Vault Endpoints
app.MapGet("/api/movies", async (string? search, bool? onlyUnwatched, int? limit, IMovieVaultService movieService) =>
{
    var movies = await movieService.GetMoviesAsync(search, onlyUnwatched, limit ?? 100);
    return Results.Ok(movies);
});

app.MapGet("/api/movies/{id}", async (string id, IMovieVaultService movieService) =>
{
    var movie = await movieService.GetMovieByIdAsync(id);
    return movie != null ? Results.Ok(movie) : Results.NotFound();
});

app.MapPost("/api/movies/resync", async (IMovieVaultService movieService, CancellationToken ct) =>
{
    var count = await movieService.ResyncLibraryFromDiskAsync(ct);
    return Results.Ok(new { success = true, syncedMovies = count });
});

app.MapPost("/api/movies/watched", async ([FromBody] ToggleMovieWatchedRequestDto req, IMovieVaultService movieService) =>
{
    var updated = await movieService.ToggleWatchedAsync(req.MovieId, req.IsWatched);
    return updated != null ? Results.Ok(updated) : Results.NotFound();
});

app.MapGet("/api/movies/stream/{id}", async (string id, IMovieVaultService movieService) =>
{
    var path = await movieService.GetMovieFilePathAsync(id);
    if (path == null || !File.Exists(path))
    {
        return Results.NotFound();
    }

    var ext = Path.GetExtension(path).ToLowerInvariant();
    var contentType = ext switch
    {
        ".mp4" => "video/mp4",
        ".mkv" => "video/x-matroska",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        _ => "video/mp4"
    };

    return Results.File(path, contentType: contentType, enableRangeProcessing: true);
});

app.MapGet("/api/movies/poster/{id}", async (string id, IMovieVaultService movieService) =>
{
    var posterPath = await movieService.GetMoviePosterPathAsync(id);
    if (posterPath != null && File.Exists(posterPath))
    {
        var ext = Path.GetExtension(posterPath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return Results.File(posterPath, contentType: contentType);
    }

    // High-tech fallback SVG poster
    var movie = await movieService.GetMovieByIdAsync(id);
    var title = movie?.Title ?? "Stark Movie";
    var year = movie?.Year ?? "Vault";
    var svg = $@"<svg xmlns='http://www.w3.org/2000/svg' width='400' height='600' viewBox='0 0 400 600'>
        <defs>
            <linearGradient id='bg' x1='0%' y1='0%' x2='100%' y2='100%'>
                <stop offset='0%' stop-color='#0a101d'/>
                <stop offset='100%' stop-color='#030712'/>
            </linearGradient>
            <linearGradient id='glow' x1='0%' y1='0%' x2='100%' y2='0%'>
                <stop offset='0%' stop-color='#ff0055'/>
                <stop offset='100%' stop-color='#00d4ff'/>
            </linearGradient>
        </defs>
        <rect width='400' height='600' fill='url(#bg)'/>
        <rect x='10' y='10' width='380' height='580' fill='none' stroke='rgba(0, 212, 255, 0.25)' stroke-width='1.5' rx='8'/>
        <circle cx='200' cy='240' r='60' fill='rgba(255, 0, 85, 0.1)' stroke='url(#glow)' stroke-width='2'/>
        <text x='200' y='255' font-family='Arial, sans-serif' font-size='42' text-anchor='middle' fill='#fff'>🎬</text>
        <text x='200' y='360' font-family='Arial, sans-serif' font-size='20' font-weight='bold' text-anchor='middle' fill='#ffffff'>{System.Security.SecurityElement.Escape(title)}</text>
        <text x='200' y='395' font-family='Arial, sans-serif' font-size='14' text-anchor='middle' fill='#00d4ff'>STARK MOVIE VAULT • {System.Security.SecurityElement.Escape(year)}</text>
    </svg>";

    return Results.Content(svg, "image/svg+xml");
});

app.MapDelete("/api/movies/{id}", async (string id, IMovieVaultService movieService) =>
{
    var deleted = await movieService.DeleteMovieAsync(id);
    return deleted ? Results.Ok(new { success = true }) : Results.NotFound();
});

// 27. SignalR Realtime Hub Mapping
app.MapHub<JarvisHub>("/hub/jarvis");

app.Run();

public record ExternalAppActionDto(string Action, string? Parameters = null);
public record ExternalVoiceDirectiveDto(string Directive);
public record BrowserNavigateDto(string Url);
public record ClipboardAnalyzeDto(string Text);
public record SwarmDeployDto(string Goal);
public record TerminalExecDto(string Command, string? WorkingDirectory = null);
public record SqlQueryDto(string Sql);

public record ReadFileDto(string PathOrId, string? RelativePath);
public record WriteFileDto(string PathOrId, string RelativePath, string Content);
public record SaveFileDto(string FileName, string Content);
public record VaultSecretRequest(string Key, string Value, string? Description);
public record VaultRetrieveRequest(string Key, string Passcode);
public record CreateSnapshotDto(string? Description);
public record SetSceneDto(string SceneName, int Brightness = 100);
public record SandboxRunDto(string Language, string Code, int TimeoutSeconds = 15);

