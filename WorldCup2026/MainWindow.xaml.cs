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
        LangBtn.Click += (_, _) => LocalizationService.Toggle();
        RefreshBtn.Click += async (_, _) => await RefreshDataAsync();
        AutoBtn.Click += (_, _) => ToggleAutoRefresh();
        ExportBtn.Click += async (_, _) => await ExportPdfAsync();

        // Listen for data changes
        _aggregator.DataRefreshed += OnDataRefreshed;
        _aggregator.StatusChanged += OnStatusChanged;

        // Language switch re-localizes this window's own chrome; BracketView/GroupStageView
        // localize themselves independently via the same event.
        LocalizationService.LanguageChanged += () => Dispatcher.Invoke(ApplyLocalization);
        ApplyLocalization();

        Loaded += async (_, _) => await InitializeAsync();
    }

    private void ApplyLocalization()
    {
        Title = LocalizationService.T("AppTitle");
        HeaderTitle.Text = LocalizationService.T("HeaderTitle");
        HeaderSub.Text = LocalizationService.T("HeaderSub");
        LangBtn.Content = LocalizationService.T("Language");
        RefreshBtn.Content = LocalizationService.T("Refresh");
        ExportBtn.Content = "📥 " + LocalizationService.T("ExportPdf");
        KnockoutTitleLabel.Text = LocalizationService.T("KnockoutTitle");
        GroupTitleLabel.Text = LocalizationService.T("GroupTitle");
        UpdateStatusDisplay();
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

        // Initial data load (local baseline)
        await RefreshDataAsync();

        // Start auto-refresh (fires immediately, then every 60s)
        _aggregator.StartAutoRefresh(TimeSpan.FromSeconds(60));

        // Trigger a second refresh after a short delay to pick up live scores
        _ = Task.Run(async () => { await Task.Delay(2000); await RefreshDataAsync(); });

        UpdateStatusDisplay();
    }

    private async Task RefreshDataAsync()
    {
        try
        {
            StatusLabel.Text = LocalizationService.T("Refreshing");
            await _aggregator.RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"{LocalizationService.T("ErrorPrefix")}: {ex.Message}";
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
                StatusLabel.Text = LocalizationService.T("ExportingPdf");
                var vm = BracketSection.DataContext as BracketViewModel;
                if (vm == null) { StatusLabel.Text = LocalizationService.T("NoBracketData"); return; }
                var (upper, lower) = vm.GetSplitBracket();
                var third = vm.GetThirdPlaceMatch();

                await Task.Run(() =>
                {
                    _pdfExport.ExportBracket(dialog.FileName,
                        upper, lower,
                        upper.FirstOrDefault(r => r.Stage == Models.TournamentStage.Final)?.Matches.FirstOrDefault(),
                        third,
                        _aggregator.Groups.ToList(),
                        _aggregator.LastUpdated);
                });

                StatusLabel.Text = LocalizationService.T("PdfExported");
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
            ? LocalizationService.T("Never")
            : _aggregator.LastUpdated.ToString("HH:mm:ss");
        var auto = _aggregator.IsAutoRefreshEnabled ? LocalizationService.T("StatusOn") : LocalizationService.T("StatusOff");

        StatusLabel.Text = $"{LocalizationService.T("StatusSource")}: {source} | {LocalizationService.T("StatusLastUpdate")}: {last} | {LocalizationService.T("StatusAuto")}: {auto}";
        AutoLabel.Text = $"{LocalizationService.T("StatusAuto")}: {auto}";
    }
}
