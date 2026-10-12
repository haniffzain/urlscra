using AngleSharp;
using AngleSharp.Dom;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using URLScra.Models;

namespace URLScra.Services;

public sealed class SmartScraperService
{
    private static readonly Regex PriceRegex = new(
        @"(?ix)(?:\b(?:RM|MYR|USD|SGD|EUR|GBP|AUD|JPY)\s*|[$€£¥]\s*)\d{1,3}(?:[,\s]\d{3})*(?:\.\d{1,2})?|\b\d+(?:\.\d{2})\s*(?:RM|MYR|USD|SGD|EUR|GBP|AUD|JPY)\b",
        RegexOptions.Compiled);

    private readonly HttpClient _http;

    public SmartScraperService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("URLScra", "0.3"));
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,ms;q=0.8");
    }

    public async Task<IReadOnlyList<SmartRecord>> AnalyzeAsync(
        string url,
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

        var results = new List<SmartRecord>();
        AddJsonLdProducts(document, pageUri, results);
        AddDomRecords(document, pageUri, results);

        return results
            .Where(HasUsefulData)
            .GroupBy(
                x => $"{x.Name}|{x.Price}|{x.Link}|{x.Image}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(500)
            .ToList();
    }

    private static void AddJsonLdProducts(IDocument document, Uri pageUri, List<SmartRecord> results)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']").Take(100))
        {
            var json = script.TextContent.Trim();
            if (string.IsNullOrWhiteSpace(json)) continue;

            try
            {
                using var doc = JsonDocument.Parse(json);
                WalkJson(doc.RootElement, pageUri, results);
            }
            catch (JsonException)
            {
                // Ignore malformed structured data and continue with DOM detection.
            }
        }
    }

    private static void WalkJson(JsonElement element, Uri pageUri, List<SmartRecord> results)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                WalkJson(item, pageUri, results);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("@graph", out var graph))
            WalkJson(graph, pageUri, results);

        var type = GetJsonString(element, "@type");
        if (IsProductType(type))
        {
            var record = new SmartRecord
            {
                Name = GetJsonString(element, "name"),
                Image = GetJsonImage(element, pageUri),
                Link = ToAbsolute(pageUri, GetJsonString(element, "url")) ?? "",
                Rating = GetNestedJsonString(element, "aggregateRating", "ratingValue"),
                SourceUrl = pageUri.ToString()
            };

            if (element.TryGetProperty("offers", out var offers))
                FillOfferData(offers, pageUri, record);

            results.Add(record);
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                WalkJson(property.Value, pageUri, results);
        }
    }

    private static void FillOfferData(JsonElement offers, Uri pageUri, SmartRecord record)
    {
        JsonElement offer = offers;
        if (offers.ValueKind == JsonValueKind.Array)
        {
            var enumerator = offers.EnumerateArray();
            if (!enumerator.MoveNext()) return;
            offer = enumerator.Current;
        }

        if (offer.ValueKind != JsonValueKind.Object) return;

        var price = GetJsonString(offer, "price");
        var currency = GetJsonString(offer, "priceCurrency");
        if (!string.IsNullOrWhiteSpace(price))
            record.Price = string.IsNullOrWhiteSpace(currency) ? price : $"{currency} {price}";

        var availability = GetJsonString(offer, "availability");
        if (!string.IsNullOrWhiteSpace(availability))
            record.Stock = NormalizeAvailability(availability);

        if (string.IsNullOrWhiteSpace(record.Link))
            record.Link = ToAbsolute(pageUri, GetJsonString(offer, "url")) ?? "";
    }

    private static void AddDomRecords(IDocument document, Uri pageUri, List<SmartRecord> results)
    {
        var selectors = new[]
        {
            "[itemtype*='Product']",
            "[class*='product-card' i]",
            "[class*='product-item' i]",
            "[class*='product-tile' i]",
            "[class*='product' i] article",
            "article[class*='product' i]",
            "li[class*='product' i]",
            "div[class*='product' i]"
        };

        var seenElements = new HashSet<IElement>();

        foreach (var selector in selectors)
        {
            foreach (var element in document.QuerySelectorAll(selector).Take(500))
            {
                if (!seenElements.Add(element)) continue;

                var record = BuildRecordFromElement(element, pageUri);
                if (Score(record) >= 2)
                    results.Add(record);
            }
        }
    }

    private static SmartRecord BuildRecordFromElement(IElement element, Uri pageUri)
    {
        var name = FirstText(element,
            "[itemprop='name']",
            "[class*='product-name' i]",
            "[class*='product-title' i]",
            "[class*='title' i]",
            "h1", "h2", "h3", "h4");

        var priceText = FirstText(element,
            "[itemprop='price']",
            "[class*='price' i]",
            "[data-price]");

        var price = ExtractPrice(priceText);
        if (string.IsNullOrWhiteSpace(price))
            price = ExtractPrice(Clean(element.TextContent));

        var imageElement = element.QuerySelector("img");
        var image = imageElement is null ? "" : ResolveImage(pageUri, imageElement);

        if (string.IsNullOrWhiteSpace(name) && imageElement is not null)
            name = Clean(imageElement.GetAttribute("alt") ?? "");

        var anchor = element.QuerySelector("a[href]");
        var link = ToAbsolute(pageUri, anchor?.GetAttribute("href")) ?? "";

        var rating = FirstAttributeOrText(element,
            "[itemprop='ratingValue']", "content");
        if (string.IsNullOrWhiteSpace(rating))
            rating = FirstText(element, "[class*='rating' i]", "[class*='stars' i]");

        var stock = FirstAttributeOrText(element,
            "[itemprop='availability']", "href");
        if (string.IsNullOrWhiteSpace(stock))
            stock = FirstText(element, "[class*='stock' i]", "[class*='availability' i]");
        stock = NormalizeAvailability(stock);

        return new SmartRecord
        {
            Name = name,
            Price = price,
            Image = image,
            Link = link,
            Rating = rating,
            Stock = stock,
            SourceUrl = pageUri.ToString()
        };
    }

    private static int Score(SmartRecord record)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(record.Name)) score++;
        if (!string.IsNullOrWhiteSpace(record.Price)) score++;
        if (!string.IsNullOrWhiteSpace(record.Image)) score++;
        if (!string.IsNullOrWhiteSpace(record.Link)) score++;
        if (!string.IsNullOrWhiteSpace(record.Rating)) score++;
        if (!string.IsNullOrWhiteSpace(record.Stock)) score++;
        return score;
    }

    private static bool HasUsefulData(SmartRecord record) => Score(record) >= 2;

    private static string FirstText(IElement root, params string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var value = Clean(root.QuerySelector(selector)?.TextContent ?? "");
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    private static string FirstAttributeOrText(IElement root, string selector, string attribute)
    {
        var element = root.QuerySelector(selector);
        if (element is null) return "";
        var value = element.GetAttribute(attribute);
        return Clean(string.IsNullOrWhiteSpace(value) ? element.TextContent : value);
    }

    private static string ResolveImage(Uri pageUri, IElement image)
    {
        var raw = image.GetAttribute("src") ??
                  image.GetAttribute("data-src") ??
                  image.GetAttribute("data-lazy-src");

        if (string.IsNullOrWhiteSpace(raw))
        {
            var srcset = image.GetAttribute("srcset");
            raw = srcset?
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
        }

        return ToAbsolute(pageUri, raw) ?? "";
    }

    private static string ExtractPrice(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var match = PriceRegex.Match(value);
        return match.Success ? Clean(match.Value) : "";
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)) return "";
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? "",
            JsonValueKind.Number => property.GetRawText(),
            _ => ""
        };
    }

    private static string GetNestedJsonString(JsonElement element, string parent, string child)
    {
        if (!element.TryGetProperty(parent, out var parentElement) ||
            parentElement.ValueKind != JsonValueKind.Object)
            return "";

        return GetJsonString(parentElement, child);
    }

    private static string GetJsonImage(JsonElement element, Uri pageUri)
    {
        if (!element.TryGetProperty("image", out var image)) return "";

        string raw = image.ValueKind switch
        {
            JsonValueKind.String => image.GetString() ?? "",
            JsonValueKind.Array when image.GetArrayLength() > 0 && image[0].ValueKind == JsonValueKind.String
                => image[0].GetString() ?? "",
            JsonValueKind.Object => GetJsonString(image, "url"),
            _ => ""
        };

        return ToAbsolute(pageUri, raw) ?? "";
    }

    private static bool IsProductType(string type) =>
        type.Equals("Product", StringComparison.OrdinalIgnoreCase) ||
        type.EndsWith("Product", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAvailability(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var clean = Clean(value);
        var slash = clean.LastIndexOf('/');
        return slash >= 0 && slash < clean.Length - 1 ? clean[(slash + 1)..] : clean;
    }

    private static string? ToAbsolute(Uri pageUri, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("#"))
            return null;

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        return Uri.TryCreate(pageUri, value, out var relative)
            ? relative.ToString()
            : null;
    }

    private static string Clean(string value) =>
        Regex.Replace(value, @"\s+", " ").Trim();
}
