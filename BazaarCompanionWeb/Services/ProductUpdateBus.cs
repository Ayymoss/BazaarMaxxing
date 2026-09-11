using System.Collections.Concurrent;
using BazaarCompanionWeb.Dtos;

namespace BazaarCompanionWeb.Services;

/// <summary>
/// In-process fan-out of per-product live updates from the Hypixel poll to the pages viewing them.
/// Replaces the old SignalR <c>ProductHub</c>: the product page is InteractiveServer, so it runs in this
/// process and can subscribe directly — no loopback HTTP hop, no reconnect, no group membership to lose.
/// </summary>
public sealed class ProductUpdateBus
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Subscription, byte>> _subscribers = new();
    private readonly ILogger<ProductUpdateBus> _logger;

    public ProductUpdateBus(ILogger<ProductUpdateBus> logger)
    {
        _logger = logger;
    }

    /// <summary>True if at least one page is watching this product — lets the poll skip building DTOs nobody reads.</summary>
    public bool HasSubscribers(string productKey) =>
        _subscribers.TryGetValue(productKey, out var set) && !set.IsEmpty;

    /// <summary>Subscribe to updates for one product. Dispose the returned handle to stop.</summary>
    public IDisposable Subscribe(string productKey, Func<ProductDataInfo, LiveTick, Task> handler)
    {
        var subscription = new Subscription(this, productKey, handler);
        _subscribers.GetOrAdd(productKey, _ => new ConcurrentDictionary<Subscription, byte>())[subscription] = 0;
        return subscription;
    }

    /// <summary>
    /// Deliver an update to every subscriber of the product. Each handler is isolated: a failing or slow page
    /// must never break the poll or starve other viewers.
    /// </summary>
    public async Task PublishAsync(string productKey, ProductDataInfo product, LiveTick tick)
    {
        if (!_subscribers.TryGetValue(productKey, out var set) || set.IsEmpty)
            return;

        foreach (var subscription in set.Keys)
        {
            try
            {
                await subscription.Handler(product, tick);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Product update handler failed for {ProductKey}", productKey);
            }
        }
    }

    private void Unsubscribe(Subscription subscription)
    {
        // Empty sets are left in place: removing one races with a concurrent Subscribe that just added to it,
        // and the key space is bounded by the product count (~1.6k), so there is nothing to reclaim.
        if (_subscribers.TryGetValue(subscription.ProductKey, out var set))
            set.TryRemove(subscription, out _);
    }

    private sealed class Subscription(ProductUpdateBus bus, string productKey, Func<ProductDataInfo, LiveTick, Task> handler) : IDisposable
    {
        public string ProductKey { get; } = productKey;
        public Func<ProductDataInfo, LiveTick, Task> Handler { get; } = handler;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                bus.Unsubscribe(this);
        }
    }
}
