namespace OzzieAI.Jarvis.Models;

public class PredicateItem
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Category { get; set; } = "General"; // Protocol, Identity, Environment, Suit, Security
    public string Description { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
