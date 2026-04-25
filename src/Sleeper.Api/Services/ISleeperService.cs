using Sleeper.Api.Models;

namespace Sleeper.Api.Services;

public interface ISleeperService
{
    Task<Roster?> GetMyRosterAsync(string leagueId, string username, CancellationToken ct = default);
    Task<List<MatchupWithNames>> GetWeekScoreboardAsync(string leagueId, int week, CancellationToken ct = default);
    Task<DraftPick?> FindPlayerDraftPickAsync(string leagueId, string playerName, CancellationToken ct = default);
    Task<List<RosterWithOwner>> GetRostersWithOwnersAsync(string leagueId, CancellationToken ct = default);
    Task<List<PlayerInfo>> GetRosterPlayersAsync(string leagueId, string username, CancellationToken ct = default);
    Task<string?> GetLeagueIdForSeasonAsync(string currentLeagueId, string targetSeason, CancellationToken ct = default);
    Task<List<KeeperValue>> GetRosterKeeperValuesAsync(string leagueId, string username, int undraftedCost = 10, CancellationToken ct = default);
}
