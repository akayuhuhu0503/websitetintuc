using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Infrastructure.Migrations;
public partial class HomeEventBanner : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Description", table: "SiteBanner", type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<bool>(name: "Enabled", table: "SiteBanner", type: "bit", nullable: false, defaultValue: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Description", table: "SiteBanner");
        migrationBuilder.DropColumn(name: "Enabled", table: "SiteBanner");
    }
}
