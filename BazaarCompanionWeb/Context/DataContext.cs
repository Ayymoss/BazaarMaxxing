using BazaarCompanionWeb.Entities;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Context;

public class DataContext(DbContextOptions<DataContext> options) : DbContext(options)
{
    public DbSet<EFPriceSnapshot> PriceSnapshots { get; set; }
    public DbSet<EFProduct> Products { get; set; }
    public DbSet<EFMarketData> MarketData { get; set; }
    public DbSet<EFProductMeta> Metas { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EFPriceSnapshot>().ToTable("EFPriceSnapshots");
        modelBuilder.Entity<EFProduct>().ToTable("EFProducts");
        modelBuilder.Entity<EFMarketData>().ToTable("EFMarketData");
        modelBuilder.Entity<EFProductMeta>().ToTable("EFProductMetas");

        base.OnModelCreating(modelBuilder);
    }
}
