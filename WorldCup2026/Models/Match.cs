namespace WorldCup2026.Models;

/// <summary>
/// Represents a tournament match.
/// </summary>
public class Match
{
    public int Id { get; set; }
    public int? HomeTeamId { get; set; }
    public int? AwayTeamId { get; set; }
    public string? HomeTeamName { get; set; }
    public string? AwayTeamName { get; set; }
    public string? HomeTeamCode { get; set; }
    public string? AwayTeamCode { get; set; }
    public string? HomeFlagUrl { get; set; }
    public string? AwayFlagUrl { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }
    public int? HomePenalties { get; set; }
    public int? AwayPenalties { get; set; }
    public TournamentStage Stage { get; set; }
    public string Group { get; set; } = string.Empty;
    public int Matchday { get; set; }
    public DateTime? DateTime { get; set; }
    public string Stadium { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // SCHEDULED, LIVE, FINISHED, etc.
    public int? TimeElapsed { get; set; }
    public List<MatchEvent> Events { get; set; } = new();

    public bool IsFinished => Status == "FINISHED";
    public bool IsLive => Status == "LIVE";
    public bool IsScheduled => Status == "SCHEDULED" || Status == "TIMED";
    public bool HasPenalties => HomePenalties.HasValue && AwayPenalties.HasValue;

    /// <summary>UTC offset in hours of the match local time (e.g. -4 for ET, -7 for PT).</summary>
    public double? UtcOffsetHours { get; set; }
}
