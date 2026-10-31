using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Llm;

namespace OzzieAI.Jarvis.Services.Voice;

public record MeetingMinutes(string Title, List<string> KeyTopics, List<string> ActionItems, string Summary);

public interface IMeetingEarService
{
    Task<MeetingMinutes> SummarizeMeetingTranscriptAsync(string rawTranscript);
}

public class MeetingEarService : IMeetingEarService
{
    private readonly LlmProviderFactory _llmFactory;

    public MeetingEarService(LlmProviderFactory llmFactory)
    {
        _llmFactory = llmFactory;
    }

    public async Task<MeetingMinutes> SummarizeMeetingTranscriptAsync(string rawTranscript)
    {
        var (provider, _, _) = await _llmFactory.GetResilientProviderAsync();
        var prompt = $"Analyze this audio transcript from a live meeting or discussion and format an executive summary:\n\n{rawTranscript}\n\nProvide:\n1. Title\n2. Key Topics Discussed\n3. Action Items & Assignees\n4. Executive Conclusion";

        var response = await provider.GenerateResponseAsync(
            new List<ChatMessage> { new() { Role = "user", Content = prompt } },
            "You are J.A.R.V.I.S., Tony Stark's executive assistant.");
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var topics = new List<string>();
        var actions = new List<string>();

        foreach (var l in lines)
        {
            if (l.StartsWith("- ") || l.StartsWith("* ") || l.StartsWith("• "))
            {
                if (topics.Count < 5) topics.Add(l.TrimStart('-', '*', '•', ' '));
                else actions.Add(l.TrimStart('-', '*', '•', ' '));
            }
        }

        return new MeetingMinutes("Tactical Meeting Briefing", topics, actions, response);
    }
}
