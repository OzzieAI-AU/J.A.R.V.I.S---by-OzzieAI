using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Calendar;

public interface ICalendarService
{
    Task<List<CalendarAppointment>> GetAllAppointmentsAsync();
    Task<CalendarAppointment?> GetAppointmentAsync(string id);
    Task<CalendarAppointment> CreateAppointmentAsync(CalendarAppointment appointment);
    Task<bool> UpdateAppointmentAsync(CalendarAppointment appointment);
    Task<bool> DismissAppointmentAsync(string id);
    Task<bool> CancelAppointmentAsync(string id);
    Task<bool> DeleteAppointmentAsync(string id);
    Task<List<CalendarAppointment>> GetPendingRemindersAsync(DateTime asOfTime, int leadMinutes);
    CalendarAppointment ParseAppointmentFromNaturalLanguage(string prompt);
}
