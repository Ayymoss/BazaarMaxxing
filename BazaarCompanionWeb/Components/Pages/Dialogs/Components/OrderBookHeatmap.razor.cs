using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BazaarCompanionWeb.Components.Pages.Dialogs.Components;

public partial class OrderBookHeatmap
{
    [Parameter] public required string ProductKey { get; set; }
    [Inject] private OrderBookAnalysisService AnalysisService { get; set; } = null!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    private List<HeatmapDataPoint> _heatmapData = [];
    private readonly string _chartId = Guid.NewGuid().ToString("N")[..8];
    private int _hours = 24;
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        await LoadHeatmapAsync();
    }

    private async Task LoadHeatmapAsync()
    {
        _loading = true;
        StateHasChanged();

        _heatmapData = await AnalysisService.GetHeatmapDataAsync(ProductKey, _hours);
        _loading = false;
        StateHasChanged();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_loading && _heatmapData.Count > 0)
        {
            await RenderHeatmapAsync();
        }
    }

    private async Task RenderHeatmapAsync()
    {
        var data = _heatmapData.Select(d => new
        {
            time = d.Time.ToString("O"),
            price = d.Price,
            volume = d.Volume
        }).ToArray();

        await JSRuntime.InvokeVoidAsync("renderHeatmap", $"heatmap-canvas-{_chartId}", data);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}