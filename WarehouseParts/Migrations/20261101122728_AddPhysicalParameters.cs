using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseParts.Migrations
{
    /// <inheritdoc />
    public partial class AddPhysicalParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Diameter",
                table: "Items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Thickness",
                table: "Items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Width",
                table: "Items",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Diameter",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Thickness",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "Items");
        }
    }
}
