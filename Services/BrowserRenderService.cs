using Microsoft.Playwright;
using System.IO;
using System.Net;
using URLScra.Models;

namespace URLScra.Services;

public sealed class BrowserRenderService
{
    private const int MaxScrollPasses = 12;
    private const int StablePassesToStop = 2;

    private const string RenderedTextScript = """
        () => {
            const chunks = [];
            const skipTags = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEMPLATE']);

            const collect = (root) => {
                const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
                while (walker.nextNode()) {
                    const node = walker.currentNode;
                    const parent = node.parentElement;
                    if (parent && skipTags.has(parent.tagName)) continue;
                    if (parent) {
                        const style = getComputedStyle(parent);
                        if (style.display === 'none' || style.visibility === 'hidden') continue;
                    }

                    const text = (node.nodeValue || '').replace(/\s+/g, ' ').trim();
                    if (text.length > 1) chunks.push(text);
                }

                if (!root.querySelectorAll) return;
                for (const element of root.querySelectorAll('*')) {
                    if (element.shadowRoot) collect(element.shadowRoot);
                }
            };

            collect(document);
            return chunks.join('\n');
        }
        """;

    private const string RenderedImagesScript = """
        () => {
            const urls = new Set();

            const add = (value) => {
                if (!value || typeof value !== 'string') return;
                const raw = value.trim().replace(/^['"]|['"]$/g, '');
                if (!raw || raw.startsWith('data:') || raw.startsWith('blob:')) return;
                try {
                    const absolute = new URL(raw, document.baseURI).href;
                    if (absolute.startsWith('http://') || absolute.startsWith('https://')) {
                        urls.add(absolute);
                    }
                } catch (_) { }
            };

            const addSrcset = (value) => {
                if (!value) return;
                for (const candidate of value.split(',')) {
                    const first = candidate.trim().split(/\s+/)[0];
                    add(first);
                }
            };

            const collect = (root) => {
                if (!root.querySelectorAll) return;

                for (const img of root.querySelectorAll('img')) {
                    add(img.currentSrc);
                    add(img.src);
                    add(img.getAttribute('src'));
                    add(img.getAttribute('data-src'));
                    add(img.getAttribute('data-lazy-src'));
                    add(img.getAttribute('data-original'));
                    add(img.getAttribute('data-url'));
                    addSrcset(img.getAttribute('srcset'));
                    addSrcset(img.getAttribute('data-srcset'));
                }

                for (const source of root.querySelectorAll('picture source, source[type^="image/"]')) {
                    add(source.getAttribute('src'));
                    addSrcset(source.getAttribute('srcset'));
                    addSrcset(source.getAttribute('data-srcset'));
                }

                for (const element of root.querySelectorAll('*')) {
                    try {
                        const background = getComputedStyle(element).backgroundImage || '';
                        const regex = /url\((['"]?)(.*?)\1\)/g;
                        let match;
                        while ((match = regex.exec(background)) !== null) add(match[2]);
                    } catch (_) { }

                    if (element.shadowRoot) collect(element.shadowRoot);
                }
            };

            collect(document);

            try {
                for (const entry of performance.getEntriesByType('resource')) {
                    const name = entry.name || '';
                    const type = (entry.initiatorType || '').toLowerCase();
                    if (type === 'img' || /\.(?:avif|bmp|gif|ico|jpe?g|png|svg|webp)(?:$|[?#])/i.test(name)) {
                        add(name);
                    }
                }
            } catch (_) { }

            return Array.from(urls).slice(0, 1500);
        }
        """;

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
                    JavaScriptEnabled = true,
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
                        // Continue; later capture will decide whether useful content exists.
                    }

                    await WaitForClientRenderAsync(page, progress, cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();
                    await AdaptiveScrollAsync(page, progress, cancellationToken);

                    progress?.Report("Capturing rendered DOM, text and images...");
                    await page.EvaluateAsync("() => window.scrollTo(0, 0)");
                    await Task.Delay(250, cancellationToken);

                    var html = await page.ContentAsync();
                    var renderedText = await CaptureRenderedTextAsync(page);
                    var renderedImages = await CaptureRenderedImagesAsync(page);
                    var finalUrl = string.IsNullOrWhiteSpace(page.Url) ? url : page.Url;

                    var augmentedHtml = AppendRenderedText(html, renderedText);
                    augmentedHtml = AppendRenderedImages(augmentedHtml, renderedImages);

                    progress?.Report($"Browser rendering complete · {renderedImages.Count} image resources captured.");
                    return new RenderedPage(augmentedHtml, finalUrl, renderedText);
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

    private static async Task WaitForClientRenderAsync(
        IPage page,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        for (var pass = 1; pass <= 12; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await CaptureRenderedTextAsync(page);

            if (text.Length >= 180 && !LooksLikeJavaScriptPlaceholder(text))
                return;

            progress?.Report($"Waiting for client content {pass}/12...");
            await Task.Delay(500, cancellationToken);
        }
    }

    private static Task<string> CaptureRenderedTextAsync(IPage page) =>
        page.EvaluateAsync<string>(RenderedTextScript);

    private static async Task<IReadOnlyList<string>> CaptureRenderedImagesAsync(IPage page)
    {
        var urls = await page.EvaluateAsync<string[]>(RenderedImagesScript);
        return urls
            .Where(IsHttpUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(1500)
            .ToList();
    }

    private static string AppendRenderedText(string html, string renderedText)
    {
        var lines = renderedText
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length >= 3 && !LooksLikeJavaScriptPlaceholder(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(600)
            .Select(line => $"<p data-urlscra-rendered=\"true\">{WebUtility.HtmlEncode(line)}</p>")
            .ToArray();

        if (lines.Length == 0)
            return html;

        var block = $"<section data-urlscra-rendered-text=\"true\">{string.Join(string.Empty, lines)}</section>";
        return InsertBeforeBodyClose(html, block);
    }

    private static string AppendRenderedImages(string html, IReadOnlyList<string> imageUrls)
    {
        if (imageUrls.Count == 0)
            return html;

        var tags = imageUrls
            .Where(IsHttpUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(1500)
            .Select(url => $"<img data-urlscra-rendered-image=\"true\" src=\"{WebUtility.HtmlEncode(url)}\" alt=\"Rendered Browser\" />")
            .ToArray();

        if (tags.Length == 0)
            return html;

        var block = $"<section data-urlscra-rendered-images=\"true\" style=\"display:none\">{string.Join(string.Empty, tags)}</section>";
        return InsertBeforeBodyClose(html, block);
    }

    private static string InsertBeforeBodyClose(string html, string block)
    {
        var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyClose >= 0 ? html.Insert(bodyClose, block) : html + block;
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool LooksLikeJavaScriptPlaceholder(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        var value = text.ToLowerInvariant();
        return value.Contains("javascript is required") ||
               value.Contains("please enable javascript") ||
               value.Contains("enable javascript") ||
               value.Contains("requires javascript") ||
               value.Contains("javascript must be enabled") ||
               value.Contains("upgrade to a modern browser");
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
