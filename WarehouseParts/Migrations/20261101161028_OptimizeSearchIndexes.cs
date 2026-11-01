using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseParts.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PartNumbers_NumberNormalized",
                table: "PartNumbers",
                column: "NumberNormalized");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PartNumbers_NumberNormalized",
                table: "PartNumbers");
        }
    }
}
