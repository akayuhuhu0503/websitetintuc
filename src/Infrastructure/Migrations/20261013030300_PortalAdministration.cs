using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PortalAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                table: "Contents",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsDisabled",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DraftMetadata",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IssuingBody = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Signer = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IssuedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectivenessStatus = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    EffectivenessEvidence = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ConsultationStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ConsultationEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ConsultationStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsultationEndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsultationDateEvidence = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    MappingStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    MetadataOrigin = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftMetadata", x => x.ContentId);
                    table.ForeignKey(
                        name: "FK_DraftMetadata_ContentDrafts_ContentId",
                        column: x => x.ContentId,
                        principalTable: "ContentDrafts",
                        principalColumn: "ContentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PublicationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ReviewerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ApprovalHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Direct = table.Column<bool>(type: "bit", nullable: false),
                    DueUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextAttemptUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PublishCommandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublicationJobs_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PublicationJobs_Revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "Revisions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RevisionMetadata",
                columns: table => new
                {
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IssuingBody = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Signer = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IssuedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectivenessStatus = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    EffectivenessEvidence = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ConsultationStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ConsultationEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ConsultationStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsultationEndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsultationDateEvidence = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    MappingStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    MetadataOrigin = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevisionMetadata", x => x.RevisionId);
                    table.ForeignKey(
                        name: "FK_RevisionMetadata_Revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "Revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RevisionSearchProjection",
                columns: table => new
                {
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Authority = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevisionSearchProjection", x => x.RevisionId);
                    table.ForeignKey(
                        name: "FK_RevisionSearchProjection_Revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "Revisions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SiteConfigurationRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    EditedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OrganizationName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FooterText = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                    ContactAddress = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContactHotline = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContactEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    LogoAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteConfigurationRevision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteConfigurationRevision_MediaAssets_LogoAssetId",
                        column: x => x.LogoAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkerHeartbeats",
                columns: table => new
                {
                    WorkerId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerHeartbeats", x => x.WorkerId);
                });

            migrationBuilder.CreateTable(
                name: "PublicationJobAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkerId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationJobAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublicationJobAttempts_PublicationJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "PublicationJobs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SiteBanner",
                columns: table => new
                {
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Alt = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RouteKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ExternalUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    OpenNewWindow = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteBanner", x => new { x.RevisionId, x.Id });
                    table.ForeignKey(
                        name: "FK_SiteBanner_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SiteBanner_SiteConfigurationRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "SiteConfigurationRevision",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SiteCategoryConfiguration",
                columns: table => new
                {
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ParentCategoryId = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ShowInNavigation = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteCategoryConfiguration", x => new { x.RevisionId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_SiteCategoryConfiguration_SiteConfigurationRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "SiteConfigurationRevision",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SiteConfiguration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DraftRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LiveRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteConfiguration", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteConfiguration_SiteConfigurationRevision_DraftRevisionId",
                        column: x => x.DraftRevisionId,
                        principalTable: "SiteConfigurationRevision",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SiteConfiguration_SiteConfigurationRevision_LiveRevisionId",
                        column: x => x.LiveRevisionId,
                        principalTable: "SiteConfigurationRevision",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SiteHomeBlock",
                columns: table => new
                {
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Limit = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteHomeBlock", x => new { x.RevisionId, x.Id });
                    table.ForeignKey(
                        name: "FK_SiteHomeBlock_SiteConfigurationRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "SiteConfigurationRevision",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SiteHomePin",
                columns: table => new
                {
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteHomePin", x => new { x.RevisionId, x.BlockId, x.ContentId });
                    table.ForeignKey(
                        name: "FK_SiteHomePin_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SiteHomePin_SiteConfigurationRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "SiteConfigurationRevision",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SiteMenuItem",
                columns: table => new
                {
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Position = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TargetKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    ContentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RouteKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ExternalUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    OpenNewWindow = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    SystemProtected = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteMenuItem", x => new { x.RevisionId, x.Id });
                    table.ForeignKey(
                        name: "FK_SiteMenuItem_SiteConfigurationRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "SiteConfigurationRevision",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SiteVocabulary",
                columns: table => new
                {
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteVocabulary", x => new { x.RevisionId, x.Id });
                    table.ForeignKey(
                        name: "FK_SiteVocabulary_SiteConfigurationRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "SiteConfigurationRevision",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJobAttempts_JobId_LeaseToken",
                table: "PublicationJobAttempts",
                columns: new[] { "JobId", "LeaseToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJobs_ContentId",
                table: "PublicationJobs",
                column: "ContentId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJobs_IsActive_State_DueUtc_NextAttemptUtc",
                table: "PublicationJobs",
                columns: new[] { "IsActive", "State", "DueUtc", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJobs_PublishCommandId",
                table: "PublicationJobs",
                column: "PublishCommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJobs_RevisionId",
                table: "PublicationJobs",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_RevisionSearchProjection_Number",
                table: "RevisionSearchProjection",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_SiteBanner_MediaAssetId",
                table: "SiteBanner",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteConfiguration_DraftRevisionId",
                table: "SiteConfiguration",
                column: "DraftRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteConfiguration_LiveRevisionId",
                table: "SiteConfiguration",
                column: "LiveRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteConfigurationRevision_LogoAssetId",
                table: "SiteConfigurationRevision",
                column: "LogoAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteHomePin_ContentId",
                table: "SiteHomePin",
                column: "ContentId");

            // Snapshot the existing typed provenance without modifying any old payload/hash/date.
            migrationBuilder.Sql("""
                INSERT dbo.RevisionMetadata
                    (RevisionId,PublicationDate,Unit,Number,DocumentType,IssuingBody,Signer,Field,IssuedDate,EffectiveDate,EffectivenessStatus,EffectivenessEvidence,ConsultationStartDate,ConsultationEndDate,ConsultationStartUtc,ConsultationEndUtc,ConsultationDateEvidence,MappingStatus,MetadataOrigin)
                SELECT r.Id,NULL,N'',COALESCE(l.Number,N''),COALESCE(l.DocumentType,N''),COALESCE(l.IssuingBody,N''),COALESCE(l.Signer,N''),COALESCE(l.Field,N''),l.IssuedDate,l.EffectiveDate,l.EffectivenessStatus,
                    CASE WHEN l.EffectiveDate IS NOT NULL OR l.EffectivenessStatus IS NOT NULL THEN N'Legacy typed metadata retained; requires review' ELSE N'' END,
                    CAST(SWITCHOFFSET(d.ConsultationStartUtc,'+07:00') AS date),
                    DATEADD(day,-1,CAST(SWITCHOFFSET(d.ConsultationEndUtc,'+07:00') AS date)),
                    d.ConsultationStartUtc,d.ConsultationEndUtc,COALESCE(d.ConsultationDateEvidence,N''),N'Unmapped',N'LegacyContentMetadata'
                FROM dbo.Revisions r LEFT JOIN dbo.LegalDocuments l ON l.ContentId=r.ContentId LEFT JOIN dbo.DraftDocuments d ON d.ContentId=r.ContentId;
                INSERT dbo.DraftMetadata
                    (ContentId,PublicationDate,Unit,Number,DocumentType,IssuingBody,Signer,Field,IssuedDate,EffectiveDate,EffectivenessStatus,EffectivenessEvidence,ConsultationStartDate,ConsultationEndDate,ConsultationStartUtc,ConsultationEndUtc,ConsultationDateEvidence,MappingStatus,MetadataOrigin)
                SELECT d.ContentId,m.PublicationDate,m.Unit,m.Number,m.DocumentType,m.IssuingBody,m.Signer,m.Field,m.IssuedDate,m.EffectiveDate,m.EffectivenessStatus,m.EffectivenessEvidence,m.ConsultationStartDate,m.ConsultationEndDate,m.ConsultationStartUtc,m.ConsultationEndUtc,m.ConsultationDateEvidence,m.MappingStatus,m.MetadataOrigin
                FROM dbo.ContentDrafts d JOIN dbo.Contents c ON c.Id=d.ContentId JOIN dbo.RevisionMetadata m ON m.RevisionId=c.CurrentRevisionId;
                """);
            // SQL Server binds columns on existing tables before executing the
            // idempotent migration batch. Defer binding until AddColumn above
            // has run; preserve the backfill and EF transaction unchanged.
            migrationBuilder.Sql("""
                EXEC(N'
                UPDATE c SET ApprovedByUserId=(SELECT TOP(1) a.Actor FROM dbo.AuditLogs a JOIN dbo.AspNetUsers u ON u.Id=a.Actor WHERE a.ContentId=c.Id AND a.RevisionId=c.ApprovedRevisionId AND a.Action=N''Approve'' ORDER BY a.CreatedUtc DESC,a.Id DESC)
                FROM dbo.Contents c WHERE c.ApprovedRevisionId IS NOT NULL;
                ');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DraftMetadata");

            migrationBuilder.DropTable(
                name: "PublicationJobAttempts");

            migrationBuilder.DropTable(
                name: "RevisionMetadata");

            migrationBuilder.DropTable(
                name: "RevisionSearchProjection");

            migrationBuilder.DropTable(
                name: "SiteBanner");

            migrationBuilder.DropTable(
                name: "SiteCategoryConfiguration");

            migrationBuilder.DropTable(
                name: "SiteConfiguration");

            migrationBuilder.DropTable(
                name: "SiteHomeBlock");

            migrationBuilder.DropTable(
                name: "SiteHomePin");

            migrationBuilder.DropTable(
                name: "SiteMenuItem");

            migrationBuilder.DropTable(
                name: "SiteVocabulary");

            migrationBuilder.DropTable(
                name: "WorkerHeartbeats");

            migrationBuilder.DropTable(
                name: "PublicationJobs");

            migrationBuilder.DropTable(
                name: "SiteConfigurationRevision");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsDisabled",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AspNetUsers");
        }
    }
}
