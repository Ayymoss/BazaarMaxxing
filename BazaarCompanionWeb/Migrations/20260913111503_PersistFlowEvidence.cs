using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BazaarCompanionWeb.Migrations
{
    /// <inheritdoc />
    public partial class PersistFlowEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FlowEvidenceKnown",
                table: "EFPriceTicks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "EstimatedVolume",
                table: "EFOhlcCandles",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FlowEvidenceKnown",
                table: "EFPriceTicks");

            migrationBuilder.DropColumn(
                name: "EstimatedVolume",
                table: "EFOhlcCandles");
        }
    }
}
