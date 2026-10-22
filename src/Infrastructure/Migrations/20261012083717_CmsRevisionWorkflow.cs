using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CmsRevisionWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CoverAssetId",
                table: "Revisions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrimaryCategoryId",
                table: "Revisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "MediaAssets",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Source");

            migrationBuilder.AddColumn<string>(
                name: "UploadedByUserId",
                table: "MediaAssets",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationStatus",
                table: "MediaAssets",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "SourceValidated");

            migrationBuilder.AddColumn<string>(
                name: "ApprovalHash",
                table: "Contents",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedRevisionId",
                table: "Contents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EditGeneration",
                table: "Contents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Contents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTrashed",
                table: "Contents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedRevisionId",
                table: "Contents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContentCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ResultRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentCommands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentDrafts",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SearchText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Author = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceCredit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrimaryCategoryId = table.Column<int>(type: "int", nullable: true),
                    CoverAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentDrafts", x => x.ContentId);
                    table.ForeignKey(
                        name: "FK_ContentDrafts_Categories_PrimaryCategoryId",
                        column: x => x.PrimaryCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContentDrafts_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentDrafts_MediaAssets_CoverAssetId",
                        column: x => x.CoverAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RevisionCategories",
                columns: table => new
                {
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevisionCategories", x => new { x.RevisionId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_RevisionCategories_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RevisionCategories_Revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "Revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DraftAssets",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Usage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftAssets", x => new { x.ContentId, x.MediaAssetId });
                    table.ForeignKey(
                        name: "FK_DraftAssets_ContentDrafts_ContentId",
                        column: x => x.ContentId,
                        principalTable: "ContentDrafts",
                        principalColumn: "ContentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DraftAssets_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DraftCategories",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftCategories", x => new { x.ContentId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_DraftCategories_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DraftCategories_ContentDrafts_ContentId",
                        column: x => x.ContentId,
                        principalTable: "ContentDrafts",
                        principalColumn: "ContentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Revisions_CoverAssetId",
                table: "Revisions",
                column: "CoverAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Revisions_PrimaryCategoryId",
                table: "Revisions",
                column: "PrimaryCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Contents_ApprovedRevisionId",
                table: "Contents",
                column: "ApprovedRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Contents_SubmittedRevisionId",
                table: "Contents",
                column: "SubmittedRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentCommands_ContentId_CreatedUtc",
                table: "ContentCommands",
                columns: new[] { "ContentId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentDrafts_CoverAssetId",
                table: "ContentDrafts",
                column: "CoverAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentDrafts_PrimaryCategoryId",
                table: "ContentDrafts",
                column: "PrimaryCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_DraftAssets_MediaAssetId",
                table: "DraftAssets",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DraftCategories_CategoryId",
                table: "DraftCategories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RevisionCategories_CategoryId",
                table: "RevisionCategories",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Contents_Revisions_ApprovedRevisionId",
                table: "Contents",
                column: "ApprovedRevisionId",
                principalTable: "Revisions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Contents_Revisions_SubmittedRevisionId",
                table: "Contents",
                column: "SubmittedRevisionId",
                principalTable: "Revisions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Revisions_Categories_PrimaryCategoryId",
                table: "Revisions",
                column: "PrimaryCategoryId",
                principalTable: "Categories",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Revisions_MediaAssets_CoverAssetId",
                table: "Revisions",
                column: "CoverAssetId",
                principalTable: "MediaAssets",
                principalColumn: "Id");

            // Additive backfill only: original text, identifiers, timestamps, files and
            // published pointers stay unchanged. Dynamic SQL also supports reviewed
            // single-batch application when columns were introduced in this migration.
            migrationBuilder.Sql("""
                EXEC(N'
                UPDATE dbo.Contents SET IsPublic=CASE WHEN PublishedRevisionId IS NOT NULL AND State NOT IN (4,5) THEN 1 ELSE 0 END,
                    IsTrashed=CASE WHEN State=5 THEN 1 ELSE 0 END;
                UPDATE r SET PrimaryCategoryId=c.PrimaryCategoryId, CoverAssetId=cover.MediaAssetId
                FROM dbo.Revisions r JOIN dbo.Contents c ON c.Id=r.ContentId
                OUTER APPLY (SELECT TOP (1) ca.MediaAssetId
                    FROM dbo.ContentAssets ca JOIN dbo.MediaAssets m ON m.Id=ca.MediaAssetId
                    WHERE ca.RevisionId=r.Id AND m.MimeType LIKE ''image/%'' AND m.IsQuarantined=0
                    ORDER BY CASE WHEN ca.Usage IN (''Thumbnail'',''Cover'') THEN 0 ELSE 1 END, ca.SortOrder) cover;
                INSERT dbo.RevisionCategories (RevisionId, CategoryId)
                    SELECT r.Id, cc.CategoryId FROM dbo.Revisions r JOIN dbo.ContentCategories cc ON cc.ContentId=r.ContentId;
                INSERT dbo.RevisionCategories (RevisionId, CategoryId)
                    SELECT r.Id,r.PrimaryCategoryId FROM dbo.Revisions r WHERE r.PrimaryCategoryId IS NOT NULL
                    AND NOT EXISTS(SELECT 1 FROM dbo.RevisionCategories rc WHERE rc.RevisionId=r.Id AND rc.CategoryId=r.PrimaryCategoryId);
                INSERT dbo.ContentDrafts (ContentId,Title,Summary,BodyHtml,SearchText,Author,SourceCredit,PrimaryCategoryId,CoverAssetId,UpdatedUtc,UpdatedByUserId)
                    SELECT c.Id,r.Title,r.Summary,r.BodyHtml,r.SearchText,r.Author,r.SourceCredit,r.PrimaryCategoryId,r.CoverAssetId,r.CreatedUtc,COALESCE(r.EditedByUserId,c.OwnerUserId,'''')
                    FROM dbo.Contents c JOIN dbo.Revisions r ON r.Id=c.CurrentRevisionId WHERE c.State IN (0,3);
                INSERT dbo.DraftCategories (ContentId,CategoryId)
                    SELECT d.ContentId,rc.CategoryId FROM dbo.ContentDrafts d JOIN dbo.Contents c ON c.Id=d.ContentId
                    JOIN dbo.RevisionCategories rc ON rc.RevisionId=c.CurrentRevisionId;
                INSERT dbo.DraftAssets (ContentId,MediaAssetId,Usage,Caption,SortOrder)
                    SELECT d.ContentId,ca.MediaAssetId,ca.Usage,ca.Caption,ca.SortOrder FROM dbo.ContentDrafts d
                    JOIN dbo.Contents c ON c.Id=d.ContentId JOIN dbo.ContentAssets ca ON ca.RevisionId=c.CurrentRevisionId;
                ');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contents_Revisions_ApprovedRevisionId",
                table: "Contents");

            migrationBuilder.DropForeignKey(
                name: "FK_Contents_Revisions_SubmittedRevisionId",
                table: "Contents");

            migrationBuilder.DropForeignKey(
                name: "FK_Revisions_Categories_PrimaryCategoryId",
                table: "Revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_Revisions_MediaAssets_CoverAssetId",
                table: "Revisions");

            migrationBuilder.DropTable(
                name: "ContentCommands");

            migrationBuilder.DropTable(
                name: "DraftAssets");

            migrationBuilder.DropTable(
                name: "DraftCategories");

            migrationBuilder.DropTable(
                name: "RevisionCategories");

            migrationBuilder.DropTable(
                name: "ContentDrafts");

            migrationBuilder.DropIndex(
                name: "IX_Revisions_CoverAssetId",
                table: "Revisions");

            migrationBuilder.DropIndex(
                name: "IX_Revisions_PrimaryCategoryId",
                table: "Revisions");

            migrationBuilder.DropIndex(
                name: "IX_Contents_ApprovedRevisionId",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_SubmittedRevisionId",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "CoverAssetId",
                table: "Revisions");

            migrationBuilder.DropColumn(
                name: "PrimaryCategoryId",
                table: "Revisions");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "UploadedByUserId",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "ValidationStatus",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "ApprovalHash",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "ApprovedRevisionId",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "EditGeneration",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "IsTrashed",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "SubmittedRevisionId",
                table: "Contents");
        }
    }
}
