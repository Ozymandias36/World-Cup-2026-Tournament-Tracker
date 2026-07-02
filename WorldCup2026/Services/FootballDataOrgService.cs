using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldCup2026.Helpers;
using WorldCup2026.Models;

namespace WorldCup2026.Services;

/// <summary>
/// Secondary data service using football-data.org v4 API.
/// Requires a free API key. Provides richer match events (goals + assists).
/// </summary>
public class FootballDataOrgService : IDataService
{
    private readonly HttpClient _client;

    // 2026 World Cup competition code
    private const string CompetitionCode = "WC";
    private const string Season = "2026";

    public string SourceName => "football-data.org";

    public FootballDataOrgService(HttpClient client, string apiKey)
    {
        _client = client;
        _client.DefaultRequestHeaders.Add("X-Auth-Token", apiKey);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetAsync($"/v4/competitions/{CompetitionCode}/teams", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<Team>> GetTeamsAsync(CancellationToken ct = default)
    {
        var data = await ApiClientHelper.GetWithRetryAsync<FdCompetitionTeams>(
            _client, $"/v4/competitions/{CompetitionCode}/teams", ct: ct);
        if (data?.Teams == null) return new List<Team>();

        return data.Teams.Select((t, i) => new Team
        {
            Id = t.Id,
            Name = t.Name ?? string.Empty,
            NameEn = t.Name ?? string.Empty,
            FifaCode = t.Tla ?? string.Empty,
            Confederation = t.Area?.Name ?? string.Empty,
            FlagUrl = t.Crest ?? string.Empty
        }).ToList();
    }

    public async Task<List<Match>> GetMatchesAsync(CancellationToken ct = default)
    {
        var data = await ApiClientHelper.GetWithRetryAsync<FdMatchResponse>(
            _client, $"/v4/competitions/{CompetitionCode}/matches?season={Season}", ct: ct);
        if (data?.Matches == null) return new List<Match>();

        return data.Matches.Select(m => new Match
        {
            Id = m.Id,
            HomeTeamName = m.HomeTeam?.Name ?? "TBD",
            AwayTeamName = m.AwayTeam?.Name ?? "TBD",
            HomeTeamCode = m.HomeTeam?.Tla,
            AwayTeamCode = m.AwayTeam?.Tla,
            HomeScore = m.Score?.FullTime?.Home,
            AwayScore = m.Score?.FullTime?.Away,
            HomePenalties = m.Score?.Penalties?.Home,
            AwayPenalties = m.Score?.Penalties?.Away,
            Stage = ParseStage(m.Stage),
            Group = m.Group?.Replace("GROUP_", "") ?? string.Empty,
            Matchday = m.Matchday,
            DateTime = m.UtcDate,
            Status = MapStatus(m.Status),
            Events = m.Goals?.Select(g => new MatchEvent
            {
                Type = ParseGoalType(g),
                Minute = g.Minute,
                Team = g.Team == "HOME_TEAM" ? "home" : "away",
                PlayerName = g.Scorer?.Name,
                AssistPlayerName = g.Assist?.Name
            }).ToList() ?? new List<MatchEvent>()
        }).ToList();
    }

    public async Task<List<Group>> GetGroupsAsync(CancellationToken ct = default)
    {
        var data = await ApiClientHelper.GetWithRetryAsync<FdStandingsResponse>(
            _client, $"/v4/competitions/{CompetitionCode}/standings?season={Season}", ct: ct);
        if (data?.Standings == null) return new List<Group>();

        return data.Standings.Select(s => new Group
        {
            Name = s.Group?.Replace("GROUP_", "") ?? string.Empty,
            Standings = s.Table?.Select(t => new GroupStanding
            {
                TeamId = t.Team?.Id ?? 0,
                TeamName = t.Team?.Name ?? string.Empty,
                TeamCode = t.Team?.Tla ?? string.Empty,
                FlagUrl = t.Team?.Crest ?? string.Empty,
                Position = t.Position,
                Played = t.PlayedGames,
                Wins = t.Won,
                Draws = t.Draw,
                Losses = t.Lost,
                GoalsFor = t.GoalsFor,
                GoalsAgainst = t.GoalsAgainst,
                Points = t.Points
            }).ToList() ?? new List<GroupStanding>()
        }).ToList();
    }

    public async Task<List<PlayerStat>> GetPlayerStatsAsync(CancellationToken ct = default)
    {
        // football-data.org free tier doesn't provide top scorers endpoint;
        // fall back to aggregating from match events
        var matches = await GetMatchesAsync(ct);
        return AggregateFromMatches(matches);
    }

    public async Task<List<TeamStat>> GetTeamStatsAsync(CancellationToken ct = default)
    {
        var matches = await GetMatchesAsync(ct);
        var teams = await GetTeamsAsync(ct);

        return teams.Select(team =>
        {
            var tm = matches.Where(m =>
                m.HomeTeamName == team.Name || m.AwayTeamName == team.Name).ToList();
            return new TeamStat
            {
                TeamId = team.Id,
                TeamName = team.Name,
                TeamCode = team.FifaCode,
                GoalsFor = tm.Sum(m => m.HomeTeamName == team.Name ? (m.HomeScore ?? 0) : (m.AwayScore ?? 0)),
                GoalsAgainst = tm.Sum(m => m.HomeTeamName == team.Name ? (m.AwayScore ?? 0) : (m.HomeScore ?? 0))
            };
        }).ToList();
    }

    private static TournamentStage ParseStage(string? stage)
    {
        return stage?.Replace("_", "").ToUpperInvariant() switch
        {
            "GROUPSTAGE" => TournamentStage.GroupStage,
            "ROUNDOF32" => TournamentStage.RoundOf32,
            "ROUNDOF16" => TournamentStage.RoundOf16,
            "QUARTERFINALS" => TournamentStage.QuarterFinal,
            "SEMIFINALS" => TournamentStage.SemiFinal,
            "THIRDPLACE" => TournamentStage.ThirdPlace,
            "FINAL" => TournamentStage.Final,
            _ => TournamentStage.GroupStage
        };
    }

    private static string MapStatus(string? status)
    {
        return status switch
        {
            "FINISHED" => "FINISHED",
            "LIVE" or "IN_PLAY" or "PAUSED" => "LIVE",
            "SCHEDULED" or "TIMED" => "SCHEDULED",
            _ => "SCHEDULED"
        };
    }

    private static MatchEventType ParseGoalType(FdGoal g)
    {
        if (g.Type == "PENALTY") return MatchEventType.PenaltyGoal;
        if (g.Type == "OWN") return MatchEventType.OwnGoal;
        return MatchEventType.Goal;
    }

    private static List<PlayerStat> AggregateFromMatches(List<Match> matches)
    {
        var dict = new Dictionary<string, PlayerStat>();
        foreach (var m in matches)
        {
            foreach (var ev in m.Events)
            {
                if (ev.PlayerName == null) continue;
                var key = ev.PlayerName;
                if (!dict.ContainsKey(key))
                {
                    dict[key] = new PlayerStat
                    {
                        PlayerName = ev.PlayerName,
                        TeamName = ev.Team == "home" ? (m.HomeTeamName ?? "") : (m.AwayTeamName ?? "")
                    };
                }
                var s = dict[key];
                if (ev.Type is MatchEventType.Goal or MatchEventType.PenaltyGoal) s.Goals++;
                if (!string.IsNullOrEmpty(ev.AssistPlayerName))
                {
                    if (!dict.ContainsKey(ev.AssistPlayerName))
                        dict[ev.AssistPlayerName] = new PlayerStat { PlayerName = ev.AssistPlayerName };
                    dict[ev.AssistPlayerName].Assists++;
                }
            }
        }
        return dict.Values.OrderByDescending(s => s.Goals).ThenByDescending(s => s.Assists).ToList();
    }
}

// JSON DTOs for football-data.org API

public class FdCompetitionTeams { [JsonPropertyName("teams")] public List<FdTeam>? Teams { get; set; } }
public class FdTeam
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("tla")] public string? Tla { get; set; }
    [JsonPropertyName("crest")] public string? Crest { get; set; }
    [JsonPropertyName("area")] public FdArea? Area { get; set; }
}
public class FdArea { [JsonPropertyName("name")] public string? Name { get; set; } }

