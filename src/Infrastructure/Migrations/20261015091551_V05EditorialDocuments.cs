using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V05EditorialDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WeeklyOriginalAssetId",
                table: "RevisionMetadata",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeeklyOriginalReviewEvidence",
                table: "RevisionMetadata",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WeeklyOriginalAssetId",
                table: "DraftMetadata",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeeklyOriginalReviewEvidence",
                table: "DraftMetadata",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EditorialDocuments",
                columns: table => new
                {
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ScanState = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    AvailableUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ScannedSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ScannedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EngineVersion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorialDocuments", x => x.MediaAssetId);
                    table.ForeignKey(
                        name: "FK_EditorialDocuments_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EditorialDocuments_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EditorialDocuments_ContentId",
                table: "EditorialDocuments",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_EditorialDocuments_ScanState_AvailableUtc",
                table: "EditorialDocuments",
                columns: new[] { "ScanState", "AvailableUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EditorialDocuments");

            migrationBuilder.DropColumn(
                name: "WeeklyOriginalAssetId",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "WeeklyOriginalReviewEvidence",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "WeeklyOriginalAssetId",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "WeeklyOriginalReviewEvidence",
                table: "DraftMetadata");
        }
    }
}
