using BazaarCompanionWeb.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

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

        // Auto-refresh on the configured cadence. Skip the fetch if the browser tab is hidden —
        // no point hammering the API/cache when the user isn't looking.
        var interval = TimeSpan.FromSeconds(UIConfig.Value.InsightsPanelRefreshSeconds);
        _refreshTimer = new Timer(async _ =>
        {
            if (await IsTabHiddenAsync()) return;
            await LoadInsightsAsync();
            await InvokeAsync(StateHasChanged);
        }, null, interval, interval);
    }

    private async Task<bool> IsTabHiddenAsync()
    {
        try
        {
            return await JSRuntime.InvokeAsync<bool>("eval", "document.hidden");
        }
        catch
        {
            // If JS interop fails (prerender, etc.) assume visible to keep behaviour conservative.
            return false;
        }
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
