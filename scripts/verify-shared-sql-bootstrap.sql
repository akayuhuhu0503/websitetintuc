-- READ ONLY. Run the complete file in a NEW SSMS DBA query window.
-- Matching names/counts alone is insufficient: exact columns, PK and zero rows
-- are required. No DROP, schema migration, permission change or role creation.
USE [website_tin_tuc];
SET NOCOUNT ON;
SET LOCK_TIMEOUT 5000;
IF DB_NAME()<>N'website_tin_tuc'
 OR COALESCE(CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),N'')<>N'WIN-S1IBJB84OOO'
BEGIN
 ;THROW 51000,'Wrong physical SQL server/database. Stop.',1;
END;
IF @@TRANCOUNT<>0
BEGIN
 ;THROW 51000,'Use a new diagnostic connection; do not inspect your own pending DDL.',1;
END;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION'),0)<>1
BEGIN
 ;THROW 51000,'Full database metadata visibility required; do not infer from filtered metadata.',1;
END;
IF (SELECT compatibility_level FROM sys.databases WHERE name=DB_NAME())<>160
BEGIN
 ;THROW 51000,'Expected compatibility160; no automatic database setting change.',1;
END;

SELECT SCHEMA_NAME(schema_id) AS SchemaName,name AS TableName,object_id
FROM sys.tables WHERE is_ms_shipped=0;
SELECT SCHEMA_NAME(schema_id) AS SchemaName,type_desc,name,object_id,parent_object_id
FROM sys.objects WHERE is_ms_shipped=0 ORDER BY type_desc,name;

DECLARE @history int=OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U');
DECLARE @pk int,@pkIndex int;
SELECT @pk=k.object_id,@pkIndex=k.unique_index_id
FROM sys.key_constraints k
WHERE k.parent_object_id=@history AND k.type=N'PK'
 AND k.name COLLATE Latin1_General_100_BIN2=N'PK___EFMigrationsHistory'
 AND k.schema_id=SCHEMA_ID(N'dbo');
IF @history IS NULL OR @pk IS NULL
 OR (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0)<>1
 OR (SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped=0)<>2
 OR EXISTS(SELECT 1 FROM sys.objects WHERE is_ms_shipped=0 AND object_id NOT IN(@history,@pk))
BEGIN
 ;THROW 51000,'Unexpected residual objects; only exact dbo history table and its named PK are permitted. No cleanup attempted.',1;
END;
IF NOT EXISTS(SELECT 1 FROM sys.tables WHERE object_id=@history AND schema_id=SCHEMA_ID(N'dbo')
 AND name COLLATE Latin1_General_100_BIN2=N'__EFMigrationsHistory'
 AND temporal_type=0 AND is_memory_optimized=0 AND is_filetable=0
 AND is_replicated=0 AND is_tracked_by_cdc=0)
BEGIN
 ;THROW 51000,'History is not the expected ordinary dbo table.',1;
END;

SELECT c.column_id,c.name,TYPE_NAME(c.user_type_id) AS DataType,c.max_length,
 c.is_nullable,c.is_identity,c.is_computed,c.default_object_id,c.collation_name
FROM sys.columns c WHERE c.object_id=@history ORDER BY c.column_id;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=@history)<>2
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=@history AND column_id=1
  AND name COLLATE Latin1_General_100_BIN2=N'MigrationId' AND system_type_id=231 AND user_type_id=231
  AND max_length=300 AND is_nullable=0 AND is_identity=0 AND is_computed=0
  AND default_object_id=0 AND rule_object_id=0 AND generated_always_type=0
  AND encryption_type IS NULL AND is_sparse=0 AND is_column_set=0
  AND collation_name=CAST(DATABASEPROPERTYEX(DB_NAME(),'Collation') AS sysname))
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=@history AND column_id=2
  AND name COLLATE Latin1_General_100_BIN2=N'ProductVersion' AND system_type_id=231 AND user_type_id=231
  AND max_length=64 AND is_nullable=0 AND is_identity=0 AND is_computed=0
  AND default_object_id=0 AND rule_object_id=0 AND generated_always_type=0
  AND encryption_type IS NULL AND is_sparse=0 AND is_column_set=0
  AND collation_name=CAST(DATABASEPROPERTYEX(DB_NAME(),'Collation') AS sysname))
 OR EXISTS(SELECT 1 FROM sys.masked_columns WHERE object_id=@history AND is_masked=1)
BEGIN
 ;THROW 51000,'History column definitions differ from the reviewed EF bootstrap. Preserve and reconcile.',1;
END;

SELECT i.name,i.type_desc,i.is_unique,i.is_primary_key,i.is_disabled,i.has_filter,
 c.name AS KeyColumn,ic.key_ordinal,ic.is_descending_key,ic.is_included_column
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id=@history AND i.index_id>0 ORDER BY i.index_id,ic.index_column_id;
IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id=@history AND index_id>0)<>1
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=@history AND index_id=@pkIndex
  AND type=1 AND is_unique=1 AND is_primary_key=1 AND is_disabled=0 AND is_hypothetical=0
  AND has_filter=0 AND filter_definition IS NULL
  AND name COLLATE Latin1_General_100_BIN2=N'PK___EFMigrationsHistory')
 OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=@history AND index_id=@pkIndex AND key_ordinal>0)<>1
 OR NOT EXISTS(SELECT 1 FROM sys.index_columns WHERE object_id=@history AND index_id=@pkIndex
  AND column_id=1 AND key_ordinal=1 AND is_descending_key=0 AND is_included_column=0)
 OR EXISTS(SELECT 1 FROM sys.index_columns WHERE object_id=@history AND index_id=@pkIndex AND is_included_column=1)
 OR EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=@history OR referenced_object_id=@history)
 OR EXISTS(SELECT 1 FROM sys.triggers WHERE parent_id=@history)
BEGIN
 ;THROW 51000,'History primary key/index/dependencies differ; no DROP or repair attempted.',1;
END;

DECLARE @rows bigint;
EXEC sys.sp_executesql N'SELECT @n=COUNT_BIG(*) FROM dbo.__EFMigrationsHistory;',N'@n bigint OUTPUT',@n=@rows OUTPUT;
IF @rows IS NULL OR @rows<>0
BEGIN
 ;THROW 51000,'History has migration rows; this bootstrap-only recovery is forbidden.',1;
END;
IF DATABASE_PRINCIPAL_ID(N'LandPortalRuntime') IS NOT NULL
BEGIN
 ;THROW 51000,'Runtime role already exists; reconcile separately, do not change its secret or grants.',1;
END;
IF NOT EXISTS(SELECT 1 FROM sys.database_principals WHERE name=N'website_tin_tuc_app'
 AND type=N'S' AND authentication_type=1 AND sid=SUSER_SID(N'website_tin_tuc_app'))
BEGIN
 ;THROW 51000,'Existing application login/user mapping differs; no account creation or privilege change permitted.',1;
END;
SELECT N'BOOTSTRAP_STRUCTURE_AND_ZERO_ROWS_MATCH' AS GuardResult,
 N'WIN-S1IBJB84OOO' AS PhysicalServer,DB_NAME() AS DatabaseName,@rows AS MigrationHistoryRows,
 N'Preserve table/PK. Recovery still requires approved manifest, DBA authority, backup verification and no other user transaction.' AS NextCondition;
