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
    /// Refresh all data: baseline from local, live scores overlaid from APIs.
    /// </summary>
    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        StatusChanged?.Invoke("Refreshing...");

        var matchDict = new Dictionary<int, Match>();
        var mergedTeams = new List<Team>();
        var localTeams = new List<Team>(); // always keep local teams for Chinese names
        var mergedGroups = new List<Group>();
        var mergedPlayers = new List<PlayerStat>();
        var mergedTeamStats = new List<TeamStat>();
        var sources = new List<string>();
        bool hasLive = false;

        try
        {
            foreach (var service in _services)
            {
                try
                {
                    if (!await service.IsAvailableAsync(ct)) continue;
                    sources.Add(service.SourceName);
                    bool isApi = service.SourceName != "Local Data";

                    var teams = await service.GetTeamsAsync(ct);
                    var matches = await service.GetMatchesAsync(ct);

                    foreach (var m in matches)
                    {
                        if (m.Id == 0) continue;
                        if (matchDict.TryGetValue(m.Id, out var existing))
                        {
                            if (isApi) { OverlayApiData(existing, m); hasLive = true; }
                        }
                        else if (isApi)
                        {
                            // API has different ID — try fuzzy match by team codes
                            var target = FindByTeams(matchDict.Values, m);
                            if (target != null) { OverlayApiData(target, m); hasLive = true; }
                            // else: skip — don't add API-only entries
                        }
                        else
                        {
                            matchDict[m.Id] = m;
                        }
                    }

                    if (teams.Count > mergedTeams.Count) mergedTeams = teams;
                    if (!isApi && teams.Count > localTeams.Count) localTeams = teams;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Service {service.SourceName} failed: {ex.Message}");
                }
            }

            var mergedMatches = matchDict.Values.OrderBy(m => m.Id).ToList();

            // Convert team names to Chinese (always use localTeams which have NameZh)
            var nameMapEn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var nameMapCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in localTeams)
            {
                if (!string.IsNullOrEmpty(t.NameZh))
                {
                    if (!string.IsNullOrEmpty(t.NameEn)) nameMapEn[t.NameEn] = t.NameZh;
                    if (!string.IsNullOrEmpty(t.FifaCode)) nameMapCode[t.FifaCode] = t.NameZh;
                }
            }
            foreach (var m in mergedMatches)
            {
                if (nameMapEn.TryGetValue(m.HomeTeamName ?? "", out var zh)) m.HomeTeamName = zh;
                else if (nameMapCode.TryGetValue(m.HomeTeamCode ?? "", out zh)) m.HomeTeamName = zh;
                if (nameMapEn.TryGetValue(m.AwayTeamName ?? "", out zh)) m.AwayTeamName = zh;
                else if (nameMapCode.TryGetValue(m.AwayTeamCode ?? "", out zh)) m.AwayTeamName = zh;
            }
            // Also convert team names in mergedTeams for standings lookup
            foreach (var t in mergedTeams)
                if (nameMapEn.TryGetValue(t.Name, out var zh)) t.Name = zh;

            mergedGroups = ComputeGroups(mergedMatches, mergedTeams);

            if (mergedMatches.Count > 0)
            {
                UpdateCollection(Teams, mergedTeams);
                UpdateCollection(Matches, mergedMatches);
                UpdateCollection(Groups, mergedGroups);
                UpdateCollection(PlayerStats, mergedPlayers);
                UpdateCollection(TeamStats, mergedTeamStats);

                ActiveSource = string.Join(" + ", sources) + (hasLive ? " 🔴LIVE" : "");
                LastUpdated = DateTime.Now;
                StatusChanged?.Invoke($"{ActiveSource} | {LastUpdated:HH:mm:ss}");
                DataRefreshed?.Invoke();
            }
        }
        finally { IsRefreshing = false; }
    }

    private static Match? FindByTeams(IEnumerable<Match> existing, Match api)
    {
        foreach (var m in existing)
        {
            if (m.Stage != api.Stage) continue;
            if (m.Stage == TournamentStage.GroupStage &&
                !string.IsNullOrEmpty(m.Group) && !string.IsNullOrEmpty(api.Group) &&
                !string.Equals(m.Group, api.Group, StringComparison.OrdinalIgnoreCase)) continue;

            // Match by FIFA codes (handle placeholders — at least 1 code must match)
            if (CodeMatch(m.HomeTeamCode, api.HomeTeamCode) && CodeMatch(m.AwayTeamCode, api.AwayTeamCode)) return m;
            if (CodeMatch(m.HomeTeamCode, api.AwayTeamCode) && CodeMatch(m.AwayTeamCode, api.HomeTeamCode)) return m;

            // Fallback: match by team names (placeholders are wildcards)
            if (NameMatch(m.HomeTeamName, api.HomeTeamName) && NameMatch(m.AwayTeamName, api.AwayTeamName)) return m;
            if (NameMatch(m.HomeTeamName, api.AwayTeamName) && NameMatch(m.AwayTeamName, api.HomeTeamName)) return m;
        }
        return null;
    }

    /// <summary>Match two team codes. Empty/placeholder matches anything.</summary>
    private static bool CodeMatch(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return true; // placeholder = wildcard
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Match two team names. Placeholder ("Winner X"/"Loser X") matches anything.</summary>
    private static bool NameMatch(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return true;
        if (IsPlaceholder(a) || IsPlaceholder(b)) return true;
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaceholder(string? s) =>
        !string.IsNullOrEmpty(s) && (s.StartsWith("Winner ", StringComparison.OrdinalIgnoreCase) || s.StartsWith("Loser ", StringComparison.OrdinalIgnoreCase));

    private static void OverlayApiData(Match existing, Match api)
    {
        // Scores
        if (api.HomeScore.HasValue) { existing.HomeScore = api.HomeScore; existing.AwayScore = api.AwayScore; }
        if (api.HomePenalties.HasValue) { existing.HomePenalties = api.HomePenalties; existing.AwayPenalties = api.AwayPenalties; }
        // Status
        if (!string.IsNullOrEmpty(api.Status)) existing.Status = api.Status;
        // Team names (API overwrites placeholders like "Winner Match 80")
        if (!string.IsNullOrEmpty(api.HomeTeamName) && (string.IsNullOrEmpty(existing.HomeTeamName) || existing.HomeTeamName.StartsWith("Winner ") || existing.HomeTeamName.StartsWith("Loser ")))
            existing.HomeTeamName = api.HomeTeamName;
        if (!string.IsNullOrEmpty(api.AwayTeamName) && (string.IsNullOrEmpty(existing.AwayTeamName) || existing.AwayTeamName.StartsWith("Winner ") || existing.AwayTeamName.StartsWith("Loser ")))
            existing.AwayTeamName = api.AwayTeamName;
        // Codes
        if (!string.IsNullOrEmpty(api.HomeTeamCode) && string.IsNullOrEmpty(existing.HomeTeamCode)) existing.HomeTeamCode = api.HomeTeamCode;
        if (!string.IsNullOrEmpty(api.AwayTeamCode) && string.IsNullOrEmpty(existing.AwayTeamCode)) existing.AwayTeamCode = api.AwayTeamCode;
        // Events
        if (api.Events.Count > 0) existing.Events = api.Events;
        // Preserve local utc_offset
    }

    private static List<Group> ComputeGroups(List<Match> matches, List<Team> teams)
    {
        var lookup = teams.ToDictionary(t => t.Name, t => t);
        return matches.Where(m => m.Stage == TournamentStage.GroupStage && !string.IsNullOrEmpty(m.Group))
            .GroupBy(m => m.Group).Select(g =>
            {
                var ms = g.ToList();
                var dict = new Dictionary<string, GroupStanding>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in ms)
                {
                    foreach (var (name, code) in new[] { (m.HomeTeamName, m.HomeTeamCode), (m.AwayTeamName, m.AwayTeamCode) })
                    {
                        if (string.IsNullOrEmpty(name) || dict.ContainsKey(name)) continue;
                        var t = lookup.GetValueOrDefault(name);
                        dict[name] = new GroupStanding { TeamName = name, TeamCode = code ?? t?.FifaCode ?? "", FlagUrl = t?.FlagUrl ?? "", TeamId = t?.Id ?? 0 };
                    }
                }
                foreach (var m in ms)
                {
                    if (m.HomeTeamName == null || m.AwayTeamName == null) continue;
                    if (!m.HomeScore.HasValue || !m.AwayScore.HasValue) continue;
                    if (!dict.ContainsKey(m.HomeTeamName)) dict[m.HomeTeamName] = new GroupStanding { TeamName = m.HomeTeamName, TeamCode = m.HomeTeamCode ?? "" };
                    if (!dict.ContainsKey(m.AwayTeamName)) dict[m.AwayTeamName] = new GroupStanding { TeamName = m.AwayTeamName, TeamCode = m.AwayTeamCode ?? "" };
                    var h = dict[m.HomeTeamName]; var a = dict[m.AwayTeamName];
                    h.Played++; a.Played++; h.GoalsFor += m.HomeScore.Value; h.GoalsAgainst += m.AwayScore.Value;
                    a.GoalsFor += m.AwayScore.Value; a.GoalsAgainst += m.HomeScore.Value;
                    if (m.HomeScore > m.AwayScore) { h.Wins++; h.Points += 3; a.Losses++; }
                    else if (m.HomeScore < m.AwayScore) { a.Wins++; a.Points += 3; h.Losses++; }
                    else { h.Draws++; a.Draws++; h.Points++; a.Points++; }
                }
                var st = dict.Values.OrderByDescending(s => s.Points).ThenByDescending(s => s.GoalDifference).ThenByDescending(s => s.GoalsFor).ToList();
                for (int i = 0; i < st.Count; i++) { st[i].Position = i + 1; st[i].IsQualified = i < 2; }
                return new Group { Name = g.Key, Standings = st, Matches = ms };
            }).OrderBy(g => g.Name).ToList();
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
