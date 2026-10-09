using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockroom.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "ix_products_name_trgm",
                table: "products",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_products_sku_trgm",
                table: "products",
                column: "sku")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_product_barcodes_barcode_trgm",
                table: "product_barcodes",
                column: "barcode")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_name_trgm",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_products_sku_trgm",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_product_barcodes_barcode_trgm",
                table: "product_barcodes");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
