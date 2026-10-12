using AngleSharp;
using AngleSharp.Dom;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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

    private static readonly Regex PhoneRegex = new(
        @"(?x)(?<!\w)(?:\+?\d{1,3}[\s.()-]*)?(?:\d[\s.()-]*){7,14}(?!\w)",
        RegexOptions.Compiled);

    private static readonly string[] DocumentExtensions =
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".rtf", ".odt", ".ods", ".odp"
    };

    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".webm", ".mov", ".m4v", ".avi", ".m3u8"
    };

    private static readonly string[] AudioExtensions =
    {
        ".mp3", ".wav", ".ogg", ".m4a", ".aac", ".flac", ".opus"
    };

    private static readonly string[] DownloadableExtensions =
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf",
        ".odt", ".ods", ".odp", ".csv", ".json", ".xml", ".zip", ".rar", ".7z", ".tar",
        ".gz", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".mp3", ".wav", ".ogg",
        ".m4a", ".aac", ".flac", ".opus", ".mp4", ".webm", ".mov", ".m4v", ".avi", ".m3u8"
    };

    private static readonly string[] SocialHosts =
    {
        "facebook.com", "instagram.com", "x.com", "twitter.com", "linkedin.com", "youtube.com",
        "youtu.be", "tiktok.com", "threads.net", "pinterest.com", "reddit.com", "telegram.me", "t.me"
    };

    private readonly HttpClient _http;

    public ScraperService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("URLScra", "0.2"));
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

        if (all || mode == "text") AddText(document, pageUri, results);
        if (all || mode == "title") AddTitle(document, pageUri, results);
        if (all || mode == "price") AddPrices(document, pageUri, results);
        if (all || mode == "image") AddImages(document, pageUri, results);
        if (all || mode == "video") AddVideos(document, pageUri, results);
        if (all || mode == "audio") AddAudio(document, pageUri, results);
        if (all || mode == "link") AddLinks(document, pageUri, results);
        if (all || mode == "doc" || mode == "document") AddDocuments(document, pageUri, results);
        if (all || mode == "csv") AddCsvFiles(document, pageUri, results);
        if (all || mode == "email") AddEmails(document, pageUri, results);
        if (all || mode == "phone") AddPhones(document, pageUri, results);
        if (all || mode == "json") AddJson(document, pageUri, results);
        if (all || mode == "table") AddTables(document, pageUri, results);
        if (all || mode == "metadata") AddMetadata(document, pageUri, results);
        if (all || mode == "social") AddSocialLinks(document, pageUri, results);
        if (all || mode == "downloadable file") AddDownloadableFiles(document, pageUri, results);
        if (all || mode == "product") AddProducts(document, pageUri, results);

        return results
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => $"{x.Type}|{x.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(1500)
            .ToList();
    }

    private static void AddText(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var element in document.QuerySelectorAll("h1,h2,h3,h4,h5,h6,p,li,blockquote").Take(400))
        {
            var text = Clean(element.TextContent);
            if (text.Length < 3) continue;
            if (text.Length > 1000) text = text[..1000] + "…";

            results.Add(new ScrapeItem
            {
                Type = "Text",
                Value = text,
                SourceUrl = element.TagName.ToUpperInvariant()
            });
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
                results.Add(new ScrapeItem
                {
                    Type = "Image",
                    Value = absolute,
                    SourceUrl = Clean(img.GetAttribute("alt") ?? "") is { Length: > 0 } alt ? alt : pageUri.ToString()
                });
        }

        var ogImage = ToAbsolute(pageUri, document.QuerySelector("meta[property='og:image']")?.GetAttribute("content"));
        if (ogImage is not null)
            results.Add(new ScrapeItem { Type = "Image", Value = ogImage, SourceUrl = "OpenGraph" });
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
                    results.Add(new ScrapeItem { Type = "Video", Value = src, SourceUrl = source.GetAttribute("type") ?? pageUri.ToString() });
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

        AddFileLinks(document, pageUri, results, "Video", VideoExtensions);

        var ogVideo = document.QuerySelector("meta[property='og:video']")?.GetAttribute("content") ??
                      document.QuerySelector("meta[property='og:video:url']")?.GetAttribute("content");
        var absoluteOgVideo = ToAbsolute(pageUri, ogVideo);
        if (absoluteOgVideo is not null)
            results.Add(new ScrapeItem { Type = "Video", Value = absoluteOgVideo, SourceUrl = "OpenGraph" });
    }

    private static void AddAudio(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var audio in document.QuerySelectorAll("audio").Take(200))
        {
            var direct = ToAbsolute(pageUri, audio.GetAttribute("src"));
            if (direct is not null)
                results.Add(new ScrapeItem { Type = "Audio", Value = direct, SourceUrl = pageUri.ToString() });

            foreach (var source in audio.QuerySelectorAll("source"))
            {
                var src = ToAbsolute(pageUri, source.GetAttribute("src"));
                if (src is not null)
                    results.Add(new ScrapeItem { Type = "Audio", Value = src, SourceUrl = source.GetAttribute("type") ?? pageUri.ToString() });
            }
        }

        AddFileLinks(document, pageUri, results, "Audio", AudioExtensions);
    }

    private static void AddLinks(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var anchor in document.Links.Take(800))
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

    private static void AddDocuments(IDocument document, Uri pageUri, List<ScrapeItem> results) =>
        AddFileLinks(document, pageUri, results, "Document", DocumentExtensions);

    private static void AddCsvFiles(IDocument document, Uri pageUri, List<ScrapeItem> results) =>
        AddFileLinks(document, pageUri, results, "CSV", new[] { ".csv" });

    private static void AddEmails(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var anchor in document.Links.Take(800))
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
            results.Add(new ScrapeItem { Type = "Email", Value = match.Value, SourceUrl = pageUri.ToString() });
        }
    }

    private static void AddPhones(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var anchor in document.Links.Take(800))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || !href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                continue;

            var phone = Uri.UnescapeDataString(href[4..]).Trim();
            if (phone.Length < 7) continue;
            results.Add(new ScrapeItem { Type = "Phone", Value = phone, SourceUrl = LabelOrPage(anchor.TextContent, pageUri) });
        }

        var text = Clean(document.Body?.TextContent ?? "");
        foreach (Match match in PhoneRegex.Matches(text).Cast<Match>().Take(200))
        {
            var value = Clean(match.Value);
            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length < 7 || digits.Length > 15) continue;
            results.Add(new ScrapeItem { Type = "Phone", Value = value, SourceUrl = pageUri.ToString() });
        }
    }

    private static void AddJson(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json'],script[type='application/json']").Take(100))
        {
            var json = script.TextContent.Trim();
            if (string.IsNullOrWhiteSpace(json)) continue;
            if (json.Length > 12000) json = json[..12000] + "…";
            results.Add(new ScrapeItem
            {
                Type = script.GetAttribute("type")?.Contains("ld+json", StringComparison.OrdinalIgnoreCase) == true ? "JSON-LD" : "JSON",
                Value = json,
                SourceUrl = pageUri.ToString()
            });
        }

        AddFileLinks(document, pageUri, results, "JSON File", new[] { ".json" });
    }

    private static void AddTables(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        var tables = document.QuerySelectorAll("table").Take(50).ToList();
        for (var tableIndex = 0; tableIndex < tables.Count; tableIndex++)
        {
            var rows = tables[tableIndex].QuerySelectorAll("tr").Take(300).ToList();
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var cells = rows[rowIndex].QuerySelectorAll("th,td")
                    .Select(cell => Clean(cell.TextContent))
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                if (cells.Count == 0) continue;
                results.Add(new ScrapeItem
                {
                    Type = "Table Row",
                    Value = string.Join(" | ", cells),
                    SourceUrl = $"Table {tableIndex + 1}, Row {rowIndex + 1}"
                });
            }
        }
    }

    private static void AddMetadata(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var meta in document.QuerySelectorAll("meta").Take(300))
        {
            var key = meta.GetAttribute("name") ?? meta.GetAttribute("property") ?? meta.GetAttribute("http-equiv");
            var value = meta.GetAttribute("content");
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;

            results.Add(new ScrapeItem
            {
                Type = "Metadata",
                Value = $"{Clean(key)}: {Clean(value)}",
                SourceUrl = pageUri.ToString()
            });
        }

        var canonical = ToAbsolute(pageUri, document.QuerySelector("link[rel='canonical']")?.GetAttribute("href"));
        if (canonical is not null)
            results.Add(new ScrapeItem { Type = "Metadata", Value = $"canonical: {canonical}", SourceUrl = pageUri.ToString() });
    }

    private static void AddSocialLinks(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var anchor in document.Links.Take(800))
        {
            var href = ToAbsolute(pageUri, anchor.GetAttribute("href"));
            if (href is null || !Uri.TryCreate(href, UriKind.Absolute, out var uri)) continue;
            if (!SocialHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)))
                continue;

            results.Add(new ScrapeItem
            {
                Type = "Social",
                Value = href,
                SourceUrl = LabelOrPage(anchor.TextContent, pageUri)
            });
        }
    }

    private static void AddDownloadableFiles(IDocument document, Uri pageUri, List<ScrapeItem> results) =>
        AddFileLinks(document, pageUri, results, "Downloadable File", DownloadableExtensions);

    private static void AddProducts(IDocument document, Uri pageUri, List<ScrapeItem> results)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']").Take(100))
        {
            var json = script.TextContent.Trim();
            if (string.IsNullOrWhiteSpace(json)) continue;

            try
            {
                using var parsed = JsonDocument.Parse(json);
                AddProductsFromJson(parsed.RootElement, pageUri, results);
            }
            catch (JsonException)
            {
                // Ignore malformed JSON-LD; DOM heuristics below may still detect products.
            }
        }

        foreach (var node in document.QuerySelectorAll("[itemtype*='Product' i],[class*='product' i],[data-product]").Take(250))
        {
            var name = Clean(
                node.QuerySelector("[itemprop='name']")?.TextContent ??
                node.QuerySelector("h1,h2,h3,h4")?.TextContent ?? "");

            var price = Clean(
                node.QuerySelector("[itemprop='price']")?.GetAttribute("content") ??
                node.QuerySelector("[itemprop='price']")?.TextContent ??
                node.QuerySelector("[class*='price' i]")?.TextContent ?? "");

            var link = ToAbsolute(pageUri, node.QuerySelector("a[href]")?.GetAttribute("href"));
            var image = ToAbsolute(pageUri, node.QuerySelector("img")?.GetAttribute("src"));

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(price)) continue;

            var pieces = new List<string>();
            if (!string.IsNullOrWhiteSpace(name)) pieces.Add(name);
            if (!string.IsNullOrWhiteSpace(price)) pieces.Add($"Price: {price}");
            if (!string.IsNullOrWhiteSpace(image)) pieces.Add($"Image: {image}");

            results.Add(new ScrapeItem
            {
                Type = "Product",
                Value = string.Join(" | ", pieces),
                SourceUrl = link ?? pageUri.ToString()
            });
        }
    }

    private static void AddProductsFromJson(JsonElement element, Uri pageUri, List<ScrapeItem> results)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) AddProductsFromJson(item, pageUri, results);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object) return;

        if (IsProductType(element))
        {
            var name = GetJsonString(element, "name");
            var sku = GetJsonString(element, "sku");
            var url = GetJsonString(element, "url");
            var image = GetJsonImage(element);
            var (price, currency) = GetOfferPrice(element);

            var pieces = new List<string>();
            if (!string.IsNullOrWhiteSpace(name)) pieces.Add(name);
            if (!string.IsNullOrWhiteSpace(price)) pieces.Add($"Price: {price}{(string.IsNullOrWhiteSpace(currency) ? "" : " " + currency)}");
            if (!string.IsNullOrWhiteSpace(sku)) pieces.Add($"SKU: {sku}");
            if (!string.IsNullOrWhiteSpace(image)) pieces.Add($"Image: {image}");

            if (pieces.Count > 0)
            {
                results.Add(new ScrapeItem
                {
                    Type = "Product",
                    Value = string.Join(" | ", pieces),
                    SourceUrl = ToAbsolute(pageUri, url) ?? pageUri.ToString()
                });
            }
        }

        foreach (var property in element.EnumerateObject())
            AddProductsFromJson(property.Value, pageUri, results);
    }

    private static bool IsProductType(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var type)) return false;
        if (type.ValueKind == JsonValueKind.String)
            return string.Equals(type.GetString(), "Product", StringComparison.OrdinalIgnoreCase);
        if (type.ValueKind == JsonValueKind.Array)
            return type.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && string.Equals(x.GetString(), "Product", StringComparison.OrdinalIgnoreCase));
        return false;
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? GetJsonImage(JsonElement element)
    {
        if (!element.TryGetProperty("image", out var image)) return null;
        if (image.ValueKind == JsonValueKind.String) return image.GetString();
        if (image.ValueKind == JsonValueKind.Array)
        {
            var first = image.EnumerateArray().FirstOrDefault();
            return first.ValueKind == JsonValueKind.String ? first.GetString() : null;
        }
        if (image.ValueKind == JsonValueKind.Object && image.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
            return url.GetString();
        return null;
    }

    private static (string? Price, string? Currency) GetOfferPrice(JsonElement element)
    {
        if (!element.TryGetProperty("offers", out var offers)) return (null, null);
        if (offers.ValueKind == JsonValueKind.Array)
            offers = offers.EnumerateArray().FirstOrDefault();
        if (offers.ValueKind != JsonValueKind.Object) return (null, null);

        var price = GetJsonString(offers, "price") ?? GetJsonString(offers, "lowPrice");
        var currency = GetJsonString(offers, "priceCurrency");
        return (price, currency);
    }

    private static void AddFileLinks(IDocument document, Uri pageUri, List<ScrapeItem> results, string type, IEnumerable<string> extensions)
    {
        foreach (var anchor in document.Links.Take(800))
        {
            var href = ToAbsolute(pageUri, anchor.GetAttribute("href"));
            if (href is null || !HasExtension(href, extensions)) continue;
            results.Add(new ScrapeItem
            {
                Type = type,
                Value = href,
                SourceUrl = LabelOrPage(anchor.TextContent, pageUri)
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
