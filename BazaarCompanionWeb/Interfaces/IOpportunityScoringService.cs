using BazaarCompanionWeb.Dtos;

namespace BazaarCompanionWeb.Interfaces;

public interface IOpportunityScoringService
{
    /// <summary>
    /// Batch scoring: computes opportunity scores, manipulation flags, and trade recommendations
    /// using preloaded candles. Results align by index with the input list.
    /// </summary>
    IReadOnlyList<ScoringResult> CalculateScoresBatch(
        IReadOnlyList<ScoringProductInput> products,
        IReadOnlyDictionary<string, List<OhlcDataPoint>> candlesByProduct);
}
