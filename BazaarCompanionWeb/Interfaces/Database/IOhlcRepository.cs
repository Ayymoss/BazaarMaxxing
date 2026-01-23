using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;

namespace BazaarCompanionWeb.Interfaces.Database;

public interface IOhlcRepository
{
    Task RecordTicksAsync(IEnumerable<(string ProductKey, double BuyPrice, double SellPrice, long BuyVolume, long SellVolume)> ticks, CancellationToken ct = default);
    Task<List<OhlcDataPoint>> GetCandlesAsync(string productKey, CandleInterval interval, int limit = 100, CancellationToken ct = default);
    Task<List<EFPriceTick>> GetTicksForAggregationAsync(string productKey, DateTime since, CancellationToken ct = default);
    Task SaveCandlesAsync(IEnumerable<EFOhlcCandle> candles, CancellationToken ct = default);
    Task<DateTime?> GetLatestCandleTimeAsync(string productKey, CandleInterval interval, CancellationToken ct = default);
    Task<List<string>> GetAllProductKeysAsync(CancellationToken ct = default);
    Task PruneOldTicksAsync(TimeSpan retention, CancellationToken ct = default);
    Task PruneOldCandlesAsync(CancellationToken ct = default);
    Task VacuumDatabaseAsync(CancellationToken ct = default);
}