public class FdMatchResponse { [JsonPropertyName("matches")] public List<FdMatch>? Matches { get; set; } }
public class FdMatch
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("utcDate")] public DateTime? UtcDate { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("matchday")] public int Matchday { get; set; }
    [JsonPropertyName("stage")] public string? Stage { get; set; }
    [JsonPropertyName("group")] public string? Group { get; set; }
    [JsonPropertyName("homeTeam")] public FdTeam? HomeTeam { get; set; }
    [JsonPropertyName("awayTeam")] public FdTeam? AwayTeam { get; set; }
    [JsonPropertyName("score")] public FdScore? Score { get; set; }
    [JsonPropertyName("goals")] public List<FdGoal>? Goals { get; set; }
}

public class FdScore
{
    [JsonPropertyName("fullTime")] public FdScoreDetail? FullTime { get; set; }
    [JsonPropertyName("penalties")] public FdScoreDetail? Penalties { get; set; }
}
public class FdScoreDetail
{
    [JsonPropertyName("home")] public int? Home { get; set; }
    [JsonPropertyName("away")] public int? Away { get; set; }
}

public class FdGoal
{
    [JsonPropertyName("minute")] public int Minute { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("team")] public string? Team { get; set; }
    [JsonPropertyName("scorer")] public FdPlayer? Scorer { get; set; }
    [JsonPropertyName("assist")] public FdPlayer? Assist { get; set; }
}

public class FdPlayer
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class FdStandingsResponse { [JsonPropertyName("standings")] public List<FdStanding>? Standings { get; set; } }
public class FdStanding
{
    [JsonPropertyName("group")] public string? Group { get; set; }
    [JsonPropertyName("table")] public List<FdTableEntry>? Table { get; set; }
}
public class FdTableEntry
{
    [JsonPropertyName("position")] public int Position { get; set; }
    [JsonPropertyName("team")] public FdTeam? Team { get; set; }
    [JsonPropertyName("playedGames")] public int PlayedGames { get; set; }
    [JsonPropertyName("won")] public int Won { get; set; }
    [JsonPropertyName("draw")] public int Draw { get; set; }
    [JsonPropertyName("lost")] public int Lost { get; set; }
    [JsonPropertyName("goalsFor")] public int GoalsFor { get; set; }
    [JsonPropertyName("goalsAgainst")] public int GoalsAgainst { get; set; }
    [JsonPropertyName("points")] public int Points { get; set; }
}
