using System.Net.Http;
using System.Text.RegularExpressions;

namespace OzzieAI.Jarvis.Services.Tools;

public record BrowserPageResult(string Url, string Title, string ContentMarkdown, List<string> Links, bool Success, string? Error);

public interface IBrowserAgentService
{
    Task<BrowserPageResult> NavigateAndExtractAsync(string url);
    Task<string> SearchAndSummarizeAsync(string query);
}

public class BrowserAgentService : IBrowserAgentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BrowserAgentService> _logger;

    public BrowserAgentService(ILogger<BrowserAgentService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<BrowserPageResult> NavigateAndExtractAsync(string url)
    {
        try
        {
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            var response = await _httpClient.GetAsync(url);
            var html = await response.Content.ReadAsStringAsync();

            var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var title = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : url;

            var links = new List<string>();
            var linkMatches = Regex.Matches(html, @"href=[""'](https?://[^""']+)[""']", RegexOptions.IgnoreCase);
            foreach (Match m in linkMatches.Take(10))
            {
                if (!links.Contains(m.Groups[1].Value)) links.Add(m.Groups[1].Value);
            }

            // Strip scripts and styles
            var cleaned = Regex.Replace(html, @"<(script|style|svg|noscript)[^>]*>.*?</\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Replace header tags with markdown
            cleaned = Regex.Replace(cleaned, @"<h1[^>]*>(.*?)</h1>", "\n# $1\n", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<h2[^>]*>(.*?)</h2>", "\n## $1\n", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<h3[^>]*>(.*?)</h3>", "\n### $1\n", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<p[^>]*>(.*?)</p>", "\n$1\n", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<li[^>]*>(.*?)</li>", "\n• $1", RegexOptions.IgnoreCase);
            // Strip remaining HTML tags
            cleaned = Regex.Replace(cleaned, @"<[^>]+>", " ");
            cleaned = System.Net.WebUtility.HtmlDecode(cleaned);
            cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
            cleaned = Regex.Replace(cleaned, @"\n\s*\n+", "\n\n").Trim();

            if (cleaned.Length > 8000)
            {
                cleaned = cleaned.Substring(0, 8000) + "\n\n[... content truncated for token limits ...]";
            }

            return new BrowserPageResult(url, title, cleaned, links, true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to {Url}", url);
            return new BrowserPageResult(url, "Error", "", new(), false, ex.Message);
        }
    }

    public async Task<string> SearchAndSummarizeAsync(string query)
    {
        var targetUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
        var res = await NavigateAndExtractAsync(targetUrl);
        if (!res.Success) return $"Search navigation failed: {res.Error}";

        return $"### Web Intelligence for '{query}':\n\n{res.ContentMarkdown.Substring(0, Math.Min(res.ContentMarkdown.Length, 2500))}";
    }
}
