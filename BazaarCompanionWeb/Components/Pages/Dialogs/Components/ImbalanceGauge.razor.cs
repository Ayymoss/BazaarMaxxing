using BazaarCompanionWeb.Dtos;
using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Pages.Dialogs.Components;

public partial class ImbalanceGauge
{
    [Parameter] public OrderBookImbalance? Imbalance { get; set; }

    private string GetGaugeColor() => Imbalance?.ImbalanceRatio switch
    {
        > 0.3 => "#22c55e", // Strong buy - green
        > 0.1 => "#86efac", // Moderate buy - light green
        < -0.3 => "#ef4444", // Strong sell - red
        < -0.1 => "#fca5a5", // Moderate sell - light red
        _ => "#64748b" // Neutral - slate
    };

    private string GetTrendLabel() => Imbalance?.Trend switch
    {
        ImbalanceTrend.Improving => "↗ Buyers dominating",
        ImbalanceTrend.Worsening => "↘ Sellers dominating",
        _ => "⟷ Balanced"
    };

    private const double ArcLength = 141.37; // π * 45 (half circle)
    private string GetDashArray() => $"{ArcLength} {ArcLength}";

    private string GetDashOffset()
    {
        var ratio = Imbalance?.ImbalanceRatio ?? 0;
        var normalized = (ratio + 1) / 2; // Convert -1..1 to 0..1
        var offset = ArcLength * (1 - normalized);
        return offset.ToString("F2");
    }

    private string GetNeedleX()
    {
        var ratio = Imbalance?.ImbalanceRatio ?? 0;
        var angle = Math.PI * (1 - (ratio + 1) / 2); // Map to 0..π
        return (50 + 35 * Math.Cos(angle)).ToString("F2");
    }

    private string GetNeedleY()
    {
        var ratio = Imbalance?.ImbalanceRatio ?? 0;
        var angle = Math.PI * (1 - (ratio + 1) / 2);
        return (50 - 35 * Math.Sin(angle)).ToString("F2");
    }
}