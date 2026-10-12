using Microsoft.Playwright;
using System.IO;
using URLScra.Models;

namespace URLScra.Services;

public sealed class BrowserRenderService
{
    public string BrowserPath { get; }

    public BrowserRenderService()
    {
        BrowserPath = ResolveBrowserPath();
        Directory.CreateDirectory(BrowserPath);
    }

    public async Task<RenderedPage> RenderAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("URL mesti bermula dengan http:// atau https://");

        Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", BrowserPath);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var playwright = await Playwright.CreateAsync();
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            try
            {
                var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    Locale = "en-MY",
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36 URLScra/0.4",
                    ViewportSize = new ViewportSize { Width = 1365, Height = 900 }
                });

                try
                {
                    var page = await context.NewPageAsync();
                    await page.GotoAsync(url, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 30000
                    });

                    try
                    {
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
                        {
                            Timeout = 6000
                        });
                    }
                    catch (PlaywrightException)
                    {
                        // Many modern sites keep network connections alive. Continue with the rendered DOM.
                    }

                    for (var i = 0; i < 4; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await page.EvaluateAsync("window.scrollBy(0, Math.max(window.innerHeight * 0.9, 700))");
                        await Task.Delay(650, cancellationToken);
                    }

                    await page.EvaluateAsync("window.scrollTo(0, 0)");
                    await Task.Delay(250, cancellationToken);

                    var html = await page.ContentAsync();
                    var finalUrl = string.IsNullOrWhiteSpace(page.Url) ? url : page.Url;
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
                $"Chromium Playwright belum dipasang. Jalankan install-browser.ps1 sekali sahaja. Browser akan disimpan di: {BrowserPath}",
                ex);
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
