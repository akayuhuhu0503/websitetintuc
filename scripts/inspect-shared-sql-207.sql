-- READ ONLY: run the WHOLE file in a NEW SSMS DBA connection.
-- No schema retry, backup, restore, permissions or data changes.
USE [website_tin_tuc];
SET NOCOUNT ON;
SET LOCK_TIMEOUT 5000; -- diagnostic session only; close window afterwards
IF DB_NAME() COLLATE Latin1_General_100_BIN2<>N'website_tin_tuc'
 OR COALESCE(CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),N'') COLLATE Latin1_General_100_BIN2<>N'WIN-S1IBJB84OOO'
 THROW 51000,'Wrong physical server/database; stop.',1;
IF @@TRANCOUNT<>0 THROW 51000,'Use a new DBA connection without a transaction.',1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION'),0)<>1
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL'),0)<>1
 THROW 51000,'DBA full metadata/CONTROL required; filtered metadata is not proof of emptiness.',1;

SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)) AS PhysicalServer,
 DB_NAME() AS DatabaseName, CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ProductVersion,
 (SELECT compatibility_level FROM sys.databases WHERE name=DB_NAME()) AS Compatibility,
 @@TRANCOUNT AS DiagnosticSessionTransactions, SYSUTCDATETIME() AS CapturedUtc;

SELECT COUNT(*) AS ActualUserTableCount,
 (SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped=0) AS ActualUserObjectCount,
 (SELECT COUNT(*) FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id WHERE t.is_ms_shipped=0 AND i.index_id>0) AS ActualIndexCount,
 70 AS FinalExpectedTables,16 AS FinalExpectedMigrations,
 N'RECONCILE_ONLY_NO_AUTOMATIC_RESUME' AS Status
FROM sys.tables WHERE is_ms_shipped=0;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NOT NULL
BEGIN
 -- Separate compilation prevents a missing or malformed history table being
 -- mistaken for an empty history. Only migration identifiers are selected.
 EXEC(N'SELECT COUNT_BIG(*) AS ActualMigrationCount FROM dbo.__EFMigrationsHistory;
 SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;');
END
ELSE SELECT N'HISTORY_ABSENT_STOP' AS MigrationStatus;

SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,t.name AS TableName,
 t.object_id,t.create_date,t.modify_date,
 (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id=t.object_id AND p.index_id IN(0,1)) AS MetadataRowEstimate
FROM sys.tables t WHERE t.is_ms_shipped=0 ORDER BY SchemaName,TableName;

SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,t.name AS TableName,c.column_id,c.name AS ColumnName,
 TYPE_NAME(c.system_type_id) AS SqlType,c.max_length,c.precision,c.scale,c.is_nullable,
 c.is_identity,c.is_computed,c.collation_name,dc.name AS DefaultName,dc.definition AS DefaultDefinition
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
WHERE t.is_ms_shipped=0 ORDER BY SchemaName,TableName,c.column_id;

SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,t.name AS TableName,i.name AS IndexName,
 i.type_desc,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.is_disabled,i.has_filter,i.filter_definition,
 ic.index_column_id,ic.key_ordinal,ic.is_descending_key,ic.is_included_column,c.name AS ColumnName
FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id>0
LEFT JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
LEFT JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE t.is_ms_shipped=0 ORDER BY SchemaName,TableName,IndexName,ic.index_column_id;

SELECT SCHEMA_NAME(o.schema_id) AS SchemaName,o.name,o.type_desc,
 OBJECT_NAME(o.parent_object_id) AS ParentName,o.object_id
FROM sys.objects o WHERE o.is_ms_shipped=0 ORDER BY SchemaName,o.type_desc,o.name;

SELECT fk.name AS ForeignKeyName,OBJECT_SCHEMA_NAME(fk.parent_object_id) AS ParentSchema,
 OBJECT_NAME(fk.parent_object_id) AS ParentTable,COL_NAME(fk.parent_object_id,fkc.parent_column_id) AS ParentColumn,
 OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS ReferencedSchema,OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable,
 COL_NAME(fk.referenced_object_id,fkc.referenced_column_id) AS ReferencedColumn,
 fk.delete_referential_action_desc,fk.update_referential_action_desc,fk.is_disabled,fk.is_not_trusted,fkc.constraint_column_id
FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fk.object_id=fkc.constraint_object_id
ORDER BY fk.name,fkc.constraint_column_id;

SELECT COL_LENGTH(N'dbo.Contents',N'ApprovedByUserId') AS ApprovedByUserIdBytes,
 OBJECT_ID(N'dbo.RevisionMetadata',N'U') AS RevisionMetadataId,
 OBJECT_ID(N'dbo.DraftMetadata',N'U') AS DraftMetadataId,
 OBJECT_ID(N'dbo.PublicationJobs',N'U') AS PublicationJobsId,
 DATABASE_PRINCIPAL_ID(N'LandPortalRuntime') AS RuntimeRoleId;

-- App account metadata only; never password hashes, private keys or token data.
SELECT p.name,p.type_desc,CONVERT(varchar(170),p.sid,2) AS MappedUserSid,
 CONVERT(varchar(170),SUSER_SID(N'website_tin_tuc_app'),2) AS LoginSid
FROM sys.database_principals p WHERE p.name=N'website_tin_tuc_app';
SELECT r.name AS AppDatabaseRole FROM sys.database_role_members m
JOIN sys.database_principals r ON r.principal_id=m.role_principal_id
WHERE m.member_principal_id=DATABASE_PRINCIPAL_ID(N'website_tin_tuc_app');
SELECT class,major_id,minor_id,state_desc,permission_name FROM sys.database_permissions
WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'website_tin_tuc_app');

IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),0)=1
 OR COALESCE(HAS_PERMS_BY_NAME(NULL,'SERVER','VIEW SERVER PERFORMANCE STATE'),0)=1
BEGIN
 SELECT dt.transaction_id,st.session_id,dt.database_transaction_state,
 dt.database_transaction_begin_time,st.is_user_transaction,st.is_local,
 dt.database_transaction_log_bytes_used
 FROM sys.dm_tran_database_transactions dt
 LEFT JOIN sys.dm_tran_session_transactions st ON st.transaction_id=dt.transaction_id
 WHERE dt.database_id=DB_ID() ORDER BY dt.transaction_id,st.session_id;
 SELECT N'TARGET_TRANSACTION_DMV_READ_COMPLETE' AS TransactionInspection;
END
ELSE SELECT N'UNKNOWN_DMV_PERMISSION_MISSING_STOP_BEFORE_RESUME' AS TransactionInspection;

-- Return results to project owner; do not re-run the bootstrap-only packet.
-- A new exact-state, remaining-migrations-only packet requires DBA readback
-- plus explicit owner approval before any DDL. No DROP or history cleanup.
