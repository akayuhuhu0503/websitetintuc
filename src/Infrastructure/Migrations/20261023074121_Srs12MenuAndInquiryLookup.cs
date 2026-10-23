using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Srs12MenuAndInquiryLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "SiteMenuItem",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "InquiryLookupAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    SessionHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InquiryLookupAttempt", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InquiryLookupAttempt_ReceiptHash_CreatedUtc",
                table: "InquiryLookupAttempt",
                columns: new[] { "ReceiptHash", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InquiryLookupAttempt_SessionHash_CreatedUtc",
                table: "InquiryLookupAttempt",
                columns: new[] { "SessionHash", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InquiryLookupAttempt");

            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "SiteMenuItem");
        }
    }
}
