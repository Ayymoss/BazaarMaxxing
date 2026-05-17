using BazaarCompanionWeb.Entities;

namespace BazaarCompanionWeb.Services.Ingestion;

public sealed record SnapshotDrainResult(
    IReadOnlyList<EFProduct> ChangedProducts,
    IReadOnlyList<EFPriceTick> ChangedTicks);
