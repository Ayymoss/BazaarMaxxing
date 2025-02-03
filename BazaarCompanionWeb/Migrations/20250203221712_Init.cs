using System;
using Microsoft.EntityFrameworkCore.Migrations;

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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UnitPrice = table.Column<double>(type: "REAL", nullable: false),
                    OrderVolumeWeek = table.Column<double>(type: "REAL", nullable: false),
                    OrderVolume = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderCount = table.Column<int>(type: "INTEGER", nullable: false),
                    BookValue = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFMarketData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EFProducts",
                columns: table => new
                {
                    ProductKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FriendlyName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    Unstackable = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFProducts", x => x.ProductKey);
                });

            migrationBuilder.CreateTable(
                name: "EFBuyMarketData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFBuyMarketData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EFBuyMarketData_EFMarketData_Id",
                        column: x => x.Id,
                        principalTable: "EFMarketData",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EFBuyMarketData_EFProducts_ProductKey",
                        column: x => x.ProductKey,
                        principalTable: "EFProducts",
                        principalColumn: "ProductKey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EFPriceSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BuyUnitPrice = table.Column<double>(type: "REAL", nullable: false),
                    SellUnitPrice = table.Column<double>(type: "REAL", nullable: false),
                    Taken = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ProductKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
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
                name: "EFProductMetas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProfitMultiplier = table.Column<double>(type: "REAL", nullable: false),
                    Margin = table.Column<double>(type: "REAL", nullable: false),
                    TotalWeekVolume = table.Column<double>(type: "REAL", nullable: false),
                    FlipOpportunityScore = table.Column<double>(type: "REAL", nullable: false),
                    ProductKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
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

            migrationBuilder.CreateTable(
                name: "EFSellMarketData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFSellMarketData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EFSellMarketData_EFMarketData_Id",
                        column: x => x.Id,
                        principalTable: "EFMarketData",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EFSellMarketData_EFProducts_ProductKey",
                        column: x => x.ProductKey,
                        principalTable: "EFProducts",
                        principalColumn: "ProductKey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EFBuyMarketData_ProductKey",
                table: "EFBuyMarketData",
                column: "ProductKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EFPriceSnapshots_ProductKey",
                table: "EFPriceSnapshots",
                column: "ProductKey");

            migrationBuilder.CreateIndex(
                name: "IX_EFProductMetas_ProductKey",
                table: "EFProductMetas",
                column: "ProductKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EFSellMarketData_ProductKey",
                table: "EFSellMarketData",
                column: "ProductKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EFBuyMarketData");

            migrationBuilder.DropTable(
                name: "EFPriceSnapshots");

            migrationBuilder.DropTable(
                name: "EFProductMetas");

            migrationBuilder.DropTable(
                name: "EFSellMarketData");

            migrationBuilder.DropTable(
                name: "EFMarketData");

            migrationBuilder.DropTable(
                name: "EFProducts");
        }
    }
}
