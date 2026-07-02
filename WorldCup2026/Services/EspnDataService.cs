using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldCup2026.Helpers;
using WorldCup2026.Models;

namespace WorldCup2026.Services;

/// <summary>
/// Fallback data service using ESPN's public (unofficial) API.
/// No API key required. Provides live scores and basic match data.
/// </summary>
public class EspnDataService : IDataService
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public string SourceName => "ESPN API";

    public EspnDataService(HttpClient client)
    {
        _client = client;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetAsync("/apis/site/v2/sports/soccer/fifa.world/scoreboard", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<Team>> GetTeamsAsync(CancellationToken ct = default)
    {
        var matches = await GetMatchesAsync(ct);
        var teams = new Dictionary<string, Team>();

        foreach (var m in matches)
        {
            if (m.HomeTeamName != null && !teams.ContainsKey(m.HomeTeamName))
                teams[m.HomeTeamName] = new Team { Name = m.HomeTeamName, NameEn = m.HomeTeamName, FifaCode = m.HomeTeamCode ?? "" };
            if (m.AwayTeamName != null && !teams.ContainsKey(m.AwayTeamName))
                teams[m.AwayTeamName] = new Team { Name = m.AwayTeamName, NameEn = m.AwayTeamName, FifaCode = m.AwayTeamCode ?? "" };
        }

        return teams.Values.ToList();
    }

    public async Task<List<Match>> GetMatchesAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _client.GetStringAsync("/apis/site/v2/sports/soccer/fifa.world/scoreboard", ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("events", out var events))
                return new List<Match>();

            var matches = new List<Match>();
            foreach (var e in events.EnumerateArray())
            {
                try
                {
                    var match = ParseEspnEvent(e);
                    if (match != null) matches.Add(match);
                }
                catch { /* skip malformed events */ }
            }

            return matches;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ESPN GetMatches error: {ex.Message}");
            return new List<Match>();
        }
    }

    private static Match? ParseEspnEvent(JsonElement e)
    {
        var competitions = e.TryGetProperty("competitions", out var comps) ? comps : default;
        if (competitions.ValueKind != JsonValueKind.Array) return null;

        var competition = competitions[0];
        var competitors = competition.TryGetProperty("competitors", out var comps2) ? comps2 : default;
        if (competitors.ValueKind != JsonValueKind.Array) return null;

        JsonElement? home = null, away = null;
        foreach (var c in competitors.EnumerateArray())
        {
            var ha = c.TryGetProperty("homeAway", out var h) ? h.GetString() : "";
            if (ha == "home") home = c;
            else if (ha == "away") away = c;
        }

        var homeTeam = home?.TryGetProperty("team", out var ht) == true ? ht : default;
        var awayTeam = away?.TryGetProperty("team", out var at) == true ? at : default;

        // Parse score — could be string or int in JSON
        int? ParseScore(JsonElement? comp)
        {
            if (comp == null) return null;
            var el = comp.Value;
            if (!el.TryGetProperty("score", out var s)) return null;
            if (s.ValueKind == JsonValueKind.String && int.TryParse(s.GetString(), out var si)) return si;
            if (s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var ni)) return ni;
            return null;
        }

        var status = e.TryGetProperty("status", out var st) ? st : default;
        var statusType = status.ValueKind != JsonValueKind.Undefined && status.TryGetProperty("type", out var stt) ? stt : default;
        var statusName = statusType.ValueKind != JsonValueKind.Undefined && statusType.TryGetProperty("name", out var sn) ? sn.GetString() : null;

        var group = e.TryGetProperty("group", out var grp) ? grp : default;
        var groupName = group.ValueKind != JsonValueKind.Undefined && group.TryGetProperty("name", out var gn) ? gn.GetString() : "";

        var venue = e.TryGetProperty("venue", out var v) ? v : default;
        var venueName = venue.ValueKind != JsonValueKind.Undefined && venue.TryGetProperty("fullName", out var vn) ? vn.GetString() : "";

        var compType = competition.TryGetProperty("type", out var ct) ? ct : default;
        var compTypeText = compType.ValueKind != JsonValueKind.Undefined && compType.TryGetProperty("text", out var ctt) ? ctt.GetString()
            : compType.ValueKind != JsonValueKind.Undefined && compType.TryGetProperty("name", out var ctn) ? ctn.GetString() : null;

        // Parse date
        DateTime? matchDate = null;
        if (e.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(dateEl.GetString(), out var dt))
                matchDate = dt;
        }

        return new Match
        {
            Id = e.TryGetProperty("id", out var idEl) ? (idEl.ValueKind == JsonValueKind.String ? 0 : (idEl.TryGetInt32(out var iid) ? iid : 0)) : 0,
            HomeTeamName = homeTeam.ValueKind != JsonValueKind.Undefined && homeTeam.TryGetProperty("displayName", out var hdn) ? hdn.GetString() : "TBD",
            AwayTeamName = awayTeam.ValueKind != JsonValueKind.Undefined && awayTeam.TryGetProperty("displayName", out var adn) ? adn.GetString() : "TBD",
            HomeTeamCode = homeTeam.ValueKind != JsonValueKind.Undefined && homeTeam.TryGetProperty("abbreviation", out var hab) ? hab.GetString() : null,
            AwayTeamCode = awayTeam.ValueKind != JsonValueKind.Undefined && awayTeam.TryGetProperty("abbreviation", out var aab) ? aab.GetString() : null,
            HomeScore = ParseScore(home),
            AwayScore = ParseScore(away),
            Stage = ParseStageFromRound(compTypeText),
            Group = groupName ?? "",
            DateTime = matchDate,
            Stadium = venueName ?? "",
            Status = MapEspnStatus(statusName)
        };
    }

    public async Task<List<Group>> GetGroupsAsync(CancellationToken ct = default)
    {
        var matches = await GetMatchesAsync(ct);
        var groupMatches = matches.Where(m => !string.IsNullOrEmpty(m.Group)).ToList();
        return groupMatches.GroupBy(m => m.Group).Select(g =>
        {
            var standings = CalculateStandings(g.ToList());
            return new Group { Name = g.Key, Standings = standings, Matches = g.ToList() };
        }).ToList();
    }

    public async Task<List<PlayerStat>> GetPlayerStatsAsync(CancellationToken ct = default)
    {
        return await Task.FromResult(new List<PlayerStat>());
    }

    public async Task<List<TeamStat>> GetTeamStatsAsync(CancellationToken ct = default)
    {
        var matches = await GetMatchesAsync(ct);
        var teams = await GetTeamsAsync(ct);
        return teams.Select(t =>
        {
            var tm = matches.Where(m => m.HomeTeamName == t.Name || m.AwayTeamName == t.Name).ToList();
            return new TeamStat
            {
                TeamName = t.Name,
                TeamCode = t.FifaCode,
                GoalsFor = tm.Sum(m => m.HomeTeamName == t.Name ? (m.HomeScore ?? 0) : (m.AwayScore ?? 0)),
                GoalsAgainst = tm.Sum(m => m.HomeTeamName == t.Name ? (m.AwayScore ?? 0) : (m.HomeScore ?? 0))
            };
        }).ToList();
    }

    private static TournamentStage ParseStageFromRound(string? roundText)
    {
        if (roundText == null) return TournamentStage.GroupStage;
        var t = roundText.ToLowerInvariant();
        if (t.Contains("group")) return TournamentStage.GroupStage;
        if (t.Contains("32")) return TournamentStage.RoundOf32;
        if (t.Contains("16")) return TournamentStage.RoundOf16;
        if (t.Contains("quarter")) return TournamentStage.QuarterFinal;
        if (t.Contains("semi")) return TournamentStage.SemiFinal;
        if (t.Contains("third")) return TournamentStage.ThirdPlace;
        if (t.Contains("final")) return TournamentStage.Final;
        return TournamentStage.GroupStage;
    }

    private static string MapEspnStatus(string? status)
    {
        return status?.ToUpperInvariant() switch
        {
            "STATUS_FINAL" => "FINISHED",
            "STATUS_IN_PROGRESS" => "LIVE",
            "STATUS_SCHEDULED" => "SCHEDULED",
            _ => "SCHEDULED"
        };
    }

    private static List<GroupStanding> CalculateStandings(List<Match> matches)
    {
        var teamStats = new Dictionary<string, GroupStanding>();
        foreach (var m in matches)
        {
            if (m.HomeTeamName == null || m.AwayTeamName == null) continue;
            if (!m.HomeScore.HasValue || !m.AwayScore.HasValue) continue;

            foreach (var teamName in new[] { m.HomeTeamName, m.AwayTeamName })
            {
                if (!teamStats.ContainsKey(teamName))
                    teamStats[teamName] = new GroupStanding { TeamName = teamName };
            }

            var home = teamStats[m.HomeTeamName];
            var away = teamStats[m.AwayTeamName];

            home.Played++; away.Played++;
            home.GoalsFor += m.HomeScore.Value; home.GoalsAgainst += m.AwayScore.Value;
            away.GoalsFor += m.AwayScore.Value; away.GoalsAgainst += m.HomeScore.Value;

            if (m.HomeScore > m.AwayScore) { home.Wins++; home.Points += 3; away.Losses++; }
            else if (m.HomeScore < m.AwayScore) { away.Wins++; away.Points += 3; home.Losses++; }
            else { home.Draws++; away.Draws++; home.Points++; away.Points++; }
        }

        return teamStats.Values
            .OrderByDescending(s => s.Points)
            .ThenByDescending(s => s.GoalDifference)
            .ThenByDescending(s => s.GoalsFor)
            .Select((s, i) => { s.Position = i + 1; return s; })
            .ToList();
    }
}
