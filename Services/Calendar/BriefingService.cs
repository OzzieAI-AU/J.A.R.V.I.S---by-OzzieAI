using System.Text;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Calendar;
using OzzieAI.Jarvis.Services.Config;
using OzzieAI.Jarvis.Services.Projects;
using OzzieAI.Jarvis.Services.Sensors;
using OzzieAI.Jarvis.Services.Storage;

namespace OzzieAI.Jarvis.Services.Calendar;

public interface IBriefingService
{
    Task<string> GenerateMorningBriefingAsync();
}

public class BriefingService : IBriefingService
{
    private readonly ICalendarService _calendarService;
    private readonly IProjectService _projectService;
    private readonly IGhostDriveService _ghostDriveService;
    private readonly IMcpSensorService _sensorService;
    private readonly IJarvisConfigService _configService;
    private readonly ILogger<BriefingService> _logger;

    public BriefingService(
        ICalendarService calendarService,
        IProjectService projectService,
        IGhostDriveService ghostDriveService,
        IMcpSensorService sensorService,
        IJarvisConfigService configService,
        ILogger<BriefingService> logger)
    {
        _calendarService = calendarService;
        _projectService = projectService;
        _ghostDriveService = ghostDriveService;
        _sensorService = sensorService;
        _configService = configService;
        _logger = logger;
    }

    public async Task<string> GenerateMorningBriefingAsync()
    {
        var config = _configService.GetConfig();
        var user = config.Profile?.Honorific ?? "Sir";
        var appts = await _calendarService.GetAllAppointmentsAsync();
        var projects = await _projectService.GetAllProjectsAsync();
        var storageAlerts = await _ghostDriveService.CheckLowSpaceAlertsAsync();
        var tel = _sensorService.GetCurrentTelemetry();

        var today = DateTime.UtcNow.Date;
        var todayAppts = appts.Where(a => a.StartTime.Date == today && a.Status == "Active").ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"### Good morning, {user}. Tactical Executive Briefing:\n");
        sb.AppendLine($"• **Facility Chronometer:** `{DateTime.Now:dddd, MMMM dd, yyyy - h:mm tt}` ({TimeZoneInfo.Local.DisplayName})");
        sb.AppendLine($"• **Arc Reactor Harmonic Output:** `{tel.ArcReactorOutputGigawatts:F2} GW` | Core Temp: `{tel.CoreTemperatureCelsius:F1}°C` (Isotope Stability: 100%)");

        // Calendar Agenda
        if (todayAppts.Count > 0)
        {
            sb.AppendLine($"\n**📅 Today's Strategic Agenda ({todayAppts.Count} Events Scheduled):**");
            foreach (var a in todayAppts)
            {
                sb.AppendLine($"  - **{a.StartTime.ToLocalTime():h:mm tt}**: {a.Title} {(string.IsNullOrEmpty(a.Attendees) ? "" : $"*(with {a.Attendees})*")}");
            }
        }
        else
        {
            sb.AppendLine("\n**📅 Calendar Agenda:** No scheduled briefings or appointments for today, Sir. Tactical calendar clear.");
        }

        // Active Projects
        var activeProjects = projects.Where(p => p.Status == "Active").Take(3).ToList();
        if (activeProjects.Count > 0)
        {
            sb.AppendLine("\n**📁 High-Priority Project Matrix:**");
            foreach (var p in activeProjects)
            {
                var pendingTasks = p.Tasks.Count(t => t.Status != "Completed");
                sb.AppendLine($"  - **{p.Name}** ({p.Category} | Priority: `{p.Priority}`): {p.ProgressPercent:F0}% Complete ({pendingTasks} pending tasks remaining)");
            }
        }

        // Storage & Defense Alerts
        if (storageAlerts.Count > 0)
        {
            sb.AppendLine("\n**⚠️ Environmental & Storage Alerts:**");
            foreach (var alert in storageAlerts)
            {
                sb.AppendLine($"  - `{alert.DriveName}`: {alert.AlertMessage}");
            }
        }
        else
        {
            sb.AppendLine("\n**🛡️ Perimeter & Drive Integrity:** All physical and virtual Ghost Drive partitions report nominal capacity.");
        }

        sb.AppendLine("\n*All lab telemetry and neural links are primed. How shall we begin, Sir?*");

        return sb.ToString();
    }
}
