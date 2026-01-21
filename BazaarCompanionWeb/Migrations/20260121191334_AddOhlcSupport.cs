using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BazaarCompanionWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddOhlcSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EFOhlcCandles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Interval = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Open = table.Column<double>(type: "REAL", nullable: false),
                    High = table.Column<double>(type: "REAL", nullable: false),
                    Low = table.Column<double>(type: "REAL", nullable: false),
                    Close = table.Column<double>(type: "REAL", nullable: false)
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
                name: "EFPriceTicks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BuyPrice = table.Column<double>(type: "REAL", nullable: false),
                    SellPrice = table.Column<double>(type: "REAL", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_EFOhlcCandles_ProductKey_Interval_PeriodStart",
                table: "EFOhlcCandles",
                columns: new[] { "ProductKey", "Interval", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_EFPriceTicks_ProductKey_Timestamp",
                table: "EFPriceTicks",
                columns: new[] { "ProductKey", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EFOhlcCandles");

            migrationBuilder.DropTable(
                name: "EFPriceTicks");
        }
    }
}
