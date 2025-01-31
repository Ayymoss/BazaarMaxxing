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
                    OrderCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFMarketData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EFProducts",
                columns: table => new
                {
                    ProductGuid = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    FriendlyName = table.Column<string>(type: "TEXT", nullable: false),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    Unstackable = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFProducts", x => x.ProductGuid);
                });

            migrationBuilder.CreateTable(
                name: "EFOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Amount = table.Column<int>(type: "INTEGER", nullable: false),
                    Orders = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<double>(type: "REAL", nullable: false),
                    MarketDataId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EFOrders_EFMarketData_MarketDataId",
                        column: x => x.MarketDataId,
                        principalTable: "EFMarketData",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EFBuyMarketData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductGuid = table.Column<Guid>(type: "TEXT", nullable: false)
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
                        name: "FK_EFBuyMarketData_EFProducts_ProductGuid",
                        column: x => x.ProductGuid,
                        principalTable: "EFProducts",
                        principalColumn: "ProductGuid",
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
                    Taken = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProductGuid = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFPriceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EFPriceSnapshots_EFProducts_ProductGuid",
                        column: x => x.ProductGuid,
                        principalTable: "EFProducts",
                        principalColumn: "ProductGuid",
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
                    ProductGuid = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFProductMetas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EFProductMetas_EFProducts_ProductGuid",
                        column: x => x.ProductGuid,
                        principalTable: "EFProducts",
                        principalColumn: "ProductGuid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EFSellMarketData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductGuid = table.Column<Guid>(type: "TEXT", nullable: false)
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
                        name: "FK_EFSellMarketData_EFProducts_ProductGuid",
                        column: x => x.ProductGuid,
                        principalTable: "EFProducts",
                        principalColumn: "ProductGuid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EFBuyMarketData_ProductGuid",
                table: "EFBuyMarketData",
                column: "ProductGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EFOrders_MarketDataId",
                table: "EFOrders",
                column: "MarketDataId");

            migrationBuilder.CreateIndex(
                name: "IX_EFPriceSnapshots_ProductGuid",
                table: "EFPriceSnapshots",
                column: "ProductGuid");

            migrationBuilder.CreateIndex(
                name: "IX_EFProductMetas_ProductGuid",
                table: "EFProductMetas",
                column: "ProductGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EFSellMarketData_ProductGuid",
                table: "EFSellMarketData",
                column: "ProductGuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EFBuyMarketData");

            migrationBuilder.DropTable(
                name: "EFOrders");

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
