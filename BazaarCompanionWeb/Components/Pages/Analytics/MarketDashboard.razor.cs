using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Services;
using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Pages.Analytics;

public partial class MarketDashboard
{
    [Inject] private MarketAnalyticsService MarketAnalyticsService { get; set; } = null!;
    private bool _loading = true;
    private MarketMetrics? _metrics;
    private Dtos.CorrelationMatrix? _correlationMatrix;
    private List<ProductTrend> _trendingProducts = new();
    private MarketHeatmapData? _heatmapData;
    private DateTime? _lastUpdate;

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _loading = true;
        StateHasChanged();

        try
        {
            _metrics = await MarketAnalyticsService.GetMarketMetricsAsync();
            _correlationMatrix = await MarketAnalyticsService.GetCorrelationMatrixAsync();
            _trendingProducts = await MarketAnalyticsService.GetTrendingProductsAsync(10);
            _heatmapData = await MarketAnalyticsService.GetMarketHeatmapAsync();
            _lastUpdate = DateTime.Now;
        }
        catch (Exception ex)
        {
            // Log error - in production, show user-friendly error message
            Console.WriteLine($"Error loading market analytics: {ex.Message}");
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }
}