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
        var itemResponse = await hyPixelApi.GetItemsAsync();
        var bazaarResponse = await hyPixelApi.GetBazaarAsync();

        var products = bazaarResponse.Products.Values
            .Where(x => x.BuySummary.Count is not 0)
            .Where(x => x.SellSummary.Count is not 0)
            .Where(x => x.QuickStatus.SellMovingWeek > 35_000)
            .Where(x => x.QuickStatus.BuyMovingWeek > 35_000)
            .Join(itemResponse.Items, bazaar => bazaar.ProductId, item => item.Id, (bazaar, item) =>
            {
                var buy = bazaar.BuySummary.First();
                var sell = bazaar.SellSummary.First();

                var margin = buy.PricePerUnit - sell.PricePerUnit;
                var marginPercentage = 1 - sell.PricePerUnit / buy.PricePerUnit;
                var totalWeekVolume = bazaar.QuickStatus.SellMovingWeek + bazaar.QuickStatus.BuyMovingWeek;

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
                        Margin = margin,
                        MarginPercentage = marginPercentage,
                        TotalWeekVolume = totalWeekVolume
                    }
                };
            })
            .Where(x => x.OrderMeta.Margin > 100)
            .ToList();

        var currentSort = SortTypes.MarginPercentage;

        while (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.Clear();

            RenderTable(products, currentSort);

            var sortColumn = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Sort by which column? (or Quit)")
                    .AddChoices("Name", "Buy Price", "Sell Price", "Margin", "Margin %", "Total Week Vol.", "Sell Week Vol.",
                        "Buy Week Vol.", "Quit"));

            if (sortColumn == "Quit")
            {
                Environment.Exit(1);
                break;
            }

            currentSort = sortColumn switch
            {
                "Name" => SortTypes.Name,
                "Buy Price" => SortTypes.BuyPrice,
                "Sell Price" => SortTypes.SellPrice,
                "Margin" => SortTypes.Margin,
                "Margin %" => SortTypes.MarginPercentage,
                "Total Week Vol." => SortTypes.TotalMovingVolume,
                "Sell Week Vol." => SortTypes.SellMovingWeek,
                "Buy Week Vol." => SortTypes.BuyMovingWeek,
                _ => currentSort
            };
        }
    }

    private void RenderTable(List<ProductData> products, SortTypes currentSort)
    {
        var orderedProducts = currentSort switch
        {
            SortTypes.Name => products.OrderBy(x => x.Item.FriendlyName),
            SortTypes.BuyPrice => products.OrderByDescending(x => x.Buy.UnitPrice),
            SortTypes.SellPrice => products.OrderByDescending(x => x.Sell.UnitPrice),
            SortTypes.Margin => products.OrderByDescending(x => x.OrderMeta.Margin),
            SortTypes.MarginPercentage => products.OrderByDescending(x => x.OrderMeta.MarginPercentage),
            SortTypes.TotalMovingVolume => products.OrderByDescending(x => x.OrderMeta.TotalWeekVolume),
            SortTypes.SellMovingWeek => products.OrderByDescending(x => x.Sell.WeekVolume),
            SortTypes.BuyMovingWeek => products.OrderByDescending(x => x.Buy.WeekVolume),
            _ => throw new ArgumentOutOfRangeException(nameof(currentSort), currentSort, null)
        };

        var table = new Table()
            .Expand()
            .Title("[bold blue]Bazaar Orders[/]")
            .RoundedBorder()
            .BorderColor(Color.DarkCyan);

        table.AddColumn(new TableColumn("[u]Name[/]").LeftAligned().Width(20).NoWrap());
        table.AddColumn(new TableColumn("[bold green]Buy Price[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold red]Sell Price[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #FFA500]Margin[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #00BFFF]Margin %[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #FF6347]Total Week Vol.[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #DDA0DD]Buy Week Vol.[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold #90EE90]Sell Week Vol.[/]").RightAligned());

        foreach (var product in orderedProducts)
        {
            var color = product.Item.Tier switch
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

            table.AddRow(
                $"[{color}]{product.Item.FriendlyName}[/]",
                $"{product.Buy.UnitPrice:N2}",
                $"{product.Sell.UnitPrice:N2}",
                $"{product.OrderMeta.Margin:N1}",
                $"{product.OrderMeta.MarginPercentage:P1}",
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
        SellPrice
    }
}
