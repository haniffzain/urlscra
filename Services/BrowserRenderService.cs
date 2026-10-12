using Microsoft.Playwright;
using System.IO;
using URLScra.Models;

namespace URLScra.Services;

public sealed class BrowserRenderService
{
    private const int MaxScrollPasses = 12;
    private const int StablePassesToStop = 2;

    public string BrowserPath { get; }

    public BrowserRenderService()
    {
        BrowserPath = ResolveBrowserPath();
        Directory.CreateDirectory(BrowserPath);
    }

    public async Task<RenderedPage> RenderAsync(
        string url,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("URL mesti bermula dengan http:// atau https://");

        Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", BrowserPath);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Launching Chromium...");

        try
        {
            using var playwright = await Playwright.CreateAsync();
            cancellationToken.ThrowIfCancellationRequested();

            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            try
            {
                var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    Locale = "en-MY",
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36 URLScra/0.7.1",
                    ViewportSize = new ViewportSize { Width = 1365, Height = 900 }
                });

                try
                {
                    var page = await context.NewPageAsync();
                    page.SetDefaultTimeout(15000);

                    progress?.Report("Loading page...");
                    try
                    {
                        await page.GotoAsync(url, new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = 30000
                        });
                    }
                    catch (PlaywrightException ex) when (IsTimeout(ex))
                    {
                        // Streaming / SPA pages may keep navigation busy even when useful DOM is present.
                        // Continue if Chromium reached a real page rather than aborting the scrape.
                        if (string.IsNullOrWhiteSpace(page.Url) ||
                            page.Url.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
                            throw;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report("Waiting for rendered content...");

                    try
                    {
                        await page.Locator("body").WaitForAsync(new LocatorWaitForOptions
                        {
                            State = WaitForSelectorState.Attached,
                            Timeout = 5000
                        });
                    }
                    catch (PlaywrightException ex) when (IsTimeout(ex))
                    {
                        // Continue and let ContentAsync decide whether useful DOM exists.
                    }

                    // Give initial client-side rendering a brief chance to populate the DOM.
                    await Task.Delay(700, cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();
                    await AdaptiveScrollAsync(page, progress, cancellationToken);

                    progress?.Report("Capturing rendered DOM...");
                    await page.EvaluateAsync("() => window.scrollTo(0, 0)");
                    await Task.Delay(200, cancellationToken);

                    var html = await page.ContentAsync();
                    var finalUrl = string.IsNullOrWhiteSpace(page.Url) ? url : page.Url;
                    progress?.Report("Browser rendering complete.");
                    return new RenderedPage(html, finalUrl);
                }
                finally
                {
                    await context.CloseAsync();
                }
            }
            finally
            {
                await browser.CloseAsync();
            }
        }
        catch (PlaywrightException ex) when (
            ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("playwright install", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Chromium Playwright belum dipasang. Jalankan .\\install-browser.ps1 sekali sahaja. Browser akan disimpan di: {BrowserPath}",
                ex);
        }
    }

    private static bool IsTimeout(PlaywrightException ex) =>
        ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("exceeded", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase);

    private static async Task AdaptiveScrollAsync(
        IPage page,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var previousHeight = 0;
        var stablePasses = 0;

        for (var pass = 1; pass <= MaxScrollPasses; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Auto-scroll {pass}/{MaxScrollPasses}...");

            var currentHeight = await page.EvaluateAsync<int>(
                "() => Math.max(document.body?.scrollHeight || 0, document.documentElement?.scrollHeight || 0)");

            if (currentHeight <= previousHeight + 10)
                stablePasses++;
            else
                stablePasses = 0;

            previousHeight = Math.Max(previousHeight, currentHeight);

            await page.EvaluateAsync("() => window.scrollTo(0, Math.max(document.body?.scrollHeight || 0, document.documentElement?.scrollHeight || 0))");
            await Task.Delay(700, cancellationToken);

            if (stablePasses >= StablePassesToStop)
                break;
        }
    }

    private static string ResolveBrowserPath()
    {
        var configured = Environment.GetEnvironmentVariable("URLSCRA_PLAYWRIGHT_BROWSERS_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        if (Directory.Exists(@"D:\"))
            return @"D:\URLScra\PlaywrightBrowsers";

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "URLScra",
            "PlaywrightBrowsers");
    }
}
