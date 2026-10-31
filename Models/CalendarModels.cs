namespace OzzieAI.Jarvis.Models;

public class CalendarAppointment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Attendees { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = DateTime.UtcNow.AddHours(1);
    public DateTime EndTime { get; set; } = DateTime.UtcNow.AddHours(2);
    public int RemindBeforeMinutes { get; set; } = 15;
    public string Status { get; set; } = "Active"; // Active, Dismissed, Cancelled, Completed
    public bool IsRecurring { get; set; } = false;
    public string RecurrencePattern { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DismissedAt { get; set; }
}

public class ScheduledTaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AssignedAgentId { get; set; } = "jarvis_prime";
    public string PromptOrDirective { get; set; } = string.Empty;
    public int RunsPerDay { get; set; } = 1;
    public int IntervalMinutes { get; set; } = 1440; // Default once a day = 1440 min
    public DateTime NextRun { get; set; } = DateTime.UtcNow;
    public DateTime? LastRun { get; set; }
    public string Status { get; set; } = "Active"; // Active, Paused, Completed
    public string? LastOutput { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AppointmentReminderAlert
{
    public CalendarAppointment Appointment { get; set; } = new();
    public string VerbalAlertText { get; set; } = string.Empty;
    public string UrgencyLevel { get; set; } = "High"; // Low, Medium, High, Critical
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
