using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SrsV05PublicFormsAndContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisclosureGroupId",
                table: "RevisionMetadata",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisclosureYear",
                table: "RevisionMetadata",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalBasisReference",
                table: "RevisionMetadata",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "RevisionMetadata",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvinceCode",
                table: "RevisionMetadata",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicationReviewEvidence",
                table: "RevisionMetadata",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicationSource",
                table: "RevisionMetadata",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeeklyChangeNote",
                table: "RevisionMetadata",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeeklyRowsJson",
                table: "RevisionMetadata",
                type: "nvarchar(max)",
                maxLength: 30000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeeklyWeek",
                table: "RevisionMetadata",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeeklyYear",
                table: "RevisionMetadata",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisclosureGroupId",
                table: "DraftMetadata",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisclosureYear",
                table: "DraftMetadata",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalBasisReference",
                table: "DraftMetadata",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "DraftMetadata",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvinceCode",
                table: "DraftMetadata",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicationReviewEvidence",
                table: "DraftMetadata",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicationSource",
                table: "DraftMetadata",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeeklyChangeNote",
                table: "DraftMetadata",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeeklyRowsJson",
                table: "DraftMetadata",
                type: "nvarchar(max)",
                maxLength: 30000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeeklyWeek",
                table: "DraftMetadata",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeeklyYear",
                table: "DraftMetadata",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DisclosureGroups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisclosureGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublicCaptchaAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddressHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicCaptchaAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublicCaptchaChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    AddressHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AnswerHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ProtectedKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicCaptchaChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyScheduleReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsoYear = table.Column<int>(type: "int", nullable: false),
                    IsoWeek = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    PublishedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublisherId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ChangeNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyScheduleReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeeklyScheduleReleases_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WeeklyScheduleReleases_Revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "Revisions",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "DisclosureGroups",
                columns: new[] { "Id", "Name", "Status" },
                values: new object[,]
                {
                    { "giay-chung-nhan-sai-hong-huy", "Giấy chứng nhận sai hỏng, hủy", "NameApproved" },
                    { "nguoi-nuoc-ngoai-dinh-cu-o-viet-nam", "Người nước ngoài định cư ở Việt Nam", "NeedsNameApproval" },
                    { "to-chuc-vi-pham-phap-luat-dat-dai", "Tổ chức vi phạm pháp luật đất đai", "NameApproved" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RevisionMetadata_DisclosureGroupId_ProvinceCode_DisclosureYear",
                table: "RevisionMetadata",
                columns: new[] { "DisclosureGroupId", "ProvinceCode", "DisclosureYear" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftMetadata_DisclosureGroupId_ProvinceCode_DisclosureYear",
                table: "DraftMetadata",
                columns: new[] { "DisclosureGroupId", "ProvinceCode", "DisclosureYear" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicCaptchaAttempts_AddressHash_CreatedUtc",
                table: "PublicCaptchaAttempts",
                columns: new[] { "AddressHash", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicCaptchaChallenges_AddressHash_CreatedUtc",
                table: "PublicCaptchaChallenges",
                columns: new[] { "AddressHash", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicCaptchaChallenges_ExpiresUtc",
                table: "PublicCaptchaChallenges",
                column: "ExpiresUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PublicCaptchaChallenges_SessionHash_CreatedUtc",
                table: "PublicCaptchaChallenges",
                columns: new[] { "SessionHash", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyScheduleReleases_ContentId",
                table: "WeeklyScheduleReleases",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyScheduleReleases_IsoYear_IsoWeek_Version",
                table: "WeeklyScheduleReleases",
                columns: new[] { "IsoYear", "IsoWeek", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyScheduleReleases_RevisionId",
                table: "WeeklyScheduleReleases",
                column: "RevisionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DisclosureGroups");

            migrationBuilder.DropTable(
                name: "PublicCaptchaAttempts");

            migrationBuilder.DropTable(
                name: "PublicCaptchaChallenges");

            migrationBuilder.DropTable(
                name: "WeeklyScheduleReleases");

            migrationBuilder.DropIndex(
                name: "IX_RevisionMetadata_DisclosureGroupId_ProvinceCode_DisclosureYear",
                table: "RevisionMetadata");

            migrationBuilder.DropIndex(
                name: "IX_DraftMetadata_DisclosureGroupId_ProvinceCode_DisclosureYear",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "DisclosureGroupId",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "DisclosureYear",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "LegalBasisReference",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "ProvinceCode",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "PublicationReviewEvidence",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "PublicationSource",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "WeeklyChangeNote",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "WeeklyRowsJson",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "WeeklyWeek",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "WeeklyYear",
                table: "RevisionMetadata");

            migrationBuilder.DropColumn(
                name: "DisclosureGroupId",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "DisclosureYear",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "LegalBasisReference",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "ProvinceCode",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "PublicationReviewEvidence",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "PublicationSource",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "WeeklyChangeNote",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "WeeklyRowsJson",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "WeeklyWeek",
                table: "DraftMetadata");

            migrationBuilder.DropColumn(
                name: "WeeklyYear",
                table: "DraftMetadata");
        }
    }
}
