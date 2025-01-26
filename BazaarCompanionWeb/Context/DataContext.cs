using BazaarCompanionWeb.Entities;
using Microsoft.EntityFrameworkCore;

namespace BazaarCompanionWeb.Context;

public class DataContext(DbContextOptions<DataContext> options) : DbContext(options)
{
    public DbSet<EFPriceSnapshot> PriceSnapshots { get; set; }
    public DbSet<EFProduct> Products { get; set; }
    public DbSet<EFOrder> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EFProduct>().ToTable("EFProducts");
        modelBuilder.Entity<EFProductMeta>().ToTable("EFProductMetas");
        modelBuilder.Entity<EFPriceSnapshot>().ToTable("EFPriceSnapshots");
        modelBuilder.Entity<EFOrder>().ToTable("EFOrders");

        modelBuilder.Entity<EFMarketData>(x =>
        {
            x.ToTable("EFMarketData");
            x.UseTptMappingStrategy();
        });
        modelBuilder.Entity<EFBuyMarketData>().ToTable("EFBuyMarketData");
        modelBuilder.Entity<EFSellMarketData>().ToTable("EFSellMarketData");

        base.OnModelCreating(modelBuilder);
    }
}
