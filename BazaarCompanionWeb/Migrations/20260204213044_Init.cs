using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BazaarCompanionWeb.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EFMarketData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UnitPrice = table.Column<double>(type: "double precision", nullable: false),
                    OrderVolumeWeek = table.Column<double>(type: "double precision", nullable: false),
                    OrderVolume = table.Column<int>(type: "integer", nullable: false),
                    OrderCount = table.Column<int>(type: "integer", nullable: false),
                    BookValue = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFMarketData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EFOrderBookSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PriceLevel = table.Column<double>(type: "double precision", nullable: false),
                    BidVolume = table.Column<int>(type: "integer", nullable: false),
                    AskVolume = table.Column<int>(type: "integer", nullable: false),
                    BidOrderCount = table.Column<int>(type: "integer", nullable: false),
                    AskOrderCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFOrderBookSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EFProducts",
                columns: table => new
                {
                    ProductKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FriendlyName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    Unstackable = table.Column<bool>(type: "boolean", nullable: false),
                    SkinUrl = table.Column<string>(type: "text", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFProducts", x => x.ProductKey);
                });

            migrationBuilder.CreateTable(
                name: "EFAskMarketData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    ProductKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFAskMarketData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EFAskMarketData_EFMarketData_Id",
                        column: x => x.Id,
                        principalTable: "EFMarketData",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EFAskMarketData_EFProducts_ProductKey",
                        column: x => x.ProductKey,
                        principalTable: "EFProducts",
                        principalColumn: "ProductKey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EFBidMarketData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    ProductKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFBidMarketData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EFBidMarketData_EFMarketData_Id",
                        column: x => x.Id,
                        principalTable: "EFMarketData",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EFBidMarketData_EFProducts_ProductKey",
                        column: x => x.ProductKey,
                        principalTable: "EFProducts",
                        principalColumn: "ProductKey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EFOhlcCandles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Interval = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Open = table.Column<double>(type: "double precision", nullable: false),
                    High = table.Column<double>(type: "double precision", nullable: false),
                    Low = table.Column<double>(type: "double precision", nullable: false),
                    Close = table.Column<double>(type: "double precision", nullable: false),
                    Volume = table.Column<double>(type: "double precision", nullable: false),
                    Spread = table.Column<double>(type: "double precision", nullable: false),
                    AskClose = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFOhlcCandles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EFOhlcCandles_EFProducts_ProductKey",
                        column: x => x.ProductKey,
                        principalTable: "EFProducts",
                        principalColumn: "ProductKey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EFPriceSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BidUnitPrice = table.Column<double>(type: "double precision", nullable: false),
                    AskUnitPrice = table.Column<double>(type: "double precision", nullable: false),
                    Taken = table.Column<DateOnly>(type: "date", nullable: false),
                    ProductKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFPriceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EFPriceSnapshots_EFProducts_ProductKey",
                        column: x => x.ProductKey,
                        principalTable: "EFProducts",
                        principalColumn: "ProductKey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EFPriceTicks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BidPrice = table.Column<double>(type: "double precision", nullable: false),
                    AskPrice = table.Column<double>(type: "double precision", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BidVolume = table.Column<long>(type: "bigint", nullable: false),
                    AskVolume = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFPriceTicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EFPriceTicks_EFProducts_ProductKey",
                        column: x => x.ProductKey,
                        principalTable: "EFProducts",
                        principalColumn: "ProductKey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EFProductMetas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProfitMultiplier = table.Column<double>(type: "double precision", nullable: false),
                    Spread = table.Column<double>(type: "double precision", nullable: false),
                    TotalWeekVolume = table.Column<double>(type: "double precision", nullable: false),
                    FlipOpportunityScore = table.Column<double>(type: "double precision", nullable: false),
                    IsManipulated = table.Column<bool>(type: "boolean", nullable: false),
                    ManipulationIntensity = table.Column<double>(type: "double precision", nullable: false),
                    PriceDeviationPercent = table.Column<double>(type: "double precision", nullable: false),
                    ProductKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFProductMetas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EFProductMetas_EFProducts_ProductKey",
                        column: x => x.ProductKey,
                        principalTable: "EFProducts",
                        principalColumn: "ProductKey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EFAskMarketData_ProductKey",
                table: "EFAskMarketData",
                column: "ProductKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EFBidMarketData_ProductKey",
                table: "EFBidMarketData",
                column: "ProductKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EFOhlcCandles_Interval_PeriodStart",
                table: "EFOhlcCandles",
                columns: new[] { "Interval", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_EFOhlcCandles_ProductKey_Interval_PeriodStart",
                table: "EFOhlcCandles",
                columns: new[] { "ProductKey", "Interval", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EFOrderBookSnapshots_ProductKey_Timestamp",
                table: "EFOrderBookSnapshots",
                columns: new[] { "ProductKey", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_EFOrderBookSnapshots_Timestamp",
                table: "EFOrderBookSnapshots",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_EFPriceSnapshots_ProductKey",
                table: "EFPriceSnapshots",
                column: "ProductKey");

            migrationBuilder.CreateIndex(
                name: "IX_EFPriceTicks_ProductKey_Timestamp",
                table: "EFPriceTicks",
                columns: new[] { "ProductKey", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_EFProductMetas_ProductKey",
                table: "EFProductMetas",
                column: "ProductKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EFProducts_LastSeenAt",
                table: "EFProducts",
                column: "LastSeenAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EFAskMarketData");

            migrationBuilder.DropTable(
                name: "EFBidMarketData");

            migrationBuilder.DropTable(
                name: "EFOhlcCandles");

            migrationBuilder.DropTable(
                name: "EFOrderBookSnapshots");

            migrationBuilder.DropTable(
                name: "EFPriceSnapshots");

            migrationBuilder.DropTable(
                name: "EFPriceTicks");

            migrationBuilder.DropTable(
                name: "EFProductMetas");

            migrationBuilder.DropTable(
                name: "EFMarketData");

            migrationBuilder.DropTable(
                name: "EFProducts");
        }
    }
}
