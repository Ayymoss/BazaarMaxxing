using BazaarCompanionWeb.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BazaarCompanionWeb.Components.Pages.Dialogs.Components;

public partial class OrderBookDepth
{
    [Parameter] public OrderBookDepthMetrics? DepthMetrics { get; set; }
    [Parameter] public List<DepthChartPoint> DepthChart { get; set; } = [];
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    private IJSObjectReference? _chartModule;
    private readonly string _chartId = Guid.NewGuid().ToString("N")[..8];
    private bool _chartRendered;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _chartModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/chartInit.js");
        }

        if (_chartModule is not null && DepthChart.Count > 0 && !_chartRendered)
        {
            await RenderDepthChartAsync();
            _chartRendered = true;
        }
    }

    private async Task RenderDepthChartAsync()
    {
        if (_chartModule is null) return;

        var bidData = DepthChart.Where(p => p.IsBid)
            .OrderByDescending(p => p.Price)
            .Select(p => new { price = p.Price, volume = p.CumulativeVolume })
            .ToArray();

        var askData = DepthChart.Where(p => !p.IsBid)
            .OrderBy(p => p.Price)
            .Select(p => new { price = p.Price, volume = p.CumulativeVolume })
            .ToArray();

        await _chartModule.InvokeVoidAsync("createDepthChart", $"depth-chart-{_chartId}", bidData, askData);
    }

    private string GetLiquidityColor() => DepthMetrics?.LiquidityScore switch
    {
        >= 70 => "#22c55e",
        >= 40 => "#f59e0b",
        _ => "#ef4444"
    };

    public async ValueTask DisposeAsync()
    {
        if (_chartModule is not null)
        {
            try
            {
                await _chartModule.DisposeAsync();
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
