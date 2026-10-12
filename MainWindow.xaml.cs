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

    public ObservableCollection<ScrapeItem> Results { get; } = new();
    public ObservableCollection<SmartRecord> SmartResults { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        UrlTextBox.Text = "https://example.com";
    }

    private bool IsSmartMode =>
        OutputModeComboBox.SelectedItem is ComboBoxItem selected &&
        string.Equals(selected.Content?.ToString(), "Smart Records", StringComparison.OrdinalIgnoreCase);

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Masukkan URL dahulu.", "URLScra", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var criterion = SearchCriteriaComboBox.SelectedItem is ComboBoxItem selected
            ? selected.Content?.ToString() ?? "All"
            : "All";

        var smartMode = IsSmartMode;

        AnalyzeButton.IsEnabled = false;
        OutputModeComboBox.IsEnabled = false;
        SearchCriteriaComboBox.IsEnabled = false;
        ExportButton.IsEnabled = false;
        Results.Clear();
        SmartResults.Clear();

        ResultsGrid.Visibility = smartMode ? Visibility.Collapsed : Visibility.Visible;
        SmartResultsGrid.Visibility = smartMode ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = smartMode ? "Detecting smart records..." : $"Analyzing {criterion}...";

        try
        {
            if (smartMode)
            {
                var records = await _smartScraper.AnalyzeAsync(url);
                foreach (var record in records) SmartResults.Add(record);

                CountText.Text = $"{SmartResults.Count} smart records";
                StatusText.Text = SmartResults.Count == 0
                    ? "Tiada smart record dikesan pada HTML awal. Cuba Raw Data atau laman lain."
                    : "Smart record detection complete.";
                ExportButton.IsEnabled = SmartResults.Count > 0;
            }
            else
            {
                var items = await _scraper.AnalyzeAsync(url, criterion);
                foreach (var item in items) Results.Add(item);

                CountText.Text = $"{Results.Count} records";
                StatusText.Text = Results.Count == 0
                    ? $"Tiada {criterion} dikesan pada HTML awal."
                    : $"{criterion} analysis complete.";
                ExportButton.IsEnabled = Results.Count > 0;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Analysis failed.";
            MessageBox.Show(ex.Message, "URLScra", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
            OutputModeComboBox.IsEnabled = true;
            SearchCriteriaComboBox.IsEnabled = true;
        }
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
