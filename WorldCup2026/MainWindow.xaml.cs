using System.Windows;
using WorldCup2026.Services;
using WorldCup2026.ViewModels;

namespace WorldCup2026;

public partial class MainWindow : Window
{
    private readonly DataServiceAggregator _aggregator;
    private readonly PdfExportService _pdfExport;
    private bool _initialized;

    public MainWindow(DataServiceAggregator aggregator, PdfExportService pdfExport)
    {
        InitializeComponent();
        _aggregator = aggregator;
        _pdfExport = pdfExport;

        // Wire up toolbar buttons
        RefreshBtn.Click += async (_, _) => await RefreshDataAsync();
        AutoBtn.Click += (_, _) => ToggleAutoRefresh();
        ExportBtn.Click += async (_, _) => await ExportPdfAsync();

        // Listen for data changes
        _aggregator.DataRefreshed += OnDataRefreshed;
        _aggregator.StatusChanged += OnStatusChanged;

        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        // Create sub-ViewModels
        var bracketVm = new BracketViewModel(_aggregator);
        var groupVm = new GroupStageViewModel(_aggregator);

        BracketSection.DataContext = bracketVm;
        GroupSection.DataContext = groupVm;

        UpdateStatusDisplay();

        // Initial data load
        await RefreshDataAsync();

        // Start auto-refresh
        _aggregator.StartAutoRefresh(TimeSpan.FromSeconds(60));
        UpdateStatusDisplay();
    }

    private async Task RefreshDataAsync()
    {
        try
        {
            StatusLabel.Text = "Refreshing...";
            await _aggregator.RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private void ToggleAutoRefresh()
    {
        _aggregator.ToggleAutoRefresh();
        UpdateStatusDisplay();
    }

    private async Task ExportPdfAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"WorldCup2026_Poster_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await Task.Run(() =>
                {
                    var bracketMatches = _aggregator.Matches
                        .Where(m => m.Stage != Models.TournamentStage.GroupStage)
                        .ToList();

                    _pdfExport.ExportPoster(
                        dialog.FileName,
                        bracketMatches,
                        _aggregator.Groups.ToList(),
                        _aggregator.PlayerStats.ToList(),
                        _aggregator.TeamStats.ToList(),
                        _aggregator.LastUpdated);
                });

                StatusLabel.Text = $"PDF exported!";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"PDF export failed: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnDataRefreshed()
    {
        Dispatcher.Invoke(() => UpdateStatusDisplay());
    }

    private void OnStatusChanged(string msg)
    {
        Dispatcher.Invoke(() => StatusLabel.Text = msg);
    }

    private void UpdateStatusDisplay()
    {
        var source = _aggregator.ActiveSource;
        var last = _aggregator.LastUpdated == default
            ? "Never"
            : _aggregator.LastUpdated.ToString("HH:mm:ss");
        var auto = _aggregator.IsAutoRefreshEnabled ? "ON" : "OFF";

        StatusLabel.Text = $"Source: {source} | Last update: {last} | Auto: {auto}";
        AutoLabel.Text = $"Auto: {auto}";
    }
}
