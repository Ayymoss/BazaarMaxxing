using BazaarCompanion.Enums;
using BazaarCompanion.Interfaces;
using BazaarCompanion.Models;
using BazaarCompanion.Models.Api.Bazaar;
using BazaarCompanion.Models.Api.Items;
using BazaarCompanion.Utilities;
using Humanizer;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Item = BazaarCompanion.Models.Item;

namespace BazaarCompanion;

public class BazaarDisplay(IHyPixelApi hyPixelApi, IOptionsMonitor<Configuration> optionsMonitor)
{
    private readonly Configuration _configuration = optionsMonitor.CurrentValue;
    private bool _filter = true;
    private IEnumerable<ProductData> _products = [];

    private readonly Dictionary<MenuOption, string> _menuOptions = new()
    {
        [MenuOption.UpdateData] = "Update Data",
        [MenuOption.ToggleFilter] = "Toggle Filter",
        [MenuOption.SortOptions] = "Sort Options",
        [MenuOption.Quit] = "Quit Application"
    };

    private readonly Dictionary<SortOption, string> _sortOptions = new()
    {
        [SortOption.Name] = "Name",
        [SortOption.BuyPrice] = "Buy Price",
        [SortOption.SellPrice] = "Sell Price",
        [SortOption.Margin] = "Margin",
        [SortOption.PotentialProfitMultiplier] = "Potential Profit Multiplier",
        [SortOption.BuyOrderPower] = "Buy Order Power",
        [SortOption.TotalMovingVolume] = "Total Week Volume",
        [SortOption.BuyMovingWeek] = "Buy Week Volume",
        [SortOption.SellMovingWeek] = "Sell Week Volume",
    };

    public async Task HandleDisplayAsync(CancellationToken cancellationToken)
    {
        var data = await FetchDataAsync();
        var products = BuildProductData(data.BazaarResponse, data.ItemResponse);
        var currentSort = SortOption.PotentialProfitMultiplier;

        while (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.Clear();

            RenderTable(products, currentSort);

            var menuOption = AnsiConsole.Prompt(
                new SelectionPrompt<MenuOption>()
                    .Title("Menu Options")
                    .AddChoices(_menuOptions.Keys)
                    .UseConverter(x => _menuOptions[x]));

            switch (menuOption)
            {
                case MenuOption.Quit:
                    Environment.Exit(1);
                    break;
                case MenuOption.UpdateData:
                    data = await FetchDataAsync();
                    products = BuildProductData(data.BazaarResponse, data.ItemResponse);
                    break;
                case MenuOption.ToggleFilter:
                    _filter = !_filter;
                    products = BuildProductData(data.BazaarResponse, data.ItemResponse);
                    break;
                case MenuOption.SortOptions:
                    currentSort = AnsiConsole.Prompt(
                        new SelectionPrompt<SortOption>()
                            .Title("Sort Options")
                            .AddChoices(_sortOptions.Keys)
                            .UseConverter(x => _sortOptions[x]));
                    break;
            }
        }
    }

    private async Task<(BazaarResponse BazaarResponse, ItemResponse ItemResponse)> FetchDataAsync()
    {
        var itemResponse = await hyPixelApi.GetItemsAsync();
        var bazaarResponse = await hyPixelApi.GetBazaarAsync();
        return (bazaarResponse, itemResponse);
    }

    private List<ProductData> BuildProductData(BazaarResponse bazaarResponse, ItemResponse itemResponse)
    {
        _products = bazaarResponse.Products.Values
            .Where(x => x.BuySummary.Count is not 0)
            .Join(itemResponse.Items, bazaar => bazaar.ProductId, item => item.Id, (bazaar, item) =>
            {
                var buy = bazaar.BuySummary.FirstOrDefault();
                var sell = bazaar.SellSummary.FirstOrDefault();

                var buyPrice = buy?.PricePerUnit ?? double.MaxValue;
                var sellPrice = sell?.PricePerUnit ?? 0.1;

                var margin = buyPrice - sellPrice;
                var totalWeekVolume = bazaar.QuickStatus.SellMovingWeek + bazaar.QuickStatus.BuyMovingWeek;
                var potentialProfitMultiplier = buyPrice / sellPrice;
                var buyingPower = (float)bazaar.QuickStatus.BuyMovingWeek / bazaar.QuickStatus.SellMovingWeek;

                return new ProductData
                {
                    ItemId = bazaar.ProductId,
                    Item = new Item
                    {
                        FriendlyName = item.Name,
                        Tier = item.Tier,
                        Unstackable = item.Unstackable
                    },
                    Buy = new OrderInfo
                    {
                        UnitPrice = buy?.PricePerUnit,
                        WeekVolume = bazaar.QuickStatus.BuyMovingWeek,
                        CurrentOrders = bazaar.QuickStatus.BuyOrders,
                        CurrentVolume = bazaar.QuickStatus.BuyVolume
                    },
                    Sell = new OrderInfo
                    {
                        UnitPrice = sell?.PricePerUnit,
                        WeekVolume = bazaar.QuickStatus.SellMovingWeek,
                        CurrentOrders = bazaar.QuickStatus.SellOrders,
                        CurrentVolume = bazaar.QuickStatus.SellVolume
                    },
                    OrderMeta = new OrderMeta
                    {
                        PotentialProfitMultiplier = potentialProfitMultiplier,
                        Margin = margin,
                        TotalWeekVolume = totalWeekVolume,
                        BuyOrderPower = buyingPower,
                    }
                };
            });

        List<ProductData> products;

        if (_filter)
        {
            products = _products.Where(x => x.OrderMeta.Margin > _configuration.MinimumMargin)
                .Where(x => x.OrderMeta.PotentialProfitMultiplier > _configuration.MinimumPotentialProfitMultiplier)
                .Where(x => x.OrderMeta.BuyOrderPower > _configuration.MinimumBuyOrderPower)
                .Where(x => x.Buy.WeekVolume > _configuration.MinimumWeekVolume)
                .Where(x => x.Sell.WeekVolume > _configuration.MinimumWeekVolume)
                .ToList();
        }
        else
        {
            products = _products.ToList();
        }

        return products;
    }

