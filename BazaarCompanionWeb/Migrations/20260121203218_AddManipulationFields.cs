using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BazaarCompanionWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddManipulationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManipulated",
                table: "EFProductMetas",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "ManipulationIntensity",
                table: "EFProductMetas",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PriceDeviationPercent",
                table: "EFProductMetas",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsManipulated",
                table: "EFProductMetas");

            migrationBuilder.DropColumn(
                name: "ManipulationIntensity",
                table: "EFProductMetas");

            migrationBuilder.DropColumn(
                name: "PriceDeviationPercent",
                table: "EFProductMetas");
        }
    }
}
