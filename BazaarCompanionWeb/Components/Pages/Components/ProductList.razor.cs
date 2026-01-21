using System.Globalization;
using System.Text.Json;
using BazaarCompanionWeb.Components.Pages.Dialogs;
using BazaarCompanionWeb.Components.Shared;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Enums;
using BazaarCompanionWeb.Interfaces;
using BazaarCompanionWeb.Models.Pagination;
using BazaarCompanionWeb.Models.Pagination.MetaPaginations;
using BazaarCompanionWeb.Services;
using BazaarCompanionWeb.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using SortDescriptor = BazaarCompanionWeb.Models.Pagination.SortDescriptor;
using AppSortDirection = BazaarCompanionWeb.Enums.SortDirection;

namespace BazaarCompanionWeb.Components.Pages.Components;

public partial class ProductList(TimeCache timeCache, BrowserStorage browserStorage) : ComponentBase, IDisposable
{
    [Inject] private IResourceQueryHelper<ProductPagination, ProductDataInfo> ProductQuery { get; set; }

    private ProductDataInfo? _selectedProduct;
    private bool _showModal;

    private CultureInfo _usProvider = CultureInfo.CreateSpecificCulture("en-US");

    private string _searchString = string.Empty;
    private string _titleText = "Bazaar Flips";
    private bool _filter = false;
    private DateTimeOffset _lastServerRefresh;
    private string _pageTitle = "Bazaar Maxxing";
    private AdvancedFilterOptions _advancedFilters = new();
    private bool _useFuzzySearch = false;
    private bool _showAdvancedFilters = false;
    private List<string> _searchHistory = new();
    private const string FilterStorageKey = "bazaar_last_filters";
    private const string SearchHistoryKey = "bazaar_search_history";

    private GridItemsProvider<ProductDataInfo>? _itemsProvider;
    private QuickGrid<ProductDataInfo>? _grid;
    private PaginationState _pagination = new() { ItemsPerPage = 25 };
    private int _currentPageIndex = 0;
    private int _totalItemCount = 0;
    private Timer? _debounceTimer;

    protected override async Task OnInitializedAsync()
    {
        _lastServerRefresh = timeCache.LastUpdated;
        await LoadPersistedFiltersAsync();
        await LoadSearchHistoryAsync();
        _itemsProvider = async request =>
        {
            var sortDescriptors = new List<SortDescriptor>();
            
            // Get sort information from the request
            var sortDirection = AppSortDirection.Descending; // Default
            string? propertyName = null;
            
            if (request.SortByColumn != null)
            {
                sortDirection = request.SortByAscending 
                    ? AppSortDirection.Ascending 
                    : AppSortDirection.Descending;
                
                // Try to get property name from PropertyColumn - need to check different property types
                if (request.SortByColumn is PropertyColumn<ProductDataInfo, double> doubleColumn)
                {
                    propertyName = GetPropertyNameFromExpression(doubleColumn.Property);
                }
                else if (request.SortByColumn is PropertyColumn<ProductDataInfo, double?> nullableDoubleColumn)
                {
                    propertyName = GetPropertyNameFromExpression(nullableDoubleColumn.Property);
                }
                else if (request.SortByColumn is PropertyColumn<ProductDataInfo, int> intColumn)
                {
                    propertyName = GetPropertyNameFromExpression(intColumn.Property);
                }
                else if (request.SortByColumn is PropertyColumn<ProductDataInfo, string> stringColumn)
                {
                    propertyName = GetPropertyNameFromExpression(stringColumn.Property);
                }
                
                // For TemplateColumn or if property name extraction failed, use title mapping
                if (string.IsNullOrEmpty(propertyName))
                {
                    propertyName = GetPropertyNameFromColumnTitle(request.SortByColumn.Title);
                }
            }
            
            // Use default sort if no sort specified or property name not found
            if (string.IsNullOrEmpty(propertyName))
            {
                propertyName = nameof(ProductDataInfo.OrderMetaFlipOpportunityScore);
                sortDirection = AppSortDirection.Descending;
            }
            
            sortDescriptors.Add(new SortDescriptor
            {
                Property = propertyName,
                SortOrder = sortDirection
            });

            var itemsPerPage = _pagination.ItemsPerPage;
            
            // Parse natural language search if enabled
            var searchQuery = _searchString;
            var parsedQuery = SearchService.ParseNaturalLanguage(_searchString);
            if (parsedQuery.SelectedTiers.Any() || parsedQuery.MinPrice.HasValue || parsedQuery.MaxPrice.HasValue ||
                parsedQuery.VolumeTier != VolumeTierFilter.All || parsedQuery.ManipulationStatus != ManipulationFilter.All)
            {
                // Merge parsed query filters into advanced filters
                if (parsedQuery.SelectedTiers.Any())
                {
                    _advancedFilters.SelectedTiers = parsedQuery.SelectedTiers;
                }
                if (parsedQuery.MinPrice.HasValue) _advancedFilters.MinPrice = parsedQuery.MinPrice;
                if (parsedQuery.MaxPrice.HasValue) _advancedFilters.MaxPrice = parsedQuery.MaxPrice;
                if (parsedQuery.VolumeTier != VolumeTierFilter.All) _advancedFilters.VolumeTier = parsedQuery.VolumeTier;
                if (parsedQuery.ManipulationStatus != ManipulationFilter.All) _advancedFilters.ManipulationStatus = parsedQuery.ManipulationStatus;
                
                searchQuery = parsedQuery.ProductNameQuery;
            }

            var paginationQuery = new ProductPagination
            {
                ToggleFilter = _filter,
                Sorts = sortDescriptors,
                Search = searchQuery,
                AdvancedFilters = _advancedFilters,
                UseFuzzySearch = _useFuzzySearch,
                Top = itemsPerPage,
                Skip = _currentPageIndex * itemsPerPage,
            };

            var cancellationToken = new CancellationTokenSource();
            cancellationToken.CancelAfter(TimeSpan.FromSeconds(5));
            var context = await ProductQuery.QueryResourceAsync(paginationQuery, cancellationToken.Token);
            _lastServerRefresh = timeCache.LastUpdated;
            _totalItemCount = context.Count;
            StateHasChanged();

            return GridItemsProviderResult.From<ProductDataInfo>(
                context.Data.ToList(),
                totalItemCount: context.Count
            );
        };
    }

