-- SQL 1934 recovery inspection ONLY: run the WHOLE file in a NEW SSMS DBA window.
-- Do not run schema/role/recovery here. No customer rows, SQL text or passwords read.
IF @@TRANCOUNT<>0 THROW 51000,'Use a new diagnostic connection, not an open transaction.',1;
-- Bound metadata lock waits in this NEW diagnostic session only. Close the
-- window after inspection. Error1222 means stop/reconcile, never infer emptiness.
SET LOCK_TIMEOUT 5000;
-- READ ONLY. Open the complete file in SSMS, connect as DBA, then execute.
-- No SQLCMD mode required; no account/password data is selected.
USE [website_tin_tuc];
SET NOCOUNT ON;
IF DB_NAME() <> N'website_tin_tuc'
 OR COALESCE(CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),N'') <> N'WIN-S1IBJB84OOO'
BEGIN
 ;THROW 51000,'Wrong physical server/database. Stop.',1;
END;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION'),0)<>1
BEGIN
 ;THROW 51000,'Run with DBA metadata visibility; zero visible tables is not proof of emptiness.',1;
END;

SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)) AS PhysicalServer,
 DB_NAME() AS DatabaseName, ORIGINAL_LOGIN() AS OperatorLogin,
 CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ProductVersion,
 (SELECT compatibility_level FROM sys.databases WHERE name=DB_NAME()) AS Compatibility,
 CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)) AS DefaultBackupPath;

-- 70 includes dbo.__EFMigrationsHistory; no technical fixture tables expected.
SELECT COUNT(*) AS ActualUserTableCount,70 AS ExpectedUserTableCount,
 (SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped=0) AS ActualUserObjectCount,
 CASE WHEN COUNT(*)=0 THEN 'EMPTY_TABLE_SET_PENDING_DBA_REVIEW'
 ELSE 'EXISTING_SCHEMA_COMPARE_BEFORE_ANY_DDL' END AS Readiness
FROM sys.tables WHERE is_ms_shipped=0;

