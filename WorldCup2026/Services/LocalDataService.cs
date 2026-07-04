using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldCup2026.Helpers;
using WorldCup2026.Models;

namespace WorldCup2026.Services;

/// <summary>
/// Data service that loads tournament data from embedded resource files.
/// Always available, no network needed.
/// Teams are pre-loaded; matches/scores are embedded as known.
/// </summary>
public class LocalDataService : IDataService
{
    private List<Team>? _cachedTeams;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public string SourceName => "Local Data";

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(true); // Always available

    public async Task<List<Team>> GetTeamsAsync(CancellationToken ct = default)
    {
        if (_cachedTeams != null) return _cachedTeams;

        try
        {
            var json = await LoadEmbeddedResourceAsync("Data.tournament_teams.json");
            var wrapper = JsonSerializer.Deserialize<LocalTeamsWrapper>(json, JsonOpts);
            _cachedTeams = wrapper?.Teams?.Select(t => new Team
            {
                Id = t.Id,
                Name = t.NameZh ?? t.NameEn ?? string.Empty,
                NameEn = t.NameEn ?? string.Empty,
                NameZh = t.NameZh ?? string.Empty,
                FifaCode = t.FifaCode ?? string.Empty,
                FlagUrl = t.Flag ?? string.Empty,
                Group = t.Group ?? string.Empty
            }).ToList() ?? new List<Team>();
        }
        catch
        {
            _cachedTeams = new List<Team>();
        }

        return _cachedTeams;
    }

