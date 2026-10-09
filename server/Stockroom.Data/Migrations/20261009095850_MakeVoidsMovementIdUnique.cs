using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockroom.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeVoidsMovementIdUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_movements_voids_movement_id",
                table: "stock_movements");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_voids_movement_id",
                table: "stock_movements",
                column: "voids_movement_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_movements_voids_movement_id",
                table: "stock_movements");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_voids_movement_id",
                table: "stock_movements",
                column: "voids_movement_id");
        }
    }
}
