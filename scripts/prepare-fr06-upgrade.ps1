[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputDirectory,[Parameter(Mandatory=$true)][string]$MigrationCli,[Parameter(Mandatory=$true)][string]$Dotnet,[Parameter(Mandatory=$true)][string]$ReviewedPrefixManifest)
# Offline only. Reuses the guarded recovery runner, never executes schema or backup.
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(-not $out.StartsWith((Join-Path $root '.local/publish/'),[StringComparison]::OrdinalIgnoreCase) -or (Test-Path $out)){throw 'New project private publish directory required'}
for($node=[IO.Path]::GetDirectoryName($out);$node;$node=[IO.Path]::GetDirectoryName($node)){if((Test-Path $node) -and ((Get-Item $node).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse path refused'}}
$base=Get-Content -Raw -Encoding UTF8 $ReviewedPrefixManifest|ConvertFrom-Json
if($base.kind -cne 'LandPortal.Sql207.ResumePrefix.v1' -or $base.toMigration -cne '20261027025057_AuditRecoveryLookup' -or $base.expectedTables.Count -ne 70 -or $base.expectedMigrations.Count -ne 16){throw 'Reviewed 70-table/16-migration prefix required'}
if((Get-FileHash $ReviewedPrefixManifest).Hash.ToLowerInvariant() -cne '664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7'){throw 'Original reviewed prefix manifest changed'}
[IO.Directory]::CreateDirectory($out)|Out-Null
$schema=Join-Path $out 'schema.sqlcmd'
& $Dotnet $MigrationCli schema-script --from-migration 20261027025057_AuditRecoveryLookup --to-migration 20261102042625_DraftConsultationEmail --output $schema --target-database website_tin_tuc --server-identity WIN-S1IBJB84OOO --runtime-role-output (Join-Path $out 'role-not-for-execution.sql') --app-login website_tin_tuc_app
if($LASTEXITCODE){throw 'Offline schema generation failed'}
# Role output is deliberately excluded from the recovery packet, never executed.
$guard=@'
USE [website_tin_tuc];
SET NOCOUNT ON;
IF DB_NAME() COLLATE Latin1_General_100_BIN2<>N'website_tin_tuc' OR CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) COLLATE Latin1_General_100_BIN2<>N'WIN-S1IBJB84OOO'
 THROW 51000,'Wrong physical server/database.',1;
IF @@TRANCOUNT<>0 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL'),0)<>1 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION'),0)<>1
 THROW 51000,'Fresh DBA connection and full metadata visibility required.',1;
IF (SELECT compatibility_level FROM sys.databases WHERE name=DB_NAME())<>160 THROW 51000,'Compatibility differs.',1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51000,'History missing.',1;
DECLARE @Tables TABLE(Name nvarchar(256) COLLATE Latin1_General_100_BIN2);
'@
$tableValues=($base.expectedTables|ForEach-Object {"(N'"+$_.Replace("'","''")+"')"})-join ','
$guard+="`nINSERT @Tables VALUES $tableValues;`n"
$guard+=@'
IF EXISTS(SELECT Name FROM @Tables EXCEPT SELECT (SCHEMA_NAME(schema_id)+N'.'+name) COLLATE Latin1_General_100_BIN2 FROM sys.tables WHERE is_ms_shipped=0)
 OR EXISTS(SELECT (SCHEMA_NAME(schema_id)+N'.'+name) COLLATE Latin1_General_100_BIN2 FROM sys.tables WHERE is_ms_shipped=0 EXCEPT SELECT Name FROM @Tables) THROW 51000,'Table prefix differs.',1;
DECLARE @History TABLE(Id nvarchar(150) COLLATE Latin1_General_100_BIN2);
'@
$history=($base.expectedMigrations|ForEach-Object {"(N'"+$_+"')"})-join ','
$guard+="`nINSERT @History VALUES $history;`n"
$guard+=@'
IF EXISTS(SELECT Id FROM @History EXCEPT SELECT MigrationId COLLATE Latin1_General_100_BIN2 FROM dbo.__EFMigrationsHistory)
 OR EXISTS(SELECT MigrationId COLLATE Latin1_General_100_BIN2 FROM dbo.__EFMigrationsHistory EXCEPT SELECT Id FROM @History) THROW 51000,'Expected exact16 migration prefix; applied17 is a readback/no-op condition, never replay.',1;
IF COL_LENGTH(N'dbo.DraftMetadata',N'ConsultationEmail') IS NOT NULL OR COL_LENGTH(N'dbo.RevisionMetadata',N'ConsultationEmail') IS NOT NULL THROW 51000,'Email columns already exist; reconcile, no replay.',1;
IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.DraftMetadata') AND name=N'ContentId' AND system_type_id=36 AND is_nullable=0)
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.RevisionMetadata') AND name=N'RevisionId' AND system_type_id=36 AND is_nullable=0) THROW 51000,'Metadata key structure differs.',1;
IF (SELECT COUNT(*) FROM sys.key_constraints WHERE parent_object_id IN(OBJECT_ID(N'dbo.DraftMetadata'),OBJECT_ID(N'dbo.RevisionMetadata')) AND type=N'PK')<>2 THROW 51000,'Metadata primary keys differ.',1;
'@
[IO.File]::WriteAllText((Join-Path $out 'verify-prefix.sql'),$guard,[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'recover-shared-sql-207.ps1') -Destination (Join-Path $out 'recover-207.ps1')
[IO.File]::WriteAllText((Join-Path $out 'START.md'),"FR06 only after verified70 tables/16 migrations. Plan is offline; Inspect requires DBA. Apply requires exact new manifest approval, fresh target COPY_ONLY/CHECKSUM backup after packet timestamp, HEADERONLY/VERIFYONLY and exclusive target guard. No role creation/import. Original14-migration packet is unchanged. Do not run schema.sqlcmd directly. Idempotency: an already migrated17 target must stop and reconcile, not replay. Backup restore is a separate DBA-approved operation; never run Down or DROP to retry.",[Text.UTF8Encoding]::new($false))
$manifest=[ordered]@{kind='LandPortal.Fr06.Additive.v1';server='WIN-S1IBJB84OOO';database='website_tin_tuc';endpoint='45.120.229.43,1433';fromMigration='20261027025057_AuditRecoveryLookup';toMigration='20261102042625_DraftConsultationEmail';remainingMigrationCount=1;baselineCapturedUtc=[DateTimeOffset]::UtcNow.ToString('O');baselineIsExpectedNotObserved=$true;expectedTables=$base.expectedTables;expectedMigrations=@($base.expectedMigrations)+@('20261102042625_DraftConsultationEmail');filteredIndexes=$base.filteredIndexes;files=@()}
foreach($name in @('schema.sqlcmd','verify-prefix.sql','recover-207.ps1','START.md')){$manifest.files+=@{path=$name;sha256=(Get-FileHash (Join-Path $out $name)).Hash.ToLowerInvariant()}}
[IO.File]::WriteAllText((Join-Path $out 'manifest.json'),($manifest|ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
$sha=(Get-FileHash (Join-Path $out 'manifest.json')).Hash.ToLowerInvariant()
& (Join-Path $out 'recover-207.ps1') -PacketDirectory $out -Mode Plan -ManifestSha256 $sha
Write-Output ('OFFLINE PREPARED; target NOT INSPECTED; manifest SHA '+$sha)
