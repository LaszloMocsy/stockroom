using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockroom.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSkuSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "sku_numbers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "sku_numbers");
        }
    }
}
