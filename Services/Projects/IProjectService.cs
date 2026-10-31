using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Projects;

public interface IProjectService
{
    Task<List<JarvisProject>> GetAllProjectsAsync(string? category = null, string? status = null);
    Task<JarvisProject?> GetProjectByIdAsync(string id);
    Task<JarvisProject> CreateProjectAsync(JarvisProject project);
    Task<JarvisProject> UpdateProjectAsync(JarvisProject project);
    Task<bool> DeleteProjectAsync(string id);
    
    // Project Tasks
    Task<List<ProjectTaskItem>> GetTasksByProjectIdAsync(string projectId);
    Task<ProjectTaskItem> AddTaskAsync(string projectId, ProjectTaskItem task);
    Task<ProjectTaskItem> UpdateTaskAsync(ProjectTaskItem task);
    Task<bool> DeleteTaskAsync(string projectId, string taskId);
    Task<ProjectTaskItem> ExecuteAutonomousTaskAsync(string projectId, string taskId);

    // Research Agent
    Task<string> RunResearchAgentAsync(ProjectResearchRequest request);

    // Ghost Drive Storage Integration
    Task<List<ProjectArtifactItem>> GetProjectFilesAsync(string projectId);
    Task<bool> SaveProjectFileAsync(string projectId, string relativePath, string content);
    Task<string> ReadProjectFileAsync(string projectId, string relativePath);
}
