using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InteractionModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InteractionChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmailHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    SessionHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ProtectedKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    Revoked = table.Column<bool>(type: "bit", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractionChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InteractionCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractionCommands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InteractionOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ProtectedMail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    AvailableUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractionOutbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InteractionPublicSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    AgencyAnswer = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ApprovedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractionPublicSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InteractionSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProtectedOriginal = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmailHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ReceiptHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PublicConsent = table.Column<bool>(type: "bit", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    AssigneeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ProtectedAnswer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublicSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeadlineUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractionSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InteractionSubmissions_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InteractionSubmissions_InteractionPublicSnapshots_PublicSnapshotId",
                        column: x => x.PublicSnapshotId,
                        principalTable: "InteractionPublicSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InteractionSubmissions_Revisions_PublishedRevisionId",
                        column: x => x.PublishedRevisionId,
                        principalTable: "Revisions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InteractionChallenges_EmailHash_CreatedUtc",
                table: "InteractionChallenges",
                columns: new[] { "EmailHash", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InteractionOutbox_EventKey",
                table: "InteractionOutbox",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InteractionOutbox_State_AvailableUtc",
                table: "InteractionOutbox",
                columns: new[] { "State", "AvailableUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InteractionPublicSnapshots_SubmissionId",
                table: "InteractionPublicSnapshots",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_InteractionSubmissions_ChallengeId",
                table: "InteractionSubmissions",
                column: "ChallengeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InteractionSubmissions_ContentId_PublicSnapshotId",
                table: "InteractionSubmissions",
                columns: new[] { "ContentId", "PublicSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_InteractionSubmissions_Kind_State_CreatedUtc",
                table: "InteractionSubmissions",
                columns: new[] { "Kind", "State", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InteractionSubmissions_PublicSnapshotId",
                table: "InteractionSubmissions",
                column: "PublicSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_InteractionSubmissions_PublishedRevisionId",
                table: "InteractionSubmissions",
                column: "PublishedRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_InteractionSubmissions_ReceiptHash",
                table: "InteractionSubmissions",
                column: "ReceiptHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InteractionPublicSnapshots_InteractionSubmissions_SubmissionId",
                table: "InteractionPublicSnapshots",
                column: "SubmissionId",
                principalTable: "InteractionSubmissions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InteractionPublicSnapshots_InteractionSubmissions_SubmissionId",
                table: "InteractionPublicSnapshots");

            migrationBuilder.DropTable(
                name: "InteractionChallenges");

            migrationBuilder.DropTable(
                name: "InteractionCommands");

            migrationBuilder.DropTable(
                name: "InteractionOutbox");

            migrationBuilder.DropTable(
                name: "InteractionSubmissions");

            migrationBuilder.DropTable(
                name: "InteractionPublicSnapshots");
        }
    }
}
