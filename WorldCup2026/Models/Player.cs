namespace WorldCup2026.Models;

/// <summary>
/// Represents a player in the tournament.
/// </summary>
public class Player
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public int? ShirtNumber { get; set; }
    public string Position { get; set; } = string.Empty; // GK, DEF, MID, FWD
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string TeamCode { get; set; } = string.Empty;
}
