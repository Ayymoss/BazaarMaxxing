using BazaarCompanion.Interfaces;
using BazaarCompanion.Models;
using BazaarCompanion.Models.Api.Bazaar;
using BazaarCompanion.Models.Api.Items;
using BazaarCompanion.Utilities;
using Humanizer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Item = BazaarCompanion.Models.Item;

namespace BazaarCompanion;

public class AppEntry(IHyPixelApi hyPixelApi, IOptionsMonitor<Configuration> optionsMonitor) : IHostedService
{
    private readonly Configuration _configuration = optionsMonitor.CurrentValue;
    private bool _filter = true;
    private IEnumerable<ProductData> _products = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        new Thread(async () => await HandleDisplayAsync(cancellationToken)) { Name = nameof(AppEntry) }.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task HandleDisplayAsync(CancellationToken cancellationToken)
    {
        var data = await FetchDataAsync();
        var products = BuildProductData(data.BazaarResponse, data.ItemResponse);

        var currentSort = SortTypes.PotentialProfitMultiplier;

        while (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.Clear();

            RenderTable(products, currentSort);

            // TODO: Clean this up.
            var sortColumn = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Sort by which column? (or Quit)")
                    .AddChoices("Update Data", "Toggle Filter", "-------", "Name", "Buy Price", "Sell Price", "Margin",
                        "Potential Profit Multiplier", "Total Week Volume", "Sell Week Volume", "Buy Week Volume", "Buy Order Power",
                        "-------", "Quit"));

            switch (sortColumn)
            {
                case "Quit":
                    Environment.Exit(1);
                    break;
                case "Update Data":
                    data = await FetchDataAsync();
                    products = BuildProductData(data.BazaarResponse, data.ItemResponse);
                    break;
                case "Toggle Filter":
                    _filter = !_filter;
                    products = BuildProductData(data.BazaarResponse, data.ItemResponse);
                    break;
            }

            currentSort = sortColumn switch
            {
                "Name" => SortTypes.Name,
                "Buy Price" => SortTypes.BuyPrice,
                "Sell Price" => SortTypes.SellPrice,
                "Margin" => SortTypes.Margin,
                "Potential Profit Multiplier" => SortTypes.PotentialProfitMultiplier,
                "Total Week Volume" => SortTypes.TotalMovingVolume,
                "Sell Week Volume" => SortTypes.SellMovingWeek,
                "Buy Week Volume" => SortTypes.BuyMovingWeek,
                "Buy Order Power" => SortTypes.BuyOrderPower,
                _ => currentSort
            };
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
                var buy = bazaar.BuySummary.First();
                var sell = bazaar.SellSummary.FirstOrDefault();

                var margin = buy.PricePerUnit - (sell?.PricePerUnit ?? 0);
                var totalWeekVolume = bazaar.QuickStatus.SellMovingWeek + bazaar.QuickStatus.BuyMovingWeek;
                var potentialProfitMultiplier = buy.PricePerUnit / (sell?.PricePerUnit ?? 0.1);
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
                        UnitPrice = buy.PricePerUnit,
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

    private static void RenderTable(List<ProductData> products, SortTypes currentSort)
    {
        var orderedProducts = currentSort switch
        {
            SortTypes.Name => products.OrderBy(x => x.Item.FriendlyName),
            SortTypes.BuyPrice => products.OrderByDescending(x => x.Buy.UnitPrice),
            SortTypes.SellPrice => products.OrderByDescending(x => x.Sell.UnitPrice),
            SortTypes.Margin => products.OrderByDescending(x => x.OrderMeta.Margin),
            SortTypes.TotalMovingVolume => products.OrderByDescending(x => x.OrderMeta.TotalWeekVolume),
            SortTypes.PotentialProfitMultiplier => products.OrderByDescending(x => x.OrderMeta.PotentialProfitMultiplier),
            SortTypes.SellMovingWeek => products.OrderByDescending(x => x.Sell.WeekVolume),
            SortTypes.BuyMovingWeek => products.OrderByDescending(x => x.Buy.WeekVolume),
            SortTypes.BuyOrderPower => products.OrderByDescending(x => x.OrderMeta.BuyOrderPower),
            _ => throw new ArgumentOutOfRangeException(nameof(currentSort), currentSort, null)
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
        table.AddColumn(new TableColumn("[green]Buy Book[/]").RightAligned());
        table.AddColumn(new TableColumn("[red]Sell Book[/]").RightAligned());

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
                < 1 => "greenyellow",
                < 1.25 => "mediumspringgreen",
                < 1.5 => "springgreen1",
                < 1.75 => "paleturquoise1",
                _ => "deepskyblue1"
            };

            table.AddRow(
                $"[{tierColor}]{product.Item.FriendlyName} {(!product.Item.Unstackable ? "x64" : "x1")}[/]",
                $"{product.Buy.UnitPrice:C2}",
                $"{product.Sell.UnitPrice?.ToString("C2") ?? "--"}",
                $"{product.OrderMeta.Margin:C2}",
                $"[{multiplierColor}]{product.OrderMeta.PotentialProfitMultiplier:N2}x[/]",
                $"[{buyPowerColor}]{product.OrderMeta.BuyOrderPower:N2}x[/]",
                $"{product.OrderMeta.TotalWeekVolume:N2}",
                $"{product.Buy.WeekVolume:N2}",
                $"{product.Sell.WeekVolume:N2}",
                $"O:{product.Buy.CurrentOrders.ToMetric(decimals: 0)} -> V:{product.Buy.CurrentVolume.ToMetric(decimals: 0)}",
                $"O:{product.Sell.CurrentOrders.ToMetric(decimals: 0)} -> V:{product.Sell.CurrentVolume.ToMetric(decimals: 0)}");
        }

        AnsiConsole.Status().Start("HyPixel Bazaar", ctx => AnsiConsole.Write(table));
    }

    private enum SortTypes
    {
        Name,
        Margin,
        TotalMovingVolume,
        SellMovingWeek,
        BuyMovingWeek,
        BuyPrice,
        SellPrice,
        PotentialProfitMultiplier,
        BuyOrderPower
    }
}
