using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BazaarCompanionWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddVolumeToOhlcCandle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BuyVolume",
                table: "EFPriceTicks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SellVolume",
                table: "EFPriceTicks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Volume",
                table: "EFOhlcCandles",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyVolume",
                table: "EFPriceTicks");

            migrationBuilder.DropColumn(
                name: "SellVolume",
                table: "EFPriceTicks");

            migrationBuilder.DropColumn(
                name: "Volume",
                table: "EFOhlcCandles");
        }
    }
}
