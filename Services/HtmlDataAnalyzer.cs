using AngleSharp;
using AngleSharp.Dom;
using System.Text.RegularExpressions;
using URLScra.Models;

namespace URLScra.Services;

public static class HtmlDataAnalyzer
{
    private static readonly Regex PriceRegex = new(
        @"(?ix)(?:\b(?:RM|MYR|USD|SGD|EUR|GBP|AUD|JPY)\s*|[$€£¥]\s*)\d{1,3}(?:[,\s]\d{3})*(?:\.\d{1,2})?|\b\d+(?:\.\d{2})\s*(?:RM|MYR|USD|SGD|EUR|GBP|AUD|JPY)\b",
        RegexOptions.Compiled);

    private static readonly Regex EmailRegex = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,63}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PhoneRegex = new(
        @"(?x)(?<!\w)(?:\+?\d{1,3}[\s.()-]*)?(?:\d[\s.()-]*){7,14}(?!\w)",
        RegexOptions.Compiled);

    private static readonly string[] DocumentExtensions =
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".odp"
    };

    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".m4v", ".avi", ".m3u8" };
    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".ogg", ".m4a", ".aac", ".flac", ".opus" };
    private static readonly string[] DownloadableExtensions =
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".odp",
        ".csv", ".json", ".xml", ".zip", ".rar", ".7z", ".tar", ".gz", ".jpg", ".jpeg", ".png", ".gif",
        ".webp", ".svg", ".mp3", ".wav", ".ogg", ".m4a", ".aac", ".flac", ".opus", ".mp4", ".webm", ".mov",
        ".m4v", ".avi", ".m3u8"
    };

    private static readonly string[] SocialHosts =
    {
        "facebook.com", "instagram.com", "x.com", "twitter.com", "linkedin.com", "youtube.com", "youtu.be", "tiktok.com",
        "threads.net", "pinterest.com", "reddit.com", "telegram.me", "t.me"
    };

    public static async Task<IReadOnlyList<ScrapeItem>> AnalyzeAsync(
        string html,
        string pageUrl,
        string criterion = "All",
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri))
            throw new ArgumentException("URL halaman tidak sah.");

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html).Address(pageUri), cancellationToken);
        var results = new List<ScrapeItem>();

        var mode = string.IsNullOrWhiteSpace(criterion) ? "all" : criterion.Trim().ToLowerInvariant();
        var all = mode == "all";

        if (all || mode == "text") AddText(document, pageUri, results);
        if (all || mode == "title") AddTitle(document, pageUri, results);
        if (all || mode == "price") AddPrices(document, pageUri, results);
        if (all || mode == "image") AddImages(document, pageUri, results);
        if (all || mode == "video") AddVideos(document, pageUri, results);
        if (all || mode == "audio") AddAudio(document, pageUri, results);
        if (all || mode == "link") AddLinks(document, pageUri, results);
        if (all || mode is "doc" or "document") AddFileLinks(document, pageUri, results, "Document", DocumentExtensions);
        if (all || mode == "csv") AddFileLinks(document, pageUri, results, "CSV", new[] { ".csv" });
        if (all || mode == "email") AddEmails(document, pageUri, results);
        if (all || mode == "phone") AddPhones(document, pageUri, results);
        if (all || mode == "json") AddJson(document, pageUri, results);
        if (all || mode == "table") AddTables(document, pageUri, results);
        if (all || mode == "product") AddProducts(document, pageUri, results);
        if (all || mode == "metadata") AddMetadata(document, pageUri, results);
        if (all || mode == "social") AddSocial(document, pageUri, results);
        if (all || mode == "downloadable file") AddFileLinks(document, pageUri, results, "Download", DownloadableExtensions);

        return results
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => $"{x.Type}|{x.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(2000)
            .ToList();
    }

    private static void AddText(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var element in document.QuerySelectorAll("h1,h2,h3,h4,h5,h6,p,li,blockquote").Take(600))
        {
            var text = Clean(element.TextContent);
            if (text.Length < 3) continue;
            if (text.Length > 1200) text = text[..1200] + "…";
            results.Add(new ScrapeItem { Type = "Text", Value = text, SourceUrl = element.TagName.ToUpperInvariant() });
        }
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
            foreach (var element in document.QuerySelectorAll(selector).Take(500))
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
            foreach (Match match in PriceRegex.Matches(document.Body?.TextContent ?? "").Cast<Match>().Take(200))
                results.Add(new ScrapeItem { Type = "Price", Value = Clean(match.Value), SourceUrl = pageUri.ToString() });
        }
    }

    private static void AddImages(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var img in document.QuerySelectorAll("img").Take(800))
        {
            var raw = img.GetAttribute("src") ?? img.GetAttribute("data-src") ?? img.GetAttribute("data-lazy-src");
            if (string.IsNullOrWhiteSpace(raw))
                raw = img.GetAttribute("srcset")?.Split(',', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            var absolute = ToAbsolute(pageUri, raw);
            if (absolute is null) continue;
            var alt = Clean(img.GetAttribute("alt") ?? "");
            results.Add(new ScrapeItem { Type = "Image", Value = absolute, SourceUrl = alt.Length > 0 ? alt : pageUri.ToString() });
        }

        var og = ToAbsolute(pageUri, document.QuerySelector("meta[property='og:image']")?.GetAttribute("content"));
        if (og is not null) results.Add(new ScrapeItem { Type = "Image", Value = og, SourceUrl = "OpenGraph" });
    }

    private static void AddVideos(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var video in document.QuerySelectorAll("video").Take(300))
        {
            AddMediaSource(video.GetAttribute("src"), "Video", pageUri, results);
            foreach (var source in video.QuerySelectorAll("source")) AddMediaSource(source.GetAttribute("src"), "Video", pageUri, results);
        }

        foreach (var iframe in document.QuerySelectorAll("iframe").Take(300))
        {
            var src = ToAbsolute(pageUri, iframe.GetAttribute("src"));
            if (src is null) continue;
            if (src.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || src.Contains("youtu.be", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("vimeo.com", StringComparison.OrdinalIgnoreCase) || src.Contains("video", StringComparison.OrdinalIgnoreCase))
                results.Add(new ScrapeItem { Type = "Video Embed", Value = src, SourceUrl = pageUri.ToString() });
        }

        AddFileLinks(document, pageUri, results, "Video", VideoExtensions);
        var og = ToAbsolute(pageUri, document.QuerySelector("meta[property='og:video'],meta[property='og:video:url']")?.GetAttribute("content"));
        if (og is not null) results.Add(new ScrapeItem { Type = "Video", Value = og, SourceUrl = "OpenGraph" });
    }

    private static void AddAudio(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var audio in document.QuerySelectorAll("audio").Take(300))
        {
            AddMediaSource(audio.GetAttribute("src"), "Audio", pageUri, results);
            foreach (var source in audio.QuerySelectorAll("source")) AddMediaSource(source.GetAttribute("src"), "Audio", pageUri, results);
        }
        AddFileLinks(document, pageUri, results, "Audio", AudioExtensions);
    }

    private static void AddLinks(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var anchor in document.QuerySelectorAll("a[href]").Take(1200))
        {
            var href = ToAbsolute(pageUri, anchor.GetAttribute("href"));
            if (href is null) continue;
            results.Add(new ScrapeItem { Type = "Link", Value = href, SourceUrl = LabelOrPage(anchor.TextContent, pageUri) });
        }
    }

    private static void AddEmails(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var anchor in document.QuerySelectorAll("a[href^='mailto:' i]").Take(300))
        {
            var href = anchor.GetAttribute("href") ?? "";
            foreach (var address in href[7..].Split('?', 2)[0].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (EmailRegex.IsMatch(address))
                    results.Add(new ScrapeItem { Type = "Email", Value = address, SourceUrl = LabelOrPage(anchor.TextContent, pageUri) });
            }
        }

        foreach (Match match in EmailRegex.Matches(document.Body?.TextContent ?? "").Cast<Match>().Take(300))
            results.Add(new ScrapeItem { Type = "Email", Value = match.Value, SourceUrl = pageUri.ToString() });
    }

    private static void AddPhones(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var anchor in document.QuerySelectorAll("a[href^='tel:' i]").Take(300))
        {
            var phone = Uri.UnescapeDataString((anchor.GetAttribute("href") ?? "")[4..]).Trim();
            if (phone.Length >= 7) results.Add(new ScrapeItem { Type = "Phone", Value = phone, SourceUrl = LabelOrPage(anchor.TextContent, pageUri) });
        }

        foreach (Match match in PhoneRegex.Matches(Clean(document.Body?.TextContent ?? "")).Cast<Match>().Take(300))
        {
            var value = Clean(match.Value);
            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length is >= 7 and <= 15) results.Add(new ScrapeItem { Type = "Phone", Value = value, SourceUrl = pageUri.ToString() });
        }
    }

    private static void AddJson(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json'],script[type='application/json']").Take(150))
        {
            var json = script.TextContent.Trim();
            if (json.Length == 0) continue;
            if (json.Length > 16000) json = json[..16000] + "…";
            var type = script.GetAttribute("type")?.Contains("ld+json", StringComparison.OrdinalIgnoreCase) == true ? "JSON-LD" : "JSON";
            results.Add(new ScrapeItem { Type = type, Value = json, SourceUrl = pageUri.ToString() });
        }
        AddFileLinks(document, pageUri, results, "JSON File", new[] { ".json" });
    }

    private static void AddTables(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        var tableIndex = 0;
        foreach (var table in document.QuerySelectorAll("table").Take(100))
        {
            tableIndex++;
            var rowIndex = 0;
            foreach (var row in table.QuerySelectorAll("tr").Take(500))
            {
                rowIndex++;
                var cells = row.QuerySelectorAll("th,td").Select(x => Clean(x.TextContent)).Where(x => x.Length > 0).ToArray();
                if (cells.Length == 0) continue;
                results.Add(new ScrapeItem { Type = "Table Row", Value = string.Join(" | ", cells), SourceUrl = $"Table {tableIndex}, Row {rowIndex}" });
            }
        }
    }

    private static void AddProducts(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var element in document.QuerySelectorAll("[itemtype*='Product'],[class*='product-card' i],[class*='product-item' i],[class*='product-tile' i]").Take(500))
        {
            var name = Clean(element.QuerySelector("[itemprop='name'],[class*='name' i],h2,h3,h4")?.TextContent ?? "");
            var price = Clean(element.QuerySelector("[itemprop='price'],[class*='price' i],[data-price]")?.TextContent ?? "");
            var link = ToAbsolute(pageUri, element.QuerySelector("a[href]")?.GetAttribute("href"));
            var image = ToAbsolute(pageUri, element.QuerySelector("img")?.GetAttribute("src"));
            if (name.Length > 0) results.Add(new ScrapeItem { Type = "Product Name", Value = name, SourceUrl = pageUri.ToString() });
            if (price.Length > 0) results.Add(new ScrapeItem { Type = "Product Price", Value = price, SourceUrl = name.Length > 0 ? name : pageUri.ToString() });
            if (link is not null) results.Add(new ScrapeItem { Type = "Product Link", Value = link, SourceUrl = name.Length > 0 ? name : pageUri.ToString() });
            if (image is not null) results.Add(new ScrapeItem { Type = "Product Image", Value = image, SourceUrl = name.Length > 0 ? name : pageUri.ToString() });
        }
    }

    private static void AddMetadata(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var meta in document.QuerySelectorAll("meta[name],meta[property]").Take(300))
        {
            var key = meta.GetAttribute("name") ?? meta.GetAttribute("property") ?? "meta";
            var value = meta.GetAttribute("content");
            if (!string.IsNullOrWhiteSpace(value)) results.Add(new ScrapeItem { Type = "Metadata", Value = Clean(value), SourceUrl = key });
        }

        var canonical = ToAbsolute(pageUri, document.QuerySelector("link[rel='canonical']")?.GetAttribute("href"));
        if (canonical is not null) results.Add(new ScrapeItem { Type = "Canonical", Value = canonical, SourceUrl = pageUri.ToString() });
    }

    private static void AddSocial(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var anchor in document.QuerySelectorAll("a[href]").Take(1200))
        {
            var href = ToAbsolute(pageUri, anchor.GetAttribute("href"));
            if (href is null || !Uri.TryCreate(href, UriKind.Absolute, out var uri)) continue;
            if (!SocialHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))) continue;
            results.Add(new ScrapeItem { Type = "Social", Value = href, SourceUrl = LabelOrPage(anchor.TextContent, pageUri) });
        }
    }

    private static void AddFileLinks(IDocument document, Uri pageUri, List<ScrapeItem> results, string type, IEnumerable<string> extensions)
    {
        foreach (var anchor in document.QuerySelectorAll("a[href]").Take(1200))
        {
            var href = ToAbsolute(pageUri, anchor.GetAttribute("href"));
            if (href is null || !HasExtension(href, extensions)) continue;
            results.Add(new ScrapeItem { Type = type, Value = href, SourceUrl = LabelOrPage(anchor.TextContent, pageUri) });
        }
    }

    private static void AddMediaSource(string? raw, string type, Uri pageUri, List<ScrapeItem> results)
    {
        var absolute = ToAbsolute(pageUri, raw);
        if (absolute is not null) results.Add(new ScrapeItem { Type = type, Value = absolute, SourceUrl = pageUri.ToString() });
    }

    private static bool HasExtension(string url, IEnumerable<string> extensions)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return extensions.Any(ext => uri.AbsolutePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static string LabelOrPage(string? label, Uri pageUri)
    {
        var clean = Clean(label ?? "");
        return clean.Length > 0 ? clean : pageUri.ToString();
    }

    private static string? ToAbsolute(Uri pageUri, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("#")) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)) return absolute.ToString();
        return Uri.TryCreate(pageUri, value, out var relative) ? relative.ToString() : null;
    }

    private static string Clean(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
