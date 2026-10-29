"""Offline-only packet preparation from owner DBA metadata CSVs and EF range SQL.
No SQL client, credentials, database access or execution is implemented here.
"""
from pathlib import Path
import argparse,csv,hashlib,json,re,shutil

def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def literal(v):
    if v is None or v=='NULL':return 'NULL'
    if isinstance(v,int):return str(v)
    return "N'"+str(v).replace("'","''")+"'"

def prepare(evidence,schema,output,root):
    if output.exists():raise ValueError('New immutable packet directory required')
    if not output.resolve().is_relative_to((root/'.local/publish').resolve()):raise ValueError('Private publish output required')
    data={}; provenance=[]
    for p in sorted(evidence.glob('*.csv')):
        data[p.name]=[r for r in csv.reader(p.read_text(encoding='utf-8-sig').splitlines()) if r and any(x.strip() for x in r)]
        provenance.append({'file':p.name,'sha256':sha(p),'bytes':p.stat().st_size})
    assert set(data)=={f'result-{i:02d}.csv' for i in range(1,14)},'Unexpected evidence set'
    assert data['result-01.csv'][0][:5]==['WIN-S1IBJB84OOO','website_tin_tuc','16.0.1000.6','160','0']
    assert data['result-02.csv'][0][:3]==['31','98','66']
    assert data['result-03.csv']==[['2']]
    expected_prefix=[['20261012055549_InitialPortal','10.0.12'],['20261012083717_CmsRevisionWorkflow','10.0.12']]
    assert data['result-04.csv']==expected_prefix
    assert len(data['result-05.csv'])==31 and len(data['result-06.csv'])==211 and len(data['result-07.csv'])==82 and len(data['result-08.csv'])==98 and len(data['result-09.csv'])==31
    assert len({tuple(x[:3]) for x in data['result-07.csv']})==66
    assert data['result-10.csv']==[['NULL']*5] and data['result-13.csv']==[['TARGET_TRANSACTION_DMV_READ_COMPLETE']]
    assert data['result-11.csv'][0][:2]==['website_tin_tuc_app','SQL_USER'] and data['result-11.csv'][0][2]==data['result-11.csv'][0][3]
    assert data['result-12.csv']==[['0','0','0','GRANT','CONNECT']]
    assert all(x[5]==('2' if x[1]=='__EFMigrationsHistory' else '0') for x in data['result-05.csv'])
    parent=root/'.local/publish/shared-sql-1934-recovery-451'
    assert sha(parent/'manifest.json')=='b57c8a64531f787d97975ac6c3c9b400c74db19985ef35e6d1f4478c6bf9cff5'
    manifest=json.loads((parent/'manifest.json').read_text())
    for f in manifest['files']:assert sha(parent/f['path'])==f['sha256']
    sql=schema.read_text(encoding='utf-8-sig')
    assert re.findall(r'(?im)^USE\s+\[([^]]+)\];',sql)==['website_tin_tuc']
    assert "N'WIN-S1IBJB84OOO'" in sql and not re.search(r'\bCREATE\s+(?:DATABASE|LOGIN|APPLICATION\s+ROLE)\b',sql,re.I)
    ids=list(dict.fromkeys(re.findall(r"WHERE \[MigrationId\] = N'([^']+)'",sql)))
    assert ids==manifest['expectedMigrations'][2:] and len(ids)==14
    assert 'CREATE TABLE [__EFMigrationsHistory]' not in sql,'Range must not bootstrap history'
    assert 'UPDATE c SET ApprovedByUserId=' not in re.sub(r"N?'(?:[^']|'')*'","''",sql,flags=re.S)
    for setting in ('QUOTED_IDENTIFIER ON','ANSI_NULLS ON','ANSI_PADDING ON','ANSI_WARNINGS ON','ARITHABORT ON','CONCAT_NULL_YIELDS_NULL ON','NUMERIC_ROUNDABORT OFF','XACT_ABORT ON'):
        assert 'SET '+setting+';' in sql
    guard="""-- Exact observed migration2 prefix; no persistent DDL or data changes.
USE [website_tin_tuc];
SET NOCOUNT ON;
IF DB_NAME() COLLATE Latin1_General_100_BIN2<>N'website_tin_tuc'
 OR COALESCE(CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),N'') COLLATE Latin1_General_100_BIN2<>N'WIN-S1IBJB84OOO'
 THROW 51000,'Wrong physical server/database.',1;
IF @@TRANCOUNT<>0 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL'),0)<>1
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION'),0)<>1
 THROW 51000,'Fresh DBA connection with full target visibility and CONTROL required.',1;
IF (SELECT compatibility_level FROM sys.databases WHERE name=DB_NAME())<>160
 THROW 51000,'Compatibility differs; no automatic settings change.',1;
IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1 AND COALESCE(HAS_PERMS_BY_NAME(NULL,'SERVER','VIEW SERVER PERFORMANCE STATE'),0)<>1
 THROW 51000,'SQL2022 transaction visibility required.',1;
IF EXISTS(SELECT 1 FROM sys.dm_tran_database_transactions d JOIN sys.dm_tran_session_transactions s ON s.transaction_id=d.transaction_id WHERE d.database_id=DB_ID() AND s.session_id<>@@SPID AND s.is_user_transaction=1)
 THROW 51000,'Other target user transaction active; reconcile without KILL or blind retry.',1;
IF (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0)<>31
 THROW 51000,'Table prefix count differs.',1;
IF EXISTS(SELECT 1 FROM sys.tables WHERE is_ms_shipped=0 AND (temporal_type<>0 OR is_memory_optimized<>0 OR is_filetable<>0 OR is_replicated<>0 OR is_tracked_by_cdc<>0))
 THROW 51000,'Unsupported table features in prefix.',1;
IF EXISTS(SELECT 1 FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id WHERE t.is_ms_shipped=0 AND (c.user_type_id<>c.system_type_id OR c.generated_always_type<>0 OR c.encryption_type IS NOT NULL OR c.rule_object_id<>0 OR c.is_hidden<>0 OR c.is_sparse<>0 OR c.is_column_set<>0))
 OR EXISTS(SELECT 1 FROM sys.masked_columns c JOIN sys.tables t ON t.object_id=c.object_id WHERE t.is_ms_shipped=0 AND c.is_masked=1)
 THROW 51000,'Unsupported column features in prefix.',1;
"""
    # Every string comparison uses explicit BIN2, including catalog descriptors,
    # preventing the metadata-collation SQL451 failure without changing the DB.
    def block(name,columns,records,actual):
        nonlocal guard
        names=[x[0] for x in columns]
        assert all(len(r)==len(columns) for r in records),name
        converted=[]
        for row in records:
            converted.append([None if v=='NULL' else int(v) if ty=='int' else v for v,(_,ty) in zip(row,columns)])
        declaration=','.join('['+n+'] '+('int' if ty=='int' else 'nvarchar(max) COLLATE Latin1_General_100_BIN2') for n,ty in columns)
        selection=','.join('['+n+']' for n in names)
        guard+='\nDECLARE @'+name+' TABLE('+declaration+');\nINSERT INTO @'+name+' VALUES\n'+',\n'.join('('+','.join(literal(v) for v in row)+')' for row in converted)+';\n'
        guard+='IF EXISTS('+actual+' EXCEPT SELECT '+selection+' FROM @'+name+')\n OR EXISTS(SELECT '+selection+' FROM @'+name+' EXCEPT '+actual+')\n THROW 51000,\''+name+' differs from observed DBA prefix; no repair attempted.\',1;\n'
    def fields(*values):return [(name,'int' if name.startswith('n_') else 'text') for name in values]
    block('Objects',fields('schema','name','kind','parent','n_id'),data['result-08.csv'],
      'SELECT SCHEMA_NAME(o.schema_id) COLLATE Latin1_General_100_BIN2,o.name COLLATE Latin1_General_100_BIN2,o.type_desc COLLATE Latin1_General_100_BIN2,OBJECT_NAME(o.parent_object_id) COLLATE Latin1_General_100_BIN2,o.object_id FROM sys.objects o WHERE o.is_ms_shipped=0')
    block('Columns',fields('schema','table','n_ordinal','name','type','n_length','n_precision','n_scale','n_nullable','n_identity','n_computed','collation','defaultName','defaultSql'),data['result-06.csv'],
      'SELECT SCHEMA_NAME(t.schema_id) COLLATE Latin1_General_100_BIN2,t.name COLLATE Latin1_General_100_BIN2,c.column_id,c.name COLLATE Latin1_General_100_BIN2,TYPE_NAME(c.system_type_id) COLLATE Latin1_General_100_BIN2,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_computed,c.collation_name COLLATE Latin1_General_100_BIN2,dc.name COLLATE Latin1_General_100_BIN2,dc.definition COLLATE Latin1_General_100_BIN2 FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id WHERE t.is_ms_shipped=0')
    block('Indexes',fields('schema','table','name','type','n_unique','n_pk','n_constraint','n_disabled','n_filtered','filter','n_indexOrdinal','n_keyOrdinal','n_descending','n_included','column'),data['result-07.csv'],
      'SELECT SCHEMA_NAME(t.schema_id) COLLATE Latin1_General_100_BIN2,t.name COLLATE Latin1_General_100_BIN2,i.name COLLATE Latin1_General_100_BIN2,i.type_desc COLLATE Latin1_General_100_BIN2,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.is_disabled,i.has_filter,i.filter_definition COLLATE Latin1_General_100_BIN2,ic.index_column_id,ic.key_ordinal,ic.is_descending_key,ic.is_included_column,c.name COLLATE Latin1_General_100_BIN2 FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id>0 LEFT JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id LEFT JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE t.is_ms_shipped=0')
    block('ForeignKeys',fields('name','schema','table','column','targetSchema','targetTable','targetColumn','deleteAction','updateAction','n_disabled','n_untrusted','n_ordinal'),data['result-09.csv'],
      'SELECT fk.name COLLATE Latin1_General_100_BIN2,OBJECT_SCHEMA_NAME(fk.parent_object_id) COLLATE Latin1_General_100_BIN2,OBJECT_NAME(fk.parent_object_id) COLLATE Latin1_General_100_BIN2,COL_NAME(fk.parent_object_id,fkc.parent_column_id) COLLATE Latin1_General_100_BIN2,OBJECT_SCHEMA_NAME(fk.referenced_object_id) COLLATE Latin1_General_100_BIN2,OBJECT_NAME(fk.referenced_object_id) COLLATE Latin1_General_100_BIN2,COL_NAME(fk.referenced_object_id,fkc.referenced_column_id) COLLATE Latin1_General_100_BIN2,fk.delete_referential_action_desc COLLATE Latin1_General_100_BIN2,fk.update_referential_action_desc COLLATE Latin1_General_100_BIN2,fk.is_disabled,fk.is_not_trusted,fkc.constraint_column_id FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fk.object_id=fkc.constraint_object_id')
    block('History',fields('id','version'),expected_prefix,
      'SELECT MigrationId COLLATE Latin1_General_100_BIN2,ProductVersion COLLATE Latin1_General_100_BIN2 FROM dbo.__EFMigrationsHistory')
    block('AppPermissions',fields('n_class','n_major','n_minor','state','permission'),data['result-12.csv'],
      "SELECT class,major_id,minor_id,state_desc COLLATE Latin1_General_100_BIN2,permission_name COLLATE Latin1_General_100_BIN2 FROM sys.database_permissions WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'website_tin_tuc_app')")
    sid=data['result-11.csv'][0][2]
    assert re.fullmatch('[A-F0-9]{32}',sid)
    guard+=f"""
IF DATABASE_PRINCIPAL_ID(N'LandPortalRuntime') IS NOT NULL
 OR EXISTS(SELECT 1 FROM sys.database_role_members WHERE member_principal_id=DATABASE_PRINCIPAL_ID(N'website_tin_tuc_app'))
 OR NOT EXISTS(SELECT 1 FROM sys.database_principals WHERE name COLLATE Latin1_General_100_BIN2=N'website_tin_tuc_app' AND type=N'S' AND authentication_type=1 AND sid=SUSER_SID(N'website_tin_tuc_app') AND CONVERT(varchar(170),sid,2) COLLATE Latin1_General_100_BIN2=N'{sid}')
 THROW 51000,'Application role/mapping/membership differs; no account changes permitted.',1;
DECLARE @s sysname,@t sysname,@check nvarchar(max);
DECLARE prefix_data_check CURSOR LOCAL FAST_FORWARD FOR SELECT SCHEMA_NAME(schema_id),name FROM sys.tables WHERE is_ms_shipped=0 AND name<>N'__EFMigrationsHistory';
OPEN prefix_data_check;
FETCH NEXT FROM prefix_data_check INTO @s,@t;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @check=N'IF EXISTS(SELECT 1 FROM '+QUOTENAME(@s)+N'.'+QUOTENAME(@t)+N') THROW 51000,''Prefix has business data; preserve and request review.'',1;';
 EXEC sys.sp_executesql @check;
 FETCH NEXT FROM prefix_data_check INTO @s,@t;
END;
CLOSE prefix_data_check;DEALLOCATE prefix_data_check;
SELECT N'EXACT_COMMITTED_MIGRATION2_PREFIX_MATCH' AS GuardResult,31 AS TableCount,2 AS MigrationCount,66 AS IndexCount;
"""
    output.mkdir(parents=True)
    shutil.copyfile(schema,output/'schema.sqlcmd')
    (output/'verify-prefix.sql').write_text(guard,encoding='utf-8')
    shutil.copyfile(root/'scripts/recover-shared-sql-207.ps1',output/'recover-207.ps1')
    (output/'START.md').write_text('''# SQL207 exact-state resume: PREPARED, NOT APPROVED/APPLIED

Target WIN-S1IBJB84OOO / website_tin_tuc only. Owner DBA exports confirm31tables,
66indexes/98objects/2committed migrations. Preserve this prefix. Only remaining
14 migrations after CmsRevisionWorkflow are present; no history bootstrap,
database/login/runtime-role creation, data import, DROP cleanup or DB restore.
Migration3 UPDATE now uses EXEC so ApprovedByUserId is bound after AddColumn.
Schema still contains its reviewed EF changes; Inspect executes NONE of them.

Plan reads/hashes only local files. Inspect without BackupFile checks the exact
observed prefix but reports backupVerified=false: it is NOT Apply readiness.
Run in this directory, use full manifest SHA from Get-FileHash manifest.json:

 powershell -NoProfile -File .\\recover-207.ps1 -PacketDirectory . -Mode Inspect -ManifestSha256 <SHA256> -TemporaryCertificateTrust -ReportFile <NEW_PRIVATE_JSONL>

Credential DBA is prompted securely, never pass a password on the command line.
Report must be new/outside immutable packet; operator+SYSTEM ACL is applied.
Fresh exact schema/history/index/FK/objectID/SID/rights/data/transaction guards
run at execution, including actual absence of business rows. Any drift STOPS.
No privileges or server/database/TLS settings are modified.

Apply still requires owner's explicit approval of this NEW manifest/scope and a
NEW COPY_ONLY/CHECKSUM SQL backup of current partial-state database, with HEADER
and VERIFYONLY checks. It must finish after the observation timestamp in manifest;
original empty pre-schema backup is insufficient for preserving current prefix.
No backup was made by preparation. Media has no imported rows; preserve any
separately configured storage. No data/media/Identity import is authorized.
Only after approval, use Mode Apply + ConfirmTarget WIN-S1IBJB84OOO/website_tin_tuc
+ BackupFile <absolute_SQL_server_backup_path>; the helper verifies it before DDL.
Do not run schema.sqlcmd directly: it bypasses prefix/backup/receipt orchestration.

EF transaction boundaries remain per migration; no whole-run rollback assumed.
On failure stop/reconcile (no automatic retry, KILL, DROP or restore). Readback
checks final70tables/16migration IDs/5filtered indexes, preserved prefix table IDs,
history/PK IDs and application rights; no role creation or imported business data.
Real sharedSQL Inspect/engine DDL/Apply are NOT RUN during preparation.
''',encoding='utf-8')
    manifest.update(kind='LandPortal.Sql207.ResumePrefix.v1',status='PREPARED_PENDING_DBA_INSPECT_BACKUP_AND_OWNER_APPROVAL',
      fromMigration='20261012083717_CmsRevisionWorkflow',toMigration='20261027025057_AuditRecoveryLookup',remainingMigrationCount=14,
      baselineCapturedUtc=data['result-01.csv'][0][5].replace(' ','T')+'Z',
      baseline={'tables':31,'columns':211,'indexes':66,'objects':98,'migrations':2,'expectedCommittedMigrations':expected_prefix},
      backupPolicy='FreshTargetCopyOnlyChecksumBackupAfterBaselineCapture',parentManifestSha256=sha(parent/'manifest.json'),dbaEvidence=provenance,
      diagnosticsVersion=2)
    manifest.update(observationSource='Owner DBA CSV exports; fully read and compared offline, not a new live DBA query',
      observedMigrationRows=2,
      observedObjects=[{'schema':x[0],'name':x[1],'type':x[2],'parent':None if x[3]=='NULL' else x[3],'objectId':int(x[4])} for x in data['result-08.csv']],
      operatorMustCreateFreshBackup=True,
      sourceFilesSha256={n:sha(root/n) for n in ('src/Infrastructure/Migrations/20261013030300_PortalAdministration.cs','src/MigrationCli/Program.cs','scripts/recover-shared-sql-207.ps1','scripts/prepare-shared-sql-207.py')})
    manifest['originalSql1934FixedManifestSha256']=manifest.pop('sourceFixedManifestSha256')
    for k in ('backupPath','sql451Fix'):manifest.pop(k,None)
    manifest['files']=[{'path':n,'bytes':(output/n).stat().st_size,'sha256':sha(output/n)} for n in ('schema.sqlcmd','verify-prefix.sql','recover-207.ps1','START.md')]
    (output/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    print(json.dumps({'packet':str(output),'manifestSha256':sha(output/'manifest.json'),'remainingMigrations':14,'sqlExecution':'NOT RUN'}))

if __name__=='__main__':
    a=argparse.ArgumentParser();a.add_argument('--evidence',type=Path,required=True);a.add_argument('--schema',type=Path,required=True);a.add_argument('--output',type=Path,required=True)
    args=a.parse_args();prepare(args.evidence.resolve(),args.schema.resolve(),args.output.resolve(),Path(__file__).resolve().parent.parent)
