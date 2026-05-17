using BazaarCompanionWeb.Configurations;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Services;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace BazaarCompanionWeb.Components.Pages.Analytics;

public partial class MarketDashboard : IDisposable
{
    [Inject] private MarketAnalyticsService MarketAnalyticsService { get; set; } = null!;
    [Inject] private IOptions<UIConfig> UIConfig { get; set; } = null!;
    private bool _loading = true;
    private MarketMetrics? _metrics;
    private Dtos.CorrelationMatrix? _correlationMatrix;
    private List<ProductTrend> _trendingProducts = new();
    private DateTime? _lastUpdate;
    private bool _autoRefresh;
    private Timer? _autoRefreshTimer;

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
            _trendingProducts = await MarketAnalyticsService.GetTrendingProductsAsync(UIConfig.Value.TrendingProductsLimit);
            _lastUpdate = DateTime.Now;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading market analytics: {ex.Message}");
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private void OnAutoRefreshToggled()
    {
        _autoRefreshTimer?.Dispose();
        if (_autoRefresh)
        {
            var interval = TimeSpan.FromMinutes(UIConfig.Value.AnalyticsAutoRefreshMinutes);
            _autoRefreshTimer = new Timer(async _ => await InvokeAsync(LoadDataAsync),
                null, interval, interval);
        }
        else
        {
            _autoRefreshTimer = null;
        }
    }

    public void Dispose()
    {
        _autoRefreshTimer?.Dispose();
    }
}