;WITH Expected(SchemaName,TableName) AS (SELECT * FROM (VALUES
 (N'dbo',N'AspNetRoleClaims'),
 (N'dbo',N'AspNetRoles'),
 (N'dbo',N'AspNetUserClaims'),
 (N'dbo',N'AspNetUserLogins'),
 (N'dbo',N'AspNetUserRoles'),
 (N'dbo',N'AspNetUserTokens'),
 (N'dbo',N'AspNetUsers'),
 (N'dbo',N'AuditLogs'),
 (N'dbo',N'Categories'),
 (N'dbo',N'ContentAssets'),
 (N'dbo',N'ContentCategories'),
 (N'dbo',N'ContentCommands'),
 (N'dbo',N'ContentDrafts'),
 (N'dbo',N'Contents'),
 (N'dbo',N'CrawlItems'),
 (N'dbo',N'CrawlRuns'),
 (N'dbo',N'DisclosureGroups'),
 (N'dbo',N'DraftAssets'),
 (N'dbo',N'DraftCategories'),
 (N'dbo',N'DraftDocuments'),
 (N'dbo',N'DraftMetadata'),
 (N'dbo',N'EditorialDocuments'),
 (N'dbo',N'ImportIssues'),
 (N'dbo',N'ImportRuns'),
 (N'dbo',N'InquiryAssignments'),
 (N'dbo',N'InquiryAttachments'),
 (N'dbo',N'InquiryCalendarExceptions'),
 (N'dbo',N'InquiryCalendars'),
 (N'dbo',N'InquiryExtensions'),
 (N'dbo',N'InquiryLookupAttempt'),
 (N'dbo',N'InquiryLookupChallenges'),
 (N'dbo',N'InquiryLookupSessions'),
 (N'dbo',N'InteractionChallenges'),
 (N'dbo',N'InteractionCommands'),
 (N'dbo',N'InteractionOutbox'),
 (N'dbo',N'InteractionPublicSnapshots'),
 (N'dbo',N'InteractionSubmissions'),
 (N'dbo',N'Jobs'),
 (N'dbo',N'LandStatisticsVersions'),
 (N'dbo',N'LegalDocuments'),
 (N'dbo',N'MediaAssets'),
 (N'dbo',N'OutboxReconciliations'),
 (N'dbo',N'PublicCaptchaAttempts'),
 (N'dbo',N'PublicCaptchaChallenges'),
 (N'dbo',N'PublicVisit'),
 (N'dbo',N'PublicationJobAttempts'),
 (N'dbo',N'PublicationJobs'),
 (N'dbo',N'RevisionCategories'),
 (N'dbo',N'RevisionMetadata'),
 (N'dbo',N'RevisionSearchProjection'),
 (N'dbo',N'Revisions'),
 (N'dbo',N'SiteBanner'),
 (N'dbo',N'SiteCategoryConfiguration'),
 (N'dbo',N'SiteConfiguration'),
 (N'dbo',N'SiteConfigurationRevision'),
 (N'dbo',N'SiteHomeBlock'),
 (N'dbo',N'SiteHomePin'),
 (N'dbo',N'SiteMenuItem'),
 (N'dbo',N'SiteSettings'),
 (N'dbo',N'SiteVocabulary'),
 (N'dbo',N'SourceRecords'),
 (N'dbo',N'SourceSnapshots'),
 (N'dbo',N'SourceUrlAliases'),
 (N'dbo',N'VideoChunk'),
 (N'dbo',N'VideoFile'),
 (N'dbo',N'VideoSubtitle'),
 (N'dbo',N'VideoUploadSession'),
 (N'dbo',N'WeeklyScheduleReleases'),
 (N'dbo',N'WorkerHeartbeats'),
 (N'dbo',N'__EFMigrationsHistory')
) v(SchemaName,TableName)),
Actual AS (SELECT SCHEMA_NAME(schema_id) AS SchemaName,name AS TableName
 FROM sys.tables WHERE is_ms_shipped=0)
SELECT CASE WHEN a.TableName IS NULL THEN 'MISSING' ELSE 'EXTRA' END AS Difference,
 COALESCE(e.SchemaName,a.SchemaName) AS SchemaName,COALESCE(e.TableName,a.TableName) AS TableName
FROM Expected e FULL JOIN Actual a ON a.SchemaName=e.SchemaName AND a.TableName=e.TableName
WHERE a.TableName IS NULL OR e.TableName IS NULL ORDER BY Difference,TableName;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL
 SELECT 'ABSENT' AS MigrationHistory,0 AS ActualMigrationCount,16 AS ExpectedMigrationCount;
ELSE
BEGIN
 EXEC(N'SELECT COUNT(*) AS ActualMigrationCount,16 AS ExpectedMigrationCount,
 MAX(MigrationId) AS LatestMigration FROM dbo.__EFMigrationsHistory;
 SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;');
 EXEC(N';WITH Expected(MigrationId) AS (SELECT * FROM (VALUES
 (N''20261012055549_InitialPortal''),
 (N''20261012083717_CmsRevisionWorkflow''),
 (N''20261013030300_PortalAdministration''),
 (N''20261014023001_V04LegalExpiry''),
 (N''20261014043339_InteractionModeration''),
 (N''20261014163306_InquiryFinish''),
 (N''20261015041226_VideoUploadPipeline''),
 (N''20261015044936_VideoSubtitles''),
 (N''20261015075352_SrsV05PublicFormsAndContent''),
 (N''20261015091551_V05EditorialDocuments''),
 (N''20261023051752_EditorialArticleTemplate''),
 (N''20261023055032_AnonymousPublicTraffic''),
 (N''20261023060018_Srs11SitePresentation''),
 (N''20261023074121_Srs12MenuAndInquiryLookup''),
 (N''20261026095148_LandAreaStatistics''),
 (N''20261027025057_AuditRecoveryLookup'')
) v(MigrationId))
 SELECT CASE WHEN a.MigrationId IS NULL THEN ''MISSING'' ELSE ''EXTRA'' END AS Difference,
 COALESCE(e.MigrationId,a.MigrationId) AS MigrationId
 FROM Expected e FULL JOIN dbo.__EFMigrationsHistory a ON a.MigrationId=e.MigrationId
 WHERE a.MigrationId IS NULL OR e.MigrationId IS NULL;');
