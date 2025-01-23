using System.Globalization;
using BazaarCompanionWeb.Components.Pages.Dialogs;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Enums;
using BazaarCompanionWeb.Interfaces;
using BazaarCompanionWeb.Models.Api.Items;
using BazaarCompanionWeb.Models.Pagination;
using BazaarCompanionWeb.Models.Pagination.MetaPaginations;
using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;
using SortDescriptor = BazaarCompanionWeb.Models.Pagination.SortDescriptor;

namespace BazaarCompanionWeb.Components.Pages.Components;

public partial class ProductList : IDisposable
{
    [Inject] private DialogService DialogService { get; set; }
    [Inject] private IResourceQueryHelper<ProductPagination, ProductDataInfo> ProductQuery { get; set; }

    private RadzenDataGrid<ProductDataInfo> _dataGrid;
    private IEnumerable<ProductDataInfo> _productTable;

    private CultureInfo _usProvider = CultureInfo.CreateSpecificCulture("en-US");

    private bool _isLoading = false;
    private int _count;
    private string _searchString = string.Empty;
    private string _titleText = "Flips";
    private bool _filter = true;

    private static IEnumerable<int> PageSizes => [25, 50, 100];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
    }

    private async Task TableLoadData(LoadDataArgs args)
    {
        _isLoading = true;

        var sort = !args.Sorts.Any()
            ?
            [
                new Radzen.SortDescriptor
                {
                    Property = nameof(ProductDataInfo.OrderMetaPotentialProfitMultiplier),
                    SortOrder = SortOrder.Descending
                }
            ]
            : args.Sorts;

        var convertedSort = sort.Select(x => new SortDescriptor
        {
            Property = x.Property,
            SortOrder = x.SortOrder == SortOrder.Ascending
                ? SortDirection.Ascending
                : SortDirection.Descending
        });

        var paginationQuery = new ProductPagination
        {
            ToggleFilter = _filter,
            Sorts = convertedSort,
            Search = _searchString,
            Top = args.Top ?? 20,
            Skip = args.Skip ?? 0,
        };

        var cancellationToken = new CancellationTokenSource();
        cancellationToken.CancelAfter(TimeSpan.FromSeconds(5));
        var context = await ProductQuery.QueryResourceAsync(paginationQuery, cancellationToken.Token);
        _productTable = context.Data;
        _count = context.Count;
        _isLoading = false;
    }

    private async Task OnSearch(string text)
    {
        _searchString = text;
        await _dataGrid.GoToPage(0);
        await _dataGrid.Reload();
    }

    private async Task OnFilter(bool filter)
    {
        _filter = filter;
        await _dataGrid.GoToPage(0);
        await _dataGrid.Reload();
    }

    private async Task RowClickEvent(DataGridRowMouseEventArgs<ProductDataInfo> arg)
    {
        var parameters = new Dictionary<string, object>
        {
            { nameof(PriceHistoryDialog.Product), arg.Data }
        };

        var options = new DialogOptions
        {
            Style = "min-height:auto;min-width:auto;width:100%;max-width:75%;max-height:97%",
            CloseDialogOnOverlayClick = true,
            ShowTitle = false,
            ShowClose = true
        };

        await DialogService.OpenAsync<PriceHistoryDialog>("Price History", parameters, options);
    }

    private static string ProductTierColor(ItemTier itemTier)
    {
        return itemTier switch
        {
            ItemTier.Uncommon => "#78F86A",
            ItemTier.Rare => "#535FF8",
            ItemTier.Epic => "#A22EA5",
            ItemTier.Legendary => "#F9AD35",
            ItemTier.Mythic => "#F46DF9",
            ItemTier.Supreme => "#76FBFE",
            ItemTier.Special => "#F5655A",
            ItemTier.VerySpecial => "#F5655A",
            ItemTier.Unobtainable => "#A2240F",
            _ => "#FFFFFF"
        };
    }

    private static string ProfitColor(double multiplier)
    {
        return multiplier switch
        {
            < 2 => "#808080",
            < 3 => "#FFFFFF",
            < 5 => "#78F86A",
            < 10 => "#535FF8",
            < 50 => "#A22EA5",
            < 100 => "#F9AD35",
            < 500 => "#F46DF9",
            _ => "#76FBFE"
        };
    }

    public void Dispose()
    {
        _dataGrid.Dispose();
    }
}
