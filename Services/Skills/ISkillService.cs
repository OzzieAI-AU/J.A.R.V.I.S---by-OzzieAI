using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Skills;

public interface ISkillService
{
    Task<List<SkillItem>> GetAllSkillsAsync();
    Task<SkillItem?> GetSkillAsync(string id);
    Task SaveSkillAsync(SkillItem skill);
    Task DeleteSkillAsync(string id);
}