    private static void RenderTable(List<ProductData> products, SortOption sortOption)
    {
        var orderedProducts = sortOption switch
        {
            SortOption.Name => products.OrderBy(x => x.Item.FriendlyName),
            SortOption.BuyPrice => products.OrderByDescending(x => x.Buy.UnitPrice),
            SortOption.SellPrice => products.OrderByDescending(x => x.Sell.UnitPrice),
            SortOption.Margin => products.OrderByDescending(x => x.OrderMeta.Margin),
            SortOption.TotalMovingVolume => products.OrderByDescending(x => x.OrderMeta.TotalWeekVolume),
            SortOption.PotentialProfitMultiplier => products.OrderByDescending(x => x.OrderMeta.PotentialProfitMultiplier),
            SortOption.SellMovingWeek => products.OrderByDescending(x => x.Sell.WeekVolume),
            SortOption.BuyMovingWeek => products.OrderByDescending(x => x.Buy.WeekVolume),
            SortOption.BuyOrderPower => products.OrderByDescending(x => x.OrderMeta.BuyOrderPower),
            _ => throw new ArgumentOutOfRangeException(nameof(sortOption), sortOption, null)
        };

        var table = new Table()
            .Expand()
            .Title("[bold blue]Bazaar Orders[/]")
            .RoundedBorder()
            .BorderColor(Color.DarkCyan);

        table.AddColumn(new TableColumn("[u]Name[/]").LeftAligned().NoWrap());
        table.AddColumn(new TableColumn("[bold green]Buy Price[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold red]Sell Price[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #FFA500]Margin[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #F9AD35]Pot. Prof. Mult.[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #F46DF9]Buy Order Pow.[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #FF6347]Total Week Vol.[/]").RightAligned().NoWrap());
        table.AddColumn(new TableColumn("[green]Buy Week Vol.[/]").RightAligned().NoWrap());
        table.AddColumn(new TableColumn("[red]Sell Week Vol.[/]").RightAligned().NoWrap());
        table.AddColumn(new TableColumn("[green]Open Buys[/]").RightAligned());
        table.AddColumn(new TableColumn("[red]Open Sells[/]").RightAligned());

        foreach (var product in orderedProducts)
        {
            var tierColor = product.Item.Tier switch
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
                _ => "white"
            };

            var multiplierColor = product.OrderMeta.PotentialProfitMultiplier switch
            {
                < 2 => "grey",
                < 3 => "yellow1",
                < 5 => "greenyellow",
                < 10 => "mediumspringgreen",
                < 50 => "springgreen1",
                < 100 => "paleturquoise1",
                _ => "deepskyblue1"
            };

            var buyPowerColor = product.OrderMeta.BuyOrderPower switch
            {
                < 0.25 => "grey",
                < 0.75 => "yellow1",
                < 1.5 => "deepskyblue1",
                < 2 => "yellow1",
                _ => "grey"
            };

            table.AddRow(
                $"[{tierColor}]{product.Item.FriendlyName} {(product.Item.Unstackable ? "(UNSTKBL)" : string.Empty)}[/]",
                $"{product.Buy.UnitPrice?.ToString("C2") ?? "--"}",
                $"{product.Sell.UnitPrice?.ToString("C2") ?? "--"}",
                $"{product.OrderMeta.Margin:C2}",
                $"[{multiplierColor}]{product.OrderMeta.PotentialProfitMultiplier:N2}x[/]",
                $"[{buyPowerColor}]{product.OrderMeta.BuyOrderPower:N2}x[/]",
                $"{product.OrderMeta.TotalWeekVolume:N0}",
                $"{product.Buy.WeekVolume:N0}",
                $"{product.Sell.WeekVolume:N0}",
                $"Odr: {product.Buy.CurrentOrders.ToMetric(decimals: 0),4} | Vol: {product.Buy.CurrentVolume.ToMetric(decimals: 0),4}",
                $"Odr: {product.Sell.CurrentOrders.ToMetric(decimals: 0),4} | Vol: {product.Sell.CurrentVolume.ToMetric(decimals: 0),4}");
        }

        AnsiConsole.Status().Start("HyPixel Bazaar", ctx => AnsiConsole.Write(table));
    }
}
