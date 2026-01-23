using BazaarCompanionWeb.Dtos;
using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Pages.Components;

public partial class MarketInsightsPanel
{
    [Parameter] public EventCallback<string> ProductClicked { get; set; }
    private MarketInsights? _insights;
    private bool _showGainers = true;
    private Timer? _refreshTimer;

    protected override async Task OnInitializedAsync()
    {
        await LoadInsightsAsync();

        // Auto-refresh every 30 seconds
        _refreshTimer = new Timer(async _ =>
        {
            await LoadInsightsAsync();
            await InvokeAsync(StateHasChanged);
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private async Task LoadInsightsAsync()
    {
        try
        {
            _insights = await InsightsService.GetInsightsAsync();
        }
        catch
        {
            // Silently fail - insights are non-critical
        }
    }

    private async Task OnProductClick(string productKey)
    {
        await ProductClicked.InvokeAsync(productKey);
    }

    private static string GetTierColor(string tier) => tier switch
    {
        "Common" => "bg-slate-400",
        "Uncommon" => "bg-emerald-400",
        "Rare" => "bg-blue-400",
        "Epic" => "bg-purple-400",
        "Legendary" => "bg-amber-400",
        "Mythic" => "bg-rose-400",
        "Divine" => "bg-cyan-400",
        "Special" => "bg-pink-400",
        "VerySpecial" => "bg-rose-500",
        _ => "bg-slate-500"
    };

    public void Dispose()
    {
        _refreshTimer?.Dispose();
    }
}