using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using URLScra.Models;

namespace URLScra.Services;

public sealed class ScraperService
{
    private static readonly Regex InjectedRenderedTextRegex = new(
        "<p\\s+data-urlscra-rendered=\\\"true\\\">(?<text>.*?)</p>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private readonly HttpClient _http;

    public ScraperService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("URLScra", "0.7.1"));
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,ms;q=0.8");
    }

    public async Task<IReadOnlyList<ScrapeItem>> AnalyzeAsync(
        string url,
        string criterion = "All",
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var pageUri) ||
            (pageUri.Scheme != Uri.UriSchemeHttp && pageUri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("URL mesti bermula dengan http:// atau https://");

        using var response = await _http.GetAsync(pageUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
        return await AnalyzeHtmlAsync(html, finalUrl, criterion, cancellationToken);
    }

    public async Task<IReadOnlyList<ScrapeItem>> AnalyzeHtmlAsync(
        string html,
        string pageUrl,
        string criterion = "All",
        CancellationToken cancellationToken = default)
    {
        var htmlItems = await HtmlDataAnalyzer.AnalyzeAsync(
            html,
            pageUrl,
            criterion,
            cancellationToken);

        var wantsText = criterion.Equals("Text", StringComparison.OrdinalIgnoreCase) ||
                        criterion.Equals("All", StringComparison.OrdinalIgnoreCase);

        if (!wantsText || !html.Contains("data-urlscra-rendered=\"true\"", StringComparison.OrdinalIgnoreCase))
            return htmlItems;

        var renderedItems = ExtractInjectedRenderedText(html);
        if (renderedItems.Count == 0)
            return htmlItems.Where(item => !LooksLikeJavaScriptPlaceholder(item.Value)).ToList();

        if (criterion.Equals("Text", StringComparison.OrdinalIgnoreCase))
            return renderedItems;

        return htmlItems
            .Where(item => !item.Type.Equals("Text", StringComparison.OrdinalIgnoreCase) ||
                           !LooksLikeJavaScriptPlaceholder(item.Value))
            .Concat(renderedItems)
            .GroupBy(item => $"{item.Type}|{item.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(2000)
            .ToList();
    }

    public async Task<IReadOnlyList<ScrapeItem>> AnalyzeRenderedAsync(
        RenderedPage rendered,
        string criterion = "All",
        CancellationToken cancellationToken = default)
    {
        var items = await AnalyzeHtmlAsync(
            rendered.Html,
            rendered.FinalUrl,
            criterion,
            cancellationToken);

        if (!criterion.Equals("Text", StringComparison.OrdinalIgnoreCase) || items.Count > 0)
            return items;

        return BuildRenderedTextItems(rendered.RenderedText);
    }

    private static IReadOnlyList<ScrapeItem> ExtractInjectedRenderedText(string html)
    {
        return InjectedRenderedTextRegex
            .Matches(html)
            .Cast<Match>()
            .Select(match => WebUtility.HtmlDecode(match.Groups["text"].Value))
            .Select(CleanTextLine)
            .Where(line => line.Length >= 3 && !LooksLikeJavaScriptPlaceholder(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(600)
            .Select(ToRenderedTextItem)
            .ToList();
    }

    private static IReadOnlyList<ScrapeItem> BuildRenderedTextItems(string renderedText)
    {
        if (string.IsNullOrWhiteSpace(renderedText))
            return Array.Empty<ScrapeItem>();

        return renderedText
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanTextLine)
            .Where(line => line.Length >= 3 && !LooksLikeJavaScriptPlaceholder(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(600)
            .Select(ToRenderedTextItem)
            .ToList();
    }

    private static ScrapeItem ToRenderedTextItem(string line) => new()
    {
        Type = "Text",
        Value = line.Length > 1200 ? line[..1200] + "…" : line,
        SourceUrl = "Rendered Browser"
    };

    private static string CleanTextLine(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool LooksLikeJavaScriptPlaceholder(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text.ToLowerInvariant();
        return value.Contains("javascript is required") ||
               value.Contains("please enable javascript") ||
               value.Contains("enable javascript") ||
               value.Contains("requires javascript") ||
               value.Contains("javascript must be enabled") ||
               value.Contains("upgrade to a modern browser");
    }
}
