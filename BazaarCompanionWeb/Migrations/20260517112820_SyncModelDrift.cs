using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BazaarCompanionWeb.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelDrift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EFOhlcAggregationStates",
                columns: table => new
                {
                    ProductKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Interval = table.Column<int>(type: "integer", nullable: false),
                    LastSeenPeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFOhlcAggregationStates", x => new { x.ProductKey, x.Interval });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EFOhlcAggregationStates");
        }
    }
}
