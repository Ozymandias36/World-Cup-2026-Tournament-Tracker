namespace WorldCup2026.Models;

/// <summary>
/// Represents an event within a match (goal, card, substitution, etc.)
/// </summary>
public class MatchEvent
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public MatchEventType Type { get; set; }
    public int Minute { get; set; }
    public int? ExtraMinute { get; set; }
    public string Team { get; set; } = string.Empty; // "home" or "away"
    public string? PlayerName { get; set; }
    public string? AssistPlayerName { get; set; }
    public string? Detail { get; set; } // e.g. "Penalty", "Own Goal"
}

public enum MatchEventType
{
    Goal,
    PenaltyGoal,
    OwnGoal,
    YellowCard,
    RedCard,
    Substitution,
    PenaltyShootout
}
