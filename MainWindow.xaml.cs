using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using URLScra.Models;
using URLScra.Services;

namespace URLScra;

public partial class MainWindow : Window
{
    private readonly ScraperService _scraper = new();
    private readonly SmartScraperService _smartScraper = new();
    private readonly BrowserRenderService _browser = new();
    private CancellationTokenSource? _analysisCancellation;

    public ObservableCollection<ScrapeItem> Results { get; } = new();
    public ObservableCollection<SmartRecord> SmartResults { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        UrlTextBox.Text = "https://example.com";
    }

    private bool IsSmartMode =>
        string.Equals(GetSelectedText(OutputModeComboBox, "Raw Data"), "Smart Records", StringComparison.OrdinalIgnoreCase);

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Masukkan URL dahulu.", "URLScra", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var criterion = GetSelectedText(SearchCriteriaComboBox, "All");
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
        foreach (var item in items) Results.Add(item);
        CountText.Text = $"{Results.Count} records";
        StatusText.Text = Results.Count == 0
            ? $"Tiada {criterion} dikesan."
            : $"{criterion} complete · {(usedBrowser ? "Browser" : "Fast HTML")}.";
        ExportButton.IsEnabled = Results.Count > 0;
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
        foreach (var record in records) SmartResults.Add(record);
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
        if (!criterion.Equals("All", StringComparison.OrdinalIgnoreCase)) return false;

        var usefulTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Text", "Price", "Image", "Video", "Audio", "Link", "Email", "Phone",
            "Product Name", "Product Price", "Table Row"
        };

        return items.Count < 3 || !items.Any(item => usefulTypes.Contains(item.Type));
    }

    private void SetControlsEnabled(bool enabled)
    {
        AnalyzeButton.IsEnabled = enabled;
        EngineComboBox.IsEnabled = enabled;
        OutputModeComboBox.IsEnabled = enabled;
        SearchCriteriaComboBox.IsEnabled = enabled;
        if (!enabled) ExportButton.IsEnabled = false;
    }

    private static string GetSelectedText(ComboBox comboBox, string fallback) =>
        comboBox.SelectedItem is ComboBoxItem selected
            ? selected.Content?.ToString() ?? fallback
            : fallback;

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
}
