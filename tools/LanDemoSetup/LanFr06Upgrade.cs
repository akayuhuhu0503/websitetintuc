using System.Data.Common;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

// Owner-approved, local-only 15 -> 17 upgrade. No startup/bootstrap/Identity changes.
static class LanFr06Upgrade
{
    static readonly string[] Tail=["20261027025057_AuditRecoveryLookup","20261102042625_DraftConsultationEmail"];
    public static async Task RunAsync(string root,string configPath,IConfiguration config,PortalDbContext db,string[] args)
    {
        if(!OperatingSystem.IsWindows()||!LanDemoDeployment.Validate(config,"Development"))throw new InvalidOperationException("Local LAN only");
        string Option(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:throw new InvalidOperationException("Missing operation argument");}
        string Private(string value){var path=Path.GetFullPath(value,root);var allowed=Path.GetFullPath(Path.Combine(root,".local"))+Path.DirectorySeparatorChar;if(!path.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Private project path required");for(var d=new DirectoryInfo(path);d!=null;d=d.Parent)if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Reparse path refused");return path;}
        var report=Private(Option("--report-root"));Directory.CreateDirectory(report);
        var mode=Option("--mode");if(mode is not("Inspect" or "Apply"))throw new InvalidOperationException("Unknown mode");
        var stage="TARGET";await db.Database.OpenConnectionAsync();var conn=db.Database.GetDbConnection();
        async Task<object?> Scalar(string sql){using var cmd=conn.CreateCommand();cmd.CommandText=sql;cmd.CommandTimeout=30;return await cmd.ExecuteScalarAsync();}
        async Task Write(string name,object data){await using var file=new FileStream(Path.Combine(report,name),FileMode.CreateNew,FileAccess.Write);await JsonSerializer.SerializeAsync(file,data,new JsonSerializerOptions{WriteIndented=true});}
        try{
            using(var cmd=conn.CreateCommand()){
                cmd.CommandText="SELECT DB_NAME(),CAST(SERVERPROPERTY('MachineName') AS nvarchar(200)),CAST(SERVERPROPERTY('ServerName') AS nvarchar(200)),IS_SRVROLEMEMBER('sysadmin'),@@TRANCOUNT";
                using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync()||r.GetString(0)!="LandPortal_LanDemo"||!r.GetString(1).Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase)||!r.GetString(2).Equals(Environment.MachineName+@"\SQLEXPRESS",StringComparison.OrdinalIgnoreCase)||r.GetInt32(3)!=1||r.GetInt32(4)!=0)throw new InvalidOperationException("Target/authority/transaction guard failed");
            }
            var expected=db.Database.GetMigrations().ToArray();var applied=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
            if(expected.Length!=17||!expected.TakeLast(2).SequenceEqual(Tail)||!applied.SequenceEqual(expected.Take(applied.Length))||applied.Length is not(15 or 17))throw new InvalidOperationException("Exact history prefix required");
            var emailColumns=Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM sys.columns WHERE object_id IN(OBJECT_ID('dbo.DraftMetadata'),OBJECT_ID('dbo.RevisionMetadata')) AND name='ConsultationEmail' AND TYPE_NAME(user_type_id)='nvarchar' AND max_length=-1 AND is_nullable=1"));
            if(emailColumns!=(applied.Length==17?2:0)||Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM dbo.AuditLogs WHERE DATALENGTH(Actor)>900 OR DATALENGTH(Action)>400"))!=0)throw new InvalidOperationException("Column/data bounds guard failed");
            await Write("inspect.json",new{status="EXACT_LAN_PREFIX_PASS",server=Environment.MachineName+@"\SQLEXPRESS",database="LandPortal_LanDemo",applied,pending=expected.Skip(applied.Length),emailColumns,mode,utc=DateTimeOffset.UtcNow});
            if(mode=="Inspect"||applied.Length==17){Console.WriteLine("LAN exact schema Inspect PASS; migrations="+applied.Length+"; no Apply/backup performed");return;}
            stage="QUIESCENCE";
            Quiescence();
            if(Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM sys.dm_tran_session_transactions t JOIN sys.dm_exec_sessions s ON s.session_id=t.session_id WHERE s.is_user_process=1 AND s.session_id<>@@SPID AND s.database_id=DB_ID()"))!=0)throw new InvalidOperationException("Open LAN transaction; reconcile");
            var backup=Private(Option("--backup-root"));var allowedBackup=Path.GetFullPath(Path.Combine(root,".local","backups"))+Path.DirectorySeparatorChar;
            if(!backup.StartsWith(allowedBackup,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(backup))throw new InvalidOperationException("Existing unique backup directory required");
            stage="FILE_BACKUP";
            using(var manifest=JsonDocument.Parse(await File.ReadAllBytesAsync(Private(Option("--file-manifest"))))){
                var m=manifest.RootElement;var destination=Private(m.GetProperty("destination").GetString()!);
                if(!destination.StartsWith(backup+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Snapshot outside restore point");
                var stores=StorageOptions.FromConfiguration(config);
                var required=new[]{stores.MediaRoot,stores.SnapshotRoot,stores.KeysRoot}.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).Select(Path.GetFullPath).Append(configPath).Append(Path.Combine(root,".local","lan-demo","current.json")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var e in m.GetProperty("files").EnumerateArray()){
                    var source=Private(e.GetProperty("source").GetString()!);var copy=Private(Path.Combine(destination,e.GetProperty("key").GetString()!));
                    if(!copy.StartsWith(destination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!seen.Add(source))throw new InvalidOperationException("Snapshot path/duplicate invalid");
                    foreach(var p in new[]{source,copy}){if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Reparse refused");await using var f=File.OpenRead(p);if(f.Length!=e.GetProperty("bytes").GetInt64()||Convert.ToHexStringLower(await SHA256.HashDataAsync(f))!=e.GetProperty("sha256").GetString())throw new InvalidOperationException("Snapshot bytes mismatch");}
                }
                if(!required.IsSubsetOf(seen))throw new InvalidOperationException("Missing media/keys/config/pointer in snapshot");
            }
            var before=await Digest();await Write("before-digests.json",before);
            stage="SQL_BACKUP";var bak=Path.Combine(backup,"sql","LandPortal_LanDemo.bak");if(!Directory.Exists(Path.GetDirectoryName(bak))||File.Exists(bak))throw new InvalidOperationException("New SQL-only backup folder required; no overwrite/repeat");
            Guid backupSet;
            foreach(var sql in new[]{"BACKUP DATABASE [LandPortal_LanDemo] TO DISK=@p WITH COPY_ONLY,CHECKSUM","RESTORE VERIFYONLY FROM DISK=@p WITH CHECKSUM"}){using var cmd=conn.CreateCommand();cmd.CommandText=sql;cmd.CommandTimeout=180;var p=cmd.CreateParameter();p.ParameterName="@p";p.Value=bak;cmd.Parameters.Add(p);await cmd.ExecuteNonQueryAsync();}
            using(var cmd=conn.CreateCommand()){cmd.CommandText="RESTORE HEADERONLY FROM DISK=@p";var p=cmd.CreateParameter();p.ParameterName="@p";p.Value=bak;cmd.Parameters.Add(p);using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync()||r["DatabaseName"].ToString()!="LandPortal_LanDemo"||!(bool)r["IsCopyOnly"]||!(bool)r["HasBackupChecksums"]||Convert.ToInt32(r["BackupType"])!=1)throw new InvalidOperationException("Backup header invalid");backupSet=(Guid)r["BackupSetGUID"];}
            await using(var f=File.OpenRead(bak))await Write("backup-receipt.json",new{status="PASS",server=Environment.MachineName+@"\SQLEXPRESS",database="LandPortal_LanDemo",applied,backupSet,path=bak,bytes=f.Length,sha256=Convert.ToHexStringLower(await SHA256.HashDataAsync(f)),checksum=true,verifyOnly=true,sqlRestore="NOT_RUN",utc=DateTimeOffset.UtcNow});
            stage="FRESH_GUARDS";
            Quiescence();
            if(!(await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(applied)||!Same(before,await Digest()))throw new InvalidOperationException("LAN state drift before DDL");
            stage="MIGRATE";await db.GetService<IMigrator>().MigrateAsync(Tail[^1]);
            stage="READBACK";var after=await Digest();await Write("after-digests.json",after);
            if(!Same(before,after)||!(await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(expected)||Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM sys.columns WHERE object_id IN(OBJECT_ID('dbo.DraftMetadata'),OBJECT_ID('dbo.RevisionMetadata')) AND name='ConsultationEmail' AND TYPE_NAME(user_type_id)='nvarchar' AND max_length=-1 AND is_nullable=1"))!=2||Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.AuditLogs') AND name='IX_AuditLogs_Action_Actor'"))!=1)throw new InvalidOperationException("Readback mismatch; reconcile before retry");
            await Write("upgrade-receipt.json",new{status="APPLIED_VERIFIED",database="LandPortal_LanDemo",from=15,to=17,migrations=Tail,preservedTables=before.Count,mismatch=0,identityAndPermissionsPreserved=true,utc=DateTimeOffset.UtcNow});
            Console.WriteLine("LAN FR06 APPLIED/VERIFIED 15 -> 17; all existing table digests preserved; email columns=2; no bootstrap/import/grants");
        }catch(Exception e){await Write("stopped.json",new{stage,errorType=e.GetType().Name,sqlNumber=(e.GetBaseException() as Microsoft.Data.SqlClient.SqlException)?.Number,reconcileRequired=stage is "MIGRATE" or "READBACK",utc=DateTimeOffset.UtcNow});throw;}
        async Task<Dictionary<string,DigestRow>> Digest(){
            var tables=new List<string>();using(var cmd=conn.CreateCommand()){cmd.CommandText="SELECT name FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name<>'__EFMigrationsHistory' ORDER BY name";using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())tables.Add(r.GetString(0));}
            var result=new Dictionary<string,DigestRow>();foreach(var table in tables){
                var columns=new List<string>();using(var cmd=conn.CreateCommand()){cmd.CommandText="SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(@t) AND NOT(name='ConsultationEmail' AND @t IN('dbo.DraftMetadata','dbo.RevisionMetadata')) ORDER BY column_id";var p=cmd.CreateParameter();p.ParameterName="@t";p.Value="dbo."+table;cmd.Parameters.Add(p);using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())columns.Add("["+r.GetString(0).Replace("]","]]")+"]");}
                using var q=conn.CreateCommand();q.CommandTimeout=30;q.CommandText="SELECT "+string.Join(',',columns)+" FROM dbo.["+table.Replace("]","]]")+"] FOR JSON PATH,INCLUDE_NULL_VALUES";var text=new StringBuilder();using(var r=await q.ExecuteReaderAsync())while(await r.ReadAsync())text.Append(r.GetString(0));using var doc=JsonDocument.Parse(text.Length==0?"[]":text.ToString());var rows=doc.RootElement.EnumerateArray().Select(x=>x.GetRawText()).Order(StringComparer.Ordinal).ToArray();result[table]=new(rows.Length,Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',rows)))));
            }return result;
        }
    }
    static bool Same(Dictionary<string,DigestRow> a,Dictionary<string,DigestRow> b)=>a.Count==b.Count&&a.All(x=>b.TryGetValue(x.Key,out var v)&&v==x.Value);
    static void Quiescence(){
        // Do not rely solely on Windows CIM/.NET state enumeration, which may omit listeners.
        foreach(var port in new[]{5188,5194}){
            var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any,port){ExclusiveAddressUse=true};
            try{listener.Start();}catch(System.Net.Sockets.SocketException){throw new InvalidOperationException("LAN/candidate port still reserved; no backup or DDL allowed");}finally{listener.Stop();}
        }
    }
    sealed record DigestRow(int Rows,string Sha256);
}
