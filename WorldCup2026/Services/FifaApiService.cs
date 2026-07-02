using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldCup2026.Helpers;
using WorldCup2026.Models;

namespace WorldCup2026.Services;

/// <summary>
/// Official FIFA API data service.
/// Calls api.fifa.com directly — no API key required, official FIFA data.
/// </summary>
public class FifaApiService : IDataService
{
    private readonly HttpClient _client;
    private const string Competition = "17";
    private const string Season = "285023";

    public string SourceName => "FIFA Official";

    public FifaApiService(HttpClient client)
    {
        _client = client;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.GetAsync(
                $"/api/v3/calendar/matches?idCompetition={Competition}&idSeason={Season}&count=1", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<Team>> GetTeamsAsync(CancellationToken ct = default)
    {
        // Extract unique teams from match data
        var matches = await GetMatchesAsync(ct);
        var teams = new Dictionary<string, Team>();

        foreach (var m in matches)
        {
            void AddTeam(string? name, string? code, string? flagUrl)
            {
                if (string.IsNullOrEmpty(name)) return;
                if (!teams.ContainsKey(name))
                {
                    teams[name] = new Team
                    {
                        Name = name,
                        NameEn = name,
                        FifaCode = code ?? "",
                        FlagUrl = flagUrl ?? ""
                    };
                }
                // Update code/flag if we have better data
                if (!string.IsNullOrEmpty(code)) teams[name].FifaCode = code;
                if (!string.IsNullOrEmpty(flagUrl)) teams[name].FlagUrl = flagUrl;
            }

            AddTeam(m.HomeTeamName, m.HomeTeamCode, m.HomeFlagUrl);
            AddTeam(m.AwayTeamName, m.AwayTeamCode, m.AwayFlagUrl);
        }

        return teams.Values.OrderBy(t => t.Name).ToList();
    }

    public async Task<List<Match>> GetMatchesAsync(CancellationToken ct = default)
    {
        var allMatches = new List<Match>();
        string? token = null;

        try
        {
            // Paginate through all matches using continuation token
            for (int page = 0; page < 10; page++)
            {
                var url = $"/api/v3/calendar/matches?idCompetition={Competition}&idSeason={Season}&count=100";
                if (token != null)
                    url += $"&from={Uri.EscapeDataString(token)}";

                var json = await _client.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("ContinuationToken", out var ctElem))
                    token = ctElem.GetString();
                else
                    token = null;

                if (!root.TryGetProperty("Results", out var results))
                    break;

                foreach (var r in results.EnumerateArray())
                {
                    var match = ParseMatch(r);
                    if (match != null) allMatches.Add(match);
                }

                if (token == null) break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FIFA API error: {ex.Message}");
        }

        return allMatches;
    }

    private static Match? ParseMatch(JsonElement r)
    {
        try
        {
            var home = r.TryGetProperty("Home", out var h) ? h : default;
            var away = r.TryGetProperty("Away", out var a) ? a : default;

            var homeName = GetLocalizedName(home, "Description");
            var awayName = GetLocalizedName(away, "Description");
            var homeCode = home.ValueKind != JsonValueKind.Undefined && home.TryGetProperty("Abbreviation", out var hab) ? hab.GetString() : null;
            var awayCode = away.ValueKind != JsonValueKind.Undefined && away.TryGetProperty("Abbreviation", out var aab) ? aab.GetString() : null;
            var homeFlag = ResolveFlagUrl(home);
            var awayFlag = ResolveFlagUrl(away);

            var homeScore = home.ValueKind != JsonValueKind.Undefined && home.TryGetProperty("Score", out var hs) && hs.ValueKind == JsonValueKind.Number ? (int?)hs.GetInt32() : null;
            var awayScore = away.ValueKind != JsonValueKind.Undefined && away.TryGetProperty("Score", out var aws) && aws.ValueKind == JsonValueKind.Number ? (int?)aws.GetInt32() : null;

            var homePen = home.ValueKind != JsonValueKind.Undefined && home.TryGetProperty("PenaltyScore", out var hps) && hps.ValueKind == JsonValueKind.Number ? (int?)hps.GetInt32() : null;
            var awayPen = away.ValueKind != JsonValueKind.Undefined && away.TryGetProperty("PenaltyScore", out var aps) && aps.ValueKind == JsonValueKind.Number ? (int?)aps.GetInt32() : null;

            var stageName = GetLocalizedName(r, "StageName");
            var groupName = GetLocalizedName(r, "GroupName");

            var dateStr = r.TryGetProperty("LocalDate", out var ld) ? ld.GetString() : null;
            var statusStr = r.TryGetProperty("MatchTime", out var mt) ? mt.GetString() : null;
            var winner = r.TryGetProperty("Winner", out var w) ? w.GetString() : null;

            var status = "SCHEDULED";
            if (winner != null && homeScore.HasValue) status = "FINISHED";
            else if (statusStr != null && statusStr.Contains("'")) status = "LIVE";

            var stadium = r.TryGetProperty("Stadium", out var st) ? st : default;
            var stadiumName = stadium.ValueKind != JsonValueKind.Undefined && stadium.TryGetProperty("Name", out var sn) ? GetLocalizedName(stadium, "Name") : null;
            var cityName = stadium.ValueKind != JsonValueKind.Undefined && stadium.TryGetProperty("CityName", out var cn) ? GetLocalizedName(stadium, "CityName") : null;

            DateTime? matchDate = null;
            if (dateStr != null && DateTime.TryParse(dateStr, out var dt))
                matchDate = dt;

            return new Match
            {
                Id = r.TryGetProperty("IdMatch", out var im) && int.TryParse(im.GetString(), out var mid) ? mid : 0,
                HomeTeamName = homeName,
                AwayTeamName = awayName,
                HomeTeamCode = homeCode,
                AwayTeamCode = awayCode,
                HomeFlagUrl = homeFlag,
                AwayFlagUrl = awayFlag,
                HomeScore = homeScore,
                AwayScore = awayScore,
                HomePenalties = homePen,
                AwayPenalties = awayPen,
                Stage = ParseStage(stageName),
                Group = groupName?.Replace("Group ", "") ?? "",
                DateTime = matchDate,
                Stadium = stadiumName ?? "",
                City = cityName ?? "",
                Status = status,
                Events = new List<MatchEvent>()
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveFlagUrl(JsonElement team)
    {
        if (team.ValueKind == JsonValueKind.Undefined) return null;
        if (!team.TryGetProperty("PictureUrl", out var pu)) return null;
        var url = pu.GetString();
        if (string.IsNullOrEmpty(url)) return null;
        // FIFA returns template URLs like ".../flags-{format}-{size}/MEX"
        return url.Replace("{format}", "sq").Replace("{size}", "4");
    }

    private static string? GetLocalizedName(JsonElement el, string propName)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;
        if (!el.TryGetProperty(propName, out var arr)) return null;
        if (arr.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty("Locale", out var loc) && loc.GetString()?.StartsWith("en") == true)
                return item.TryGetProperty("Description", out var desc) ? desc.GetString() : null;
        }
        // Fallback to first entry
        if (arr.GetArrayLength() > 0)
            return arr[0].TryGetProperty("Description", out var d) ? d.GetString() : null;
        return null;
    }

    public async Task<List<Group>> GetGroupsAsync(CancellationToken ct = default)
    {
        var matches = await GetMatchesAsync(ct);
        var groupMatches = matches
            .Where(m => m.Stage == TournamentStage.GroupStage)
            .GroupBy(m => m.Group)
            .Where(g => !string.IsNullOrEmpty(g.Key));

        var result = new List<Group>();
        foreach (var g in groupMatches)
        {
            var standings = CalculateStandings(g.ToList());
            result.Add(new Group { Name = g.Key, Standings = standings, Matches = g.ToList() });
        }

        return result.OrderBy(g => g.Name).ToList();
    }

    public async Task<List<PlayerStat>> GetPlayerStatsAsync(CancellationToken ct = default)
    {
        // FIFA match list API doesn't include player-level data
        return await Task.FromResult(new List<PlayerStat>());
    }

    public async Task<List<TeamStat>> GetTeamStatsAsync(CancellationToken ct = default)
    {
        var matches = await GetMatchesAsync(ct);
        return matches
            .SelectMany(m => new[] { (m.HomeTeamName, m.HomeTeamCode, m.HomeScore ?? 0, m.AwayScore ?? 0, true),
                                     (m.AwayTeamName, m.AwayTeamCode, m.AwayScore ?? 0, m.HomeScore ?? 0, false) })
            .GroupBy(x => x.Item1)
            .Select(g => new TeamStat
            {
                TeamName = g.Key ?? "",
                TeamCode = g.First().Item2 ?? "",
                GoalsFor = g.Sum(x => x.Item3),
                GoalsAgainst = g.Sum(x => x.Item4)
            })
            .OrderByDescending(t => t.GoalsFor)
            .ToList();
    }

    private static TournamentStage ParseStage(string? name)
    {
        if (name == null) return TournamentStage.GroupStage;
        var n = name.ToLowerInvariant();
        if (n.Contains("first stage") || n.Contains("group")) return TournamentStage.GroupStage;
        if (n.Contains("round of 32")) return TournamentStage.RoundOf32;
        if (n.Contains("round of 16")) return TournamentStage.RoundOf16;
        if (n.Contains("quarter")) return TournamentStage.QuarterFinal;
        if (n.Contains("semi")) return TournamentStage.SemiFinal;
        if (n.Contains("third place") || n.Contains("3rd")) return TournamentStage.ThirdPlace;
        if (n.Contains("final")) return TournamentStage.Final;
        return TournamentStage.GroupStage;
    }

    private static List<GroupStanding> CalculateStandings(List<Match> matches)
    {
        var dict = new Dictionary<string, GroupStanding>();

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
}
