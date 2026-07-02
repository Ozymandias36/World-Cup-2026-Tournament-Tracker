using WorldCup2026.Models;

namespace WorldCup2026.Services;

/// <summary>
/// Abstraction for fetching World Cup tournament data from various sources.
/// </summary>
public interface IDataService
{
    string SourceName { get; }
    Task<List<Team>> GetTeamsAsync(CancellationToken ct = default);
    Task<List<Match>> GetMatchesAsync(CancellationToken ct = default);
    Task<List<Group>> GetGroupsAsync(CancellationToken ct = default);
    Task<List<PlayerStat>> GetPlayerStatsAsync(CancellationToken ct = default);
    Task<List<TeamStat>> GetTeamStatsAsync(CancellationToken ct = default);
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}
