using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VideoSubtitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VideoSubtitle",
                columns: table => new
                {
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VideoMediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    LastCueSeconds = table.Column<double>(type: "float", nullable: false),
                    ScannedSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ScannerVersion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ScannerProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SignatureVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ScannedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    AvailableUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoSubtitle", x => x.MediaAssetId);
                    table.ForeignKey(
                        name: "FK_VideoSubtitle_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VideoSubtitle_VideoFile_VideoMediaAssetId",
                        column: x => x.VideoMediaAssetId,
                        principalTable: "VideoFile",
                        principalColumn: "MediaAssetId");
                    table.ForeignKey(
                        name: "FK_VideoSubtitle_VideoUploadSession_UploadSessionId",
                        column: x => x.UploadSessionId,
                        principalTable: "VideoUploadSession",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoSubtitle_State_AvailableUtc",
                table: "VideoSubtitle",
                columns: new[] { "State", "AvailableUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VideoSubtitle_UploadSessionId",
                table: "VideoSubtitle",
                column: "UploadSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoSubtitle_VideoMediaAssetId",
                table: "VideoSubtitle",
                column: "VideoMediaAssetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoSubtitle");
        }
    }
}
