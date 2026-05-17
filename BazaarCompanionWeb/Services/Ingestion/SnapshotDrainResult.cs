using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;

namespace BazaarCompanionWeb.Services.Ingestion;

public sealed record OrderBookSnapshot(string ProductKey, IReadOnlyList<Order> Bids, IReadOnlyList<Order> Asks, DateTime Timestamp);

public sealed record SnapshotDrainResult(
    IReadOnlyList<EFProduct> ChangedProducts,
    IReadOnlyList<EFPriceTick> ChangedTicks,
    IReadOnlyList<OrderBookSnapshot> ChangedOrderBooks);
