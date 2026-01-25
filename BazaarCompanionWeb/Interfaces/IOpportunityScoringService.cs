using BazaarCompanionWeb.Dtos;

namespace BazaarCompanionWeb.Interfaces;

public interface IOpportunityScoringService
{
    /// <summary>
    /// Calculates a risk-adjusted opportunity score for a product based on current prices,
    /// volume, and historical OHLC data. Higher scores indicate better trading opportunities.
    /// </summary>
    /// <param name="productKey">The product identifier</param>
    /// <param name="buyPrice">Current buy order price</param>
    /// <param name="sellPrice">Current sell order price</param>
    /// <param name="buyMovingWeek">Weekly buy volume</param>
    /// <param name="sellMovingWeek">Weekly sell volume</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A normalized opportunity score (typically 0-10+ range)</returns>
    Task<double> CalculateOpportunityScoreAsync(
        string productKey,
        double bidPrice,
        double askPrice,
        long bidMovingWeek,
        long askMovingWeek,
        CancellationToken ct = default);

    /// <summary>
    /// Calculates manipulation score to detect when a product's price deviates significantly
    /// from historical norms, indicating potential market manipulation or fire sale opportunities.
    /// </summary>
    /// <param name="productKey">The product identifier</param>
    /// <param name="currentBuyPrice">Current buy order price</param>
    /// <param name="currentSellPrice">Current sell order price</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Manipulation score indicating if and how much the product is manipulated</returns>
    Task<ManipulationScore> CalculateManipulationScoreAsync(
        string productKey,
        double currentBidPrice,
        double currentAskPrice,
        CancellationToken ct = default);
}
