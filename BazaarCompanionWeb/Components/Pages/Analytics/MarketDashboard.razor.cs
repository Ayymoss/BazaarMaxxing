using BazaarCompanionWeb.Configurations;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace BazaarCompanionWeb.Components.Pages.Analytics;

public partial class MarketDashboard : IDisposable
{
    [Inject] private MarketAnalyticsService MarketAnalyticsService { get; set; } = null!;
    [Inject] private IOptions<UIConfig> UIConfig { get; set; } = null!;

    private MarketMetrics? _metrics;
    private List<ProductTrend> _trendingProducts = [];
    private bool _metricsLoading = true;
    private bool _trendsLoading = true;
    private DateTime? _lastUpdate;
    private bool _autoRefresh;
    private Timer? _autoRefreshTimer;

    private bool Loading => _metricsLoading || _trendsLoading;

    protected override async Task OnInitializedAsync() => await LoadDataAsync();

    // Load each section independently so a slow one never blocks the rest.
    private async Task LoadDataAsync()
    {
        await Task.WhenAll(LoadMetricsAsync(), LoadTrendsAsync());
        _lastUpdate = DateTime.Now;
        StateHasChanged();
    }

    private async Task LoadMetricsAsync()
    {
        _metricsLoading = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            _metrics = await MarketAnalyticsService.GetMarketMetricsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading market metrics: {ex.Message}");
        }
        finally
        {
            _metricsLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadTrendsAsync()
    {
        _trendsLoading = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            _trendingProducts = await MarketAnalyticsService.GetTrendingProductsAsync(UIConfig.Value.TrendingProductsLimit);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading trending products: {ex.Message}");
        }
        finally
        {
            _trendsLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void OnAutoRefreshToggled()
    {
        _autoRefreshTimer?.Dispose();
        if (_autoRefresh)
        {
            var interval = TimeSpan.FromMinutes(UIConfig.Value.AnalyticsAutoRefreshMinutes);
            _autoRefreshTimer = new Timer(async _ => await InvokeAsync(LoadDataAsync), null, interval, interval);
        }
        else
        {
            _autoRefreshTimer = null;
        }
    }

    public void Dispose() => _autoRefreshTimer?.Dispose();
}
