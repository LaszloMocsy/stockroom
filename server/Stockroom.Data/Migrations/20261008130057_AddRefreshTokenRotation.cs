using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockroom.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "revoked_at",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "session_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Tokens issued before rotation existed were each a login of their own, so each is its own session.
            migrationBuilder.Sql("UPDATE refresh_tokens SET session_id = id;");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "used_at",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_session_id",
                table: "refresh_tokens",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_session_id",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "revoked_at",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "session_id",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "used_at",
                table: "refresh_tokens");
        }
    }
}
