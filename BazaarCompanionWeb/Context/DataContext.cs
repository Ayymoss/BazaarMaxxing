using BazaarCompanionWeb.Entities;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Context;

public class DataContext(DbContextOptions<DataContext> options) : DbContext(options)
{
    public DbSet<EFPriceSnapshot> PriceSnapshots { get; set; }
    public DbSet<EFPriceTick> PriceTicks { get; set; }
    public DbSet<EFOhlcCandle> OhlcCandles { get; set; }
    public DbSet<EFProduct> Products { get; set; }
    public DbSet<EFMarketData> MarketData { get; set; }
    public DbSet<EFOrderBookSnapshot> OrderBookSnapshots { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EFProduct>().ToTable("EFProducts");
        modelBuilder.Entity<EFProductMeta>().ToTable("EFProductMetas");
        modelBuilder.Entity<EFPriceSnapshot>().ToTable("EFPriceSnapshots");
        modelBuilder.Entity<EFPriceTick>().ToTable("EFPriceTicks");
        modelBuilder.Entity<EFOhlcCandle>().ToTable("EFOhlcCandles");

        modelBuilder.Entity<EFMarketData>(x =>
        {
            x.ToTable("EFMarketData");
            x.UseTptMappingStrategy();
        });
        modelBuilder.Entity<EFBidMarketData>().ToTable("EFBidMarketData");
        modelBuilder.Entity<EFAskMarketData>().ToTable("EFAskMarketData");

        modelBuilder.Entity<EFOrderBookSnapshot>(x =>
        {
            x.ToTable("EFOrderBookSnapshots");
            x.HasIndex(e => new { e.ProductKey, e.Timestamp });
            x.HasIndex(e => e.Timestamp); // For cleanup queries
        });

        base.OnModelCreating(modelBuilder);
    }
}
