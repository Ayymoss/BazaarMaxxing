using BazaarCompanionWeb.Models.Api.Items;
using BazaarCompanionWeb.Models.Pagination;
using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Pages.Components;

public partial class AdvancedFilters
{
    [Parameter] public required AdvancedFilterOptions Filters { get; set; }
    [Parameter] public EventCallback<AdvancedFilterOptions> FiltersChanged { get; set; }
    private bool _collapsed = false;

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;
    }

    private void ToggleTier(ItemTier tier, bool isSelected)
    {
        if (isSelected && !Filters.SelectedTiers.Contains(tier))
        {
            Filters.SelectedTiers.Add(tier);
        }
        else if (!isSelected)
        {
            Filters.SelectedTiers.Remove(tier);
        }

        OnFiltersChanged();
    }

    private void ClearAllFilters()
    {
        Filters = new AdvancedFilterOptions();
        OnFiltersChanged();
    }

    private void OnFiltersChanged()
    {
        FiltersChanged.InvokeAsync(Filters);
    }

    private int GetActiveFilterCount()
    {
        int count = 0;
        if (Filters.SelectedTiers.Any()) count++;
        if (Filters.ManipulationStatus != ManipulationFilter.All) count++;
        if (Filters.VolumeTier != VolumeTierFilter.All) count++;
        if (Filters.Volatility != VolatilityFilter.All) count++;
        if (Filters.MinPrice.HasValue || Filters.MaxPrice.HasValue) count++;
        if (Filters.MinSpread.HasValue || Filters.MaxSpread.HasValue) count++;
        if (Filters.MinVolume.HasValue || Filters.MaxVolume.HasValue) count++;
        if (Filters.MinOpportunityScore.HasValue || Filters.MaxOpportunityScore.HasValue) count++;
        if (Filters.MinProfitMultiplier.HasValue || Filters.MaxProfitMultiplier.HasValue) count++;
        if (Filters.TrendDirection.HasValue) count++;
        if (!string.IsNullOrWhiteSpace(Filters.CorrelationProductKey)) count++;
        return count;
    }
}