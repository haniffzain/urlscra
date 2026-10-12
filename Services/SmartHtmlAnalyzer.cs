using AngleSharp;
using AngleSharp.Dom;
using System.Text.Json;
using System.Text.RegularExpressions;
using URLScra.Models;

namespace URLScra.Services;

public static class SmartHtmlAnalyzer
{
    private static readonly Regex PriceRegex = new(
        @"(?ix)(?:\b(?:RM|MYR|USD|SGD|EUR|GBP|AUD|JPY)\s*|[$€£¥]\s*)\d{1,3}(?:[,\s]\d{3})*(?:\.\d{1,2})?|\b\d+(?:\.\d{2})\s*(?:RM|MYR|USD|SGD|EUR|GBP|AUD|JPY)\b",
        RegexOptions.Compiled);

    public static async Task<IReadOnlyList<SmartRecord>> AnalyzeAsync(
        string html,
        string pageUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri))
            throw new ArgumentException("URL halaman tidak sah.");

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html).Address(pageUri), cancellationToken);
        var results = new List<SmartRecord>();

        AddJsonLdProducts(document, pageUri, results);
        AddDomRecords(document, pageUri, results);

        return results
            .Where(record => Score(record) >= 2)
            .GroupBy(x => $"{x.Name}|{x.Price}|{x.Link}|{x.Image}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(800)
            .ToList();
    }

    private static void AddJsonLdProducts(IDocument document, Uri pageUri, List<SmartRecord> results)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']").Take(150))
        {
            var json = script.TextContent.Trim();
            if (json.Length == 0) continue;

            try
            {
                using var jsonDocument = JsonDocument.Parse(json);
                WalkJson(jsonDocument.RootElement, pageUri, results);
            }
            catch (JsonException)
            {
                // Continue with DOM detection when structured data is malformed.
            }
        }
    }

    private static void WalkJson(JsonElement element, Uri pageUri, List<SmartRecord> results)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) WalkJson(child, pageUri, results);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object) return;

        var type = GetString(element, "@type");
        if (IsProductType(type))
        {
            var record = new SmartRecord
            {
                Name = GetString(element, "name"),
                Image = GetImage(element, pageUri),
                Link = ToAbsolute(pageUri, GetString(element, "url")) ?? "",
                Rating = GetNestedString(element, "aggregateRating", "ratingValue"),
                SourceUrl = pageUri.ToString()
            };

            if (element.TryGetProperty("offers", out var offers)) FillOffer(offers, pageUri, record);
            results.Add(record);
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                WalkJson(property.Value, pageUri, results);
        }
    }

    private static void FillOffer(JsonElement offers, Uri pageUri, SmartRecord record)
    {
        var offer = offers;
        if (offers.ValueKind == JsonValueKind.Array)
        {
            var enumerator = offers.EnumerateArray();
            if (!enumerator.MoveNext()) return;
            offer = enumerator.Current;
        }

        if (offer.ValueKind != JsonValueKind.Object) return;
        var price = GetString(offer, "price");
        var currency = GetString(offer, "priceCurrency");
        if (price.Length > 0) record.Price = currency.Length > 0 ? $"{currency} {price}" : price;

        var availability = GetString(offer, "availability");
        if (availability.Length > 0) record.Stock = NormalizeAvailability(availability);

        if (record.Link.Length == 0)
            record.Link = ToAbsolute(pageUri, GetString(offer, "url")) ?? "";
    }

    private static void AddDomRecords(IDocument document, Uri pageUri, List<SmartRecord> results)
    {
        var selectors = new[]
        {
            "[itemtype*='Product']",
            "[class*='product-card' i]",
            "[class*='product-item' i]",
            "[class*='product-tile' i]",
            "[class*='item-card' i]",
            "[class*='listing-card' i]",
            "article[class*='product' i]",
            "li[class*='product' i]",
            "div[class*='product' i]"
        };

        var seen = new HashSet<IElement>();
        foreach (var selector in selectors)
        {
            foreach (var element in document.QuerySelectorAll(selector).Take(800))
            {
                if (!seen.Add(element)) continue;
                var record = BuildRecord(element, pageUri);
                if (Score(record) >= 2) results.Add(record);
            }
        }
    }

    private static SmartRecord BuildRecord(IElement element, Uri pageUri)
    {
        var imageElement = element.QuerySelector("img");
        var name = FirstText(element,
            "[itemprop='name']", "[class*='product-name' i]", "[class*='product-title' i]",
            "[class*='item-name' i]", "[class*='title' i]", "h1", "h2", "h3", "h4");

        if (name.Length == 0 && imageElement is not null)
            name = Clean(imageElement.GetAttribute("alt") ?? "");

        var priceCandidate = FirstText(element,
            "[itemprop='price']", "[class*='price' i]", "[data-price]");
        var price = ExtractPrice(priceCandidate);
        if (price.Length == 0) price = ExtractPrice(Clean(element.TextContent));

        var image = imageElement is null ? "" : ResolveImage(pageUri, imageElement);
        var link = ToAbsolute(pageUri, element.QuerySelector("a[href]")?.GetAttribute("href")) ?? "";

        var rating = FirstAttributeOrText(element, "[itemprop='ratingValue']", "content");
        if (rating.Length == 0) rating = FirstText(element, "[class*='rating' i]", "[class*='star' i]");

        var stock = FirstAttributeOrText(element, "[itemprop='availability']", "href");
        if (stock.Length == 0) stock = FirstText(element, "[class*='stock' i]", "[class*='availability' i]");

        return new SmartRecord
        {
            Name = name,
            Price = price,
            Image = image,
            Link = link,
            Rating = rating,
            Stock = NormalizeAvailability(stock),
            SourceUrl = pageUri.ToString()
        };
    }

    private static int Score(SmartRecord record)
    {
        var score = 0;
        if (record.Name.Length > 0) score++;
        if (record.Price.Length > 0) score++;
        if (record.Image.Length > 0) score++;
        if (record.Link.Length > 0) score++;
        if (record.Rating.Length > 0) score++;
        if (record.Stock.Length > 0) score++;
        return score;
    }

    private static string FirstText(IElement root, params string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var value = Clean(root.QuerySelector(selector)?.TextContent ?? "");
            if (value.Length > 0) return value;
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
        var raw = image.GetAttribute("src") ?? image.GetAttribute("data-src") ?? image.GetAttribute("data-lazy-src");
        if (string.IsNullOrWhiteSpace(raw))
            raw = image.GetAttribute("srcset")?.Split(',', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return ToAbsolute(pageUri, raw) ?? "";
    }

    private static string ExtractPrice(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var match = PriceRegex.Match(value);
        return match.Success ? Clean(match.Value) : "";
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)) return "";
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? "",
            JsonValueKind.Number => property.GetRawText(),
            _ => ""
        };
    }

    private static string GetNestedString(JsonElement element, string parent, string child)
    {
        if (!element.TryGetProperty(parent, out var nested) || nested.ValueKind != JsonValueKind.Object) return "";
        return GetString(nested, child);
    }

    private static string GetImage(JsonElement element, Uri pageUri)
    {
        if (!element.TryGetProperty("image", out var image)) return "";
        var raw = image.ValueKind switch
        {
            JsonValueKind.String => image.GetString() ?? "",
            JsonValueKind.Array when image.GetArrayLength() > 0 && image[0].ValueKind == JsonValueKind.String => image[0].GetString() ?? "",
            JsonValueKind.Object => GetString(image, "url"),
            _ => ""
        };
        return ToAbsolute(pageUri, raw) ?? "";
    }

    private static bool IsProductType(string type) =>
        type.Equals("Product", StringComparison.OrdinalIgnoreCase) || type.EndsWith("Product", StringComparison.OrdinalIgnoreCase);

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
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("#")) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)) return absolute.ToString();
        return Uri.TryCreate(pageUri, value, out var relative) ? relative.ToString() : null;
    }

    private static string Clean(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
