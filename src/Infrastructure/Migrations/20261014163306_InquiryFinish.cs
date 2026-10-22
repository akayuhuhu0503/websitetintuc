using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InquiryFinish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InquiryCalendarId",
                table: "InteractionSubmissions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InquiryDueUtc",
                table: "InteractionSubmissions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InquiryField",
                table: "InteractionSubmissions",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SlaIsOfficial",
                table: "InteractionSubmissions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SlaPolicySnapshot",
                table: "InteractionSubmissions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LookupChallengeId",
                table: "InteractionOutbox",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersedesId",
                table: "InteractionOutbox",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InquiryAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousAssigneeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AssigneeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProtectedReason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InquiryAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InquiryAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StorageKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProtectedName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Mime = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ScanState = table.Column<int>(type: "int", nullable: false),
                    ScanAttempts = table.Column<int>(type: "int", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AvailableUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ScanError = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ScannedSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    EngineVersion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ScannedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InquiryAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InquiryAttachments_InteractionSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "InteractionSubmissions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InquiryCalendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    WorkingDays = table.Column<int>(type: "int", nullable: false),
                    Cutoff = table.Column<TimeOnly>(type: "time", nullable: false),
                    CountReceiptDay = table.Column<bool>(type: "bit", nullable: false),
                    WeekendDays = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InquiryCalendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InquiryExtensions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousDueUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DueUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProtectedReason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InquiryExtensions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InquiryLookupChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    EmailHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ReceiptHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ProtectedKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    Revoked = table.Column<bool>(type: "bit", nullable: false),
                    ConsumedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InquiryLookupChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InquiryLookupSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    SessionHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InquiryLookupSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InquiryLookupSessions_InteractionSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "InteractionSubmissions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OutboxReconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResendOutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProtectedEvidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceiptSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    DuplicateRiskAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxReconciliations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InquiryCalendarExceptions",
                columns: table => new
                {
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    WorkingDay = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InquiryCalendarExceptions", x => new { x.CalendarId, x.Date });
                    table.ForeignKey(
                        name: "FK_InquiryCalendarExceptions_InquiryCalendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "InquiryCalendars",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InteractionSubmissions_InquiryCalendarId",
                table: "InteractionSubmissions",
                column: "InquiryCalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_InteractionSubmissions_Kind_AssigneeId_InquiryDueUtc",
                table: "InteractionSubmissions",
                columns: new[] { "Kind", "AssigneeId", "InquiryDueUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InquiryAssignments_SubmissionId_CreatedUtc",
                table: "InquiryAssignments",
                columns: new[] { "SubmissionId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InquiryAttachments_ScanState_AvailableUtc",
                table: "InquiryAttachments",
                columns: new[] { "ScanState", "AvailableUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InquiryAttachments_SessionHash_CreatedUtc",
                table: "InquiryAttachments",
                columns: new[] { "SessionHash", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InquiryAttachments_SubmissionId",
                table: "InquiryAttachments",
                column: "SubmissionId",
                unique: true,
                filter: "[SubmissionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InquiryCalendars_EffectiveFrom_CreatedUtc",
                table: "InquiryCalendars",
                columns: new[] { "EffectiveFrom", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InquiryExtensions_SubmissionId_CreatedUtc",
                table: "InquiryExtensions",
                columns: new[] { "SubmissionId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InquiryLookupChallenges_EmailHash_CreatedUtc",
                table: "InquiryLookupChallenges",
                columns: new[] { "EmailHash", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InquiryLookupChallenges_SessionHash_CreatedUtc",
                table: "InquiryLookupChallenges",
                columns: new[] { "SessionHash", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InquiryLookupSessions_ChallengeId",
                table: "InquiryLookupSessions",
                column: "ChallengeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InquiryLookupSessions_SubmissionId",
                table: "InquiryLookupSessions",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_InquiryLookupSessions_TokenHash",
                table: "InquiryLookupSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxReconciliations_OutboxId_CreatedUtc",
                table: "OutboxReconciliations",
                columns: new[] { "OutboxId", "CreatedUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_InteractionSubmissions_InquiryCalendars_InquiryCalendarId",
                table: "InteractionSubmissions",
                column: "InquiryCalendarId",
                principalTable: "InquiryCalendars",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InteractionSubmissions_InquiryCalendars_InquiryCalendarId",
                table: "InteractionSubmissions");

            migrationBuilder.DropTable(
                name: "InquiryAssignments");

            migrationBuilder.DropTable(
                name: "InquiryAttachments");

            migrationBuilder.DropTable(
                name: "InquiryCalendarExceptions");

            migrationBuilder.DropTable(
                name: "InquiryExtensions");

            migrationBuilder.DropTable(
                name: "InquiryLookupChallenges");

            migrationBuilder.DropTable(
                name: "InquiryLookupSessions");

            migrationBuilder.DropTable(
                name: "OutboxReconciliations");

            migrationBuilder.DropTable(
                name: "InquiryCalendars");

            migrationBuilder.DropIndex(
                name: "IX_InteractionSubmissions_InquiryCalendarId",
                table: "InteractionSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_InteractionSubmissions_Kind_AssigneeId_InquiryDueUtc",
                table: "InteractionSubmissions");

            migrationBuilder.DropColumn(
                name: "InquiryCalendarId",
                table: "InteractionSubmissions");

            migrationBuilder.DropColumn(
                name: "InquiryDueUtc",
                table: "InteractionSubmissions");

            migrationBuilder.DropColumn(
                name: "InquiryField",
                table: "InteractionSubmissions");

            migrationBuilder.DropColumn(
                name: "SlaIsOfficial",
                table: "InteractionSubmissions");

            migrationBuilder.DropColumn(
                name: "SlaPolicySnapshot",
                table: "InteractionSubmissions");

            migrationBuilder.DropColumn(
                name: "LookupChallengeId",
                table: "InteractionOutbox");

            migrationBuilder.DropColumn(
                name: "SupersedesId",
                table: "InteractionOutbox");
        }
    }
}
