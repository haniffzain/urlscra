using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using URLScra.Models;
using URLScra.Services;

namespace URLScra;

public partial class MainWindow : Window
{
    private readonly ScraperService _scraper = new();
    public ObservableCollection<ScrapeItem> Results { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        UrlTextBox.Text = "https://example.com";
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Masukkan URL dahulu.", "URLScra", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AnalyzeButton.IsEnabled = false;
        ExportButton.IsEnabled = false;
        StatusText.Text = "Analyzing...";
        Results.Clear();

        try
        {
            var items = await _scraper.AnalyzeAsync(url);
            foreach (var item in items) Results.Add(item);
            CountText.Text = $"{Results.Count} records";
            StatusText.Text = Results.Count == 0 ? "Tiada data dikesan pada HTML awal." : "Analysis complete.";
            ExportButton.IsEnabled = Results.Count > 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Analysis failed.";
            MessageBox.Show(ex.Message, "URLScra", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (Results.Count == 0) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export CSV",
            Filter = "CSV file (*.csv)|*.csv",
            FileName = "urlscra.csv"
        };
        if (dialog.ShowDialog() != true) return;
        await CsvExporter.SaveAsync(dialog.FileName, Results);
        StatusText.Text = $"CSV saved: {dialog.FileName}";
    }
}
