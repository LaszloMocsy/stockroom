using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockroom.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductBarcodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_barcodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    barcode = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_barcodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_barcodes_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_barcodes_barcode",
                table: "product_barcodes",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_barcodes_product_id",
                table: "product_barcodes",
                column: "product_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_barcodes");
        }
    }
}
