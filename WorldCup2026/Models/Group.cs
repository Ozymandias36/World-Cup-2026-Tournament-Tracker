namespace WorldCup2026.Models;

/// <summary>
/// Represents a group in the group stage with its standings.
/// </summary>
public class Group
{
    public string Name { get; set; } = string.Empty; // "A" through "L"
    public List<GroupStanding> Standings { get; set; } = new();
    public List<Match> Matches { get; set; } = new();
}

/// <summary>
/// A team's standing within a group.
/// </summary>
public class GroupStanding
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string TeamCode { get; set; } = string.Empty;
    public string FlagUrl { get; set; } = string.Empty;
    public int Position { get; set; }
    public int Played { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int GoalDifference => GoalsFor - GoalsAgainst;
    public int Points { get; set; }
    public bool IsQualified { get; set; }
    public string? QualificationNote { get; set; } // "Qualified", "Best 3rd", etc.
}
