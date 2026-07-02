namespace WorldCup2026.Models;

/// <summary>
/// Represents a player's statistics in the tournament.
/// </summary>
public class PlayerStat
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string TeamCode { get; set; } = string.Empty;
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
    public int MinutesPlayed { get; set; }
    public int MatchesPlayed { get; set; }
}

/// <summary>
/// Represents team-level statistics.
/// </summary>
public class TeamStat
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string TeamCode { get; set; } = string.Empty;
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int GoalDifference => GoalsFor - GoalsAgainst;
    public int CleanSheets { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
    public double PossessionAvg { get; set; }
    public int ShotsOnTarget { get; set; }
}
