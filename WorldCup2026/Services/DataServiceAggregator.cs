using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using WorldCup2026.Models;
using RegexMatch = System.Text.RegularExpressions.Match;

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
        var mergedGroups = new List<Group>();
        var mergedPlayers = new List<PlayerStat>();
        var mergedTeamStats = new List<TeamStat>();
        var sources = new List<string>();
        var apiMatchLists = new List<List<Match>>();
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

                    if (isApi)
                    {
                        // Cache for a second overlay pass after placeholder resolution
                        apiMatchLists.Add(matches);
                        foreach (var m in matches)
                        {
                            if (string.IsNullOrEmpty(m.HomeTeamCode) || string.IsNullOrEmpty(m.AwayTeamCode)) continue;
                            var target = m.Id != 0 && matchDict.TryGetValue(m.Id, out var byId) ? byId : FindByCode(matchDict.Values, m);
                            if (target != null) { OverlayApiData(target, m); hasLive = true; }
                        }
                    }
                    else
                    {
                        foreach (var m in matches)
                            if (m.Id != 0) matchDict[m.Id] = m;
                    }

                    if (teams.Count > mergedTeams.Count) mergedTeams = teams;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Service {service.SourceName} failed: {ex.Message}");
                }
            }

            // Resolve "Winner Match N" / "Loser Match N" placeholders using local bracket
            // results only (deterministic — no guessing against API data).
            ResolvePlaceholders(matchDict);

            // Second overlay pass: matches whose placeholders just resolved to real team
            // codes can now be matched against the API safely (no more wildcards involved).
            foreach (var matches in apiMatchLists)
            {
                foreach (var m in matches)
                {
                    if (string.IsNullOrEmpty(m.HomeTeamCode) || string.IsNullOrEmpty(m.AwayTeamCode)) continue;
                    var target = FindByCode(matchDict.Values, m);
                    if (target != null) { OverlayApiData(target, m); hasLive = true; }
                }
            }

            var mergedMatches = matchDict.Values.OrderBy(m => m.Id).ToList();

            // Hardcoded Chinese name mapping (eliminates any JSON/model pipeline issues)
            ConvertToChinese(mergedMatches);
            foreach (var t in mergedTeams) ConvertTeamName(t);

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

    /// <summary>
    /// Find the local match corresponding to an API match, using FIFA codes only.
    /// No wildcard/placeholder matching here — that's ambiguous when several R16+
    /// matches are still "Winner Match X vs Winner Match Y". Advancement is instead
    /// resolved deterministically by <see cref="ResolvePlaceholders"/> below.
    /// </summary>
    private static Match? FindByCode(IEnumerable<Match> existing, Match api)
    {
        foreach (var m in existing)
        {
            if (m.Stage != api.Stage) continue;
            if (m.Stage == TournamentStage.GroupStage &&
                !string.IsNullOrEmpty(m.Group) && !string.IsNullOrEmpty(api.Group) &&
                !string.Equals(m.Group, api.Group, StringComparison.OrdinalIgnoreCase)) continue;

            if (CodeMatch(m.HomeTeamCode, api.HomeTeamCode) && CodeMatch(m.AwayTeamCode, api.AwayTeamCode)) return m;
            if (CodeMatch(m.HomeTeamCode, api.AwayTeamCode) && CodeMatch(m.AwayTeamCode, api.HomeTeamCode)) return m;
        }
        return null;
    }

    /// <summary>Resolve team identity through ChineseNames dictionary (handles codes, abbreviations, full names).</summary>
    private static string? ResolveTeam(string? name, string? code)
    {
        if (!string.IsNullOrEmpty(name) && ChineseNames.TryGetValue(name, out var zh)) return zh;
        if (!string.IsNullOrEmpty(code) && ChineseNames.TryGetValue(code, out zh)) return zh;
        return name;
    }

    /// <summary>Match two team codes. Empty = no match (never a wildcard).</summary>
    private static bool CodeMatch(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        var ra = ResolveTeam(a, a); var rb = ResolveTeam(b, b);
        return ra != null && rb != null && string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly System.Text.RegularExpressions.Regex PlaceholderRegex =
        new(@"^(Winner|Loser) Match (\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Fill in "Winner Match N" / "Loser Match N" placeholders using the actual result
    /// of local match N. Runs in ascending match-ID order, which in this tournament's
    /// numbering always places a round's feeder matches at lower IDs than the round
    /// itself, so a single pass fully propagates advancement through the bracket.
    /// </summary>
    private static void ResolvePlaceholders(Dictionary<int, Match> matchDict)
    {
        foreach (var m in matchDict.Values.OrderBy(x => x.Id))
        {
            ResolveSide(m, matchDict, home: true);
            ResolveSide(m, matchDict, home: false);
        }
    }

    private static void ResolveSide(Match m, Dictionary<int, Match> matchDict, bool home)
    {
        var name = home ? m.HomeTeamName : m.AwayTeamName;
        if (string.IsNullOrEmpty(name)) return;
        RegexMatch match = PlaceholderRegex.Match(name);
        if (!match.Success) return;

        bool wantWinner = string.Equals(match.Groups[1].Value, "Winner", StringComparison.OrdinalIgnoreCase);
        int refId = int.Parse(match.Groups[2].Value);
        if (!matchDict.TryGetValue(refId, out var refMatch)) return;
        if (!refMatch.HomeScore.HasValue || !refMatch.AwayScore.HasValue) return; // ref match not finished

        bool? homeWon = refMatch.HomeScore != refMatch.AwayScore
            ? refMatch.HomeScore > refMatch.AwayScore
            : (refMatch.HomePenalties.HasValue && refMatch.AwayPenalties.HasValue
                ? refMatch.HomePenalties > refMatch.AwayPenalties
                : (bool?)null);
        if (homeWon == null) return; // drawn with no penalty decision yet

        bool takeHome = wantWinner ? homeWon.Value : !homeWon.Value;
        var resolvedName = takeHome ? refMatch.HomeTeamName : refMatch.AwayTeamName;
        var resolvedCode = takeHome ? refMatch.HomeTeamCode : refMatch.AwayTeamCode;
        if (string.IsNullOrEmpty(resolvedName)) return;

        if (home) { m.HomeTeamName = resolvedName; m.HomeTeamCode = resolvedCode; }
        else { m.AwayTeamName = resolvedName; m.AwayTeamCode = resolvedCode; }
    }

    // ── Hardcoded Chinese team names (code → 中文) ──
    private static readonly Dictionary<string, string> ChineseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        {"MEX","墨西哥"},{"RSA","南非"},{"KOR","韩国"},{"CZE","捷克"},{"CAN","加拿大"},{"BIH","波黑"},
        {"QAT","卡塔尔"},{"SUI","瑞士"},{"BRA","巴西"},{"MAR","摩洛哥"},{"HAI","海地"},{"SCO","苏格兰"},
        {"USA","美国"},{"PAR","巴拉圭"},{"AUS","澳大利亚"},{"TUR","土耳其"},{"GER","德国"},{"CUW","库拉索"},
        {"CIV","科特迪瓦"},{"ECU","厄瓜多尔"},{"NED","荷兰"},{"JPN","日本"},{"SWE","瑞典"},{"TUN","突尼斯"},
        {"BEL","比利时"},{"EGY","埃及"},{"IRN","伊朗"},{"NZL","新西兰"},{"ESP","西班牙"},{"CPV","佛得角"},
        {"KSA","沙特"},{"URU","乌拉圭"},{"FRA","法国"},{"SEN","塞内加尔"},{"IRQ","伊拉克"},{"NOR","挪威"},
        {"ARG","阿根廷"},{"ALG","阿尔及利亚"},{"AUT","奥地利"},{"JOR","约旦"},{"POR","葡萄牙"},{"COD","民主刚果"},
        {"UZB","乌兹别克斯坦"},{"COL","哥伦比亚"},{"ENG","英格兰"},{"CRO","克罗地亚"},{"GHA","加纳"},{"PAN","巴拿马"},
        // English name fallbacks (API may return full English names)
        {"Mexico","墨西哥"},{"South Africa","南非"},{"South Korea","韩国"},{"Czech Republic","捷克"},
        {"Canada","加拿大"},{"Bosnia and Herzegovina","波黑"},{"Qatar","卡塔尔"},{"Switzerland","瑞士"},
        {"Brazil","巴西"},{"Morocco","摩洛哥"},{"Haiti","海地"},{"Scotland","苏格兰"},
        {"United States","美国"},{"Paraguay","巴拉圭"},{"Australia","澳大利亚"},{"Turkey","土耳其"},
        {"Germany","德国"},{"Curaçao","库拉索"},{"Ivory Coast","科特迪瓦"},{"Ecuador","厄瓜多尔"},
        {"Netherlands","荷兰"},{"Japan","日本"},{"Sweden","瑞典"},{"Tunisia","突尼斯"},
        {"Belgium","比利时"},{"Egypt","埃及"},{"Iran","伊朗"},{"New Zealand","新西兰"},
        {"Spain","西班牙"},{"Cape Verde","佛得角"},{"Saudi Arabia","沙特"},{"Uruguay","乌拉圭"},
        {"France","法国"},{"Senegal","塞内加尔"},{"Iraq","伊拉克"},{"Norway","挪威"},
        {"Argentina","阿根廷"},{"Algeria","阿尔及利亚"},{"Austria","奥地利"},{"Jordan","约旦"},
        {"Portugal","葡萄牙"},{"DR Congo","民主刚果"},{"Uzbekistan","乌兹别克斯坦"},{"Colombia","哥伦比亚"},
        {"England","英格兰"},{"Croatia","克罗地亚"},{"Ghana","加纳"},{"Panama","巴拿马"},
        // FIFA API specific names
        {"Korea Republic","韩国"},{"Czechia","捷克"},{"Côte d'Ivoire","科特迪瓦"},
        {"Congo DR","民主刚果"},
    };

    private static void ConvertToChinese(List<Match> matches)
    {
        foreach (var m in matches)
        {
            if (ChineseNames.TryGetValue(m.HomeTeamName ?? "", out var zh)) m.HomeTeamName = zh;
            else if (ChineseNames.TryGetValue(m.HomeTeamCode ?? "", out zh)) m.HomeTeamName = zh;
            if (ChineseNames.TryGetValue(m.AwayTeamName ?? "", out var zh2)) m.AwayTeamName = zh2;
            else if (ChineseNames.TryGetValue(m.AwayTeamCode ?? "", out zh2)) m.AwayTeamName = zh2;
        }
    }

    private static void ConvertTeamName(Team t)
    {
        if (ChineseNames.TryGetValue(t.Name, out var zh)) t.Name = zh;
        else if (ChineseNames.TryGetValue(t.FifaCode, out zh)) t.Name = zh;
    }

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