    private void OnSearchInput(ChangeEventArgs e)
    {
        _searchString = e.Value?.ToString() ?? string.Empty;
        _currentPageIndex = 0;
        
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(async _ =>
        {
            await InvokeAsync(async () =>
            {
                if (_grid is not null)
                {
                    await _grid.RefreshDataAsync();
                }
            });
        }, null, 300, Timeout.Infinite);
    }

    private async Task OnAdvancedFiltersChanged(AdvancedFilterOptions filters)
    {
        _advancedFilters = filters;
        _currentPageIndex = 0;
        await PersistFiltersAsync();
        if (_grid is not null)
        {
            await _grid.RefreshDataAsync();
        }
    }

    private async Task OnPresetLoaded(AdvancedFilterOptions filters)
    {
        _advancedFilters = filters;
        _currentPageIndex = 0;
        await PersistFiltersAsync();
        if (_grid is not null)
        {
            await _grid.RefreshDataAsync();
        }
    }

    private async Task LoadPersistedFiltersAsync()
    {
        try
        {
            var stored = await browserStorage.GetItemAsync(FilterStorageKey);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                _advancedFilters = JsonSerializer.Deserialize<AdvancedFilterOptions>(stored) ?? new AdvancedFilterOptions();
            }
        }
        catch
        {
            // Ignore errors
        }
    }

    private async Task PersistFiltersAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_advancedFilters);
            await browserStorage.SetItemAsync(FilterStorageKey, json);
        }
        catch
        {
            // Ignore storage errors
        }
    }

    private async Task LoadSearchHistoryAsync()
    {
        try
        {
            var stored = await browserStorage.GetItemAsync(SearchHistoryKey);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                _searchHistory = JsonSerializer.Deserialize<List<string>>(stored) ?? new List<string>();
            }
        }
        catch
        {
            _searchHistory = new List<string>();
        }
    }

    private async Task SaveSearchToHistoryAsync()
    {
        if (string.IsNullOrWhiteSpace(_searchString) || _searchHistory.Contains(_searchString))
            return;

        _searchHistory.Insert(0, _searchString);
        if (_searchHistory.Count > 10)
        {
            _searchHistory = _searchHistory.Take(10).ToList();
        }

        try
        {
            var json = JsonSerializer.Serialize(_searchHistory);
            await browserStorage.SetItemAsync(SearchHistoryKey, json);
        }
        catch
        {
            // Ignore storage errors
        }
    }

    private async Task OnFilter(ChangeEventArgs e)
    {
        _filter = e.Value is bool b && b;
        _currentPageIndex = 0;
        if (_grid is not null)
        {
            await _grid.RefreshDataAsync();
        }
    }
    
    private async Task ChangePage(int newIndex)
    {
        _currentPageIndex = newIndex;
        if (_grid is not null)
        {
            await _grid.RefreshDataAsync();
        }
    }
    
    private async Task ChangeItemsPerPage(int newItemsPerPage)
    {
        _pagination = new PaginationState { ItemsPerPage = newItemsPerPage };
        _currentPageIndex = 0;
        if (_grid is not null)
        {
            await _grid.RefreshDataAsync();
        }
    }

    private void RowClickEvent(ProductDataInfo product)
    {
        _selectedProduct = product;
        _pageTitle = $"{product.ItemFriendlyName} | Bazaar Maxxing";
        _showModal = true;
        StateHasChanged();
    }

    /// <summary>
    /// Shows the product detail modal for a given product key.
    /// Called externally by MarketInsightsPanel.
    /// </summary>
    public async Task ShowProductDetailAsync(string productKey)
    {
        // Query for the specific product
        var paginationQuery = new ProductPagination
        {
            ToggleFilter = false,
            Sorts = [],
            Search = productKey,
            AdvancedFilters = new AdvancedFilterOptions(),
            UseFuzzySearch = false,
            Top = 1,
            Skip = 0,
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await ProductQuery.QueryResourceAsync(paginationQuery, cts.Token);
        
        var product = result.Data.FirstOrDefault(p => p.ItemId == productKey);
        if (product is not null)
        {
            RowClickEvent(product);
        }
    }

    private void CloseModal()
    {
        _showModal = false;
        _selectedProduct = null;
        _pageTitle = "Bazaar Maxxing";
        StateHasChanged();
    }

    private static string? GetPropertyNameFromExpression<T>(System.Linq.Expressions.Expression<Func<ProductDataInfo, T>> expression)
    {
        if (expression.Body is System.Linq.Expressions.MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }
        if (expression.Body is System.Linq.Expressions.UnaryExpression unaryExpression && 
            unaryExpression.Operand is System.Linq.Expressions.MemberExpression unaryMember)
        {
            return unaryMember.Member.Name;
        }
        return null;
    }

    private static string? GetPropertyNameFromColumnTitle(string? title)
    {
        // Map known column titles to property names
        return title switch
        {
            "Product" => nameof(ProductDataInfo.ItemFriendlyName),
            "Bid" => nameof(ProductDataInfo.BuyOrderUnitPrice),
            "Spread" => nameof(ProductDataInfo.OrderMetaMargin),
            "Ask" => nameof(ProductDataInfo.SellOrderUnitPrice),
            "Profit x" => nameof(ProductDataInfo.OrderMetaPotentialProfitMultiplier),
            "Rating" => nameof(ProductDataInfo.OrderMetaFlipOpportunityScore),
            "7d Vol." => nameof(ProductDataInfo.OrderMetaTotalWeekVolume),
            _ => null
        };
    }

    private int GetActiveFilterCount()
    {
        int count = 0;
        if (_advancedFilters.SelectedTiers.Any()) count++;
        if (_advancedFilters.ManipulationStatus != ManipulationFilter.All) count++;
        if (_advancedFilters.VolumeTier != VolumeTierFilter.All) count++;
        if (_advancedFilters.Volatility != VolatilityFilter.All) count++;
        if (_advancedFilters.MinPrice.HasValue || _advancedFilters.MaxPrice.HasValue) count++;
        if (_advancedFilters.MinSpread.HasValue || _advancedFilters.MaxSpread.HasValue) count++;
        if (_advancedFilters.MinVolume.HasValue || _advancedFilters.MaxVolume.HasValue) count++;
        if (_advancedFilters.MinOpportunityScore.HasValue || _advancedFilters.MaxOpportunityScore.HasValue) count++;
        if (_advancedFilters.MinProfitMultiplier.HasValue || _advancedFilters.MaxProfitMultiplier.HasValue) count++;
        if (_advancedFilters.TrendDirection.HasValue) count++;
        if (!string.IsNullOrWhiteSpace(_advancedFilters.CorrelationProductKey)) count++;
        return count;
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
    }
}
