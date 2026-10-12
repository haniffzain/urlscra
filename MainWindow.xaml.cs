using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using URLScra.Models;
using URLScra.Services;

namespace URLScra;

public partial class MainWindow : Window
{
    private readonly ScraperService _scraper = new();
    private readonly SmartScraperService _smartScraper = new();
    private readonly BrowserRenderService _browser = new();
    private readonly HttpClient _previewHttp = CreatePreviewHttpClient();

    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _previewCancellation;
    private string? _currentPreviewUrl;

    public ObservableCollection<ScrapeItem> Results { get; } = new();
    public ObservableCollection<SmartRecord> SmartResults { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        UrlTextBox.Text = "https://example.com";

        SearchCriteriaComboBox.SelectionChanged += PreviewModeComboBox_SelectionChanged;
        OutputModeComboBox.SelectionChanged += PreviewModeComboBox_SelectionChanged;
        UpdatePreviewMode();
        ClearPreview("Select an Image, Video or PDF result to preview.");
    }

    private bool IsSmartMode =>
        string.Equals(GetSelectedText(OutputModeComboBox, "Raw Data"), "Smart Records", StringComparison.OrdinalIgnoreCase);

    private string SelectedCriterion => GetSelectedText(SearchCriteriaComboBox, "All");

    private bool IsPreviewMode
    {
        get
        {
            if (IsSmartMode) return false;
            var criterion = SelectedCriterion;
            return criterion.Equals("Image", StringComparison.OrdinalIgnoreCase) ||
                   criterion.Equals("Video", StringComparison.OrdinalIgnoreCase) ||
                   criterion.Equals("PDF", StringComparison.OrdinalIgnoreCase);
        }
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Masukkan URL dahulu.", "URLScra", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var criterion = SelectedCriterion;
        var engine = GetSelectedText(EngineComboBox, "Auto");
        var smartMode = IsSmartMode;

        _analysisCancellation?.Dispose();
        _analysisCancellation = new CancellationTokenSource();
        var cancellationToken = _analysisCancellation.Token;

        SetControlsEnabled(false);
        CancelButton.IsEnabled = true;
        Results.Clear();
        SmartResults.Clear();
        ResultsGrid.Visibility = smartMode ? Visibility.Collapsed : Visibility.Visible;
        SmartResultsGrid.Visibility = smartMode ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = "0 records";
        UpdatePreviewMode();
        ClearPreview("Waiting for results...");

        try
        {
            if (smartMode)
                await AnalyzeSmartAsync(url, engine, cancellationToken);
            else
                await AnalyzeRawAsync(url, criterion, engine, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Analysis cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Analysis failed.";
            MessageBox.Show(ex.Message, "URLScra", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CancelButton.IsEnabled = false;
            SetControlsEnabled(true);
            _analysisCancellation?.Dispose();
            _analysisCancellation = null;
        }
    }

    private async Task AnalyzeRawAsync(
        string url,
        string criterion,
        string engine,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ScrapeItem> items;
        var usedBrowser = false;

        if (engine.Equals("Browser", StringComparison.OrdinalIgnoreCase))
        {
            items = await AnalyzeRawWithBrowserAsync(url, criterion, cancellationToken);
            usedBrowser = true;
        }
        else
        {
            StatusText.Text = $"Analyzing {criterion} with Fast HTML...";
            items = await _scraper.AnalyzeAsync(url, criterion, cancellationToken);

            if (engine.Equals("Auto", StringComparison.OrdinalIgnoreCase) && NeedsBrowserFallback(items, criterion))
            {
                StatusText.Text = "HTML awal tidak mencukupi. Rendering JavaScript...";
                items = await AnalyzeRawWithBrowserAsync(url, criterion, cancellationToken);
                usedBrowser = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach (var item in items)
            Results.Add(item);

        CountText.Text = $"{Results.Count} records";
        StatusText.Text = Results.Count == 0
            ? $"Tiada {criterion} dikesan."
            : $"{criterion} complete · {(usedBrowser ? "Browser" : "Fast HTML")}.";
        ExportButton.IsEnabled = Results.Count > 0;

        if (IsPreviewMode && Results.Count > 0)
        {
            ResultsGrid.SelectedIndex = 0;
            ResultsGrid.ScrollIntoView(Results[0]);
        }
        else if (IsPreviewMode)
        {
            ClearPreview("No previewable result found.");
        }
    }

    private async Task AnalyzeSmartAsync(
        string url,
        string engine,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SmartRecord> records;
        var usedBrowser = false;

        if (engine.Equals("Browser", StringComparison.OrdinalIgnoreCase))
        {
            records = await AnalyzeSmartWithBrowserAsync(url, cancellationToken);
            usedBrowser = true;
        }
        else
        {
            StatusText.Text = "Detecting smart records with Fast HTML...";
            records = await _smartScraper.AnalyzeAsync(url, cancellationToken);

            if (engine.Equals("Auto", StringComparison.OrdinalIgnoreCase) && records.Count == 0)
            {
                StatusText.Text = "Tiada smart record pada HTML awal. Rendering JavaScript...";
                records = await AnalyzeSmartWithBrowserAsync(url, cancellationToken);
                usedBrowser = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach (var record in records)
            SmartResults.Add(record);

        CountText.Text = $"{SmartResults.Count} smart records";
        StatusText.Text = SmartResults.Count == 0
            ? "Tiada smart record dikesan."
            : $"Smart records complete · {(usedBrowser ? "Browser" : "Fast HTML")}.";
        ExportButton.IsEnabled = SmartResults.Count > 0;
    }

    private async Task<IReadOnlyList<ScrapeItem>> AnalyzeRawWithBrowserAsync(
        string url,
        string criterion,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<string>(message => StatusText.Text = message);
        var rendered = await _browser.RenderAsync(url, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        StatusText.Text = "Rendered. Extracting data...";
        return await _scraper.AnalyzeHtmlAsync(rendered.Html, rendered.FinalUrl, criterion, cancellationToken);
    }

    private async Task<IReadOnlyList<SmartRecord>> AnalyzeSmartWithBrowserAsync(
        string url,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<string>(message => StatusText.Text = message);
        var rendered = await _browser.RenderAsync(url, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        StatusText.Text = "Rendered. Detecting smart records...";
        return await _smartScraper.AnalyzeHtmlAsync(rendered.Html, rendered.FinalUrl, cancellationToken);
    }

    private static bool NeedsBrowserFallback(IReadOnlyList<ScrapeItem> items, string criterion)
    {
        if (items.Count == 0) return true;

        if (items.Any(IsJavaScriptPlaceholder))
            return true;

        if (!criterion.Equals("All", StringComparison.OrdinalIgnoreCase))
            return false;

        var usefulTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Text", "Price", "Image", "Video", "Audio", "Link", "Email", "Phone", "PDF",
            "Product Name", "Product Price", "Table Row"
        };

        return items.Count < 3 || !items.Any(item => usefulTypes.Contains(item.Type));
    }

    private static bool IsJavaScriptPlaceholder(ScrapeItem item)
    {
        if (!item.Type.Equals("Text", StringComparison.OrdinalIgnoreCase) &&
            !item.Type.Equals("Title", StringComparison.OrdinalIgnoreCase))
            return false;

        var value = item.Value?.Trim() ?? string.Empty;
        if (value.Length == 0) return false;

        var markers = new[]
        {
            "javascript is required",
            "javascript required",
            "enable javascript",
            "please enable javascript",
            "requires javascript",
            "need to enable javascript",
            "turn on javascript",
            "upgrade to a modern browser",
            "browser does not support javascript"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private void SetControlsEnabled(bool enabled)
    {
        AnalyzeButton.IsEnabled = enabled;
        EngineComboBox.IsEnabled = enabled;
        OutputModeComboBox.IsEnabled = enabled;
        SearchCriteriaComboBox.IsEnabled = enabled;
        if (!enabled)
            ExportButton.IsEnabled = false;
    }

    private static string GetSelectedText(ComboBox comboBox, string fallback) =>
        comboBox.SelectedItem is ComboBoxItem selected
            ? selected.Content?.ToString() ?? fallback
            : fallback;

    private void PreviewModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePreviewMode();
        ClearPreview("Select a result to preview.");
    }

    private void UpdatePreviewMode()
    {
        var criterion = SelectedCriterion;
        var showPreview = !IsSmartMode &&
                          (criterion.Equals("Image", StringComparison.OrdinalIgnoreCase) ||
                           criterion.Equals("Video", StringComparison.OrdinalIgnoreCase) ||
                           criterion.Equals("PDF", StringComparison.OrdinalIgnoreCase));
        var showThumbnails = showPreview && criterion.Equals("Image", StringComparison.OrdinalIgnoreCase);

        PreviewColumn.Width = showPreview ? new GridLength(430) : new GridLength(0);
        PreviewSplitterColumn.Width = showPreview ? new GridLength(6) : new GridLength(0);
        PreviewPanel.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitter.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        ThumbnailColumn.Visibility = showThumbnails ? Visibility.Visible : Visibility.Collapsed;
        ResultsGrid.RowHeight = showThumbnails ? 70 : double.NaN;
    }

    private async void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsPreviewMode) return;

        if (ResultsGrid.SelectedItem is not ScrapeItem item)
        {
            ClearPreview("Select a result to preview.");
            return;
        }

        await ShowPreviewAsync(item);
    }

    private async Task ShowPreviewAsync(ScrapeItem item)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var cancellationToken = _previewCancellation.Token;

        var criterion = SelectedCriterion;
        var isPdf = criterion.Equals("PDF", StringComparison.OrdinalIgnoreCase) ||
                    item.Type.Equals("PDF", StringComparison.OrdinalIgnoreCase);
        var isVideo = criterion.Equals("Video", StringComparison.OrdinalIgnoreCase) ||
                      item.Type.Contains("Video", StringComparison.OrdinalIgnoreCase);

        _currentPreviewUrl = item.Value;
        PreviewTitleText.Text = isPdf
            ? "PDF Preview"
            : isVideo
                ? "Video Preview"
                : "Image Preview";
        PreviewTypeText.Text = string.IsNullOrWhiteSpace(item.SourceUrl)
            ? item.Type
            : $"{item.Type} · {item.SourceUrl}";
        PreviewUrlTextBox.Text = item.Value;

        var validUrl = Uri.TryCreate(item.Value, UriKind.Absolute, out var uri) &&
                       (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        CopyPreviewUrlButton.IsEnabled = !string.IsNullOrWhiteSpace(item.Value);
        OpenPreviewUrlButton.IsEnabled = validUrl;
        SavePreviewButton.IsEnabled = validUrl;

        StopVideoPreview();
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PdfPreviewPanel.Visibility = Visibility.Collapsed;
        PreviewEmptyText.Visibility = Visibility.Collapsed;
        VideoControlsPanel.Visibility = Visibility.Collapsed;

        if (isPdf)
        {
            ShowPdfPreview(validUrl ? uri : null);
            return;
        }

        if (isVideo)
        {
            ShowVideoPreview(item, validUrl ? uri : null);
            return;
        }

        await LoadImagePreviewAsync(item.Value, cancellationToken);
    }

    private async Task LoadImagePreviewAsync(string url, CancellationToken cancellationToken)
    {
        PreviewHintText.Text = "Loading image preview...";
        PreviewImage.Visibility = Visibility.Visible;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (Uri.TryCreate(UrlTextBox.Text.Trim(), UriKind.Absolute, out var referrer))
                request.Headers.Referrer = referrer;

            using var response = await _previewHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            PreviewImage.Source = bitmap;
            PreviewHintText.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} px";
        }
        catch (OperationCanceledException)
        {
            // A newer preview selection replaced this request.
        }
        catch (Exception ex)
        {
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewEmptyText.Visibility = Visibility.Visible;
            PreviewEmptyText.Text = "Image preview unavailable.";
            PreviewHintText.Text = ex.Message;
        }
    }

    private void ShowPdfPreview(Uri? uri)
    {
        if (uri is null)
        {
            PreviewEmptyText.Visibility = Visibility.Visible;
            PreviewEmptyText.Text = "Invalid PDF URL.";
            PreviewHintText.Text = "The PDF URL cannot be opened or saved.";
            return;
        }

        PdfPreviewPanel.Visibility = Visibility.Visible;
        PreviewHintText.Text = "PDF detected · use Open to view it or Save As to keep a copy.";
    }

    private void ShowVideoPreview(ScrapeItem item, Uri? uri)
    {
        if (uri is null)
        {
            PreviewEmptyText.Visibility = Visibility.Visible;
            PreviewEmptyText.Text = "Invalid video URL.";
            PreviewHintText.Text = "The URL cannot be played or opened.";
            return;
        }

        if (IsStreamOrEmbed(item, uri))
        {
            PreviewEmptyText.Visibility = Visibility.Visible;
            PreviewEmptyText.Text = "Stream / embed detected";
            PreviewHintText.Text = "Native preview supports direct video files. Use Open for this stream/embed; browser preview is planned next.";
            return;
        }

        try
        {
            VideoPlayer.Visibility = Visibility.Visible;
            VideoControlsPanel.Visibility = Visibility.Visible;
            PreviewHintText.Text = "Loading video preview...";
            VideoPlayer.Source = uri;
            VideoPlayer.Play();
        }
        catch (Exception ex)
        {
            StopVideoPreview();
            PreviewEmptyText.Visibility = Visibility.Visible;
            PreviewEmptyText.Text = "Video preview unavailable.";
            PreviewHintText.Text = ex.Message;
        }
    }

    private static bool IsStreamOrEmbed(ScrapeItem item, Uri uri)
    {
        if (item.Type.Contains("Embed", StringComparison.OrdinalIgnoreCase)) return true;

        var path = uri.AbsolutePath.ToLowerInvariant();
        if (path.EndsWith(".m3u8") || path.EndsWith(".mpd")) return true;

        var host = uri.Host.ToLowerInvariant();
        return host.Contains("youtube.com") ||
               host.Contains("youtu.be") ||
               host.Contains("vimeo.com");
    }

    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        PreviewHintText.Text = VideoPlayer.NaturalVideoWidth > 0 && VideoPlayer.NaturalVideoHeight > 0
            ? $"Playing · {VideoPlayer.NaturalVideoWidth} × {VideoPlayer.NaturalVideoHeight}"
            : "Playing video preview.";
    }

    private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        StopVideoPreview();
        PreviewEmptyText.Visibility = Visibility.Visible;
        PreviewEmptyText.Text = "Native video preview unavailable.";
        PreviewHintText.Text = "The codec or stream is not supported by the Windows native player. Use Open to inspect it externally.";
    }

    private void PlayPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (VideoPlayer.Source is null) return;
        VideoPlayer.Play();
        PreviewHintText.Text = "Playing video preview.";
    }

    private void PausePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (VideoPlayer.Source is null) return;
        VideoPlayer.Pause();
        PreviewHintText.Text = "Video paused.";
    }

