using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Repositories;
using BazaarCompanionWeb.Services.Ingestion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace BazaarCompanionWeb.Tests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BAZAAR_TEST_POSTGRES")))
            Skip = "Requires the isolated PostgreSQL database provided by CI.";
    }
}

public class PersistenceTests
{
    [PostgreSqlFact]
    public async Task Evidence_survives_repository_insert_update_binary_copy_and_candle_readback()
    {
        var connection = Environment.GetEnvironmentVariable("BAZAAR_TEST_POSTGRES")!;
        new NpgsqlConnectionStringBuilder(connection).Database.Should().StartWith("bazaar_audit_test");
        var factory = new PooledDbContextFactory<DataContext>(new DbContextOptionsBuilder<DataContext>()
            .UseNpgsql(connection).Options);
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        var products = new ProductRepository(factory, new BazaarSnapshotStore(), NullLogger<ProductRepository>.Instance);
        var product = FlipQuotingTests.Product(1000, 1500);
        product.Meta.EvidenceLimited = true;
        await products.UpdateOrAddProductsAsync([product], CancellationToken.None);
        (await db.Products.Include(p => p.Meta).AsNoTracking().SingleAsync()).Meta.EvidenceLimited.Should().BeTrue();
        product.Meta.EvidenceLimited = false;
        await products.UpdateOrAddProductsAsync([product], CancellationToken.None);
        (await db.Products.Include(p => p.Meta).AsNoTracking().SingleAsync()).Meta.EvidenceLimited.Should().BeFalse();

        var repository = new OhlcRepository(factory, NullLogger<OhlcRepository>.Instance);
        var at = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var tick = new EFPriceTick {
            ProductKey = product.ProductKey, Timestamp = at, BidPrice = 1000, AskPrice = 1500,
            BidVolume = 100, AskVolume = 200, TradedBuy = 50, TradedSell = 70,
            TradedEstimated = 30, FlowEvidenceKnown = true
        };
        await repository.CopyTicksAsync([tick]);
        tick.TradedEstimated = 45;
        await repository.CopyTicksAsync([tick]);
        var saved = await db.PriceTicks.AsNoTracking().SingleAsync();
        saved.TradedEstimated.Should().Be(45);
        saved.FlowEvidenceKnown.Should().BeTrue();
        var candle = new EFOhlcCandle {
            ProductKey = product.ProductKey, Interval = CandleInterval.OneHour, PeriodStart = at,
            Open = 1000, High = 1100, Low = 900, Close = 1050, Spread = 500, Volume = 120,
            BuyVolume = 50, SellVolume = 70, EstimatedVolume = 45
        };
        await repository.SaveCandlesAsync([candle]);
        (await repository.GetCandlesAsync(product.ProductKey, CandleInterval.OneHour)).Single().EstimatedVolume.Should().Be(45);
        candle.EstimatedVolume = null;
        await repository.SaveCandlesAsync([candle]);
        (await repository.GetCandlesAsync(product.ProductKey, CandleInterval.OneHour)).Single().EstimatedVolume.Should().BeNull();
    }
}
