using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Calendar;

public class CalendarService : ICalendarService
{
    private readonly JarvisDatabase _database;
    private readonly ILogger<CalendarService> _logger;

    public CalendarService(JarvisDatabase database, ILogger<CalendarService> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task<List<CalendarAppointment>> GetAllAppointmentsAsync()
    {
        var list = new List<CalendarAppointment>();
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Title, Description, Attendees, StartTime, EndTime, RemindBeforeMinutes, Status, IsRecurring, RecurrencePattern, CreatedAt, DismissedAt FROM CalendarAppointments ORDER BY StartTime ASC";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(MapAppointment(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching appointments");
        }
        return list;
    }

    public async Task<CalendarAppointment?> GetAppointmentAsync(string id)
    {
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Title, Description, Attendees, StartTime, EndTime, RemindBeforeMinutes, Status, IsRecurring, RecurrencePattern, CreatedAt, DismissedAt FROM CalendarAppointments WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapAppointment(reader);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting appointment {Id}", id);
        }
        return null;
    }

    public async Task<CalendarAppointment> CreateAppointmentAsync(CalendarAppointment appointment)
    {
        if (string.IsNullOrWhiteSpace(appointment.Id))
        {
            appointment.Id = Guid.NewGuid().ToString("N");
        }

        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO CalendarAppointments (Id, Title, Description, Attendees, StartTime, EndTime, RemindBeforeMinutes, Status, IsRecurring, RecurrencePattern, CreatedAt, DismissedAt)
                VALUES ($id, $title, $desc, $att, $start, $end, $remind, $status, $rec, $recPat, $created, $dismissed)
            ";
            cmd.Parameters.AddWithValue("$id", appointment.Id);
            cmd.Parameters.AddWithValue("$title", appointment.Title);
            cmd.Parameters.AddWithValue("$desc", appointment.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("$att", appointment.Attendees ?? string.Empty);
            cmd.Parameters.AddWithValue("$start", appointment.StartTime.ToString("o"));
            cmd.Parameters.AddWithValue("$end", appointment.EndTime.ToString("o"));
            cmd.Parameters.AddWithValue("$remind", appointment.RemindBeforeMinutes);
            cmd.Parameters.AddWithValue("$status", appointment.Status);
            cmd.Parameters.AddWithValue("$rec", appointment.IsRecurring ? 1 : 0);
            cmd.Parameters.AddWithValue("$recPat", appointment.RecurrencePattern ?? string.Empty);
            cmd.Parameters.AddWithValue("$created", appointment.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$dismissed", (object?)appointment.DismissedAt?.ToString("o") ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Created appointment {Title} ({Id}) for {StartTime}", appointment.Title, appointment.Id, appointment.StartTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating appointment");
            throw;
        }

        return appointment;
    }

    public async Task<bool> UpdateAppointmentAsync(CalendarAppointment appointment)
    {
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE CalendarAppointments SET
                    Title = $title,
                    Description = $desc,
                    Attendees = $att,
                    StartTime = $start,
                    EndTime = $end,
                    RemindBeforeMinutes = $remind,
                    Status = $status,
                    IsRecurring = $rec,
                    RecurrencePattern = $recPat,
                    DismissedAt = $dismissed
                WHERE Id = $id
            ";
            cmd.Parameters.AddWithValue("$id", appointment.Id);
            cmd.Parameters.AddWithValue("$title", appointment.Title);
            cmd.Parameters.AddWithValue("$desc", appointment.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("$att", appointment.Attendees ?? string.Empty);
            cmd.Parameters.AddWithValue("$start", appointment.StartTime.ToString("o"));
            cmd.Parameters.AddWithValue("$end", appointment.EndTime.ToString("o"));
            cmd.Parameters.AddWithValue("$remind", appointment.RemindBeforeMinutes);
            cmd.Parameters.AddWithValue("$status", appointment.Status);
            cmd.Parameters.AddWithValue("$rec", appointment.IsRecurring ? 1 : 0);
            cmd.Parameters.AddWithValue("$recPat", appointment.RecurrencePattern ?? string.Empty);
            cmd.Parameters.AddWithValue("$dismissed", (object?)appointment.DismissedAt?.ToString("o") ?? DBNull.Value);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating appointment {Id}", appointment.Id);
            return false;
        }
    }

    public async Task<bool> DismissAppointmentAsync(string id)
    {
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE CalendarAppointments SET Status = 'Dismissed', DismissedAt = $now WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dismissing appointment {Id}", id);
            return false;
        }
    }

    public async Task<bool> CancelAppointmentAsync(string id)
    {
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE CalendarAppointments SET Status = 'Cancelled' WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling appointment {Id}", id);
            return false;
        }
    }

    public async Task<bool> DeleteAppointmentAsync(string id)
    {
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CalendarAppointments WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting appointment {Id}", id);
            return false;
        }
    }

    public async Task<List<CalendarAppointment>> GetPendingRemindersAsync(DateTime asOfTime, int leadMinutes)
    {
        var dueList = new List<CalendarAppointment>();
        var all = await GetAllAppointmentsAsync();

        foreach (var a in all)
        {
            // Only trigger for Active appointments
            if (a.Status != "Active") continue;

            // Trigger when within the reminder window up to the start time (and up to 30 mins after start time if not dismissed)
            var reminderTime = a.StartTime.AddMinutes(-Math.Max(5, a.RemindBeforeMinutes));
            var expiryWindow = a.StartTime.AddMinutes(45);

            if (asOfTime >= reminderTime && asOfTime <= expiryWindow && a.DismissedAt == null)
            {
                dueList.Add(a);
            }
        }

        return dueList;
    }

    public CalendarAppointment ParseAppointmentFromNaturalLanguage(string prompt)
    {
        var now = DateTime.Now;
        var lower = prompt.ToLowerInvariant();

        // Target date calculation
        var targetDate = now.Date;
        if (lower.Contains("tomorrow"))
        {
            targetDate = now.AddDays(1).Date;
        }
        else if (lower.Contains("today"))
        {
            targetDate = now.Date;
        }
        else if (lower.Contains("next week"))
        {
            targetDate = now.AddDays(7).Date;
        }

        // Time calculation
        var targetTime = new TimeSpan(10, 0, 0); // default 10:00 AM
        var timeMatch = Regex.Match(lower, @"\b(\d{1,2})(?::(\d{2}))?\s*(am|pm)?\b", RegexOptions.IgnoreCase);
        if (timeMatch.Success)
        {
            if (int.TryParse(timeMatch.Groups[1].Value, out var hour))
            {
                var min = 0;
                if (timeMatch.Groups[2].Success) int.TryParse(timeMatch.Groups[2].Value, out min);
                var ampm = timeMatch.Groups[3].Value.ToLower();

                if (ampm == "pm" && hour < 12) hour += 12;
                if (ampm == "am" && hour == 12) hour = 0;

                targetTime = new TimeSpan(hour, min, 0);
            }
        }

        var startDateTime = targetDate.Add(targetTime);
        var endDateTime = startDateTime.AddHours(1);

        // Attendees extraction ("with Renee", "with Mr. Stark and Pepper")
        var attendees = "";
        var withMatch = Regex.Match(prompt, @"\bwith\s+([A-Za-z\s\.\&]+?)(?=\s+(?:to|at|for|about|on|\.|\!|\?|$))", RegexOptions.IgnoreCase);
        if (withMatch.Success)
        {
            attendees = withMatch.Groups[1].Value.Trim();
        }

        // Topic / Description extraction ("to discuss Global Domination", "about Q3 revenue")
        var description = "";
        var topicMatch = Regex.Match(prompt, @"\b(?:to|about|discuss|for)\s+(.+?)(?=\.|\!|\?|$)", RegexOptions.IgnoreCase);
        if (topicMatch.Success)
        {
            description = topicMatch.Groups[1].Value.Trim();
        }

        var title = "Appointment";
        if (!string.IsNullOrWhiteSpace(attendees) && !string.IsNullOrWhiteSpace(description))
        {
            title = $"Meeting with {attendees}: {char.ToUpper(description[0]) + description[1..]}";
        }
        else if (!string.IsNullOrWhiteSpace(attendees))
        {
            title = $"Meeting with {attendees}";
        }
        else if (!string.IsNullOrWhiteSpace(description))
        {
            title = char.ToUpper(description[0]) + description[1..];
        }

        return new CalendarAppointment
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Description = !string.IsNullOrWhiteSpace(description) ? description : title,
            Attendees = attendees,
            StartTime = startDateTime.ToUniversalTime(),
            EndTime = endDateTime.ToUniversalTime(),
            RemindBeforeMinutes = 15,
            Status = "Active",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static CalendarAppointment MapAppointment(SqliteDataReader r)
    {
        return new CalendarAppointment
        {
            Id = r.GetString(0),
            Title = r.GetString(1),
            Description = r.IsDBNull(2) ? "" : r.GetString(2),
            Attendees = r.IsDBNull(3) ? "" : r.GetString(3),
            StartTime = DateTime.Parse(r.GetString(4), null, DateTimeStyles.RoundtripKind),
            EndTime = DateTime.Parse(r.GetString(5), null, DateTimeStyles.RoundtripKind),
            RemindBeforeMinutes = r.GetInt32(6),
            Status = r.GetString(7),
            IsRecurring = r.GetInt32(8) == 1,
            RecurrencePattern = r.IsDBNull(9) ? "" : r.GetString(9),
            CreatedAt = DateTime.Parse(r.GetString(10), null, DateTimeStyles.RoundtripKind),
            DismissedAt = r.IsDBNull(11) ? null : DateTime.Parse(r.GetString(11), null, DateTimeStyles.RoundtripKind)
        };
    }
}
