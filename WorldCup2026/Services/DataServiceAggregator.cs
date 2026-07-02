using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using WorldCup2026.Models;

namespace WorldCup2026.Services;

/// <summary>
/// Orchestrates multiple data sources with automatic fallback.
/// Tries primary source first, falls back to secondary, then tertiary.
/// Provides auto-refresh capability with configurable interval.
/// </summary>
public class DataServiceAggregator
{
    private readonly List<IDataService> _services;
    private readonly ILogger<DataServiceAggregator>? _logger;
    private Timer? _refreshTimer;
    private bool _isAutoRefreshEnabled;
    private TimeSpan _refreshInterval = TimeSpan.FromSeconds(60);

    public event Action? DataRefreshed;
    public event Action<string>? StatusChanged;

    public ObservableCollection<Team> Teams { get; } = new();
    public ObservableCollection<Match> Matches { get; } = new();
    public ObservableCollection<Group> Groups { get; } = new();
    public ObservableCollection<PlayerStat> PlayerStats { get; } = new();
    public ObservableCollection<TeamStat> TeamStats { get; } = new();

    public string ActiveSource { get; private set; } = "None";
    public DateTime LastUpdated { get; private set; }
    public bool IsRefreshing { get; private set; }
    public string LastError { get; private set; } = string.Empty;

    public DataServiceAggregator(IEnumerable<IDataService> services, ILogger<DataServiceAggregator>? logger = null)
    {
        _services = services.ToList();
        _logger = logger;
    }

    /// <summary>
    /// Refresh all data from available sources using fallback strategy.
    /// </summary>
    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        StatusChanged?.Invoke("Refreshing...");

        var mergedTeams = new List<Team>();
        var mergedMatches = new List<Match>();
        var mergedGroups = new List<Group>();
        var mergedPlayers = new List<PlayerStat>();
        var mergedTeamStats = new List<TeamStat>();
        var sources = new List<string>();

        try
        {
            foreach (var service in _services)
            {
                try
                {
                    if (!await service.IsAvailableAsync(ct)) continue;

                    var teams = await service.GetTeamsAsync(ct);
                    var matches = await service.GetMatchesAsync(ct);
                    var groups = await service.GetGroupsAsync(ct);
                    var playerStats = await service.GetPlayerStatsAsync(ct);
                    var teamStats = await service.GetTeamStatsAsync(ct);

                    // Merge: take data if this source has more
                    if (teams.Count > mergedTeams.Count)
                        mergedTeams = teams;
                    if (matches.Count > mergedMatches.Count)
                        mergedMatches = matches;
                    if (groups.Count > mergedGroups.Count)
                        mergedGroups = groups;
                    if (playerStats.Count > mergedPlayers.Count)
                        mergedPlayers = playerStats;
                    if (teamStats.Count > mergedTeamStats.Count)
                        mergedTeamStats = teamStats;

                    sources.Add(service.SourceName);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Service {service.SourceName} failed: {ex.Message}");
                }
            }

            if (mergedTeams.Count > 0 || mergedMatches.Count > 0)
            {
                UpdateCollection(Teams, mergedTeams);
                UpdateCollection(Matches, mergedMatches);
                UpdateCollection(Groups, mergedGroups);
                UpdateCollection(PlayerStats, mergedPlayers);
                UpdateCollection(TeamStats, mergedTeamStats);

                ActiveSource = string.Join(" + ", sources);
                LastUpdated = DateTime.Now;
                LastError = string.Empty;
                StatusChanged?.Invoke($"Updated via {ActiveSource} at {LastUpdated:HH:mm:ss}");
                DataRefreshed?.Invoke();
            }
            else
            {
                StatusChanged?.Invoke("No data available from any source.");
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Start automatic refresh on a timer.
    /// </summary>
    public void StartAutoRefresh(TimeSpan? interval = null)
    {
        if (interval.HasValue) _refreshInterval = interval.Value;
        _isAutoRefreshEnabled = true;
        _refreshTimer?.Dispose();
        _refreshTimer = new Timer(async _ => await RefreshAllAsync(), null, TimeSpan.Zero, _refreshInterval);
        StatusChanged?.Invoke($"Auto-refresh enabled (every {_refreshInterval.TotalSeconds}s)");
    }

    /// <summary>
    /// Stop automatic refresh.
    /// </summary>
    public void StopAutoRefresh()
    {
        _isAutoRefreshEnabled = false;
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        StatusChanged?.Invoke("Auto-refresh disabled");
    }

    /// <summary>
    /// Toggle auto-refresh on/off.
    /// </summary>
    public bool ToggleAutoRefresh()
    {
        if (_isAutoRefreshEnabled)
            StopAutoRefresh();
        else
            StartAutoRefresh();
        return _isAutoRefreshEnabled;
    }

    public bool IsAutoRefreshEnabled => _isAutoRefreshEnabled;

    /// <summary>
    /// Get the bracket (knockout stage) matches in structured format.
    /// Returns matches grouped by round: R32, R16, QF, SF, 3rd, Final.
    /// </summary>
    public Dictionary<TournamentStage, List<Match>> GetBracketMatches()
    {
        var bracket = new Dictionary<TournamentStage, List<Match>>
        {
            [TournamentStage.RoundOf32] = new(),
            [TournamentStage.RoundOf16] = new(),
            [TournamentStage.QuarterFinal] = new(),
            [TournamentStage.SemiFinal] = new(),
            [TournamentStage.ThirdPlace] = new(),
            [TournamentStage.Final] = new()
        };

        foreach (var m in Matches)
        {
            if (m.Stage != TournamentStage.GroupStage && bracket.ContainsKey(m.Stage))
                bracket[m.Stage].Add(m);
        }

        return bracket;
    }

    /// <summary>
    /// Get group stage matches grouped by group name.
    /// </summary>
    public Dictionary<string, List<Match>> GetGroupStageMatches()
    {
        return Matches
            .Where(m => m.Stage == TournamentStage.GroupStage)
            .GroupBy(m => m.Group)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static void UpdateCollection<T>(ObservableCollection<T> collection, List<T> newItems)
    {
        if (Application.Current?.Dispatcher != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                collection.Clear();
                foreach (var item in newItems)
                    collection.Add(item);
            });
        }
        else
        {
            collection.Clear();
            foreach (var item in newItems)
                collection.Add(item);
        }
    }
}
