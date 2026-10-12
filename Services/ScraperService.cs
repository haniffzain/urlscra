using AngleSharp;
using AngleSharp.Dom;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using URLScra.Models;

namespace URLScra.Services;

public sealed class ScraperService
{
    private static readonly Regex PriceRegex = new(
        @"(?ix)(?:\b(?:RM|MYR|USD|SGD|EUR|GBP|AUD|JPY)\s*|[$€£¥]\s*)\d{1,3}(?:[,\s]\d{3})*(?:\.\d{1,2})?|\b\d+(?:\.\d{2})\s*(?:RM|MYR|USD|SGD|EUR|GBP|AUD|JPY)\b",
        RegexOptions.Compiled);

    private static readonly Regex EmailRegex = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,63}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] DocumentExtensions =
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".rtf", ".odt", ".ods", ".odp"
    };

    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".webm", ".mov", ".m4v", ".avi", ".m3u8"
    };

    private readonly HttpClient _http;

    public ScraperService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("URLScra", "0.1"));
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

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html).Address(pageUri), cancellationToken);
        var results = new List<ScrapeItem>();

        var mode = string.IsNullOrWhiteSpace(criterion)
            ? "all"
            : criterion.Trim().ToLowerInvariant();
        var all = mode == "all";

        if (all) AddTitle(document, pageUri, results);
        if (all || mode == "price") AddPrices(document, pageUri, results);
        if (all || mode == "image") AddImages(document, pageUri, results);
        if (all || mode == "video") AddVideos(document, pageUri, results);
        if (all || mode == "link") AddLinks(document, pageUri, results);
        if (all || mode == "doc") AddDocuments(document, pageUri, results);
        if (all || mode == "csv") AddCsvFiles(document, pageUri, results);
        if (all || mode == "email") AddEmails(document, pageUri, results);

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

        foreach (var anchor in document.Links.Take(500))
        {
            var href = ToAbsolute(pageUri, anchor.GetAttribute("href"));
            if (href is null || !HasExtension(href, VideoExtensions)) continue;
            results.Add(new ScrapeItem
            {
                Type = "Video",
                Value = href,
                SourceUrl = LabelOrPage(anchor.TextContent, pageUri)
            });
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
            results.Add(new ScrapeItem
            {
                Type = "Link",
                Value = href,
                SourceUrl = LabelOrPage(anchor.TextContent, pageUri)
            });
        }
    }

    private static void AddDocuments(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var anchor in document.Links.Take(500))
        {
            var href = ToAbsolute(pageUri, anchor.GetAttribute("href"));
            if (href is null || !HasExtension(href, DocumentExtensions)) continue;
            results.Add(new ScrapeItem
            {
                Type = "Doc",
                Value = href,
                SourceUrl = LabelOrPage(anchor.TextContent, pageUri)
            });
        }
    }

    private static void AddCsvFiles(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var anchor in document.Links.Take(500))
        {
            var href = ToAbsolute(pageUri, anchor.GetAttribute("href"));
            if (href is null || !HasExtension(href, new[] { ".csv" })) continue;
            results.Add(new ScrapeItem
            {
                Type = "CSV",
                Value = href,
                SourceUrl = LabelOrPage(anchor.TextContent, pageUri)
            });
        }
    }

    private static void AddEmails(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var anchor in document.Links.Take(500))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || !href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                continue;

            var addresses = href[7..].Split('?', 2)[0]
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var address in addresses)
            {
                if (!EmailRegex.IsMatch(address)) continue;
                results.Add(new ScrapeItem
                {
                    Type = "Email",
                    Value = address,
                    SourceUrl = LabelOrPage(anchor.TextContent, pageUri)
                });
            }
        }

        var text = document.Body?.TextContent ?? "";
        foreach (Match match in EmailRegex.Matches(text).Cast<Match>().Take(200))
        {
            results.Add(new ScrapeItem
            {
                Type = "Email",
                Value = match.Value,
                SourceUrl = pageUri.ToString()
            });
        }
    }

    private static bool HasExtension(string url, IEnumerable<string> extensions)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var path = uri.AbsolutePath;
        return extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static string LabelOrPage(string? label, Uri pageUri)
    {
        var clean = Clean(label ?? "");
        return string.IsNullOrWhiteSpace(clean) ? pageUri.ToString() : clean;
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
