using System.Net.Http.Headers;
using URLScra.Models;

namespace URLScra.Services;

public sealed class SmartScraperService
{
    private readonly HttpClient _http;

    public SmartScraperService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("URLScra", "0.4"));
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
        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
        return await AnalyzeHtmlAsync(html, finalUrl, cancellationToken);
    }

    public Task<IReadOnlyList<SmartRecord>> AnalyzeHtmlAsync(
        string html,
        string pageUrl,
        CancellationToken cancellationToken = default) =>
        SmartHtmlAnalyzer.AnalyzeAsync(html, pageUrl, cancellationToken);
}
