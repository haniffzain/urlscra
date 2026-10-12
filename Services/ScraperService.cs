using System.Net.Http;
using System.Net.Http.Headers;
using URLScra.Models;

namespace URLScra.Services;

public sealed class ScraperService
{
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

    public Task<IReadOnlyList<ScrapeItem>> AnalyzeHtmlAsync(
        string html,
        string pageUrl,
        string criterion = "All",
        CancellationToken cancellationToken = default) =>
        HtmlDataAnalyzer.AnalyzeAsync(html, pageUrl, criterion, cancellationToken);

    public async Task<IReadOnlyList<ScrapeItem>> AnalyzeRenderedAsync(
        RenderedPage rendered,
        string criterion = "All",
        CancellationToken cancellationToken = default)
    {
        var htmlItems = await AnalyzeHtmlAsync(
            rendered.Html,
            rendered.FinalUrl,
            criterion,
            cancellationToken);

        var wantsText = criterion.Equals("Text", StringComparison.OrdinalIgnoreCase) ||
                        criterion.Equals("All", StringComparison.OrdinalIgnoreCase);

        if (!wantsText || string.IsNullOrWhiteSpace(rendered.RenderedText))
            return htmlItems;

        var renderedTextItems = BuildRenderedTextItems(rendered.RenderedText);
        if (renderedTextItems.Count == 0)
            return htmlItems;

        if (criterion.Equals("Text", StringComparison.OrdinalIgnoreCase))
            return renderedTextItems;

        return htmlItems
            .Where(item => !item.Type.Equals("Text", StringComparison.OrdinalIgnoreCase))
            .Concat(renderedTextItems)
            .GroupBy(item => $"{item.Type}|{item.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(2000)
            .ToList();
    }

    private static IReadOnlyList<ScrapeItem> BuildRenderedTextItems(string renderedText)
    {
        return renderedText
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanTextLine)
            .Where(line => line.Length >= 3 && !LooksLikeJavaScriptPlaceholder(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(600)
            .Select(line => new ScrapeItem
            {
                Type = "Text",
                Value = line.Length > 1200 ? line[..1200] + "…" : line,
                SourceUrl = "Rendered Browser"
            })
            .ToList();
    }

    private static string CleanTextLine(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool LooksLikeJavaScriptPlaceholder(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("javascript is required") ||
               value.Contains("please enable javascript") ||
               value.Contains("enable javascript") ||
               value.Contains("requires javascript") ||
               value.Contains("javascript must be enabled") ||
               value.Contains("upgrade to a modern browser");
    }
}
