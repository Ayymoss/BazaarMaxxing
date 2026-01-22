namespace BazaarCompanionWeb.Services;

/// <summary>
/// Singleton service to manage products selected for comparison.
/// </summary>
public sealed class ComparisonStateService
{
    private const int MaxProducts = 4;
    private readonly List<string> _productKeys = [];
    private readonly object _lock = new();

    public event Action? OnChange;

    public IReadOnlyList<string> ProductKeys
    {
        get
        {
            lock (_lock) return [.._productKeys];
        }
    }

    public int Count
    {
        get
        {
            lock (_lock) return _productKeys.Count;
        }
    }

    public bool Add(string productKey)
    {
        lock (_lock)
        {
            if (_productKeys.Count >= MaxProducts || _productKeys.Contains(productKey))
                return false;

            _productKeys.Add(productKey);
        }

        OnChange?.Invoke();
        return true;
    }

    public bool Remove(string productKey)
    {
        bool removed;
        lock (_lock)
        {
            removed = _productKeys.Remove(productKey);
        }

        if (removed) OnChange?.Invoke();
        return removed;
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_productKeys.Count is 0) return;
            _productKeys.Clear();
        }

        OnChange?.Invoke();
    }

    public bool Contains(string productKey)
    {
        lock (_lock) return _productKeys.Contains(productKey);
    }
}
