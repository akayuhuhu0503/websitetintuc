using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VideoUploadPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VideoUploadSession",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Mime = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChunkManifestSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ExpectedBytes = table.Column<long>(type: "bigint", nullable: false),
                    ChunkBytes = table.Column<int>(type: "int", nullable: false),
                    ChunkCount = table.Column<int>(type: "int", nullable: false),
                    ReceivedBytes = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoUploadSession", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoUploadSession_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VideoUploadSession_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VideoChunk",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Index = table.Column<int>(type: "int", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoChunk", x => new { x.SessionId, x.Index });
                    table.ForeignKey(
                        name: "FK_VideoChunk_VideoUploadSession_SessionId",
                        column: x => x.SessionId,
                        principalTable: "VideoUploadSession",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VideoFile",
                columns: table => new
                {
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    AvailableUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ScannedSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ScannerProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ScannerVersion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SignatureVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SignatureUpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ScannedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Mp4Key = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Mp4Sha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Mp4Bytes = table.Column<long>(type: "bigint", nullable: true),
                    PosterKey = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    PosterSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    PosterBytes = table.Column<long>(type: "bigint", nullable: true),
                    VideoCodec = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AudioCodec = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    DurationSeconds = table.Column<double>(type: "float", nullable: true),
                    ReadyUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoFile", x => x.MediaAssetId);
                    table.ForeignKey(
                        name: "FK_VideoFile_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VideoFile_VideoUploadSession_UploadSessionId",
                        column: x => x.UploadSessionId,
                        principalTable: "VideoUploadSession",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoFile_State_AvailableUtc",
                table: "VideoFile",
                columns: new[] { "State", "AvailableUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VideoFile_UploadSessionId",
                table: "VideoFile",
                column: "UploadSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoUploadSession_ContentId",
                table: "VideoUploadSession",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoUploadSession_MediaAssetId",
                table: "VideoUploadSession",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoUploadSession_OwnerUserId_State_ExpiresUtc",
                table: "VideoUploadSession",
                columns: new[] { "OwnerUserId", "State", "ExpiresUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoChunk");

            migrationBuilder.DropTable(
                name: "VideoFile");

            migrationBuilder.DropTable(
                name: "VideoUploadSession");
        }
    }
}