END;

SELECT N'LandPortalRuntime' AS ApplicationRole,
 CASE WHEN DATABASE_PRINCIPAL_ID(N'LandPortalRuntime') IS NULL THEN 'ABSENT' ELSE 'EXISTS_DO_NOT_RESET' END AS RoleState;
SELECT name,type_desc FROM sys.database_principals WHERE name=N'LandPortalRuntime';
SELECT p.class_desc,p.state_desc,p.permission_name,
 CASE WHEN p.class=1 THEN OBJECT_SCHEMA_NAME(p.major_id) WHEN p.class=3 THEN SCHEMA_NAME(p.major_id) END AS SchemaName,
 CASE WHEN p.class=1 THEN OBJECT_NAME(p.major_id) END AS ObjectName
FROM sys.database_permissions p
WHERE p.grantee_principal_id=DATABASE_PRINCIPAL_ID(N'LandPortalRuntime')
ORDER BY p.class_desc,ObjectName,p.permission_name;

SELECT name AS AppMappedUser,type_desc FROM sys.database_principals WHERE name=N'website_tin_tuc_app';
SELECT r.name AS ExplicitAppDatabaseRole FROM sys.database_role_members m
JOIN sys.database_principals r ON r.principal_id=m.role_principal_id
WHERE m.member_principal_id=DATABASE_PRINCIPAL_ID(N'website_tin_tuc_app');
-- public is implicit even if the last result is empty.
SELECT type_desc,name FROM sys.objects WHERE is_ms_shipped=0 ORDER BY type_desc,name;

-- These settings/transaction counters belong to THIS diagnostic connection,
-- not the failed sqlcmd session. Do not infer its past state from these values.
SELECT SYSDATETIMEOFFSET() AS ObservedAt,@@SPID AS DiagnosticSpid,
 @@TRANCOUNT AS DiagnosticTranCount,XACT_STATE() AS DiagnosticXactState,
 SESSIONPROPERTY('QUOTED_IDENTIFIER') AS DiagnosticQuotedIdentifier,
 SESSIONPROPERTY('ANSI_NULLS') AS DiagnosticAnsiNulls,
 SESSIONPROPERTY('ANSI_PADDING') AS DiagnosticAnsiPadding,
 SESSIONPROPERTY('ANSI_WARNINGS') AS DiagnosticAnsiWarnings,
 SESSIONPROPERTY('ARITHABORT') AS DiagnosticArithabort,
 SESSIONPROPERTY('CONCAT_NULL_YIELDS_NULL') AS DiagnosticConcatNullYieldsNull,
 SESSIONPROPERTY('NUMERIC_ROUNDABORT') AS DiagnosticNumericRoundabort;

SELECT COUNT(*) AS ActualNamedIndexCount FROM sys.indexes i
JOIN sys.tables t ON t.object_id=i.object_id
WHERE t.is_ms_shipped=0 AND i.index_id>0 AND i.name IS NOT NULL;
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,t.name AS TableName,t.create_date,t.modify_date,
 i.name AS IndexName,i.type_desc,i.is_primary_key,i.is_unique,i.has_filter,i.filter_definition,
 i.is_disabled,i.is_hypothetical
FROM sys.tables t LEFT JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id>0
WHERE t.is_ms_shipped=0 ORDER BY SchemaName,TableName,IndexName;

