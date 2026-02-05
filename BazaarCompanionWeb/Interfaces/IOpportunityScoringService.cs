using BazaarCompanionWeb.Dtos;

namespace BazaarCompanionWeb.Interfaces;

public interface IOpportunityScoringService
{
    /// <summary>
    /// Batch version for pipeline: computes opportunity and manipulation scores in-memory using preloaded candles.
    /// Results align by index with the input list.
    /// </summary>
    (IReadOnlyList<double> OpportunityScores, IReadOnlyList<ManipulationScore> ManipulationScores) CalculateScoresBatch(
        IReadOnlyList<ScoringProductInput> products,
        IReadOnlyDictionary<string, List<OhlcDataPoint>> candlesByProduct);
}
