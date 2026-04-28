using Microsoft.Extensions.Caching.Memory;

namespace Sleeper.Api.Caching;

internal static class MemoryCacheExtensions
{
    public static async Task<T> GetOrCreateIfNotNullAsync<T>(
        this IMemoryCache cache,
        string key,
        TimeSpan ttl,
        Func<Task<T>> factory,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;

        var result = await factory().ConfigureAwait(false);
        if (result is not null)
            cache.Set(key, result, ttl);

        return result;
    }
}