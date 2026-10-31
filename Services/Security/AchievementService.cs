namespace OzzieAI.Jarvis.Services.Security;

public record StarkAchievement(string Id, string Title, string Description, string Icon, bool Unlocked, DateTime? UnlockedAt);

public interface IAchievementService
{
    List<StarkAchievement> GetAchievements();
    StarkAchievement? UnlockAchievement(string id);
}

public class AchievementService : IAchievementService
{
    private readonly List<StarkAchievement> _achievements = new()
    {
        new("first_boot", "Proof That Tony Stark Has a Heart", "Boot J.A.R.V.I.S. and achieve 100% Arc Reactor isotope containment.", "⚛️", true, DateTime.UtcNow.AddHours(-3)),
        new("first_code", "I Am Iron Man", "Execute first script in Isolated Polyglot Sandbox without errors.", "🦾", true, DateTime.UtcNow.AddHours(-1)),
        new("sentinel_passed", "Level 10 Clearance", "Pass a Terminal Sentinel security challenge token with master passcode.", "🛡️", false, null),
        new("swarm_deployed", "House Party Protocol", "Orchestrate 4 autonomous agents concurrently in multi-agent swarm.", "🚀", false, null),
        new("cad_inspected", "Bespoke Engineering", "Render and inspect a custom 3D CAD schematic in holographic matrix.", "📐", true, DateTime.UtcNow.AddMinutes(-30)),
        new("stealth_engaged", "Clean Slate Operative", "Trigger an automated snapshot backup and seal encrypted Stark Vault.", "🔒", false, null)
    };

    public List<StarkAchievement> GetAchievements()
    {
        lock (_achievements)
        {
            return _achievements.ToList();
        }
    }

    public StarkAchievement? UnlockAchievement(string id)
    {
        lock (_achievements)
        {
            var item = _achievements.FirstOrDefault(a => a.Id == id);
            if (item != null && !item.Unlocked)
            {
                var idx = _achievements.IndexOf(item);
                var updated = item with { Unlocked = true, UnlockedAt = DateTime.UtcNow };
                _achievements[idx] = updated;
                return updated;
            }
            return item;
        }
    }
}
