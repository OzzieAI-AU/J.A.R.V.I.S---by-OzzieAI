using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Hubs;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Agents;
using OzzieAI.Jarvis.Services.Calendar;
using OzzieAI.Jarvis.Services.Config;
using OzzieAI.Jarvis.Services.Memory;
using OzzieAI.Jarvis.Services.Storage;
using OzzieAI.Jarvis.Services.Tools;

namespace OzzieAI.Jarvis.Services.Scheduler;

public class JarvisSchedulerService : BackgroundService, IJarvisSchedulerService
{
    private readonly JarvisDatabase _database;
    private readonly ICalendarService _calendarService;
    private readonly IJarvisConfigService _configService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JarvisSchedulerService> _logger;

    // Track reminders pushed in memory to avoid repeating every 10s if user hasn't dismissed yet
    private readonly Dictionary<string, DateTime> _lastRemindedAt = new();
    private DateTime _lastStorageCheck = DateTime.MinValue;

    public JarvisSchedulerService(
        JarvisDatabase database,
        ICalendarService calendarService,
        IJarvisConfigService configService,
        IServiceProvider serviceProvider,
        ILogger<JarvisSchedulerService> logger)
    {
        _database = database;
        _calendarService = calendarService;
        _configService = configService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JarvisSchedulerService background engine activated.");

        // Initial brief warm-up delay
        await Task.Delay(3000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = _configService.GetConfig();
                if (config.Scheduler.Enabled)
                {
                    await CheckAppointmentRemindersAsync();
                    await CheckScheduledTasksAsync();
                    await CheckStorageAlertsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in JarvisSchedulerService tick");
            }

            var delaySec = Math.Max(5, _configService.GetConfig().Scheduler.CheckIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySec), stoppingToken);
        }
    }

    private async Task CheckAppointmentRemindersAsync()
    {
        var config = _configService.GetConfig();
        var pending = await _calendarService.GetPendingRemindersAsync(DateTime.UtcNow, config.Scheduler.DefaultReminderLeadMinutes);

        if (pending.Count == 0) return;

        using var scope = _serviceProvider.CreateScope();
        var hubContext = scope.ServiceProvider.GetService<IHubContext<JarvisHub>>();
        if (hubContext == null) return;

        foreach (var appt in pending)
        {
            // Remind every 45 seconds until dismissed
            if (_lastRemindedAt.TryGetValue(appt.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 45)
            {
                continue;
            }

            _lastRemindedAt[appt.Id] = DateTime.UtcNow;

            var localTime = appt.StartTime.ToLocalTime();
            var attendeesNotice = !string.IsNullOrWhiteSpace(appt.Attendees) ? $" with {appt.Attendees}" : "";
            var verbalText = $"Sir, incoming calendar reminder: {appt.Title}{attendeesNotice} scheduled for {localTime:h:mm tt}.";

            var alert = new AppointmentReminderAlert
            {
                Appointment = appt,
                VerbalAlertText = verbalText,
                UrgencyLevel = "High",
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Firing Appointment Reminder alert: {Title} ({Id})", appt.Title, appt.Id);
            await hubContext.Clients.All.SendAsync("OnAppointmentReminder", alert);
        }
    }

    private async Task CheckStorageAlertsAsync()
    {
        var config = _configService.GetConfig();
        if (!config.Storage.EnableLowSpaceAlerts) return;

        // Check storage alerts every 60 seconds
        if ((DateTime.UtcNow - _lastStorageCheck).TotalSeconds < 60) return;
        _lastStorageCheck = DateTime.UtcNow;

        using var scope = _serviceProvider.CreateScope();
        var ghostService = scope.ServiceProvider.GetService<IGhostDriveService>();
        var hubContext = scope.ServiceProvider.GetService<IHubContext<JarvisHub>>();
        if (ghostService == null || hubContext == null) return;

        var alerts = await ghostService.CheckLowSpaceAlertsAsync();
        foreach (var a in alerts)
        {
            _logger.LogWarning("Low space alert on drive {Name}: {Gb} GB free ({Pct}%)", a.DriveName, a.RemainingGb, a.PercentFree);
            await hubContext.Clients.All.SendAsync("OnLowSpaceAlert", a);
        }
    }

    private async Task CheckScheduledTasksAsync()
    {
        var tasks = await GetAllTasksAsync();
        var now = DateTime.UtcNow;

        foreach (var t in tasks)
        {
            if (t.Status != "Active") continue;

            if (t.NextRun <= now)
            {
                _logger.LogInformation("Executing autonomous scheduled task: {Name} ({Id})", t.Name, t.Id);
                await ExecuteTaskInternalAsync(t);
            }
        }
    }

    private async Task<string> ExecuteTaskInternalAsync(ScheduledTaskItem task)
    {
        using var scope = _serviceProvider.CreateScope();
        var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();
        var predicateService = scope.ServiceProvider.GetRequiredService<IPredicateService>();
        var hubContext = scope.ServiceProvider.GetService<IHubContext<JarvisHub>>();

        string output;
        try
        {
            // Execute research query or general tool query
            var query = !string.IsNullOrWhiteSpace(task.PromptOrDirective) ? task.PromptOrDirective : task.Name;
            var toolRes = await toolService.ExecuteToolAsync(new ToolExecutionRequest
            {
                ToolNameOrId = "search_web",
                Arguments = new() { ["query"] = query }
            });

            output = !string.IsNullOrWhiteSpace(toolRes.Output) ? toolRes.Output : toolRes.Error ?? "Task executed with no output.";

            // Save result to persistent memory predicate
            var safeKey = $"AutonomousTask_{Regex.Replace(task.Name, @"[^a-zA-Z0-9_]", "")}";
            await predicateService.SavePredicateAsync(new PredicateItem
            {
                Key = safeKey,
                Value = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm}: {output.Replace("\n", " ")[..Math.Min(200, output.Length)]}...",
                Category = "AutonomousResearch",
                Description = $"Latest findings from scheduled task '{task.Name}'"
            });
        }
        catch (Exception ex)
        {
            output = $"Autonomous execution error: {ex.Message}";
            _logger.LogError(ex, "Failed to execute task {Name}", task.Name);
        }

        task.LastRun = DateTime.UtcNow;
        task.NextRun = DateTime.UtcNow.AddMinutes(Math.Max(5, task.IntervalMinutes));
        task.LastOutput = output;

        await UpdateTaskAsync(task);

        if (hubContext != null)
        {
            await hubContext.Clients.All.SendAsync("OnAgentTaskCompleted", new
            {
                TaskId = task.Id,
                TaskName = task.Name,
                Output = output,
                CompletedAt = DateTime.UtcNow
            });
        }

        return output;
    }

    public async Task<List<ScheduledTaskItem>> GetAllTasksAsync()
    {
        var list = new List<ScheduledTaskItem>();
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Description, AssignedAgentId, PromptOrDirective, RunsPerDay, IntervalMinutes, NextRun, LastRun, Status, LastOutput, CreatedAt FROM ScheduledTasks ORDER BY NextRun ASC";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(MapTask(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting scheduled tasks");
        }
        return list;
    }

    public async Task<ScheduledTaskItem?> GetTaskAsync(string id)
    {
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Description, AssignedAgentId, PromptOrDirective, RunsPerDay, IntervalMinutes, NextRun, LastRun, Status, LastOutput, CreatedAt FROM ScheduledTasks WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapTask(reader);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task {Id}", id);
        }
        return null;
    }

    public async Task<ScheduledTaskItem> CreateTaskAsync(ScheduledTaskItem task)
    {
        if (string.IsNullOrWhiteSpace(task.Id)) task.Id = Guid.NewGuid().ToString("N");

        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ScheduledTasks (Id, Name, Description, AssignedAgentId, PromptOrDirective, RunsPerDay, IntervalMinutes, NextRun, LastRun, Status, LastOutput, CreatedAt)
                VALUES ($id, $name, $desc, $agent, $prompt, $runs, $interval, $next, $last, $status, $out, $created)
            ";
            cmd.Parameters.AddWithValue("$id", task.Id);
            cmd.Parameters.AddWithValue("$name", task.Name);
            cmd.Parameters.AddWithValue("$desc", task.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("$agent", task.AssignedAgentId);
            cmd.Parameters.AddWithValue("$prompt", task.PromptOrDirective ?? string.Empty);
            cmd.Parameters.AddWithValue("$runs", task.RunsPerDay);
            cmd.Parameters.AddWithValue("$interval", task.IntervalMinutes);
            cmd.Parameters.AddWithValue("$next", task.NextRun.ToString("o"));
            cmd.Parameters.AddWithValue("$last", (object?)task.LastRun?.ToString("o") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", task.Status);
            cmd.Parameters.AddWithValue("$out", (object?)task.LastOutput ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", task.CreatedAt.ToString("o"));

            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Saved scheduled task {Name} ({Id}) to run {Runs} times a day", task.Name, task.Id, task.RunsPerDay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving scheduled task {Name}", task.Name);
            throw;
        }

        return task;
    }

    public async Task<bool> UpdateTaskAsync(ScheduledTaskItem task)
    {
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE ScheduledTasks SET
                    Name = $name,
                    Description = $desc,
                    AssignedAgentId = $agent,
                    PromptOrDirective = $prompt,
                    RunsPerDay = $runs,
                    IntervalMinutes = $interval,
                    NextRun = $next,
                    LastRun = $last,
                    Status = $status,
                    LastOutput = $out
                WHERE Id = $id
            ";
            cmd.Parameters.AddWithValue("$id", task.Id);
            cmd.Parameters.AddWithValue("$name", task.Name);
            cmd.Parameters.AddWithValue("$desc", task.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("$agent", task.AssignedAgentId);
            cmd.Parameters.AddWithValue("$prompt", task.PromptOrDirective ?? string.Empty);
            cmd.Parameters.AddWithValue("$runs", task.RunsPerDay);
            cmd.Parameters.AddWithValue("$interval", task.IntervalMinutes);
            cmd.Parameters.AddWithValue("$next", task.NextRun.ToString("o"));
            cmd.Parameters.AddWithValue("$last", (object?)task.LastRun?.ToString("o") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", task.Status);
            cmd.Parameters.AddWithValue("$out", (object?)task.LastOutput ?? DBNull.Value);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating task {Id}", task.Id);
            return false;
        }
    }

    public async Task<bool> DeleteTaskAsync(string id)
    {
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ScheduledTasks WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting task {Id}", id);
            return false;
        }
    }

    public async Task<string> ExecuteTaskNowAsync(string id)
    {
        var task = await GetTaskAsync(id);
        if (task == null) return "Task not found.";
        return await ExecuteTaskInternalAsync(task);
    }

    public ScheduledTaskItem ParseTaskFromNaturalLanguage(string prompt, string? assignedAgentId = null)
    {
        var lower = prompt.ToLowerInvariant();

        // Extract run frequency (e.g. "7 times a day", "3 times a day", "once a day", "every 2 hours")
        var runsPerDay = 1;
        var timesMatch = Regex.Match(lower, @"(\d+)\s*(?:times|x)\s*(?:a|per)\s*day", RegexOptions.IgnoreCase);
        if (timesMatch.Success && int.TryParse(timesMatch.Groups[1].Value, out var parsedRuns) && parsedRuns > 0)
        {
            runsPerDay = parsedRuns;
        }
        else if (lower.Contains("twice a day"))
        {
            runsPerDay = 2;
        }
        else if (lower.Contains("hourly") || lower.Contains("every hour"))
        {
            runsPerDay = 24;
        }

        var intervalMinutes = Math.Max(10, (int)Math.Round(1440.0 / runsPerDay));

        // Extract research or task topic (e.g. "research AI", "monitor crypto", "check security")
        var topic = "AI and Emerging Technology";
        var topicMatch = Regex.Match(prompt, @"(?:to\s+|for\s+)?(?:research|analyze|monitor|track|scan|check)\s+([^,\.]+?)(?:\s+and\s+run|\s+every|\s+\d+\s+times|\.|\!|\?|$)", RegexOptions.IgnoreCase);
        if (topicMatch.Success)
        {
            topic = topicMatch.Groups[1].Value.Trim();
        }

        var name = $"Autonomous Research: {char.ToUpper(topic[0]) + topic[1..]}";

        return new ScheduledTaskItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Description = $"Autonomous research on '{topic}' scheduled to run {runsPerDay} times per day.",
            AssignedAgentId = !string.IsNullOrWhiteSpace(assignedAgentId) ? assignedAgentId : "jarvis_prime",
            PromptOrDirective = $"Latest breakthroughs, news, and technical developments in {topic}",
            RunsPerDay = runsPerDay,
            IntervalMinutes = intervalMinutes,
            NextRun = DateTime.UtcNow.AddMinutes(2), // start first run in 2 minutes
            Status = "Active",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static ScheduledTaskItem MapTask(SqliteDataReader r)
    {
        return new ScheduledTaskItem
        {
            Id = r.GetString(0),
            Name = r.GetString(1),
            Description = r.IsDBNull(2) ? "" : r.GetString(2),
            AssignedAgentId = r.GetString(3),
            PromptOrDirective = r.GetString(4),
            RunsPerDay = r.GetInt32(5),
            IntervalMinutes = r.GetInt32(6),
            NextRun = DateTime.Parse(r.GetString(7), null, DateTimeStyles.RoundtripKind),
            LastRun = r.IsDBNull(8) ? null : DateTime.Parse(r.GetString(8), null, DateTimeStyles.RoundtripKind),
            Status = r.GetString(9),
            LastOutput = r.IsDBNull(10) ? null : r.GetString(10),
            CreatedAt = DateTime.Parse(r.GetString(11), null, DateTimeStyles.RoundtripKind)
        };
    }
}
