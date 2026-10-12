using AngleSharp;
using AngleSharp.Dom;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using URLScra.Models;

namespace URLScra.Services;

public sealed class ScraperService
{
    private static readonly Regex PriceRegex = new(
        @"(?ix)(?:\b(?:RM|MYR|USD|SGD|EUR|GBP|AUD|JPY)\s*|[$€£¥]\s*)\d{1,3}(?:[,\s]\d{3})*(?:\.\d{1,2})?|\b\d+(?:\.\d{2})\s*(?:RM|MYR|USD|SGD|EUR|GBP|AUD|JPY)\b",
        RegexOptions.Compiled);

    private readonly HttpClient _http;

    public ScraperService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("URLScra", "0.1"));
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,ms;q=0.8");
    }

    public async Task<IReadOnlyList<ScrapeItem>> AnalyzeAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var pageUri) ||
            (pageUri.Scheme != Uri.UriSchemeHttp && pageUri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("URL mesti bermula dengan http:// atau https://");

        using var response = await _http.GetAsync(pageUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html).Address(pageUri), cancellationToken);
        var results = new List<ScrapeItem>();

        AddTitle(document, pageUri, results);
        AddPrices(document, pageUri, results);
        AddImages(document, pageUri, results);
        AddVideos(document, pageUri, results);
        AddLinks(document, pageUri, results);

        return results
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => $"{x.Type}|{x.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(1000)
            .ToList();
    }

    private static void AddTitle(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        var title = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content") ?? document.Title;
        if (!string.IsNullOrWhiteSpace(title))
            results.Add(new ScrapeItem { Type = "Title", Value = Clean(title), SourceUrl = pageUri.ToString() });
    }

    private static void AddPrices(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var selector in new[] { "[itemprop='price']", "[class*='price' i]", "[id*='price' i]", "[data-price]" })
        {
            foreach (var element in document.QuerySelectorAll(selector).Take(300))
            {
                foreach (var candidate in new[] { element.GetAttribute("content"), element.GetAttribute("data-price"), element.TextContent })
                {
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    foreach (Match match in PriceRegex.Matches(candidate))
                        results.Add(new ScrapeItem { Type = "Price", Value = Clean(match.Value), SourceUrl = pageUri.ToString() });
                }
            }
        }

        if (!results.Any(x => x.Type == "Price"))
        {
            var text = document.Body?.TextContent ?? "";
            foreach (Match match in PriceRegex.Matches(text).Cast<Match>().Take(100))
                results.Add(new ScrapeItem { Type = "Price", Value = Clean(match.Value), SourceUrl = pageUri.ToString() });
        }
    }

    private static void AddImages(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var img in document.Images.Take(500))
        {
            var raw = img.GetAttribute("src") ?? img.GetAttribute("data-src") ?? img.GetAttribute("data-lazy-src");
            if (string.IsNullOrWhiteSpace(raw))
            {
                var srcset = img.GetAttribute("srcset");
                raw = srcset?.Split(',', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            }
            var absolute = ToAbsolute(pageUri, raw);
            if (absolute is not null)
                results.Add(new ScrapeItem { Type = "Image", Value = absolute, SourceUrl = pageUri.ToString() });
        }

        var ogImage = ToAbsolute(pageUri, document.QuerySelector("meta[property='og:image']")?.GetAttribute("content"));
        if (ogImage is not null)
            results.Add(new ScrapeItem { Type = "Image", Value = ogImage, SourceUrl = pageUri.ToString() });
    }

    private static void AddVideos(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var video in document.QuerySelectorAll("video").Take(200))
        {
            var direct = ToAbsolute(pageUri, video.GetAttribute("src"));
            if (direct is not null)
                results.Add(new ScrapeItem { Type = "Video", Value = direct, SourceUrl = pageUri.ToString() });

            foreach (var source in video.QuerySelectorAll("source"))
            {
                var src = ToAbsolute(pageUri, source.GetAttribute("src"));
                if (src is not null)
                    results.Add(new ScrapeItem { Type = "Video", Value = src, SourceUrl = pageUri.ToString() });
            }
        }

        foreach (var iframe in document.QuerySelectorAll("iframe").Take(200))
        {
            var src = ToAbsolute(pageUri, iframe.GetAttribute("src"));
            if (src is null) continue;
            if (src.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("youtu.be", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("vimeo.com", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("video", StringComparison.OrdinalIgnoreCase))
                results.Add(new ScrapeItem { Type = "Video Embed", Value = src, SourceUrl = pageUri.ToString() });
        }

        var ogVideo = document.QuerySelector("meta[property='og:video']")?.GetAttribute("content") ??
                      document.QuerySelector("meta[property='og:video:url']")?.GetAttribute("content");
        var absoluteOgVideo = ToAbsolute(pageUri, ogVideo);
        if (absoluteOgVideo is not null)
            results.Add(new ScrapeItem { Type = "Video", Value = absoluteOgVideo, SourceUrl = pageUri.ToString() });
    }

    private static void AddLinks(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var anchor in document.Links.Take(500))
        {
            var href = ToAbsolute(pageUri, anchor.GetAttribute("href"));
            if (href is null) continue;
            var label = Clean(anchor.TextContent);
            results.Add(new ScrapeItem { Type = "Link", Value = href, SourceUrl = string.IsNullOrWhiteSpace(label) ? pageUri.ToString() : label });
        }
    }

    private static string? ToAbsolute(Uri pageUri, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("#")) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)) return absolute.ToString();
        return Uri.TryCreate(pageUri, value, out var relative) ? relative.ToString() : null;
    }

    private static string Clean(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