-- Expected filtered indexes, including which migration would normally create
-- each one. Applied history does not prove every index survived the failed batch.
;WITH Expected(TableName,IndexName,MigrationId) AS (SELECT * FROM (VALUES
 (N'AspNetRoles',N'RoleNameIndex',N'20261012055549_InitialPortal'),
 (N'AspNetUsers',N'UserNameIndex',N'20261012055549_InitialPortal'),
 (N'PublicationJobs',N'IX_PublicationJobs_ContentId',N'20261013030300_PortalAdministration'),
 (N'InquiryAttachments',N'IX_InquiryAttachments_SubmissionId',N'20261014163306_InquiryFinish'),
 (N'LandStatisticsVersions',N'IX_LandStatisticsVersions_Year',N'20261026095148_LandAreaStatistics')
) v(TableName,IndexName,MigrationId))
SELECT e.*,CASE WHEN t.object_id IS NULL THEN 'TABLE_ABSENT'
 WHEN i.index_id IS NULL THEN 'INDEX_MISSING' ELSE 'INDEX_PRESENT_CHECK_DEFINITION' END AS ActualState,
 i.filter_definition,i.is_unique,i.is_disabled
FROM Expected e
LEFT JOIN sys.tables t ON t.schema_id=SCHEMA_ID(N'dbo') AND t.name=e.TableName
LEFT JOIN sys.indexes i ON i.object_id=t.object_id AND i.name=e.IndexName
ORDER BY e.MigrationId,e.TableName,e.IndexName;

SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,t.name AS TableName,c.column_id,c.name AS ColumnName,
 ty.name AS DataType,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_computed
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE t.is_ms_shipped=0 ORDER BY SchemaName,TableName,c.column_id;

-- Observe active transactions only in this database, if DBA has server DMV rights.
-- Never kill a session or roll back someone else's transaction.
IF IS_SRVROLEMEMBER(N'sysadmin')=1
 OR HAS_PERMS_BY_NAME(NULL,'SERVER','VIEW SERVER PERFORMANCE STATE')=1
 OR HAS_PERMS_BY_NAME(NULL,'SERVER','VIEW SERVER STATE')=1
BEGIN
BEGIN TRY
 SELECT d.transaction_id,d.database_transaction_begin_time,d.database_transaction_state,
 d.database_transaction_log_bytes_used,s.session_id,e.login_name,e.program_name,e.status,
 e.open_transaction_count
 FROM sys.dm_tran_database_transactions d
 LEFT JOIN sys.dm_tran_session_transactions s ON s.transaction_id=d.transaction_id
 LEFT JOIN sys.dm_exec_sessions e ON e.session_id=s.session_id
 WHERE d.database_id=DB_ID() AND s.session_id IS NOT NULL;
END TRY
BEGIN CATCH
 SELECT 'TRANSACTION_DMV_NOT_AVAILABLE_DO_NOT_ASSUME_NONE' AS TransactionObservation,ERROR_NUMBER() AS ErrorNumber;
END CATCH;
END
ELSE SELECT 'TRANSACTION_DMV_NOT_AUTHORIZED_DO_NOT_ASSUME_NONE' AS TransactionObservation;

-- System backup history READ ONLY, filtered to the target database. This does
-- not create a backup, verify/restore a backup, or write anything to msdb.
BEGIN TRY
 SELECT TOP(5) b.backup_set_id,b.backup_start_date,b.backup_finish_date,b.type,
 b.is_copy_only,b.has_backup_checksums,b.first_lsn,b.last_lsn,b.database_backup_lsn,
 f.physical_device_name
 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'website_tin_tuc' AND b.type='D'
 ORDER BY b.backup_finish_date DESC,b.backup_set_id DESC;
END TRY
BEGIN CATCH
 SELECT 'BACKUP_HISTORY_NOT_ACCESSIBLE_PROVIDE_ORIGINAL_SQLCMD_LOG' AS BackupObservation,ERROR_NUMBER() AS ErrorNumber;
END CATCH;
