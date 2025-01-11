using BazaarCompanion.Interfaces;
using BazaarCompanion.Models;
using BazaarCompanion.Models.Api.Items;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Item = BazaarCompanion.Models.Item;

namespace BazaarCompanion;

public class AppEntry(IHyPixelApi hyPixelApi) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        new Thread(async () => await HandleDisplayAsync(cancellationToken)) { Name = nameof(AppEntry) }.Start();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task HandleDisplayAsync(CancellationToken cancellationToken)
    {
        var products = await FetchDataAsync();

        var currentSort = SortTypes.PotentialProfitMultiplier;

        while (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.Clear();

            RenderTable(products, currentSort);

            var sortColumn = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Sort by which column? (or Quit)")
                    .AddChoices("Name", "Buy Price", "Sell Price", "Margin", "Margin %", "Pot. Prof. Mult.", "Total Week Vol.",
                        "Sell Week Vol.", "Buy Week Vol.", "Update Data", "Quit"));

            switch (sortColumn)
            {
                case "Quit":
                    Environment.Exit(1);
                    break;
                case "Update Data":
                    products = await FetchDataAsync();
                    break;
            }

            currentSort = sortColumn switch
            {
                "Name" => SortTypes.Name,
                "Buy Price" => SortTypes.BuyPrice,
                "Sell Price" => SortTypes.SellPrice,
                "Margin" => SortTypes.Margin,
                "Margin %" => SortTypes.MarginPercentage,
                "Pot. Prof. Mult." => SortTypes.PotentialProfitMultiplier,
                "Total Week Vol." => SortTypes.TotalMovingVolume,
                "Sell Week Vol." => SortTypes.SellMovingWeek,
                "Buy Week Vol." => SortTypes.BuyMovingWeek,
                _ => currentSort
            };
        }
    }

    private async Task<List<ProductData>> FetchDataAsync()
    {
        var itemResponse = await hyPixelApi.GetItemsAsync();
        var bazaarResponse = await hyPixelApi.GetBazaarAsync();

        var products = bazaarResponse.Products.Values
            .Where(x => x.BuySummary.Count is not 0)
            .Where(x => x.SellSummary.Count is not 0)
            .Where(x => x.QuickStatus.SellMovingWeek > 50_000)
            .Where(x => x.QuickStatus.BuyMovingWeek > 50_000)
            .Join(itemResponse.Items, bazaar => bazaar.ProductId, item => item.Id, (bazaar, item) =>
            {
                var buy = bazaar.BuySummary.First();
                var sell = bazaar.SellSummary.First();

                var margin = buy.PricePerUnit - sell.PricePerUnit;
                var marginPercentage = 1 - sell.PricePerUnit / buy.PricePerUnit;
                var totalWeekVolume = bazaar.QuickStatus.SellMovingWeek + bazaar.QuickStatus.BuyMovingWeek;
                var potentialProfitMultiplier = buy.PricePerUnit / sell.PricePerUnit;

                return new ProductData
                {
                    ItemId = bazaar.ProductId,
                    Item = new Item
                    {
                        FriendlyName = item.Name,
                        Tier = item.Tier,
                        NpcSellPrice = item.NpcSellPrice,
                        Unstackable = item.Unstackable
                    },
                    Buy = new OrderInfo
                    {
                        UnitPrice = buy.PricePerUnit,
                        WeekVolume = bazaar.QuickStatus.BuyMovingWeek
                    },
                    Sell = new OrderInfo
                    {
                        UnitPrice = sell.PricePerUnit,
                        WeekVolume = bazaar.QuickStatus.SellMovingWeek
                    },
                    OrderMeta = new OrderMeta
                    {
                        PotentialProfitMultiplier = potentialProfitMultiplier,
                        Margin = margin,
                        MarginPercentage = marginPercentage,
                        TotalWeekVolume = totalWeekVolume
                    }
                };
            })
            .Where(x => x.OrderMeta.Margin > 100)
            .Where(x => x.OrderMeta.PotentialProfitMultiplier > 2)
            .ToList();
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
            SortTypes.MarginPercentage => products.OrderByDescending(x => x.OrderMeta.MarginPercentage),
            SortTypes.TotalMovingVolume => products.OrderByDescending(x => x.OrderMeta.TotalWeekVolume),
            SortTypes.PotentialProfitMultiplier => products.OrderByDescending(x => x.OrderMeta.PotentialProfitMultiplier),
            SortTypes.SellMovingWeek => products.OrderByDescending(x => x.Sell.WeekVolume),
            SortTypes.BuyMovingWeek => products.OrderByDescending(x => x.Buy.WeekVolume),
            _ => throw new ArgumentOutOfRangeException(nameof(currentSort), currentSort, null)
        };

        var table = new Table()
            .Expand()
            .Title("[bold blue]Bazaar Orders[/]")
            .RoundedBorder()
            .BorderColor(Color.DarkCyan);

        table.AddColumn(new TableColumn("[bold grey]Stackable?[/]").Centered());
        table.AddColumn(new TableColumn("[bold grey]NPC Sell Price[/]").RightAligned());
        table.AddColumn(new TableColumn("[u]Name[/]").LeftAligned().NoWrap());
        table.AddColumn(new TableColumn("[bold green]Buy Price[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold red]Sell Price[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #FFA500]Margin[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #00BFFF]Margin %[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #F9AD35]Pot. Prof. Mult.[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #FF6347]Total Week Vol.[/]").RightAligned().NoWrap());
        table.AddColumn(new TableColumn("[bold #DDA0DD]Buy Week Vol.[/]").RightAligned().NoWrap());
        table.AddColumn(new TableColumn("[bold #90EE90]Sell Week Vol.[/]").RightAligned().NoWrap());

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

            var marginColor = product.OrderMeta.MarginPercentage switch
            {
                < 0.1 => "grey",
                < 0.2 => "yellow1",
                < 0.3 => "greenyellow",
                < 0.4 => "mediumspringgreen",
                < 0.6 => "springgreen1",
                < 0.8 => "paleturquoise1",
                _ => "deepskyblue1"
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

            var stackable = !product.Item.Unstackable;

            table.AddRow(
                $"{(stackable ? "YES" : "NO")}",
                $"{(product.Item.NpcSellPrice.HasValue ? product.Item.NpcSellPrice.Value.ToString("C0") : "--")}",
                $"[{tierColor}]{product.Item.FriendlyName}[/]",
                $"{product.Buy.UnitPrice:C2}",
                $"{product.Sell.UnitPrice:C2}",
                $"{product.OrderMeta.Margin:C2}",
                $"[{marginColor}]{product.OrderMeta.MarginPercentage:P1}[/]",
                $"[{multiplierColor}]{product.OrderMeta.PotentialProfitMultiplier:N2}x[/]",
                $"{product.OrderMeta.TotalWeekVolume:N0}",
                $"{product.Buy.WeekVolume:N0}",
                $"{product.Sell.WeekVolume:N0}");
        }

        AnsiConsole.Status().Start("HyPixel Bazaar", ctx => AnsiConsole.Write(table));
    }

    private enum SortTypes
    {
        Name,
        Margin,
        MarginPercentage,
        TotalMovingVolume,
        SellMovingWeek,
        BuyMovingWeek,
        BuyPrice,
        SellPrice,
        PotentialProfitMultiplier
    }
}
