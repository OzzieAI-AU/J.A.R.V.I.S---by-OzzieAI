namespace OzzieAI.Jarvis.Models;

public class ContactItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FullName { get; set; } = string.Empty;
    public string CallSign { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string PrimaryPhone { get; set; } = string.Empty;
    public string SecondaryPhone { get; set; } = string.Empty;
    public string PrimaryEmail { get; set; } = string.Empty;
    public string WorkEmail { get; set; } = string.Empty;
    public int ClearanceLevel { get; set; } = 1; // 1 to 10
    public string Tags { get; set; } = string.Empty; // e.g. "VIP, Avengers, Executive"
    public string Notes { get; set; } = string.Empty;
    public string AvatarColor { get; set; } = "#00d4ff";
    public string AvatarUrl { get; set; } = string.Empty; // Face photo / image URL or data URI
    public string MetadataJson { get; set; } = "{}"; // Extensible JSON for MCP, CRM, Phone sync
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ContactFilterQuery
{
    public string? Search { get; set; }
    public string? Tag { get; set; }
    public int? MinClearance { get; set; }
    public string? Organization { get; set; }
}
