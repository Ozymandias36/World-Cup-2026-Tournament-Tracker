using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldCup2026.Helpers;
using WorldCup2026.Models;

namespace WorldCup2026.Services;

/// <summary>
/// Primary data service using the free worldcup26.ir API.
/// No API key required. Matches actual API response structure.
/// </summary>
public class WorldCup26IrService : IDataService
{
    private readonly HttpClient _client;

    public string SourceName => "FIFA Live";

    public WorldCup26IrService(HttpClient client)
    {
        _client = client;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetAsync("/get/teams", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<Team>> GetTeamsAsync(CancellationToken ct = default)
    {
        try
        {
            var wrapper = await ApiClientHelper.GetWithRetryAsync<Wc26TeamsWrapper>(_client, "/get/teams", ct: ct);
            if (wrapper?.Teams == null) return new List<Team>();
            return wrapper.Teams.Select(t => new Team
            {
                Id = int.TryParse(t.Id, out var tid) ? tid : 0,
                Name = t.NameEn ?? t.NameFa ?? string.Empty,
                NameEn = t.NameEn ?? string.Empty,
                FifaCode = t.FifaCode ?? string.Empty,
                FlagUrl = t.Flag ?? string.Empty,
                Group = t.Groups ?? string.Empty
            }).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WC26 GetTeams error: {ex.Message}");
            return new List<Team>();
        }
    }

    public async Task<List<Match>> GetMatchesAsync(CancellationToken ct = default)
    {
        try
        {
            // Fetch teams for flag/code lookup
            var teams = await GetTeamsAsync(ct);
            var teamMap = teams.ToDictionary(t => t.Id, t => t);

            var wrapper = await ApiClientHelper.GetWithRetryAsync<Wc26GamesWrapper>(_client, "/get/games", ct: ct);
            if (wrapper?.Games == null) return new List<Match>();
            return wrapper.Games.Select(g =>
            {
                var hid = int.TryParse(g.HomeTeamId, out var h) ? h : (int?)null;
                var aid = int.TryParse(g.AwayTeamId, out var a) ? a : (int?)null;
                var ht = hid.HasValue && teamMap.TryGetValue(hid.Value, out var th) ? th : null;
                var at = aid.HasValue && teamMap.TryGetValue(aid.Value, out var ta) ? ta : null;

                return new Match
                {
                    Id = int.TryParse(g.Id, out var mid) ? mid : 0,
                    HomeTeamId = hid,
                    AwayTeamId = aid,
                    HomeTeamName = g.HomeTeamNameEn ?? ht?.NameEn ?? string.Empty,
                    AwayTeamName = g.AwayTeamNameEn ?? at?.NameEn ?? string.Empty,
                    HomeTeamCode = ht?.FifaCode ?? string.Empty,
                    AwayTeamCode = at?.FifaCode ?? string.Empty,
                    HomeFlagUrl = ht?.FlagUrl ?? string.Empty,
                    AwayFlagUrl = at?.FlagUrl ?? string.Empty,
                    HomeScore = int.TryParse(g.HomeScore, out var hs) ? hs : null,
                    AwayScore = int.TryParse(g.AwayScore, out var aws) ? aws : null,
                    Stage = ParseStage(g.Type, g.Stage),
                    Group = g.Group ?? string.Empty,
                    Matchday = g.Matchday,
                    DateTime = ParseDate(g.LocalDate),
                    Status = g.Finished == "TRUE" ? "FINISHED" : "SCHEDULED",
                    Events = ParseScorers(g.HomeScorers, g.AwayScorers, g)
                };
            }).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WC26 GetMatches error: {ex.Message}");
            return new List<Match>();
        }
    }

    public async Task<List<Group>> GetGroupsAsync(CancellationToken ct = default)
    {
        // Build group standings from match data since /get/groups may be unreliable
        try
        {
            var matches = await GetMatchesAsync(ct);
            var teams = await GetTeamsAsync(ct);

            var groupMatches = matches.Where(m => m.Stage == TournamentStage.GroupStage).ToList();
            var groupDict = groupMatches.GroupBy(m => m.Group).ToDictionary(g => g.Key, g => g.ToList());

            var teamsByGroup = teams.Where(t => !string.IsNullOrEmpty(t.Group))
                .GroupBy(t => t.Group)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = new List<Group>();
            foreach (var (groupName, groupMatchList) in groupDict)
            {
                var standings = CalculateStandings(groupMatchList, teamsByGroup.GetValueOrDefault(groupName) ?? new List<Team>());
                result.Add(new Group { Name = groupName, Standings = standings, Matches = groupMatchList });
            }

            return result.OrderBy(g => g.Name).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WC26 GetGroups error: {ex.Message}");
            return new List<Group>();
        }
    }

    public async Task<List<PlayerStat>> GetPlayerStatsAsync(CancellationToken ct = default)
    {
        try
        {
            var matches = await GetMatchesAsync(ct);
            return AggregatePlayerStats(matches);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WC26 GetPlayerStats error: {ex.Message}");
            return new List<PlayerStat>();
        }
    }

    public async Task<List<TeamStat>> GetTeamStatsAsync(CancellationToken ct = default)
    {
        try
        {
            var teams = await GetTeamsAsync(ct);
            var matches = await GetMatchesAsync(ct);

            var stats = new List<TeamStat>();
            foreach (var team in teams)
            {
                var teamMatches = matches.Where(m =>
                    m.HomeTeamId == team.Id || m.AwayTeamId == team.Id).ToList();

                stats.Add(new TeamStat
                {
                    TeamId = team.Id,
                    TeamName = team.Name,
                    TeamCode = team.FifaCode,
                    GoalsFor = teamMatches.Sum(m =>
                        m.HomeTeamId == team.Id ? (m.HomeScore ?? 0) : (m.AwayScore ?? 0)),
                    GoalsAgainst = teamMatches.Sum(m =>
                        m.HomeTeamId == team.Id ? (m.AwayScore ?? 0) : (m.HomeScore ?? 0)),
                    CleanSheets = teamMatches.Count(m =>
                        (m.HomeTeamId == team.Id && m.AwayScore == 0) ||
                        (m.AwayTeamId == team.Id && m.HomeScore == 0)),
                    YellowCards = teamMatches.Sum(m => m.Events.Count(e =>
                        e.Team == (m.HomeTeamId == team.Id ? "home" : "away") && e.Type == MatchEventType.YellowCard)),
                    RedCards = teamMatches.Sum(m => m.Events.Count(e =>
                        e.Team == (m.HomeTeamId == team.Id ? "home" : "away") && e.Type == MatchEventType.RedCard))
                });
            }
            return stats;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WC26 GetTeamStats error: {ex.Message}");
            return new List<TeamStat>();
        }
    }

    private static DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;
        if (DateTime.TryParse(dateStr, out var dt)) return dt;
        return null;
    }

    private static TournamentStage ParseStage(string? type, string? stage)
    {
        var t = (type ?? stage ?? "").ToLowerInvariant();
        if (t.Contains("group")) return TournamentStage.GroupStage;
        if (t.Contains("32") || t.Contains("round_of_32")) return TournamentStage.RoundOf32;
        if (t.Contains("16") || t.Contains("round_of_16")) return TournamentStage.RoundOf16;
        if (t.Contains("quarter")) return TournamentStage.QuarterFinal;
        if (t.Contains("semi")) return TournamentStage.SemiFinal;
        if (t.Contains("third")) return TournamentStage.ThirdPlace;
        if (t.Contains("final")) return TournamentStage.Final;
        return TournamentStage.GroupStage;
    }

    /// <summary>
    /// Parse the home_scorers / away_scorers JSON strings from the API.
    /// Format: "{"Player Name 1 45'", "Player Name 2 67' (p)"}" — it's a set-like JSON
    /// </summary>
    private static List<MatchEvent> ParseScorers(string? homeScorers, string? awayScorers, Wc26GameRaw game)
    {
        var events = new List<MatchEvent>();
        ParseScorerString(homeScorers, "home", game, events);
        ParseScorerString(awayScorers, "away", game, events);
        return events;
    }

    private static void ParseScorerString(string? raw, string team, Wc26GameRaw game, List<MatchEvent> events)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return;

        // Format: "{"Player Name 45'", "Player Name 67' (p)"}"
        // Extract content between the braces
        var content = raw.Trim('{', '}');
        if (string.IsNullOrWhiteSpace(content)) return;

        // Split by ",
        var parts = content.Split("\",");
        foreach (var part in parts)
        {
            var cleaned = part.Trim().Trim('"').Trim();
            if (string.IsNullOrWhiteSpace(cleaned)) continue;

            // Parse: "Player Name 45'" or "Player Name 67' (p)"
            var lastQuoteIndex = cleaned.LastIndexOf('\'');
            if (lastQuoteIndex < 0) continue;

            var nameAndMinute = cleaned[..lastQuoteIndex].Trim();
            var rest = cleaned[(lastQuoteIndex + 1)..].Trim();

            int minute = 0;
            var minuteStr = rest.Split(' ')[0].Trim('(', ')', '\'');
            int.TryParse(minuteStr, out minute);

            var isPenalty = rest.Contains("(p)") || rest.Contains("pen");
            var isOwnGoal = rest.Contains("og") || rest.Contains("own");

            events.Add(new MatchEvent
            {
                MatchId = int.TryParse(game.Id, out var mid) ? mid : 0,
                Type = isPenalty ? MatchEventType.PenaltyGoal :
                       isOwnGoal ? MatchEventType.OwnGoal : MatchEventType.Goal,
                Minute = minute,
                Team = team,
                PlayerName = nameAndMinute
            });
        }
    }

