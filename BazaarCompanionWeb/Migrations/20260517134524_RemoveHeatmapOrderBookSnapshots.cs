using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BazaarCompanionWeb.Migrations
{
    /// <inheritdoc />
    public partial class RemoveHeatmapOrderBookSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EFOrderBookSnapshots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EFOrderBookSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AskOrderCount = table.Column<int>(type: "integer", nullable: false),
                    AskVolume = table.Column<int>(type: "integer", nullable: false),
                    BidOrderCount = table.Column<int>(type: "integer", nullable: false),
                    BidVolume = table.Column<int>(type: "integer", nullable: false),
                    PriceLevel = table.Column<double>(type: "double precision", nullable: false),
                    ProductKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
    }
}
