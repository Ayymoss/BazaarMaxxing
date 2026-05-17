using System.Collections.Concurrent;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Interfaces.Database;

namespace BazaarCompanionWeb.Services;

/// <summary>
/// Per-product TTL cache in front of <see cref="IProductRepository.GetProductAsync"/>. Repeated
/// navigations between Product / Compare / list views hit RAM instead of Postgres for the cache
/// window. Live updates still arrive via SignalR — this only smooths the initial page load.
///
/// Registered as singleton: the cache is shared across all users/sessions, which is correct
/// since the underlying product data is global. Repo is resolved per call via the scope factory
/// to avoid the singleton-consuming-scoped DI lifetime mismatch.
/// </summary>
public sealed class ProductDataCache(IServiceScopeFactory scopeFactory, ILogger<ProductDataCache> logger)
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public async Task<ProductDataInfo> GetProductAsync(string productKey, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (_entries.TryGetValue(productKey, out var entry) && entry.ExpiresAt > now)
            return entry.Data;

        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var data = await repo.GetProductAsync(productKey, ct);
        _entries[productKey] = new CacheEntry(data, now + DefaultTtl);
        return data;
    }

    /// <summary>
    /// Force-invalidate a single product key. Use when local state mutation makes the cache stale.
    /// </summary>
    public void Invalidate(string productKey) => _entries.TryRemove(productKey, out _);

    /// <summary>
    /// Drop any entries whose TTL has lapsed. Cheap; safe to call from a background timer.
    /// </summary>
    public int PruneExpired()
    {
        var now = DateTime.UtcNow;
        var removed = 0;
        foreach (var (key, entry) in _entries)
        {
            if (entry.ExpiresAt <= now && _entries.TryRemove(key, out _)) removed++;
        }
        if (removed > 0) logger.LogDebug("ProductDataCache pruned {Count} expired entries", removed);
        return removed;
    }

    private sealed record CacheEntry(ProductDataInfo Data, DateTime ExpiresAt);
}