    private void StopPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (VideoPlayer.Source is null) return;
        VideoPlayer.Stop();
        PreviewHintText.Text = "Video stopped.";
    }

    private void StopVideoPreview()
    {
        try
        {
            VideoPlayer.Stop();
        }
        catch
        {
            // Ignore media shutdown errors when switching preview rows.
        }

        VideoPlayer.Source = null;
        VideoPlayer.Visibility = Visibility.Collapsed;
        VideoControlsPanel.Visibility = Visibility.Collapsed;
    }

    private void ClearPreview(string message)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        _currentPreviewUrl = null;

        StopVideoPreview();
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PdfPreviewPanel.Visibility = Visibility.Collapsed;
        PreviewEmptyText.Visibility = Visibility.Visible;
        PreviewEmptyText.Text = message;
        PreviewTitleText.Text = "Preview";
        PreviewTypeText.Text = "Image / Video / PDF";
        PreviewHintText.Text = "Select a result to preview.";
        PreviewUrlTextBox.Text = "";
        CopyPreviewUrlButton.IsEnabled = false;
        OpenPreviewUrlButton.IsEnabled = false;
        SavePreviewButton.IsEnabled = false;
    }

    private void OpenPreviewUrlButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPreviewUrl)) return;
        if (!Uri.TryCreate(_currentPreviewUrl, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;

        Process.Start(new ProcessStartInfo(uri.ToString())
        {
            UseShellExecute = true
        });
    }

    private void CopyPreviewUrlButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPreviewUrl)) return;
        Clipboard.SetText(_currentPreviewUrl);
        PreviewHintText.Text = "URL copied to clipboard.";
    }

    private async void SavePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPreviewUrl)) return;
        if (!Uri.TryCreate(_currentPreviewUrl, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;

        var dialog = new SaveFileDialog
        {
            Title = "Save selected resource",
            FileName = SuggestFileName(uri, SelectedCriterion),
            Filter = "All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        SavePreviewButton.IsEnabled = false;
        PreviewHintText.Text = "Downloading selected resource...";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (Uri.TryCreate(UrlTextBox.Text.Trim(), UriKind.Absolute, out var referrer))
                request.Headers.Referrer = referrer;

            using var response = await _previewHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = File.Create(dialog.FileName);
            await source.CopyToAsync(destination);

            PreviewHintText.Text = $"Saved: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            PreviewHintText.Text = $"Save failed: {ex.Message}";
            MessageBox.Show(ex.Message, "URLScra Save", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SavePreviewButton.IsEnabled = true;
        }
    }

    private static string SuggestFileName(Uri uri, string criterion)
    {
        var name = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = criterion.Equals("PDF", StringComparison.OrdinalIgnoreCase)
                ? "urlscra-document.pdf"
                : criterion.Equals("Image", StringComparison.OrdinalIgnoreCase)
                    ? "urlscra-image"
                    : criterion.Equals("Video", StringComparison.OrdinalIgnoreCase)
                        ? "urlscra-video"
                        : "urlscra-resource";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return name;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_analysisCancellation is null) return;
        StatusText.Text = "Cancelling analysis...";
        CancelButton.IsEnabled = false;
        _analysisCancellation.Cancel();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsSmartMode && SmartResults.Count == 0) return;
        if (!IsSmartMode && Results.Count == 0) return;

        var dialog = new SaveFileDialog
        {
            Title = "Export CSV",
            Filter = "CSV file (*.csv)|*.csv",
            FileName = IsSmartMode ? "urlscra-smart-records.csv" : "urlscra.csv"
        };

        if (dialog.ShowDialog() != true) return;

        if (IsSmartMode)
            await CsvExporter.SaveSmartAsync(dialog.FileName, SmartResults);
        else
            await CsvExporter.SaveAsync(dialog.FileName, Results);

        StatusText.Text = $"CSV saved: {dialog.FileName}";
    }

    protected override void OnClosed(EventArgs e)
    {
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        StopVideoPreview();
        _previewHttp.Dispose();
        base.OnClosed(e);
    }

    private static HttpClient CreatePreviewHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(90)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 URLScra/0.7");
        return client;
    }
}
