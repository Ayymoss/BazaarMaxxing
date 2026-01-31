using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BazaarCompanionWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddLastSeenAtToProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "EFProducts",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_EFProducts_LastSeenAt",
                table: "EFProducts",
                column: "LastSeenAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EFProducts_LastSeenAt",
                table: "EFProducts");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "EFProducts");
        }
    }
}
