﻿// <auto-generated />
using System;
using BazaarCompanionWeb.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace BazaarCompanionWeb.Migrations
{
    [DbContext(typeof(DataContext))]
    partial class DataContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "9.0.1");

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFMarketData", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("OrderCount")
                        .HasColumnType("INTEGER");

                    b.Property<int>("OrderVolume")
                        .HasColumnType("INTEGER");

                    b.Property<double>("OrderVolumeWeek")
                        .HasColumnType("REAL");

                    b.Property<double>("UnitPrice")
                        .HasColumnType("REAL");

                    b.HasKey("Id");

                    b.ToTable("EFMarketData", (string)null);

                    b.UseTptMappingStrategy();
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFOrder", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("Amount")
                        .HasColumnType("INTEGER");

                    b.Property<int>("MarketDataId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Orders")
                        .HasColumnType("INTEGER");

                    b.Property<double>("UnitPrice")
                        .HasColumnType("REAL");

                    b.HasKey("Id");

                    b.HasIndex("MarketDataId");

                    b.ToTable("EFOrders", (string)null);
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFPriceSnapshot", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<double>("BuyUnitPrice")
                        .HasColumnType("REAL");

                    b.Property<Guid>("ProductGuid")
                        .HasColumnType("TEXT");

                    b.Property<double>("SellUnitPrice")
                        .HasColumnType("REAL");

                    b.Property<DateTime>("Taken")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("ProductGuid");

                    b.ToTable("EFPriceSnapshots", (string)null);
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFProduct", b =>
                {
                    b.Property<Guid>("ProductGuid")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("FriendlyName")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Tier")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("Unstackable")
                        .HasColumnType("INTEGER");

                    b.HasKey("ProductGuid");

                    b.ToTable("EFProducts", (string)null);
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFProductMeta", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<double>("FlipOpportunityScore")
                        .HasColumnType("REAL");

                    b.Property<double>("Margin")
                        .HasColumnType("REAL");

                    b.Property<Guid>("ProductGuid")
                        .HasColumnType("TEXT");

                    b.Property<double>("ProfitMultiplier")
                        .HasColumnType("REAL");

                    b.Property<double>("TotalWeekVolume")
                        .HasColumnType("REAL");

                    b.HasKey("Id");

                    b.HasIndex("ProductGuid")
                        .IsUnique();

                    b.ToTable("EFProductMetas", (string)null);
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFBuyMarketData", b =>
                {
                    b.HasBaseType("BazaarCompanionWeb.Entities.EFMarketData");

                    b.Property<Guid>("ProductGuid")
                        .HasColumnType("TEXT");

                    b.HasIndex("ProductGuid")
                        .IsUnique();

                    b.ToTable("EFBuyMarketData", (string)null);
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFSellMarketData", b =>
                {
                    b.HasBaseType("BazaarCompanionWeb.Entities.EFMarketData");

                    b.Property<Guid>("ProductGuid")
                        .HasColumnType("TEXT");

                    b.HasIndex("ProductGuid")
                        .IsUnique();

                    b.ToTable("EFSellMarketData", (string)null);
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFOrder", b =>
                {
                    b.HasOne("BazaarCompanionWeb.Entities.EFMarketData", "MarketData")
                        .WithMany("Book")
                        .HasForeignKey("MarketDataId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("MarketData");
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFPriceSnapshot", b =>
                {
                    b.HasOne("BazaarCompanionWeb.Entities.EFProduct", "Product")
                        .WithMany("Snapshots")
                        .HasForeignKey("ProductGuid")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Product");
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFProductMeta", b =>
                {
                    b.HasOne("BazaarCompanionWeb.Entities.EFProduct", "Product")
                        .WithOne("Meta")
                        .HasForeignKey("BazaarCompanionWeb.Entities.EFProductMeta", "ProductGuid")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Product");
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFBuyMarketData", b =>
                {
                    b.HasOne("BazaarCompanionWeb.Entities.EFMarketData", null)
                        .WithOne()
                        .HasForeignKey("BazaarCompanionWeb.Entities.EFBuyMarketData", "Id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("BazaarCompanionWeb.Entities.EFProduct", "Product")
                        .WithOne("Buy")
                        .HasForeignKey("BazaarCompanionWeb.Entities.EFBuyMarketData", "ProductGuid")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Product");
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFSellMarketData", b =>
                {
                    b.HasOne("BazaarCompanionWeb.Entities.EFMarketData", null)
                        .WithOne()
                        .HasForeignKey("BazaarCompanionWeb.Entities.EFSellMarketData", "Id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("BazaarCompanionWeb.Entities.EFProduct", "Product")
                        .WithOne("Sell")
                        .HasForeignKey("BazaarCompanionWeb.Entities.EFSellMarketData", "ProductGuid")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Product");
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFMarketData", b =>
                {
                    b.Navigation("Book");
                });

            modelBuilder.Entity("BazaarCompanionWeb.Entities.EFProduct", b =>
                {
                    b.Navigation("Buy")
                        .IsRequired();

                    b.Navigation("Meta")
                        .IsRequired();

                    b.Navigation("Sell")
                        .IsRequired();

                    b.Navigation("Snapshots");
                });
#pragma warning restore 612, 618
        }
    }
}
