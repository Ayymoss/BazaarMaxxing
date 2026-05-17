namespace BazaarCompanionWeb.Configurations;

/// <summary>
/// UI-side timing and list-size knobs. Bound from <c>UIConfig</c> section of appsettings.json.
/// Defaults match what the components used pre-extraction; override per environment.
/// </summary>
public sealed class UIConfig
{
    /// <summary>
    /// How often the "Last Updated" humanized text on Product / ProductList re-renders.
    /// Lower values waste CPU; the string only changes at minute boundaries anyway.
    /// </summary>
    public int LastUpdatedRefreshSeconds { get; set; } = 30;

    /// <summary>
    /// Poll interval for <c>MarketInsightsPanel</c>. Pauses when tab is hidden.
    /// </summary>
    public int InsightsPanelRefreshSeconds { get; set; } = 30;

    /// <summary>
    /// Analytics dashboard auto-refresh interval (when the toggle is enabled).
    /// </summary>
    public int AnalyticsAutoRefreshMinutes { get; set; } = 5;

    /// <summary>
    /// How many related-product correlations to fetch + display on the Product page.
    /// </summary>
    public int RelatedProductsLimit { get; set; } = 5;

    /// <summary>
    /// How many trending products to render on the Analytics dashboard.
    /// </summary>
    public int TrendingProductsLimit { get; set; } = 10;
}
