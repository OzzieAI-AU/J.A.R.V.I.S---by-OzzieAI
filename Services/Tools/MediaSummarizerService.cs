using System.Text.RegularExpressions;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Llm;

namespace OzzieAI.Jarvis.Services.Tools;

public record MediaSummaryResult(string Url, string Title, string KeyTakeaways, List<string> Chapters);

public interface IMediaSummarizerService
{
    Task<MediaSummaryResult> SummarizeMediaAsync(string url);
}

public class MediaSummarizerService : IMediaSummarizerService
{
    private readonly LlmProviderFactory _llmFactory;
    private readonly ILogger<MediaSummarizerService> _logger;

    public MediaSummarizerService(LlmProviderFactory llmFactory, ILogger<MediaSummarizerService> logger)
    {
        _llmFactory = llmFactory;
        _logger = logger;
    }

    public async Task<MediaSummaryResult> SummarizeMediaAsync(string url)
    {
        var (provider, _, _) = await _llmFactory.GetResilientProviderAsync();

        // Extract video ID or title hint
        string title = "Media Stream Analysis";
        var ytMatch = Regex.Match(url, @"(?:v=|\/)([0-9A-Za-z_-]{11})");
        if (ytMatch.Success)
        {
            title = $"YouTube Video (ID: {ytMatch.Groups[1].Value})";
        }

        var prompt = $"A user submitted this media/video URL: {url}\nAnalyze the subject matter, summarize the core thesis, outline 3-5 key technical takeaways, and list simulated timestamp chapters.";
        var response = await provider.GenerateResponseAsync(
            new List<ChatMessage> { new() { Role = "user", Content = prompt } },
            "You are J.A.R.V.I.S., Tony Stark's analytical executive AI.");

        var chapters = new List<string>
        {
            "00:00 - Introduction & Context",
            "04:15 - Core Technical Architecture",
            "12:30 - Live Demonstration & Benchmarks",
            "21:40 - Security & Performance Takeaways"
        };

        return new MediaSummaryResult(url, title, response, chapters);
    }
}
