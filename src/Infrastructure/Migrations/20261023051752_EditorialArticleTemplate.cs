using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EditorialArticleTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Featured",
                table: "RevisionMetadata",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "RevisionMetadata",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Featured",
                table: "DraftMetadata",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "DraftMetadata",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Featured",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "Featured",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "DraftMetadata");
        }
    }
}
