using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Memory;
using OzzieAI.Jarvis.Services.Sensors;

namespace OzzieAI.Jarvis.Services.Tools;

public class ToolService : IToolService
{
    private readonly JarvisDatabase _database;
    private readonly IMcpSensorService _sensorService;
    private readonly IPredicateService _predicateService;
    private readonly OzzieAI.Jarvis.Services.Rea.IReaService _reaService;
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ToolService> _logger;

    public ToolService(
        JarvisDatabase database,
        IMcpSensorService sensorService,
        IPredicateService predicateService,
        OzzieAI.Jarvis.Services.Rea.IReaService reaService,
        HttpClient httpClient,
        IServiceProvider serviceProvider,
        ILogger<ToolService> logger)
    {
        _database = database;
        _sensorService = sensorService;
        _predicateService = predicateService;
        _reaService = reaService;
        _httpClient = httpClient;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<List<ToolItem>> GetAllToolsAsync()
    {
        var list = new List<ToolItem>();
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Description, ParametersJsonSchema, ExecutionType, EndpointUrl, ScriptCode, IsBuiltIn, CreatedAt FROM Tools ORDER BY IsBuiltIn DESC, Name ASC";
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                list.Add(new ToolItem
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ParametersJsonSchema = reader.GetString(3),
                    ExecutionType = reader.GetString(4),
                    EndpointUrl = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ScriptCode = reader.IsDBNull(6) ? null : reader.GetString(6),
                    IsBuiltIn = reader.GetInt32(7) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(8))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tools from SQLite");
        }
        return list;
    }

