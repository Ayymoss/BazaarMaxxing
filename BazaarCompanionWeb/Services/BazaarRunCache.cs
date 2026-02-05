using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Interfaces;

namespace BazaarCompanionWeb.Services;

public sealed class BazaarRunCache : IBazaarRunCache
{
    private readonly Lock _lock = new();
    private Dictionary<string, ProductState> _state = new();
    private Dictionary<string, CachedScores> _scores = new();

    public IReadOnlyList<string> GetChangedProductKeys(IReadOnlyDictionary<string, ProductState> currentState)
    {
        lock (_lock)
        {
            if (_state.Count == 0)
                return currentState.Keys.ToList();

            var changed = new List<string>();
            foreach (var (key, current) in currentState)
            {
                if (!_state.TryGetValue(key, out var prev) || !Equals(current, prev))
                    changed.Add(key);
            }

            return changed;
        }
    }

    public CachedScores? GetCachedScores(string productKey)
    {
        lock (_lock)
        {
            return _scores.TryGetValue(productKey, out var scores) ? scores : null;
        }
    }

    public void Update(IReadOnlyDictionary<string, ProductState> state, IReadOnlyDictionary<string, CachedScores> scores)
    {
        lock (_lock)
        {
            _state = state.ToDictionary(x => x.Key, x => x.Value);
            _scores = scores.ToDictionary(x => x.Key, x => x.Value);
        }
    }

    private static bool Equals(ProductState a, ProductState b) =>
        a.ProductKey == b.ProductKey
        && a.BidOrderPrice.Equals(b.BidOrderPrice)
        && a.AskOrderPrice.Equals(b.AskOrderPrice)
        && a.MovingWeekSells == b.MovingWeekSells
        && a.MovingWeekBuys == b.MovingWeekBuys;
}
