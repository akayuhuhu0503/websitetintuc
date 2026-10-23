using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Srs11SitePresentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnglishName",
                table: "SiteConfigurationRevision",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseNumber",
                table: "SiteConfigurationRevision",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponsiblePerson",
                table: "SiteConfigurationRevision",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeoDescription",
                table: "SiteConfigurationRevision",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupervisingAgency",
                table: "SiteConfigurationRevision",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnglishName",
                table: "SiteConfigurationRevision");

            migrationBuilder.DropColumn(
                name: "LicenseNumber",
                table: "SiteConfigurationRevision");

            migrationBuilder.DropColumn(
                name: "ResponsiblePerson",
                table: "SiteConfigurationRevision");

            migrationBuilder.DropColumn(
                name: "SeoDescription",
                table: "SiteConfigurationRevision");

            migrationBuilder.DropColumn(
                name: "SupervisingAgency",
                table: "SiteConfigurationRevision");
        }
    }
}
