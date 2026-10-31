using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Scheduler;

public interface IJarvisSchedulerService
{
    Task<List<ScheduledTaskItem>> GetAllTasksAsync();
    Task<ScheduledTaskItem?> GetTaskAsync(string id);
    Task<ScheduledTaskItem> CreateTaskAsync(ScheduledTaskItem task);
    Task<bool> UpdateTaskAsync(ScheduledTaskItem task);
    Task<bool> DeleteTaskAsync(string id);
    Task<string> ExecuteTaskNowAsync(string id);
    ScheduledTaskItem ParseTaskFromNaturalLanguage(string prompt, string? assignedAgentId = null);
}
