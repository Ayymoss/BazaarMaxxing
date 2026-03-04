using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BazaarCompanionWeb.Migrations
{
    /// <inheritdoc />
    public partial class ImprovedOppScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "EstimatedFillTimeHours",
                table: "EFProductMetas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "EstimatedProfitPerUnit",
                table: "EFProductMetas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "EstimatedTotalProfit",
                table: "EFProductMetas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RecommendationConfidence",
                table: "EFProductMetas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SuggestedAskPrice",
                table: "EFProductMetas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SuggestedBidPrice",
                table: "EFProductMetas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SuggestedBidVolume",
                table: "EFProductMetas",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedFillTimeHours",
                table: "EFProductMetas");

            migrationBuilder.DropColumn(
                name: "EstimatedProfitPerUnit",
                table: "EFProductMetas");

            migrationBuilder.DropColumn(
                name: "EstimatedTotalProfit",
                table: "EFProductMetas");

            migrationBuilder.DropColumn(
                name: "RecommendationConfidence",
                table: "EFProductMetas");

            migrationBuilder.DropColumn(
                name: "SuggestedAskPrice",
                table: "EFProductMetas");

            migrationBuilder.DropColumn(
                name: "SuggestedBidPrice",
                table: "EFProductMetas");

            migrationBuilder.DropColumn(
                name: "SuggestedBidVolume",
                table: "EFProductMetas");
        }
    }
}