    private static List<GroupStanding> CalculateStandings(List<Match> matches, List<Team> groupTeams)
    {
        var dict = new Dictionary<string, GroupStanding>();

        // Initialize all teams in the group
        foreach (var team in groupTeams)
        {
            dict[team.Name] = new GroupStanding
            {
                TeamId = team.Id,
                TeamName = team.Name,
                TeamCode = team.FifaCode,
                FlagUrl = team.FlagUrl
            };
        }

        foreach (var m in matches)
        {
            if (m.HomeTeamName == null || m.AwayTeamName == null) continue;
            if (!m.HomeScore.HasValue || !m.AwayScore.HasValue) continue;

            if (!dict.ContainsKey(m.HomeTeamName))
                dict[m.HomeTeamName] = new GroupStanding { TeamName = m.HomeTeamName, TeamCode = m.HomeTeamCode ?? "" };
            if (!dict.ContainsKey(m.AwayTeamName))
                dict[m.AwayTeamName] = new GroupStanding { TeamName = m.AwayTeamName, TeamCode = m.AwayTeamCode ?? "" };

            var home = dict[m.HomeTeamName];
            var away = dict[m.AwayTeamName];

            home.Played++; away.Played++;
            home.GoalsFor += m.HomeScore.Value; home.GoalsAgainst += m.AwayScore.Value;
            away.GoalsFor += m.AwayScore.Value; away.GoalsAgainst += m.HomeScore.Value;

            if (m.HomeScore > m.AwayScore) { home.Wins++; home.Points += 3; away.Losses++; }
            else if (m.HomeScore < m.AwayScore) { away.Wins++; away.Points += 3; home.Losses++; }
            else { home.Draws++; away.Draws++; home.Points++; away.Points++; }
        }

        return dict.Values
            .OrderByDescending(s => s.Points)
            .ThenByDescending(s => s.GoalDifference)
            .ThenByDescending(s => s.GoalsFor)
            .Select((s, i) => { s.Position = i + 1; s.IsQualified = i < 2; return s; })
            .ToList();
    }

