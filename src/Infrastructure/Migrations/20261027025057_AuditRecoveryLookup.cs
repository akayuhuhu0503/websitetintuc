using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditRecoveryLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stop instead of truncating historic audit identifiers on any target.
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE DATALENGTH(Actor)>900 OR DATALENGTH(Action)>400) THROW 51000, 'Audit identifiers exceed reviewed lookup bounds; no truncation permitted.', 1;");
            migrationBuilder.AlterColumn<string>(
                name: "Actor",
                table: "AuditLogs",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "AuditLogs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action_Actor",
                table: "AuditLogs",
                columns: new[] { "Action", "Actor" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Action_Actor",
                table: "AuditLogs");

            migrationBuilder.AlterColumn<string>(
                name: "Actor",
                table: "AuditLogs",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "AuditLogs",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);
        }
    }
}
