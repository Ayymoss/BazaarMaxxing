using BazaarCompanionWeb.Dtos;

namespace BazaarCompanionWeb.Interfaces;

/// <summary>
/// In-memory cache of previous run's per-product state and scores for change detection and unchanged-product scoring.
/// </summary>
public interface IBazaarRunCache
{
    /// <summary>
    /// Returns product keys that are new or whose state differs from the last stored state. First run (empty cache) returns all keys.
    /// </summary>
    IReadOnlyList<string> GetChangedProductKeys(IReadOnlyDictionary<string, ProductState> currentState);

    /// <summary>
    /// Returns last run's scores for the product, or null if missing.
    /// </summary>
    CachedScores? GetCachedScores(string productKey);

    /// <summary>
    /// Replaces stored state and scores with the current run's data. Products not in the dictionaries are dropped from the cache.
    /// </summary>
    void Update(IReadOnlyDictionary<string, ProductState> state, IReadOnlyDictionary<string, CachedScores> scores);
}
