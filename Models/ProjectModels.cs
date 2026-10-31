namespace OzzieAI.Jarvis.Models;

public class JarvisProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Engineering"; // Engineering, Tactical, Defense, AI/Research, Automation
    public string Status { get; set; } = "Active"; // Planning, Active, InProgress, Review, Completed, Paused
    public string Priority { get; set; } = "High"; // Critical, High, Medium, Low
    public double ProgressPercent { get; set; } = 0.0; // 0 to 100
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }
    
    // Ghost Drive Storage Integration
    public string GhostDriveTag { get; set; } = "PROJECTS";
    public string GhostDrivePath { get; set; } = string.Empty; // e.g. "Projects/Mark-85-Nano-Infusion"
    
    // Lead Agent & Automation
    public string LeadAgentId { get; set; } = "agent-jarvis-core";
    public string LeadAgentName { get; set; } = "J.A.R.V.I.S. Core";
    public bool AutomationEnabled { get; set; } = true;
    public string Color { get; set; } = "#00d4ff";
    public string Tags { get; set; } = "Nanotech, Armor, Stark Labs";
    
    // Research Dossier
    public string ResearchDossier { get; set; } = string.Empty;
    
    // Child Collections
    public List<ProjectTaskItem> Tasks { get; set; } = new();
    public List<ProjectArtifactItem> Artifacts { get; set; } = new();
}

public class ProjectTaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, InProgress, AutonomousExecuting, Completed, Failed
    public string Priority { get; set; } = "Medium"; // Critical, High, Medium, Low
    public string AssignedAgentId { get; set; } = "agent-jarvis-core";
    public string AssignedAgentName { get; set; } = "J.A.R.V.I.S.";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool CanAutoExecute { get; set; } = true;
    public string AutomationResult { get; set; } = string.Empty;
}

public class ProjectArtifactItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; } = 0;
    public string FileType { get; set; } = "text/plain";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}

public class ProjectResearchRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string ResearchTopic { get; set; } = string.Empty;
    public string? SpecificDifficulty { get; set; }
    public string? AgentId { get; set; }
}
