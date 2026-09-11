using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BazaarCompanionWeb.Migrations
{
    /// <inheritdoc />
    public partial class RealTradedVolume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EFPriceTicks_ProductKey_Timestamp",
                table: "EFPriceTicks");

            migrationBuilder.AddColumn<double>(
                name: "AskHigh",
                table: "EFPriceTicks",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "AskLow",
                table: "EFPriceTicks",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "AskOpen",
                table: "EFPriceTicks",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BidHigh",
                table: "EFPriceTicks",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BidLow",
                table: "EFPriceTicks",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BidOpen",
                table: "EFPriceTicks",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<long>(
                name: "TradedBuy",
                table: "EFPriceTicks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TradedSell",
                table: "EFPriceTicks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<double>(
                name: "AskHigh",
                table: "EFOhlcCandles",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "AskLow",
                table: "EFOhlcCandles",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "AskOpen",
                table: "EFOhlcCandles",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BuyVolume",
                table: "EFOhlcCandles",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "SellVolume",
                table: "EFOhlcCandles",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            // Existing ticks were single samples (one per product per ten-minute flush) with no open/high/low:
            // backfill them flat so the aggregator can treat every row as a bar. Traded volume stays zero —
            // the counters that derive it were never stored.
            migrationBuilder.Sql("""
                UPDATE "EFPriceTicks"
                SET "BidOpen" = "BidPrice", "BidHigh" = "BidPrice", "BidLow" = "BidPrice",
                    "AskOpen" = "AskPrice", "AskHigh" = "AskPrice", "AskLow" = "AskPrice";
                """);

            // Candle "volume" until now was resting order-book depth summed across samples — one to two
            // orders of magnitude above anything traded. Zero it rather than let it dwarf the real figures.
            // Ask open/high/low were never kept; flat at the close draws a dash at the ask level.
            migrationBuilder.Sql("""
                UPDATE "EFOhlcCandles"
                SET "Volume" = 0, "BuyVolume" = 0, "SellVolume" = 0,
                    "AskOpen" = "AskClose", "AskHigh" = "AskClose", "AskLow" = "AskClose";
                """);

            // Bars are keyed by (product, bucket) from here on. The old rows were per-sample timestamps, so
            // duplicates are not expected, but the unique index must not fail on a stray one.
            migrationBuilder.Sql("""
                DELETE FROM "EFPriceTicks" t
                USING "EFPriceTicks" d
                WHERE t."ProductKey" = d."ProductKey" AND t."Timestamp" = d."Timestamp" AND t."Id" < d."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_EFPriceTicks_ProductKey_Timestamp",
                table: "EFPriceTicks",
                columns: new[] { "ProductKey", "Timestamp" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EFPriceTicks_ProductKey_Timestamp",
                table: "EFPriceTicks");

            migrationBuilder.DropColumn(
                name: "AskHigh",
                table: "EFPriceTicks");

            migrationBuilder.DropColumn(
                name: "AskLow",
                table: "EFPriceTicks");

            migrationBuilder.DropColumn(
                name: "AskOpen",
                table: "EFPriceTicks");

            migrationBuilder.DropColumn(
                name: "BidHigh",
                table: "EFPriceTicks");

            migrationBuilder.DropColumn(
                name: "BidLow",
                table: "EFPriceTicks");

            migrationBuilder.DropColumn(
                name: "BidOpen",
                table: "EFPriceTicks");

            migrationBuilder.DropColumn(
                name: "TradedBuy",
                table: "EFPriceTicks");

            migrationBuilder.DropColumn(
                name: "TradedSell",
                table: "EFPriceTicks");

            migrationBuilder.DropColumn(
                name: "AskHigh",
                table: "EFOhlcCandles");

            migrationBuilder.DropColumn(
                name: "AskLow",
                table: "EFOhlcCandles");

            migrationBuilder.DropColumn(
                name: "AskOpen",
                table: "EFOhlcCandles");

            migrationBuilder.DropColumn(
                name: "BuyVolume",
                table: "EFOhlcCandles");

            migrationBuilder.DropColumn(
                name: "SellVolume",
                table: "EFOhlcCandles");

            migrationBuilder.CreateIndex(
                name: "IX_EFPriceTicks_ProductKey_Timestamp",
                table: "EFPriceTicks",
                columns: new[] { "ProductKey", "Timestamp" });
        }
    }
}