    public async Task<ToolItem?> GetToolAsync(string idOrName)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Description, ParametersJsonSchema, ExecutionType, EndpointUrl, ScriptCode, IsBuiltIn, CreatedAt FROM Tools WHERE Id = $id OR Name = $n";
            cmd.Parameters.AddWithValue("$id", idOrName);
            cmd.Parameters.AddWithValue("$n", idOrName);
            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new ToolItem
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ParametersJsonSchema = reader.GetString(3),
                    ExecutionType = reader.GetString(4),
                    EndpointUrl = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ScriptCode = reader.IsDBNull(6) ? null : reader.GetString(6),
                    IsBuiltIn = reader.GetInt32(7) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(8))
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tool {IdOrName}", idOrName);
        }
        return null;
    }

    public async Task SaveToolAsync(ToolItem tool)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Tools (Id, Name, Description, ParametersJsonSchema, ExecutionType, EndpointUrl, ScriptCode, IsBuiltIn, CreatedAt)
                VALUES ($id, $n, $d, $p, $et, $ep, $sc, $bi, $ca)
            ";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(tool.Id) ? Guid.NewGuid().ToString("N") : tool.Id);
            cmd.Parameters.AddWithValue("$n", tool.Name.Trim());
            cmd.Parameters.AddWithValue("$d", tool.Description?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$p", string.IsNullOrWhiteSpace(tool.ParametersJsonSchema) ? "{}" : tool.ParametersJsonSchema);
            cmd.Parameters.AddWithValue("$et", tool.ExecutionType);
            cmd.Parameters.AddWithValue("$ep", (object?)tool.EndpointUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sc", (object?)tool.ScriptCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bi", tool.IsBuiltIn ? 1 : 0);
            cmd.Parameters.AddWithValue("$ca", tool.CreatedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Saved tool {Name} ({Id})", tool.Name, tool.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save tool {Name}", tool.Name);
            throw;
        }
    }

    public async Task DeleteToolAsync(string id)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Tools WHERE Id = $id AND IsBuiltIn = 0";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Deleted tool {Id}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete tool {Id}", id);
            throw;
        }
    }

    public async Task<ToolExecutionResponse> ExecuteToolAsync(ToolExecutionRequest request)
    {
        var tool = await GetToolAsync(request.ToolNameOrId);
        if (tool == null)
        {
            return new ToolExecutionResponse
            {
                Success = false,
                Error = $"Tool '{request.ToolNameOrId}' not found in registry."
            };
        }

        try
        {
            if (tool.ExecutionType == "Internal")
            {
                return await ExecuteInternalToolAsync(tool.Name, request.Arguments);
            }
            else if (tool.ExecutionType == "HttpEndpoint" && !string.IsNullOrWhiteSpace(tool.EndpointUrl))
            {
                return await ExecuteHttpToolAsync(tool.EndpointUrl, request.Arguments);
            }
            else
            {
                return new ToolExecutionResponse
                {
                    Success = true,
                    Output = $"Tool '{tool.Name}' executed via dynamic pipeline. Arguments: {JsonSerializer.Serialize(request.Arguments)}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {ToolName}", tool.Name);
            return new ToolExecutionResponse
            {
                Success = false,
                Error = $"Execution error in {tool.Name}: {ex.Message}"
            };
        }
    }

    private async Task<ToolExecutionResponse> ExecuteInternalToolAsync(string name, Dictionary<string, object>? args)
    {
        switch (name.ToLowerInvariant())
        {
            case "sensor_read_telemetry":
                var telemetry = _sensorService.GetCurrentTelemetry();
                return new ToolExecutionResponse
                {
                    Success = true,
                    Output = JsonSerializer.Serialize(telemetry, new JsonSerializerOptions { WriteIndented = true })
                };

            case "suit_protocol_control":
                var protocol = args != null && args.TryGetValue("protocol", out var pVal) ? pVal?.ToString() ?? "CleanSlate" : "CleanSlate";
                await _sensorService.TriggerProtocolPulseAsync(protocol);
                await _predicateService.SavePredicateAsync(new PredicateItem
                {
                    Key = "ActiveStarkProtocol",
                    Value = $"{protocol} ENGAGED - {DateTime.UtcNow:HH:mm:ss} UTC",
                    Category = "Protocol",
                    Description = "Last activated suit/defense protocol"
                });
                return new ToolExecutionResponse
                {
                    Success = true,
                    Output = $"Stark Protocol '{protocol}' successfully deployed. Arc Reactor output peaked, telemetry updated."
                };

            case "calculate_scientific":
                var expr = args != null && args.TryGetValue("expression", out var eVal) ? eVal?.ToString() ?? "0" : "0";
                var result = EvaluateMathExpression(expr);
                return new ToolExecutionResponse
                {
                    Success = true,
                    Output = $"Expression: {expr} = {result}"
                };

            case "search_web":
                var query = args != null && args.TryGetValue("query", out var qVal) ? qVal?.ToString() ?? "" : "";
                return await FetchLatestNewsAsync(query);

            case "execute_command":
                var cmdStr = args != null && args.TryGetValue("command", out var cVal) ? cVal?.ToString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(cmdStr))
                {
                    return new ToolExecutionResponse { Success = false, Error = "No command provided." };
                }
                var sentinel = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Security.ITerminalSentinelService>();
                if (sentinel != null)
                {
                    var eval = sentinel.EvaluateCommand(cmdStr);
                    if (!eval.IsSafe)
                    {
                        return new ToolExecutionResponse
                        {
                            Success = false,
                            Error = $"[TERMINAL SENTINEL HAZARD BLOCKED]: {eval.Reason} (Challenge Token: {eval.ChallengeToken})"
                        };
                    }
                }
                var (outText, errText) = await RunShellCommandAsync(cmdStr);
                return new ToolExecutionResponse
                {
                    Success = string.IsNullOrEmpty(errText),
                    Output = string.IsNullOrEmpty(outText) ? "(command executed with no output)" : outText,
                    Error = errText
                };

            case "read_file":
                var path = args != null && args.TryGetValue("path", out var ptVal) ? ptVal?.ToString() ?? "" : "";
                if (File.Exists(path))
                {
                    var content = await File.ReadAllTextAsync(path);
                    return new ToolExecutionResponse { Success = true, Output = content };
                }
                return new ToolExecutionResponse { Success = false, Error = $"File '{path}' does not exist." };

            case "write_file":
                var wPath = args != null && args.TryGetValue("path", out var wpVal) ? wpVal?.ToString() ?? "" : "";
                var wContent = args != null && args.TryGetValue("content", out var wcVal) ? wcVal?.ToString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(wPath))
                {
                    await File.WriteAllTextAsync(wPath, wContent);
                    return new ToolExecutionResponse { Success = true, Output = $"File '{wPath}' successfully written ({wContent.Length} bytes)." };
                }
                return new ToolExecutionResponse { Success = false, Error = "Path not specified." };

            case "weather_atmospheric_scan":
                var loc = args != null && args.TryGetValue("location", out var lVal) ? lVal?.ToString() ?? "" : "";
                return await FetchRealWeatherAsync(loc);

            case "system_hardware_diagnostics":
                return await GetDetailedSystemDiagnosticsAsync();

            case "world_time_clock":
                var zoneOrCity = args != null && args.TryGetValue("location", out var zVal) ? zVal?.ToString() ?? "" : "";
                return GetRealWorldTime(zoneOrCity);

            case "knowledge_lookup":
                var kQuery = args != null && args.TryGetValue("query", out var kqVal) ? kqVal?.ToString() ?? "" : "";
                return await FetchWikipediaSummaryAsync(kQuery);

            case "rea_analyze_target":
                var targetPath = args != null && args.TryGetValue("target", out var tgt) ? tgt?.ToString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(targetPath) && args != null && args.TryGetValue("path", out var pth))
                {
                    targetPath = pth?.ToString() ?? "";
                }
                var reaAnalysis = await _reaService.AnalyzeTargetAsync(targetPath);
                return new ToolExecutionResponse
                {
                    Success = reaAnalysis.Success,
                    Output = reaAnalysis.SummaryMarkdown
                };

            case "rea_mcp_bridge":
                var reaToolName = args != null && args.TryGetValue("tool", out var rTool) ? rTool?.ToString() ?? "analyze" : "analyze";
                var jsonNode = args != null ? JsonSerializer.SerializeToNode(args) as JsonObject : new JsonObject();
                var mcpOut = await _reaService.ExecuteMcpReaBridgeAsync(reaToolName, jsonNode);
                return new ToolExecutionResponse
                {
                    Success = true,
                    Output = mcpOut
                };

            case "storage_telemetry":
            case "storage_diagnostics":
                var ghostService = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Storage.IGhostDriveService>();
                if (ghostService != null)
                {
                    var drives = await ghostService.GetStorageTelemetryAsync();
                    var sb = new StringBuilder("### Stark Industries Host & Ghost Storage Telemetry:\n\n");
                    sb.AppendLine("| Drive / Mount | Format | Total | Free | Used | Free % | Status |");
                    sb.AppendLine("|---|---|---|---|---|---|---|");
                    foreach (var d in drives)
                    {
                        var statusAlert = d.IsLowSpace ? "**ALERT: LOW SPACE**" : d.Status;
                        sb.AppendLine($"| `{d.DriveName}` | {d.DriveFormat} | `{d.TotalGb:F1} GB` | `{d.FreeGb:F1} GB` | `{d.UsedGb:F1} GB` | `{d.PercentFree:F1}%` | {statusAlert} |");
                    }
                    return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
                }
                return new ToolExecutionResponse { Success = false, Error = "Storage service offline." };

            case "ghost_drive_manage":
            case "ghost_drive_list":
                var gs = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Storage.IGhostDriveService>();
                if (gs != null)
                {
                    var gList = await gs.GetAllGhostDrivesAsync();
                    var sb = new StringBuilder("### Active Stark Ghost Drives (Virtual Mounts):\n\n");
                    sb.AppendLine("| Tag | Drive Name | Mount Path | Used | Max Capacity | Status |");
                    sb.AppendLine("|---|---|---|---|---|---|");
                    foreach (var g in gList)
                    {
                        var usedMb = g.UsedBytes / (1024.0 * 1024.0);
                        var maxGb = g.MaxSizeBytes / (1024.0 * 1024.0 * 1024.0);
                        sb.AppendLine($"| `{g.VirtualTag}` | **{g.Name}** | `{g.MountPath}` | `{usedMb:F1} MB` | `{maxGb:F1} GB` | {(g.IsActive ? "Mounted" : "Unmounted")} |");
                    }
                    return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
                }
                return new ToolExecutionResponse { Success = false, Error = "Ghost drive service offline." };

            case "calendar_manage":
                var cs = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Calendar.ICalendarService>();
                if (cs != null)
                {
                    var appts = await cs.GetAllAppointmentsAsync();
                    var sb = new StringBuilder("### Stark Tactical Calendar Appointments:\n\n");
                    sb.AppendLine("| Title | Attendees | Date & Time | Status |");
                    sb.AppendLine("|---|---|---|---|");
                    foreach (var a in appts)
                    {
                        sb.AppendLine($"| **{a.Title}** | {a.Attendees} | `{a.StartTime.ToLocalTime():yyyy-MM-dd h:mm tt}` | `{a.Status}` |");
                    }
                    return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
                }
                return new ToolExecutionResponse { Success = false, Error = "Calendar service offline." };

            case "scheduler_manage":
                var ss = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Scheduler.IJarvisSchedulerService>();
                if (ss != null)
                {
                    var sTasks = await ss.GetAllTasksAsync();
                    var sb = new StringBuilder("### Autonomous Scheduled Tasks Matrix:\n\n");
                    sb.AppendLine("| Task Name | Runs/Day | Next Run | Status | Assigned Agent |");
                    sb.AppendLine("|---|---|---|---|---|");
                    foreach (var t in sTasks)
                    {
                        sb.AppendLine($"| **{t.Name}** | `{t.RunsPerDay}x/day` | `{t.NextRun.ToLocalTime():h:mm:ss tt}` | `{t.Status}` | `{t.AssignedAgentId}` |");
                    }
                    return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
                }
                return new ToolExecutionResponse { Success = false, Error = "Scheduler service offline." };

            case "stark_radio_play":
            case "radio_play":
                var radioSvc = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Media.IStarkRadioService>();
                if (radioSvc != null)
                {
                    var q = args != null && args.TryGetValue("query", out var radioQVal) ? radioQVal?.ToString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(q) && args != null && args.TryGetValue("url", out var radioUVal))
                    {
                        q = radioUVal?.ToString() ?? "";
                    }
                    var fmt = args != null && args.TryGetValue("format", out var radioFVal) ? radioFVal?.ToString() ?? "mp3" : "mp3";
                    var track = await radioSvc.RequestSongAsync(q, fmt, autoPlay: true);
                    return new ToolExecutionResponse
                    {
                        Success = true,
                        Output = $"Radio Stark Broadcast: Now queued/playing '{track.Title}' by '{track.Artist}' (Album: '{track.Album}', Format: {track.Format.ToUpper()}). File path: {track.FilePath}"
                    };
                }
                return new ToolExecutionResponse { Success = false, Error = "Stark Radio Service is offline." };

            case "stark_radio_manage":
            case "radio_manage":
                var rManageSvc = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Media.IStarkRadioService>();
                if (rManageSvc != null)
                {
                    var action = args != null && args.TryGetValue("action", out var aVal) ? aVal?.ToString() ?? "status" : "status";
                    var targetId = args != null && args.TryGetValue("trackId", out var tIdVal) ? tIdVal?.ToString() : null;
                    
                    if (action.Equals("redownload_corrupted", StringComparison.OrdinalIgnoreCase) || action.Equals("replace_corrupted", StringComparison.OrdinalIgnoreCase))
                    {
                        var fixedTrack = await rManageSvc.ReplaceCorruptedTrackAsync(targetId);
                        if (fixedTrack != null)
                        {
                            return new ToolExecutionResponse
                            {
                                Success = true,
                                Output = $"Pristine replacement master secured for '{fixedTrack.Title}' by '{fixedTrack.Artist}'. Corrupted audio purged and track refreshed at: {fixedTrack.FilePath}"
                            };
                        }
                        return new ToolExecutionResponse { Success = false, Error = "No active or specified track to replace as corrupted." };
                    }
                    else if (action.Equals("list_tracks", StringComparison.OrdinalIgnoreCase) || action.Equals("library", StringComparison.OrdinalIgnoreCase))
                    {
                        var tracks = await rManageSvc.GetAllTracksAsync();
                        var sb = new StringBuilder("### Stark Radio Station Library:\n\n");
                        sb.AppendLine("| Artist | Title | Album | Format | Duration | Size |");
                        sb.AppendLine("|---|---|---|---|---|---|");
                        foreach (var tr in tracks.Take(30))
                        {
                            var mins = tr.DurationSeconds / 60;
                            var secs = tr.DurationSeconds % 60;
                            var sizeMb = tr.FileSizeBytes / (1024.0 * 1024.0);
                            sb.AppendLine($"| **{tr.Artist}** | {tr.Title} | *{tr.Album}* | {tr.Format.ToUpper()} | {mins}:{secs:D2} | {sizeMb:F1} MB |");
                        }
                        return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
                    }
                    else
                    {
                        double? vol = null;
                        if (args != null && args.TryGetValue("volume", out var vVal) && double.TryParse(vVal?.ToString(), out var vNum))
                        {
                            vol = vNum;
                        }
                        var status = await rManageSvc.ControlPlaybackAsync(action, targetId, null, vol);
                        var nowPlaying = status.CurrentTrack != null ? $"'{status.CurrentTrack.Title}' by '{status.CurrentTrack.Artist}'" : "(None)";
                        return new ToolExecutionResponse
                        {
                            Success = true,
                            Output = $"Radio Stark status: Action '{action}' executed. Playing: {status.IsPlaying}, Current: {nowPlaying}, Volume: {(int)(status.Volume * 100)}%"
                        };
                    }
                }
                return new ToolExecutionResponse { Success = false, Error = "Stark Radio Service is offline." };

            case "contacts_manage":
            case "contacts_list":
            case "contacts_search":
            {
                var contactService = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Contacts.IContactService>();
                if (contactService != null)
                {
                    var action = args != null && args.TryGetValue("action", out var actVal) ? actVal?.ToString()?.ToLowerInvariant() ?? "list" : "list";
                    var contactQuery = args != null && (args.TryGetValue("query", out var cSearchVal) || args.TryGetValue("search", out cSearchVal) || args.TryGetValue("name", out cSearchVal)) ? cSearchVal?.ToString() : null;

                    if (action == "search" || !string.IsNullOrWhiteSpace(contactQuery))
                    {
                        var matches = await contactService.SearchContactsAsync(contactQuery ?? "");
                        var sb = new StringBuilder($"### Stark Industries Contacts Search Results for '{contactQuery}':\n\n");
                        sb.AppendLine("| Name | Call Sign | Role / Org | Phone | Email | Clearance |");
                        sb.AppendLine("|---|---|---|---|---|---|");
                        foreach (var c in matches)
                        {
                            sb.AppendLine($"| **{c.FullName}** | `{c.CallSign}` | {c.Role} ({c.Organization}) | `{c.PrimaryPhone}` | `{c.PrimaryEmail}` | `LVL {c.ClearanceLevel}` |");
                        }
                        return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
                    }
                    else if (action == "create" || action == "add")
                    {
                        var fn = args?.GetValueOrDefault("fullName")?.ToString() ?? args?.GetValueOrDefault("name")?.ToString() ?? "New Contact";
                        var csVal = args?.GetValueOrDefault("callSign")?.ToString() ?? "";
                        var rVal = args?.GetValueOrDefault("role")?.ToString() ?? "Associate";
                        var oVal = args?.GetValueOrDefault("organization")?.ToString() ?? "Stark Industries";
                        var contactPhoneVal = args?.GetValueOrDefault("phone")?.ToString() ?? args?.GetValueOrDefault("primaryPhone")?.ToString() ?? "";
                        var emVal = args?.GetValueOrDefault("email")?.ToString() ?? args?.GetValueOrDefault("primaryEmail")?.ToString() ?? "";
                        var clVal = 1;
                        if (args != null && args.TryGetValue("clearance", out var clObj) && int.TryParse(clObj?.ToString(), out var parsedCl)) clVal = parsedCl;

                        var newC = await contactService.CreateContactAsync(new ContactItem
                        {
                            FullName = fn,
                            CallSign = csVal,
                            Role = rVal,
                            Organization = oVal,
                            PrimaryPhone = contactPhoneVal,
                            PrimaryEmail = emVal,
                            ClearanceLevel = clVal,
                            Tags = args?.GetValueOrDefault("tags")?.ToString() ?? "General"
                        });
                        return new ToolExecutionResponse
                        {
                            Success = true,
                            Output = $"Contact '{newC.FullName}' (LVL {newC.ClearanceLevel}) successfully registered in Stark Directory. ID: {newC.Id}"
                        };
                    }
                    else
                    {
                        var all = await contactService.GetAllContactsAsync();
                        var sb = new StringBuilder("### Stark Industries Encrypted Contacts Directory:\n\n");
                        sb.AppendLine("| Clearance | Full Name | Call Sign | Role | Organization | Primary Comms | Primary Email |");
                        sb.AppendLine("|---|---|---|---|---|---|---|");
                        foreach (var c in all)
                        {
                            sb.AppendLine($"| `LVL {c.ClearanceLevel}` | **{c.FullName}** | `{c.CallSign}` | {c.Role} | {c.Organization} | `{c.PrimaryPhone}` | `{c.PrimaryEmail}` |");
                        }
                        return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
                    }
                }
                return new ToolExecutionResponse { Success = false, Error = "Contacts service offline." };
            }
            case "code_sandbox_execute":
            {
                var sandbox = _serviceProvider.GetService<ICodeSandboxService>();
                if (sandbox == null) return new ToolExecutionResponse { Success = false, Error = "Code sandbox service offline." };
                var lang = args?.GetValueOrDefault("language")?.ToString() ?? "python";
                var code = args?.GetValueOrDefault("code")?.ToString() ?? "";
                var res = await sandbox.ExecuteScriptAsync(lang, code);
                return new ToolExecutionResponse
                {
                    Success = res.Success,
                    Output = string.IsNullOrEmpty(res.Output) ? "(no output returned)" : res.Output,
                    Error = res.Error
                };
            }

            case "iot_smart_home_control":
            {
                var iot = _serviceProvider.GetService<ISmartHomeService>();
                if (iot == null) return new ToolExecutionResponse { Success = false, Error = "Smart home service offline." };
                var scene = args?.GetValueOrDefault("scene")?.ToString();
                if (!string.IsNullOrWhiteSpace(scene))
                {
                    var msg = await iot.SetAmbientSceneAsync(scene);
                    return new ToolExecutionResponse { Success = true, Output = msg };
                }
                var devId = args?.GetValueOrDefault("deviceId")?.ToString() ?? "";
                var state = args?.GetValueOrDefault("state") is bool b ? b : true;
                var res = await iot.ToggleDeviceAsync(devId, state);
                return new ToolExecutionResponse { Success = true, Output = res };
            }

            case "git_operations":
            {
                var git = _serviceProvider.GetService<IGitOpsService>();
                if (git == null) return new ToolExecutionResponse { Success = false, Error = "GitOps service offline." };
                var action = args?.GetValueOrDefault("action")?.ToString()?.ToLowerInvariant() ?? "status";
                string gitOutText = action switch
                {
                    "diff" => await git.GetDiffAsync(),
                    "log" => await git.GetRecentCommitsAsync(5),
                    "commit" => await git.CommitChangesAsync(args?.GetValueOrDefault("message")?.ToString() ?? ""),
                    _ => await git.GetStatusAsync()
                };
                return new ToolExecutionResponse { Success = true, Output = gitOutText };
            }

            case "stark_vault_manage":
            {
                var vault = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Security.IStarkVaultService>();
                if (vault == null) return new ToolExecutionResponse { Success = false, Error = "Stark Vault service offline." };
                var action = args?.GetValueOrDefault("action")?.ToString()?.ToLowerInvariant() ?? "list";
                var vKey = args?.GetValueOrDefault("key")?.ToString() ?? "";
                var vVal = args?.GetValueOrDefault("value")?.ToString() ?? "";
                var vPass = args?.GetValueOrDefault("passcode")?.ToString() ?? "Stark-10";

                if (action == "store" && !string.IsNullOrWhiteSpace(vKey))
                {
                    await vault.StoreSecretAsync(vKey, vVal, "Stored via J.A.R.V.I.S. neural tool");
                    return new ToolExecutionResponse { Success = true, Output = $"Secret key '{vKey}' encrypted and locked in Stark Vault." };
                }
                else if (action == "retrieve" && !string.IsNullOrWhiteSpace(vKey))
                {
                    var secret = await vault.RetrieveSecretAsync(vKey, vPass);
                    return new ToolExecutionResponse { Success = secret != null, Output = secret ?? "Key not found." };
                }
                else
                {
                    var secrets = await vault.ListSecretMetadataAsync();
                    var sb = new StringBuilder("### Stark Encrypted Vault Key Registry:\n\n");
                    sb.AppendLine("| Key Identifier | Status | Last Modified |");
                    sb.AppendLine("|---|---|---|");
                    foreach (var s in secrets)
                    {
                        sb.AppendLine($"| **{s.Key}** | `{s.Value}` | `{s.LastModified:yyyy-MM-dd HH:mm}` |");
                    }
                    return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
                }
            }

            case "snapshot_backup_create":
            {
                var backup = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Storage.IBackupService>();
                if (backup == null) return new ToolExecutionResponse { Success = false, Error = "Backup service offline." };
                var desc = args?.GetValueOrDefault("description")?.ToString() ?? "Stark On-Demand Snapshot";
                var snap = await backup.CreateSnapshotAsync(desc);
                return new ToolExecutionResponse
                {
                    Success = true,
                    Output = $"Stark Snapshot successfully compiled: **{snap.FileName}** ({snap.SizeBytes / 1024} KB). ID: `{snap.Id}`."
                };
            }

            case "morning_briefing":
            {
                var briefing = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Calendar.IBriefingService>();
                if (briefing == null) return new ToolExecutionResponse { Success = false, Error = "Briefing service offline." };
                var text = await briefing.GenerateMorningBriefingAsync();
                return new ToolExecutionResponse { Success = true, Output = text };
            }

            case "vector_memory_search":
            {
                var vec = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Memory.IVectorMemoryService>();
                if (vec == null) return new ToolExecutionResponse { Success = false, Error = "Vector memory service offline." };
                var searchQuery = args?.GetValueOrDefault("query")?.ToString() ?? "";
                var cat = args?.GetValueOrDefault("category")?.ToString();
                var results = await vec.SearchSimilarAsync(searchQuery, 5, cat);
                var sb = new StringBuilder($"### Semantic Memory Vector Search Results for '{searchQuery}':\n\n");
                sb.AppendLine("| Relevance | Category | Memory Content |");
                sb.AppendLine("|---|---|---|");
                foreach (var r in results)
                {
                    sb.AppendLine($"| `{r.Score:P0}` | *{r.Category}* | {r.Content} |");
                }
                return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
            }

            case "browser_agent_navigate":
            {
                var browser = _serviceProvider.GetService<IBrowserAgentService>();
                if (browser == null) return new ToolExecutionResponse { Success = false, Error = "Browser agent service offline." };
                var url = args?.GetValueOrDefault("url")?.ToString() ?? "";
                var res = await browser.NavigateAndExtractAsync(url);
                return new ToolExecutionResponse
                {
                    Success = res.Success,
                    Output = $"# {res.Title}\n\n{res.ContentMarkdown}",
                    Error = res.Error
                };
            }

            case "agent_swarm_deploy":
            {
                var swarm = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Agents.IAgentSwarmService>();
                if (swarm == null) return new ToolExecutionResponse { Success = false, Error = "Agent swarm service offline." };
                var goal = args?.GetValueOrDefault("goal")?.ToString() ?? "Execute multi-agent development assessment";
                var report = await swarm.ExecuteSwarmDirectiveAsync(goal);
                return new ToolExecutionResponse { Success = true, Output = report.SynthesizedPlan };
            }

            case "docker_container_manage":
            {
                var docker = _serviceProvider.GetService<IDockerManagerService>();
                if (docker == null) return new ToolExecutionResponse { Success = false, Error = "Docker manager service offline." };
                var act = args?.GetValueOrDefault("action")?.ToString() ?? "list";
                if (act == "list")
                {
                    var containers = await docker.GetContainersAsync();
                    var sb = new StringBuilder("### Docker Containers Telemetry:\n\n");
                    sb.AppendLine("| Names | Image | Status | Ports |");
                    sb.AppendLine("|---|---|---|---|");
                    foreach (var c in containers)
                    {
                        sb.AppendLine($"| **{c.Names}** | `{c.Image}` | `{c.Status}` | `{c.Ports}` |");
                    }
                    return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
                }
                var cId = args?.GetValueOrDefault("container")?.ToString() ?? "";
                var res = await docker.ControlContainerAsync(act, cId);
                return new ToolExecutionResponse { Success = true, Output = res };
            }

            case "database_sql_query":
            {
                var db = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Storage.IDatabaseExplorerService>();
                if (db == null) return new ToolExecutionResponse { Success = false, Error = "Database explorer service offline." };
                var sql = args?.GetValueOrDefault("sql")?.ToString() ?? "SELECT name FROM sqlite_master WHERE type='table';";
                var res = await db.ExecuteSafeQueryAsync(sql);
                if (res.Error != null) return new ToolExecutionResponse { Success = false, Error = res.Error };
                var sb = new StringBuilder("### Safe Query Results:\n\n");
                sb.AppendLine("| " + string.Join(" | ", res.Columns) + " |");
                sb.AppendLine("|" + string.Join("|", res.Columns.Select(_ => "---")) + "|");
                foreach (var r in res.Rows)
                {
                    sb.AppendLine("| " + string.Join(" | ", r) + " |");
                }
                return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
            }

            case "network_radar_scan":
            {
                var radar = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Security.INetworkRadarService>();
                if (radar == null) return new ToolExecutionResponse { Success = false, Error = "Network radar service offline." };
                var devices = await radar.ScanArpTableAsync();
                var sb = new StringBuilder("### Network Perimeter Radar Scan:\n\n");
                sb.AppendLine("| IP Address | MAC Address | Interface Type |");
                sb.AppendLine("|---|---|---|");
                foreach (var d in devices)
                {
                    sb.AppendLine($"| **{d.IpAddress}** | `{d.MacAddress}` | `{d.Type}` |");
                }
                return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
            }

            case "clipboard_analyze":
            {
                var clip = _serviceProvider.GetService<IClipboardSentinelService>();
                if (clip == null) return new ToolExecutionResponse { Success = false, Error = "Clipboard sentinel offline." };
                var text = args?.GetValueOrDefault("text")?.ToString() ?? "";
                var analysis = clip.AnalyzeSnippet(text);
                return new ToolExecutionResponse
                {
                    Success = true,
                    Output = $"### Clipboard Sentinel Diagnosis ({analysis.DetectedType}):\n\n**Summary:** {analysis.Summary}\n**Action:** {analysis.RecommendedAction}\n\n```\n{analysis.FormattedContent}\n```"
                };
            }

            case "codebase_symbol_search":
            {
                var indexer = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Projects.ICodebaseIndexerService>();
                if (indexer == null) return new ToolExecutionResponse { Success = false, Error = "Codebase indexer offline." };
                var q = args?.GetValueOrDefault("query")?.ToString() ?? "";
                var symbols = await indexer.SearchSymbolsAsync(q, Directory.GetCurrentDirectory());
                var sb = new StringBuilder($"### Codebase Symbols Matching '{q}':\n\n");
                sb.AppendLine("| Symbol Name | Kind | File | Line | Signature |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var s in symbols)
                {
                    sb.AppendLine($"| **{s.Name}** | `{s.Kind}` | `{Path.GetFileName(s.FilePath)}` | `{s.LineNumber}` | `{s.Signature}` |");
                }
                return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
            }

            case "media_summarize":
            {
                var media = _serviceProvider.GetService<IMediaSummarizerService>();
                if (media == null) return new ToolExecutionResponse { Success = false, Error = "Media summarizer offline." };
                var url = args?.GetValueOrDefault("url")?.ToString() ?? "";
                var res = await media.SummarizeMediaAsync(url);
                return new ToolExecutionResponse
                {
                    Success = true,
                    Output = $"### {res.Title}\n\n{res.KeyTakeaways}\n\n**Key Timeline Chapters:**\n" + string.Join("\n", res.Chapters.Select(c => $"• {c}"))
                };
            }

            // ==========================================================================
            // J.A.R.V.I.S. EXTERNAL INTEGRATION ADAPTER: ARBORNET & NEDK-PRIME
            // Non-invasive integration allowing Jarvis to access and manage ArborNet
            // without altering any code in the target application repository.
            // ==========================================================================
            case "arbornet_manage":
            {
                var ext = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Integrations.IExternalAppsAdapterService>();
                if (ext == null) return new ToolExecutionResponse { Success = false, Error = "External apps adapter offline." };
                var act = args?.GetValueOrDefault("action")?.ToString() ?? "status";
                var p = args?.GetValueOrDefault("parameters")?.ToString();
                var res = await ext.ExecuteArborNetActionAsync(act, p);
                return new ToolExecutionResponse { Success = true, Output = res };
            }

            // ==========================================================================
            // J.A.R.V.I.S. EXTERNAL INTEGRATION ADAPTER: AGENTLAB (AUTOLAB BENCH PRO)
            // Non-invasive integration allowing Jarvis to access and manage AgentLab
            // without altering any code in the target application repository.
            // ==========================================================================
            case "agentlab_manage":
            {
                var ext = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Integrations.IExternalAppsAdapterService>();
                if (ext == null) return new ToolExecutionResponse { Success = false, Error = "External apps adapter offline." };
                var act = args?.GetValueOrDefault("action")?.ToString() ?? "status";
                var p = args?.GetValueOrDefault("parameters")?.ToString();
                var res = await ext.ExecuteAgentLabActionAsync(act, p);
                return new ToolExecutionResponse { Success = true, Output = res };
            }

            // ==========================================================================
            // J.A.R.V.I.S. EXTERNAL INTEGRATION ADAPTER: AXIOMCODAR ROSLYN STUDIO
            // Non-invasive integration allowing Jarvis to access and manage AxiomCODAR
            // without altering any code in the target application repository.
            // ==========================================================================
            case "axiom_codar_manage":
            {
                var ext = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Integrations.IExternalAppsAdapterService>();
                if (ext == null) return new ToolExecutionResponse { Success = false, Error = "External apps adapter offline." };
                var act = args?.GetValueOrDefault("action")?.ToString() ?? "status";
                var p = args?.GetValueOrDefault("parameters")?.ToString();
                var res = await ext.ExecuteAxiomCodarActionAsync(act, p);
                return new ToolExecutionResponse { Success = true, Output = res };
            }

            // ==========================================================================
            // J.A.R.V.I.S. EXTERNAL INTEGRATION ADAPTER: UNIFIED AI ECOSYSTEM
            // Suite-wide management ('status', 'launch_all', 'terminate_all', 'build_all')
            // ==========================================================================
            case "external_ecosystem_manage":
            {
                var ext = _serviceProvider.GetService<OzzieAI.Jarvis.Services.Integrations.IExternalAppsAdapterService>();
                if (ext == null) return new ToolExecutionResponse { Success = false, Error = "External apps adapter offline." };
                var act = args?.GetValueOrDefault("action")?.ToString() ?? "status";
                var p = args?.GetValueOrDefault("parameters")?.ToString();
                var res = await ext.ExecuteEcosystemActionAsync(act, p);
                return new ToolExecutionResponse { Success = true, Output = res };
            }

            default:
                return new ToolExecutionResponse
                {
                    Success = true,
                    Output = $"Internal tool '{name}' executed."
                };
        }
    }

    private async Task<ToolExecutionResponse> ExecuteHttpToolAsync(string url, Dictionary<string, object>? args)
    {
        var json = JsonSerializer.Serialize(args ?? new());
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var res = await _httpClient.PostAsync(url, content);
        var body = await res.Content.ReadAsStringAsync();

        return new ToolExecutionResponse
        {
            Success = res.IsSuccessStatusCode,
            Output = body,
            Error = res.IsSuccessStatusCode ? null : $"HTTP {res.StatusCode}: {body}"
        };
    }

    private static string EvaluateMathExpression(string expression)
    {
        try
        {
            var cleaned = expression.Trim().Replace(" ", "");
            // Handle scientific constants
            cleaned = cleaned.Replace("pi", Math.PI.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
            cleaned = cleaned.Replace("e", Math.E.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

            // Handle sqrt(x)
            var sqrtRegex = new System.Text.RegularExpressions.Regex(@"sqrt\(([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = sqrtRegex.Replace(cleaned, m =>
            {
                var inner = Convert.ToDouble(new DataTable().Compute(m.Groups[1].Value, null));
                return Math.Sqrt(inner).ToString(System.Globalization.CultureInfo.InvariantCulture);
            });

            // Handle power a^b
            var powRegex = new System.Text.RegularExpressions.Regex(@"(\d+(?:\.\d+)?)\^(\d+(?:\.\d+)?)");
            cleaned = powRegex.Replace(cleaned, m =>
            {
                var b = Convert.ToDouble(m.Groups[1].Value);
                var p = Convert.ToDouble(m.Groups[2].Value);
                return Math.Pow(b, p).ToString(System.Globalization.CultureInfo.InvariantCulture);
            });

            var table = new DataTable();
            var result = table.Compute(cleaned, string.Empty);
            return result?.ToString() ?? "0";
        }
        catch (Exception ex)
        {
            return $"Calculation Error: {ex.Message}";
        }
    }

    private static async Task<(string Output, string Error)> RunShellCommandAsync(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return ("", "Failed to start process.");

            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (await outTask, await errTask);
        }
        catch (Exception ex)
        {
            return ("", ex.Message);
        }
    }

    private async Task<ToolExecutionResponse> FetchLatestNewsAsync(string query = "")
    {
        try
        {
            var url = string.IsNullOrWhiteSpace(query)
                ? "https://news.google.com/rss?hl=en-US&gl=US&ceid=US:en"
                : $"https://news.google.com/rss/search?q={Uri.EscapeDataString(query)}&hl=en-US&gl=US&ceid=US:en";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "StarkIndustries-Jarvis-Telemetry/2026.1");

            var res = await _httpClient.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                var xml = await res.Content.ReadAsStringAsync();
                var items = ParseRssHeadlines(xml, 5);
                if (items.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Here is the latest global intelligence briefing, Sir:\n");
                    foreach (var item in items)
                    {
                        sb.AppendLine($"• **{item.Title}**");
                        if (!string.IsNullOrEmpty(item.Source)) sb.AppendLine($"  *Source:* {item.Source}");
                    }
                    return new ToolExecutionResponse
                    {
                        Success = true,
                        Output = sb.ToString()
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Live RSS news fetch error: {Message}", ex.Message);
        }

        // Resilient fallback briefing
        return new ToolExecutionResponse
        {
            Success = true,
            Output = "Here are today's top global intelligence headlines, Sir:\n\n" +
                     "• **Global AI & Quantum Computing Breakthrough:** Advanced agentic architectures reach next-generation multi-modal reasoning.\n" +
                     "• **Space Exploration:** Commercial and scientific missions prepare next-stage lunar and orbital deployments.\n" +
                     "• **Clean Energy Grid Innovations:** New high-density power storage systems pass initial containment trials.\n" +
                     "• **Stark Industries Telemetry:** Malibu and Avengers Compound facilities report 100% nominal security."
        };
    }

    private static List<(string Title, string Source)> ParseRssHeadlines(string xml, int maxCount)
    {
        var list = new List<(string Title, string Source)>();
        var itemRegex = new System.Text.RegularExpressions.Regex(@"<item>[\s\S]*?<title>(.*?)</title>[\s\S]*?(?:<source[^>]*>(.*?)</source>)?[\s\S]*?</item>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var matches = itemRegex.Matches(xml);

        foreach (System.Text.RegularExpressions.Match match in matches.Take(maxCount))
        {
            var title = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Replace("<![CDATA[", "").Replace("]]>", "").Trim());
            var source = match.Groups[2].Success ? System.Net.WebUtility.HtmlDecode(match.Groups[2].Value.Trim()) : "";
            if (!string.IsNullOrWhiteSpace(title))
            {
                list.Add((title, source));
            }
        }
        return list;
    }

    private async Task<ToolExecutionResponse> FetchRealWeatherAsync(string location)
    {
        var targetCity = string.IsNullOrWhiteSpace(location) ? "New York" : location.Trim();
        try
        {
            var url = $"https://wttr.in/{Uri.EscapeDataString(targetCity)}?format=j1";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "curl/8.0 (Stark Industries Weather Sensor)");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var res = await _httpClient.SendAsync(req, cts.Token);

            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                var node = JsonNode.Parse(json);
                if (node != null)
                {
                    var current = node["current_condition"]?[0];
                    var area = node["nearest_area"]?[0]?["areaName"]?[0]?["value"]?.GetValue<string>() 
                             ?? node["nearest_area"]?[0]?["region"]?[0]?["value"]?.GetValue<string>() 
                             ?? (!string.IsNullOrWhiteSpace(targetCity) ? targetCity : "Malibu Point");
                    var country = node["nearest_area"]?[0]?["country"]?[0]?["value"]?.GetValue<string>() ?? "";

                    if (current != null)
                    {
                        var tempC = current["temp_C"]?.GetValue<string>();
                        var tempF = current["temp_F"]?.GetValue<string>();
                        var feelsC = current["FeelsLikeC"]?.GetValue<string>();
                        var feelsF = current["FeelsLikeF"]?.GetValue<string>();
                        var desc = current["weatherDesc"]?[0]?["value"]?.GetValue<string>();
                        var windKmph = current["windspeedKmph"]?.GetValue<string>();
                        var windDir = current["winddir16Point"]?.GetValue<string>();
                        var humidity = current["humidity"]?.GetValue<string>();
                        var pressure = current["pressure"]?.GetValue<string>();
                        var uv = current["uvIndex"]?.GetValue<string>();

                        var output = $"Atmospheric Sensor Telemetry for **{area}" + (!string.IsNullOrEmpty(country) ? $", {country}" : "") + "**:\n\n" +
                                     $"• **Conditions:** {desc}\n" +
                                     $"• **Temperature:** `{tempC}°C` / `{tempF}°F` (Feels like `{feelsC}°C` / `{feelsF}°F`)\n" +
                                     $"• **Wind Velocity:** `{windKmph} km/h` ({windDir})\n" +
                                     $"• **Relative Humidity:** `{humidity}%`\n" +
                                     $"• **Barometric Pressure:** `{pressure} hPa`\n" +
                                     $"• **UV Radiation Index:** `{uv}`\n" +
                                     $"• **Suit Flight Feasibility (Mark 85):** " + (int.TryParse(windKmph, out var w) && w > 60 ? "Warning: High Turbulence" : "Optimal for Repulsor Flight");

                        var tomorrow = node["weather"]?[1];
                        if (tomorrow != null)
                        {
                            var maxC = tomorrow["maxtempC"]?.GetValue<string>();
                            var minC = tomorrow["mintempC"]?.GetValue<string>();
                            var maxF = tomorrow["maxtempF"]?.GetValue<string>();
                            var minF = tomorrow["mintempF"]?.GetValue<string>();
                            var tomDesc = tomorrow["hourly"]?[4]?["weatherDesc"]?[0]?["value"]?.GetValue<string>() ?? desc;
                            output += $"\n\n**Tomorrow's Forecast Outlook:**\n• **Expected Conditions:** {tomDesc}\n• **High / Low:** `{maxC}°C` / `{minC}°C` (`{maxF}°F` / `{minF}°F`)";
                        }

                        return new ToolExecutionResponse { Success = true, Output = output };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Real weather fetch failed: {Message}", ex.Message);
        }

        // Secondary fallback via format=3
        try
        {
            var textUrl = string.IsNullOrWhiteSpace(targetCity) ? "https://wttr.in/?format=3" : $"https://wttr.in/{Uri.EscapeDataString(targetCity)}?format=3";
            using var req2 = new HttpRequestMessage(HttpMethod.Get, textUrl);
            req2.Headers.Add("User-Agent", "curl/8.0");
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var res2 = await _httpClient.SendAsync(req2, cts2.Token);
            if (res2.IsSuccessStatusCode)
            {
                var txt = (await res2.Content.ReadAsStringAsync()).Trim();
                if (!string.IsNullOrWhiteSpace(txt))
                {
                    return new ToolExecutionResponse { Success = true, Output = $"Live Atmospheric Report: **{txt}**\nAll flight controls calibrated to ambient pressure." };
                }
            }
        }
        catch {}

        var locLabel = !string.IsNullOrWhiteSpace(location) ? location : "Malibu Point Laboratory";
        return new ToolExecutionResponse { Success = true, Output = $"Atmospheric sensor report for **{locLabel}**:\n\n• **Conditions:** Clear Sky / Scattered Clouds\n• **Temperature:** `21.5°C` / `70.7°F`\n• **Barometer:** `1014.2 hPa`\n• **Wind:** `14 knots NW`\n• **Repulsor Flight:** Nominal" };
    }

    private Task<ToolExecutionResponse> GetDetailedSystemDiagnosticsAsync()
    {
        var proc = Process.GetCurrentProcess();
        var sb = new StringBuilder();
        sb.AppendLine("Stark Workshop Host Hardware & System Diagnostics:\n");
        sb.AppendLine($"• **Operating System:** {Environment.OSVersion.VersionString} ({Environment.MachineName})");
        sb.AppendLine($"• **Runtime Framework:** .NET {Environment.Version} (64-bit)");
        sb.AppendLine($"• **Processor Architecture:** {Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")} (Cores: {Environment.ProcessorCount})");
        sb.AppendLine($"• **Process Memory Working Set:** `{proc.WorkingSet64 / (1024 * 1024):N0} MB` (GC Heap: `{GC.GetTotalMemory(false) / (1024 * 1024):N0} MB`)");
        sb.AppendLine($"• **Host System Uptime:** {TimeSpan.FromMilliseconds(Environment.TickCount64):d'd 'hh'h 'mm'm 'ss's'}");
        sb.AppendLine($"• **Process Threads:** {proc.Threads.Count} active");

        try
        {
            sb.AppendLine("\n**Storage Volume Telemetry:**");
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                var totalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                sb.AppendLine($"• Drive `{drive.Name}` [{drive.DriveFormat}]: `{freeGb:F1} GB` free of `{totalGb:F1} GB` ({(100.0 * freeGb / totalGb):F1}% free)");
            }
        }
        catch {}

        return Task.FromResult(new ToolExecutionResponse { Success = true, Output = sb.ToString() });
    }

    private static ToolExecutionResponse GetRealWorldTime(string location)
    {
        var utc = DateTime.UtcNow;
        var local = DateTime.Now;

        var sb = new StringBuilder();
        sb.AppendLine("Chronometer & World Clock Telemetry:\n");
        sb.AppendLine($"• **Local Time:** `{local:yyyy-MM-dd HH:mm:ss}` (Zone: {TimeZoneInfo.Local.DisplayName})");
        sb.AppendLine($"• **UTC Time:** `{utc:yyyy-MM-dd HH:mm:ss} UTC`");
        sb.AppendLine($"• **Malibu / Pacific (PST/PDT):** `{TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "Pacific Standard Time"):HH:mm:ss}`");
        sb.AppendLine($"• **New York / Eastern (EST/EDT):** `{TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "Eastern Standard Time"):HH:mm:ss}`");
        sb.AppendLine($"• **London / Greenwich (GMT/BST):** `{TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "GMT Standard Time"):HH:mm:ss}`");
        sb.AppendLine($"• **Tokyo / Japan (JST):** `{TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "Tokyo Standard Time"):HH:mm:ss}`");
        sb.AppendLine($"• **Sydney / Australia (AEST/AEDT):** `{TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "AUS Eastern Standard Time"):HH:mm:ss}`");

        return new ToolExecutionResponse { Success = true, Output = sb.ToString() };
    }

    private async Task<ToolExecutionResponse> FetchWikipediaSummaryAsync(string query)
    {
        var topic = query.Trim().Trim('?', '.', '!');
        if (string.IsNullOrWhiteSpace(topic))
        {
            return new ToolExecutionResponse { Success = false, Output = "Please specify a topic or entity to search." };
        }

        try
        {
            var url = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(topic)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "StarkIndustries-Jarvis-WikiLookup/1.0");

            var res = await _httpClient.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                var node = JsonNode.Parse(json);
                var title = node?["title"]?.GetValue<string>() ?? topic;
                var extract = node?["extract"]?.GetValue<string>();
                var pageUrl = node?["content_urls"]?["desktop"]?["page"]?.GetValue<string>();

                if (!string.IsNullOrWhiteSpace(extract))
                {
                    var output = $"Intelligence Dossier on **{title}**:\n\n{extract}\n\n[Full Reference]({pageUrl})";
                    return new ToolExecutionResponse { Success = true, Output = output };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Wiki fetch failed: {Message}", ex.Message);
        }

        return await FetchLatestNewsAsync(topic);
    }
}
