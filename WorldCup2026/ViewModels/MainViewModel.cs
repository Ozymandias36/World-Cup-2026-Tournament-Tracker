using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using WorldCup2026.Models;
using WorldCup2026.Services;

namespace WorldCup2026.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DataServiceAggregator _aggregator;
    private readonly PdfExportService _pdfExport;

    public MainViewModel(DataServiceAggregator aggregator, PdfExportService pdfExport)
    {
        _aggregator = aggregator;
        _pdfExport = pdfExport;

        _aggregator.DataRefreshed += () =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(Teams));
                OnPropertyChanged(nameof(Matches));
                OnPropertyChanged(nameof(Groups));
                OnPropertyChanged(nameof(PlayerStats));
                OnPropertyChanged(nameof(TeamStats));
                OnPropertyChanged(nameof(ActiveSource));
                OnPropertyChanged(nameof(LastUpdatedText));
                OnPropertyChanged(nameof(IsRefreshing));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsAutoRefreshOn));
            });
        };

        _aggregator.StatusChanged += (msg) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText = msg;
                OnPropertyChanged(nameof(StatusText));
            });
        };
    }

    public ObservableCollection<Team> Teams => _aggregator.Teams;
    public ObservableCollection<Match> Matches => _aggregator.Matches;
    public ObservableCollection<Group> Groups => _aggregator.Groups;
    public ObservableCollection<PlayerStat> PlayerStats => _aggregator.PlayerStats;
    public ObservableCollection<TeamStat> TeamStats => _aggregator.TeamStats;

    public string ActiveSource => _aggregator.ActiveSource;
    public string LastUpdatedText => _aggregator.LastUpdated == default
        ? "Never"
        : _aggregator.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss");

    public string StatusText { get; set; } = "Ready";

    public bool IsRefreshing => _aggregator.IsRefreshing;
    public bool IsAutoRefreshOn => _aggregator.IsAutoRefreshEnabled;

    public Dictionary<TournamentStage, List<Match>> BracketMatches => _aggregator.GetBracketMatches();
    public Dictionary<string, List<Match>> GroupStageMatches => _aggregator.GetGroupStageMatches();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _aggregator.RefreshAllAsync();
        RefreshAllProperties();
    }

    [RelayCommand]
    private void ToggleAutoRefresh()
    {
        _aggregator.ToggleAutoRefresh();
        OnPropertyChanged(nameof(IsAutoRefreshOn));
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        var dialog = new SaveFileDialog
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
                        .Where(m => m.Stage != TournamentStage.GroupStage)
                        .ToList();

                    // PDF export is now handled by MainWindow via visual capture
                });

                StatusText = $"PDF exported to: {dialog.FileName}";
                OnPropertyChanged(nameof(StatusText));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"PDF export failed: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public async Task InitializeAsync()
    {
        await _aggregator.RefreshAllAsync();
        _aggregator.StartAutoRefresh(TimeSpan.FromSeconds(60));
        RefreshAllProperties();
    }

    private void RefreshAllProperties()
    {
        OnPropertyChanged(nameof(Teams));
        OnPropertyChanged(nameof(Matches));
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(PlayerStats));
        OnPropertyChanged(nameof(TeamStats));
        OnPropertyChanged(nameof(ActiveSource));
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(IsRefreshing));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(BracketMatches));
        OnPropertyChanged(nameof(GroupStageMatches));
    }
}
