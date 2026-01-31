using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Models.Pagination;
using BazaarCompanionWeb.Models.Pagination.MetaPaginations;
using BazaarCompanionWeb.Services;
using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Layout;

public partial class SearchBar
{
    private string _searchString = string.Empty;
    private List<ProductDataInfo> _results = new();
    private bool _isLoading;
    private bool _showResults;
    private Timer? _debounceTimer;

    private void OnSearchInput(ChangeEventArgs e)
    {
        _searchString = e.Value?.ToString() ?? string.Empty;

        _debounceTimer?.Dispose();
        if (string.IsNullOrWhiteSpace(_searchString))
        {
            _results.Clear();
            _showResults = false;
            return;
        }

        _isLoading = true;
        _debounceTimer = new Timer(async _ =>
        {
            await InvokeAsync(async () =>
            {
                await PerformSearch();
                _isLoading = false;
                _showResults = true;
                StateHasChanged();
            });
        }, null, 400, Timeout.Infinite);
    }

    private async Task PerformSearch()
    {
        try
        {
            var parsedQuery = SearchService.ParseNaturalLanguage(_searchString);

            var paginationQuery = new ProductPagination
            {
                Search = parsedQuery.ProductNameQuery,
                AdvancedFilters = new AdvancedFilterOptions
                {
                    SelectedTiers = parsedQuery.SelectedTiers,
                    MinPrice = parsedQuery.MinPrice,
                    MaxPrice = parsedQuery.MaxPrice,
                    VolumeTier = parsedQuery.VolumeTier,
                    ManipulationStatus = parsedQuery.ManipulationStatus
                },
                Top = 8,
                Skip = 0,
                Sorts =
                [
                    new SortDescriptor
                        { Property = nameof(ProductDataInfo.OrderMetaFlipOpportunityScore), SortOrder = Enums.SortDirection.Descending }
                ]
            };

            var result = await ProductQuery.QueryResourceAsync(paginationQuery, default);
            _results = result.Data.ToList();
        }
        catch (Exception)
        {
            _results = new();
        }
    }

    private void OnFocus()
    {
        if (!string.IsNullOrWhiteSpace(_searchString))
        {
            _showResults = true;
        }
    }

    private async Task OnBlur()
    {
        // Use mousedown on items instead of click to fire before blur, 
        // or small delay here. Mousedown is preferred for responsiveness.
        await Task.Delay(150);
        _showResults = false;
        StateHasChanged();
    }

    private void NavigateToProduct(string productKey)
    {
        NavigationManager.NavigateTo($"/product/{productKey}");
        _showResults = false;
        _searchString = string.Empty;
        _results.Clear();
    }

    private void SeeAllResults()
    {
        NavigationManager.NavigateTo($"/?search={Uri.EscapeDataString(_searchString)}");
        _showResults = false;
        _searchString = string.Empty;
        _results.Clear();
    }

    private void ClearSearch()
    {
        _searchString = string.Empty;
        _results.Clear();
        _showResults = false;
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
    }
}