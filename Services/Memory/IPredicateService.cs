using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Memory;

public interface IPredicateService
{
    Task<List<PredicateItem>> GetAllPredicatesAsync();
    Task<PredicateItem?> GetPredicateAsync(string key);
    Task SavePredicateAsync(PredicateItem item);
    Task DeletePredicateAsync(string key);
    Task<string> BuildPredicateSystemContextAsync();
}