    public async Task<List<Match>> GetMatchesAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await LoadFileAsync("Data", "tournament_matches.json");
            if (json == null) return new List<Match>();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("matches", out var arr)) return new List<Match>();

            var teams = await GetTeamsAsync(ct);
            var teamMapByName = teams.ToDictionary(t => t.Name, t => t);
            var teamMapByCode = teams.Where(t => !string.IsNullOrEmpty(t.FifaCode))
                .ToDictionary(t => t.FifaCode, t => t);

            Team? LookupTeam(string? name, string? code)
            {
                if (!string.IsNullOrEmpty(code) && teamMapByCode.TryGetValue(code, out var t)) return t;
                if (!string.IsNullOrEmpty(name) && teamMapByName.TryGetValue(name, out var t2)) return t2;
                return null;
            }

            var matches = new List<Match>();
            foreach (var m in arr.EnumerateArray())
            {
                var homeName = m.TryGetProperty("home_team", out var hn) ? hn.GetString() ?? "" : "";
                var awayName = m.TryGetProperty("away_team", out var an) ? an.GetString() ?? "" : "";

                // Get FIFA codes from the enriched match data
                var homeCode = m.TryGetProperty("home_code", out var hc) && hc.ValueKind == JsonValueKind.String ? hc.GetString() : null;
                var awayCode = m.TryGetProperty("away_code", out var ac) && ac.ValueKind == JsonValueKind.String ? ac.GetString() : null;

                // Lookup team info for flag URL
                var ht = LookupTeam(homeName, homeCode);
                var at = LookupTeam(awayName, awayCode);

                var stageStr = m.TryGetProperty("stage", out var ss) ? ss.GetString() : "GroupStage";
                var stage = stageStr switch
                {
                    "GroupStage" => TournamentStage.GroupStage,
                    "RoundOf32" => TournamentStage.RoundOf32,
                    "RoundOf16" => TournamentStage.RoundOf16,
                    "QuarterFinal" => TournamentStage.QuarterFinal,
                    "SemiFinal" => TournamentStage.SemiFinal,
                    "ThirdPlace" => TournamentStage.ThirdPlace,
                    "Final" => TournamentStage.Final,
                    _ => TournamentStage.GroupStage
                };

                matches.Add(new Match
                {
                    Id = m.TryGetProperty("id", out var i) && i.TryGetInt32(out var mid) ? mid : 0,
                    HomeTeamName = homeName,
                    AwayTeamName = awayName,
                    HomeTeamCode = homeCode ?? ht?.FifaCode ?? "",
                    AwayTeamCode = awayCode ?? at?.FifaCode ?? "",
                    HomeFlagUrl = ht?.FlagUrl ?? "",
                    AwayFlagUrl = at?.FlagUrl ?? "",
                    HomeScore = m.TryGetProperty("home_score", out var hs) && hs.ValueKind == JsonValueKind.Number ? hs.GetInt32() : null,
                    AwayScore = m.TryGetProperty("away_score", out var aws) && aws.ValueKind == JsonValueKind.Number ? aws.GetInt32() : null,
                    HomePenalties = m.TryGetProperty("home_penalty", out var hp) && hp.ValueKind == JsonValueKind.Number ? hp.GetInt32() : null,
                    AwayPenalties = m.TryGetProperty("away_penalty", out var ap) && ap.ValueKind == JsonValueKind.Number ? ap.GetInt32() : null,
                    Stage = stage,
                    Group = m.TryGetProperty("group", out var g) ? g.GetString() ?? "" : "",
                    Matchday = m.TryGetProperty("matchday", out var md) && md.TryGetInt32(out var mdv) ? mdv : 1,
                    DateTime = m.TryGetProperty("date", out var dt) && DateTime.TryParseExact(dt.GetString(), "MM/dd/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                    UtcOffsetHours = m.TryGetProperty("utc_offset", out var uo) && uo.ValueKind == JsonValueKind.Number ? uo.GetDouble() : null,
                    Status = m.TryGetProperty("finished", out var f) && f.GetBoolean() ? "FINISHED" : "SCHEDULED"
                });
            }
            return matches;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Local matches load error: {ex.Message}");
            return new List<Match>();
        }
    }

    public async Task<List<Group>> GetGroupsAsync(CancellationToken ct = default)
    {
        var teams = await GetTeamsAsync(ct);
        var matches = await GetMatchesAsync(ct);
        var groupMatches = matches.Where(m => m.Stage == TournamentStage.GroupStage && !string.IsNullOrEmpty(m.Group));

        var result = new List<Group>();
        foreach (var g in groupMatches.GroupBy(m => m.Group))
        {
            var standings = CalculateStandings(g.ToList(), teams.Where(t => t.Group == g.Key).ToList());
            result.Add(new Group { Name = g.Key, Standings = standings, Matches = g.ToList() });
        }
        return result.OrderBy(g => g.Name).ToList();
    }

    private static List<GroupStanding> CalculateStandings(List<Match> matches, List<Team> groupTeams)
    {
        var dict = new Dictionary<string, GroupStanding>();
        foreach (var t in groupTeams)
            dict[t.Name] = new GroupStanding { TeamName = t.Name, TeamCode = t.FifaCode, FlagUrl = t.FlagUrl, TeamId = t.Id };

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

        return dict.Values.OrderByDescending(s => s.Points).ThenByDescending(s => s.GoalDifference)
            .ThenByDescending(s => s.GoalsFor).Select((s, i) => { s.Position = i + 1; s.IsQualified = i < 2; return s; }).ToList();
    }

    public Task<List<PlayerStat>> GetPlayerStatsAsync(CancellationToken ct = default)
        => Task.FromResult(new List<PlayerStat>());

    public Task<List<TeamStat>> GetTeamStatsAsync(CancellationToken ct = default)
        => Task.FromResult(new List<TeamStat>());

    private static async Task<string?> LoadFileAsync(string dir, string filename)
    {
        // 1. Try file on disk (debug mode)
        var filePath = Path.Combine(AppContext.BaseDirectory, dir, filename);
        if (File.Exists(filePath))
            return await File.ReadAllTextAsync(filePath);

        // 2. Try embedded resource (single-file publish)
        var asm = typeof(LocalDataService).Assembly;
        var resName = $"{asm.GetName().Name}.{dir}.{filename}".Replace("/", ".").Replace("\\", ".");
        using var stream = asm.GetManifestResourceStream(resName);
        if (stream != null)
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        return null;
    }

    private static async Task<string> LoadEmbeddedResourceAsync(string name)
    {
        // 1. Try file on disk (debug mode)
        var filePath = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(filePath))
            return await File.ReadAllTextAsync(filePath);

        // 2. Try embedded resource (single-file publish)
        var asm = typeof(LocalDataService).Assembly;
        var resName = $"{asm.GetName().Name}.{name}".Replace("/", ".").Replace("\\", ".");
        using var stream = asm.GetManifestResourceStream(resName);
        if (stream != null)
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        throw new FileNotFoundException($"Data file '{name}' not found on disk or in embedded resources");
    }
}

// DTOs for local JSON
public class LocalTeamsWrapper { public List<LocalTeam>? Teams { get; set; } }
public class LocalTeam
{
    public int Id { get; set; }
    [JsonPropertyName("name_en")] public string? NameEn { get; set; }
    [JsonPropertyName("name_zh")] public string? NameZh { get; set; }
    [JsonPropertyName("fifa_code")] public string? FifaCode { get; set; }
    [JsonPropertyName("flag")] public string? Flag { get; set; }
    [JsonPropertyName("group")] public string? Group { get; set; }
}
