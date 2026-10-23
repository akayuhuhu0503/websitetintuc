using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AnonymousPublicTraffic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicVisit",
                columns: table => new
                {
                    SessionHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicVisit", x => x.SessionHash);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicVisit_LastSeenUtc",
                table: "PublicVisit",
                column: "LastSeenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PublicVisit_StartedUtc",
                table: "PublicVisit",
                column: "StartedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublicVisit");
        }
    }
}
