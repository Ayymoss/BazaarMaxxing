using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;

namespace BazaarCompanionWeb.Utilities;

/// <summary>Maps the persisted <see cref="EFProduct"/> entity to the UI <see cref="ProductDataInfo"/> DTO.</summary>
public static class ProductMapping
{
    public static ProductDataInfo ToInfo(EFProduct product) => new()
    {
        BidMarketDataId = product.Bid.Id,
        AskMarketDataId = product.Ask.Id,
        ItemId = product.ProductKey,
        ItemFriendlyName = product.FriendlyName,
        ItemTier = product.Tier,
        ItemUnstackable = product.Unstackable,
        SkinUrl = product.SkinUrl,
        BidUnitPrice = product.Bid.UnitPrice,
        BidWeekVolume = product.Bid.OrderVolumeWeek,
        BidCurrentOrders = product.Bid.OrderCount,
        BidCurrentVolume = product.Bid.OrderVolume,
        AskUnitPrice = product.Ask.UnitPrice,
        AskWeekVolume = product.Ask.OrderVolumeWeek,
        AskCurrentOrders = product.Ask.OrderCount,
        AskCurrentVolume = product.Ask.OrderVolume,
        OrderMetaPotentialProfitMultiplier = product.Meta.ProfitMultiplier,
        OrderMetaSpread = product.Meta.Spread,
        OrderMetaTotalWeekVolume = product.Meta.TotalWeekVolume,
        OrderMetaFlipOpportunityScore = product.Meta.FlipOpportunityScore,
        IsManipulated = product.Meta.IsManipulated,
        EvidenceLimited = product.Meta.EvidenceLimited,
        ManipulationIntensity = product.Meta.ManipulationIntensity,
        PriceDeviationPercent = product.Meta.PriceDeviationPercent,
        SuggestedBidVolume = product.Meta.SuggestedBidVolume,
        SuggestedBidPrice = product.Meta.SuggestedBidPrice,
        SuggestedAskPrice = product.Meta.SuggestedAskPrice,
        EstimatedFillTimeHours = product.Meta.EstimatedFillTimeHours,
        EstimatedProfitPerUnit = product.Meta.EstimatedProfitPerUnit,
        EstimatedTotalProfit = product.Meta.EstimatedTotalProfit,
        RecommendationConfidence = product.Meta.RecommendationConfidence,
    };
}