    private static List<PlayerStat> AggregatePlayerStats(List<Match> matches)
    {
        var dict = new Dictionary<string, PlayerStat>();

        foreach (var match in matches)
        {
            foreach (var ev in match.Events)
            {
                if (ev.PlayerName == null) continue;
                var key = ev.PlayerName;
                if (!dict.ContainsKey(key))
                {
                    dict[key] = new PlayerStat
                    {
                        PlayerName = ev.PlayerName,
                        TeamName = ev.Team == "home" ? (match.HomeTeamName ?? "") : (match.AwayTeamName ?? ""),
                        TeamCode = ev.Team == "home" ? (match.HomeTeamCode ?? "") : (match.AwayTeamCode ?? "")
                    };
                }

                var stat = dict[key];
                stat.MatchesPlayed++;
                switch (ev.Type)
                {
                    case MatchEventType.Goal:
                    case MatchEventType.PenaltyGoal:
                    case MatchEventType.OwnGoal:
                        stat.Goals++;
                        break;
                    case MatchEventType.YellowCard:
                        stat.YellowCards++;
                        break;
                    case MatchEventType.RedCard:
                        stat.RedCards++;
                        break;
                }
            }
        }

        return dict.Values.OrderByDescending(s => s.Goals).ThenByDescending(s => s.Assists).ToList();
    }
}

// --- JSON DTOs matching actual worldcup26.ir API structure ---

public class Wc26TeamsWrapper
{
    [JsonPropertyName("teams")] public List<Wc26TeamRaw>? Teams { get; set; }
}

public class Wc26TeamRaw
{
    [JsonPropertyName("id")] public string Id { get; set; } = "0";
    [JsonPropertyName("name_en")] public string? NameEn { get; set; }
    [JsonPropertyName("name_fa")] public string? NameFa { get; set; }
    [JsonPropertyName("flag")] public string? Flag { get; set; }
    [JsonPropertyName("fifa_code")] public string? FifaCode { get; set; }
    [JsonPropertyName("groups")] public string? Groups { get; set; }
}

public class Wc26GamesWrapper
{
    [JsonPropertyName("games")] public List<Wc26GameRaw>? Games { get; set; }
}

public class Wc26GameRaw
{
    [JsonPropertyName("id")] public string Id { get; set; } = "0";
    [JsonPropertyName("home_team_id")] public string? HomeTeamId { get; set; }
    [JsonPropertyName("away_team_id")] public string? AwayTeamId { get; set; }
    [JsonPropertyName("home_score")] public string? HomeScore { get; set; }
    [JsonPropertyName("away_score")] public string? AwayScore { get; set; }
    [JsonPropertyName("home_scorers")] public string? HomeScorers { get; set; }
    [JsonPropertyName("away_scorers")] public string? AwayScorers { get; set; }
    [JsonPropertyName("group")] public string? Group { get; set; }
    [JsonPropertyName("matchday")] public int Matchday { get; set; }
    [JsonPropertyName("local_date")] public string? LocalDate { get; set; }
    [JsonPropertyName("finished")] public string? Finished { get; set; }
    [JsonPropertyName("time_elapsed")] public string? TimeElapsed { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("stage")] public string? Stage { get; set; }
    [JsonPropertyName("home_team_name_en")] public string? HomeTeamNameEn { get; set; }
    [JsonPropertyName("away_team_name_en")] public string? AwayTeamNameEn { get; set; }
    [JsonPropertyName("home_team_code")] public string? HomeTeamCode { get; set; }
    [JsonPropertyName("away_team_code")] public string? AwayTeamCode { get; set; }
}
