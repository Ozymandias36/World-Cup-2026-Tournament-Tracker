namespace WorldCup2026.Models;

/// <summary>
/// Represents a national team in the tournament.
/// </summary>
public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string NameZh { get; set; } = string.Empty;
    public string FifaCode { get; set; } = string.Empty;
    public string FlagUrl { get; set; } = string.Empty;
    public string Confederation { get; set; } = string.Empty; // UEFA, CONMEBOL, etc.
    public string Group { get; set; } = string.Empty;
    public int WorldRanking { get; set; }
}
