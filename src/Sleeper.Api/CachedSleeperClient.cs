using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Sleeper.Api.Models;

namespace Sleeper.Api;

public class CachedSleeperClient : ISleeperClient
{
    private readonly ISleeperClient _inner;
    private readonly IMemoryCache _cache;
    private readonly SleeperClientOptions _options;

    public CachedSleeperClient(ISleeperClient inner, IMemoryCache cache, IOptions<SleeperClientOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _options = options.Value;
    }

    // User
    public Task<User?> GetUserAsync(string usernameOrId, CancellationToken ct = default)
        => GetOrCreateAsync($"user:{usernameOrId}", _options.UserCacheTtl, () => _inner.GetUserAsync(usernameOrId, ct));

    // Leagues
    public Task<List<League>> GetUserLeaguesAsync(string userId, string sport, string season, CancellationToken ct = default)
        => GetOrCreateAsync($"user-leagues:{userId}:{sport}:{season}", _options.LeagueCacheTtl, () => _inner.GetUserLeaguesAsync(userId, sport, season, ct));

    public Task<League?> GetLeagueAsync(string leagueId, CancellationToken ct = default)
        => GetOrCreateAsync($"league:{leagueId}", _options.LeagueCacheTtl, () => _inner.GetLeagueAsync(leagueId, ct));

    public Task<List<Roster>> GetLeagueRostersAsync(string leagueId, CancellationToken ct = default)
        => GetOrCreateAsync($"rosters:{leagueId}", _options.RosterCacheTtl, () => _inner.GetLeagueRostersAsync(leagueId, ct));

    public Task<List<LeagueUser>> GetLeagueUsersAsync(string leagueId, CancellationToken ct = default)
        => GetOrCreateAsync($"users:{leagueId}", _options.LeagueCacheTtl, () => _inner.GetLeagueUsersAsync(leagueId, ct));

    public Task<List<Matchup>> GetLeagueMatchupsAsync(string leagueId, int week, CancellationToken ct = default)
        => GetOrCreateAsync($"matchups:{leagueId}:{week}", _options.MatchupCacheTtl, () => _inner.GetLeagueMatchupsAsync(leagueId, week, ct));

    public Task<List<PlayoffBracketMatch>> GetWinnersBracketAsync(string leagueId, CancellationToken ct = default)
        => GetOrCreateAsync($"winners:{leagueId}", _options.LeagueCacheTtl, () => _inner.GetWinnersBracketAsync(leagueId, ct));

    public Task<List<PlayoffBracketMatch>> GetLosersBracketAsync(string leagueId, CancellationToken ct = default)
        => GetOrCreateAsync($"losers:{leagueId}", _options.LeagueCacheTtl, () => _inner.GetLosersBracketAsync(leagueId, ct));

    public Task<List<Transaction>> GetTransactionsAsync(string leagueId, int week, CancellationToken ct = default)
        => GetOrCreateAsync($"transactions:{leagueId}:{week}", _options.TransactionCacheTtl, () => _inner.GetTransactionsAsync(leagueId, week, ct));

    public Task<List<TradedPick>> GetTradedPicksAsync(string leagueId, CancellationToken ct = default)
        => GetOrCreateAsync($"traded-picks:{leagueId}", _options.LeagueCacheTtl, () => _inner.GetTradedPicksAsync(leagueId, ct));

    public Task<NflState?> GetNflStateAsync(CancellationToken ct = default)
        => GetOrCreateAsync("nfl-state", _options.NflStateCacheTtl, () => _inner.GetNflStateAsync(ct));

    // Drafts
    public Task<List<Draft>> GetUserDraftsAsync(string userId, string sport, string season, CancellationToken ct = default)
        => GetOrCreateAsync($"user-drafts:{userId}:{sport}:{season}", _options.DraftCacheTtl, () => _inner.GetUserDraftsAsync(userId, sport, season, ct));

    public Task<List<Draft>> GetLeagueDraftsAsync(string leagueId, CancellationToken ct = default)
        => GetOrCreateAsync($"league-drafts:{leagueId}", _options.DraftCacheTtl, () => _inner.GetLeagueDraftsAsync(leagueId, ct));

    public Task<Draft?> GetDraftAsync(string draftId, CancellationToken ct = default)
        => GetOrCreateAsync($"draft:{draftId}", _options.DraftCacheTtl, () => _inner.GetDraftAsync(draftId, ct));

    public Task<List<DraftPick>> GetDraftPicksAsync(string draftId, CancellationToken ct = default)
        => GetOrCreateAsync($"draft-picks:{draftId}", _options.DraftCacheTtl, () => _inner.GetDraftPicksAsync(draftId, ct));

    public Task<List<TradedPick>> GetDraftTradedPicksAsync(string draftId, CancellationToken ct = default)
        => GetOrCreateAsync($"draft-traded-picks:{draftId}", _options.DraftCacheTtl, () => _inner.GetDraftTradedPicksAsync(draftId, ct));

    // Players
    public Task<Dictionary<string, Player>> GetAllPlayersAsync(string sport = "nfl", CancellationToken ct = default)
        => GetOrCreateAsync($"players:{sport}", _options.PlayersCacheTtl, () => _inner.GetAllPlayersAsync(sport, ct));

    public Task<List<TrendingPlayer>> GetTrendingPlayersAsync(string sport = "nfl", string type = "add", int? lookbackHours = null, int? limit = null, CancellationToken ct = default)
        => GetOrCreateAsync($"trending:{sport}:{type}:{lookbackHours}:{limit}", _options.TrendingCacheTtl, () => _inner.GetTrendingPlayersAsync(sport, type, lookbackHours, limit, ct));

    // Helper
    private async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;

        var result = await factory().ConfigureAwait(false);
        _cache.Set(key, result, ttl);
        return result!;
    }
}
