using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BazaarCompanionWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderBookSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EFOrderBookSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PriceLevel = table.Column<double>(type: "REAL", nullable: false),
                    BuyVolume = table.Column<int>(type: "INTEGER", nullable: false),
                    SellVolume = table.Column<int>(type: "INTEGER", nullable: false),
                    BuyOrderCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SellOrderCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFOrderBookSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EFOrderBookSnapshots_ProductKey_Timestamp",
                table: "EFOrderBookSnapshots",
                columns: new[] { "ProductKey", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_EFOrderBookSnapshots_Timestamp",
                table: "EFOrderBookSnapshots",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EFOrderBookSnapshots");
        }
    }
}
