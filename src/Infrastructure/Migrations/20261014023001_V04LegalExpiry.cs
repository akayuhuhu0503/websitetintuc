using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V04LegalExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveUntilDate",
                table: "RevisionMetadata",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EffectivenessBoundary",
                table: "RevisionMetadata",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveUntilDate",
                table: "DraftMetadata",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EffectivenessBoundary",
                table: "DraftMetadata",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EffectiveUntilDate",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "EffectivenessBoundary",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "EffectiveUntilDate",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "EffectivenessBoundary",
                table: "DraftMetadata");
        }
    }
}
