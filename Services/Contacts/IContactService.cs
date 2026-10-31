using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Contacts;

public interface IContactService
{
    Task<List<ContactItem>> GetAllContactsAsync(string? search = null, string? tag = null, int? minClearance = null);
    Task<ContactItem?> GetContactByIdAsync(string id);
    Task<ContactItem?> FindContactByNameOrCallSignAsync(string nameOrCallSign);
    Task<ContactItem> CreateContactAsync(ContactItem contact);
    Task<ContactItem> UpdateContactAsync(ContactItem contact);
    Task<bool> DeleteContactAsync(string id);
    Task<List<ContactItem>> SearchContactsAsync(string query);
}
