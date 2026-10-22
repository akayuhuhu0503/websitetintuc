using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Infrastructure;

// Explicit SQL Test diagnostics only. Never log SQL parameters, credentials, bodies or exception messages.
public static class MigrationDiagnostics
{
    sealed record Context(string OperationId,string Stage,Guid? BatchId,int? ExpectedRows);
    static readonly AsyncLocal<Context?> current=new();
    static readonly object gate=new();static string? file;
    public static DbCommandInterceptor? Configure(IConfiguration config)
    {
        var configured=config["MigrationDiagnostics:Root"];if(string.IsNullOrWhiteSpace(configured))return null;
        if(config["DOTNET_ENVIRONMENT"]!="Development"||config["Sql:ExpectedDatabase"]!="LandPortal_Test")throw new InvalidOperationException("Migration diagnostics require explicit Development SQL Test.");
        var workspace=Path.GetFullPath(config["WorkspaceRoot"]??PortalConfiguration.FindWorkspace());var root=Path.GetFullPath(configured,workspace);
        if(!new[]{"phase3e","phase3f"}.Any(phase=>root.StartsWith(Path.Combine(workspace,".local","evidence",phase)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Private migration diagnostics root required.");
        for(var p=new DirectoryInfo(root);p!=null;p=p.Parent)if(p.Exists&&p.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new InvalidOperationException("No reparse diagnostic root.");
        lock(gate){if(file==null){Directory.CreateDirectory(root);file=Path.Combine(root,$"sql-{Environment.ProcessId}-{Guid.NewGuid():N}.jsonl");using var f=new FileStream(file,FileMode.CreateNew);}}
        return new Commands();
    }
    public static string Fingerprint(string sql)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
    public static object ConnectionInfo(DbConnection? connection)
    {
        try{return connection is SqlConnection sql?new{state=sql.State.ToString(),clientConnectionId=(Guid?)sql.ClientConnectionId,serverSession=sql.State==System.Data.ConnectionState.Open?(int?)sql.ServerProcessId:null}:new{state=connection?.State.ToString()??"Unavailable",clientConnectionId=(Guid?)null,serverSession=(int?)null};}
        catch(InvalidOperationException){return new{state="Unavailable",clientConnectionId=(Guid?)null,serverSession=(int?)null};}
    }
    public static void Record(string status,object? detail=null)
    {
        lock(gate){if(file!=null)File.AppendAllText(file,JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,current.Value?.OperationId,current.Value?.Stage,current.Value?.BatchId,current.Value?.ExpectedRows,status,detail})+Environment.NewLine);}
    }
    public static async Task<T> StepAsync<T>(string stage,Func<Task<T>> operation,Guid? batch=null,int? expectedRows=null)
    {
        var previous=current.Value;current.Value=new(Guid.NewGuid().ToString("N"),stage,batch,expectedRows);var watch=System.Diagnostics.Stopwatch.StartNew();Record("START");
        try{var result=await operation();Record("PASS",new{elapsedMs=watch.Elapsed.TotalMilliseconds});return result;}
        catch(Exception ex){Record("FAIL",new{elapsedMs=watch.Elapsed.TotalMilliseconds,error=ex.GetType().Name,sqlNumber=(ex as SqlException)?.Number});throw;}
        finally{current.Value=previous;}
    }
    sealed class Commands:DbCommandInterceptor
    {
        // Only these fixed parameterized SELECT templates may be recorded. Never log
        // parameter values, approle RPCs, arbitrary SQL or returned audit details.
        static string? AuditTemplate(DbCommand c)=>c.CommandType==System.Data.CommandType.Text
            && c.CommandText.TrimStart().StartsWith("SELECT",StringComparison.Ordinal)
            && c.CommandText.Contains("[AuditLogs]",StringComparison.Ordinal)
            && (c.CommandText.Contains("N'Phase3C.InventoryBinding'",StringComparison.Ordinal)
                || c.CommandText.Contains("N'Phase3C.ScanProof'",StringComparison.Ordinal)) ? c.CommandText:null;
        static int? Session(DbConnection? connection){try{return connection is SqlConnection{State:System.Data.ConnectionState.Open} sql?sql.ServerProcessId:null;}catch(InvalidOperationException){return null;}}
        static object Info(DbCommand c,CommandEventData e,double? elapsed=null,Exception? error=null)=>new{e.CommandId,fingerprint=Fingerprint(c.CommandText),auditSqlTemplate=AuditTemplate(c),kind=c.CommandType.ToString(),timeoutSeconds=c.CommandTimeout,parameterCount=c.Parameters.Count,parameterTypes=c.Parameters.Cast<DbParameter>().Select(p=>new{p.ParameterName,dbType=p.DbType.ToString(),p.Size}).ToArray(),transaction=c.Transaction==null?"NONE":"ACTIVE",serverSession=Session(c.Connection),connection=ConnectionInfo(c.Connection),elapsedMs=elapsed,error=error?.GetType().Name,sqlNumber=(error as SqlException)?.Number};
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<DbDataReader> r,CancellationToken ct=default){Record("SQL_START",Info(c,e));return ValueTask.FromResult(r);}
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand c,CommandExecutedEventData e,DbDataReader r,CancellationToken ct=default){Record("SQL_READER",Info(c,e,e.Duration.TotalMilliseconds));return ValueTask.FromResult(r);}
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<int> r,CancellationToken ct=default){Record("SQL_START",Info(c,e));return ValueTask.FromResult(r);}
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand c,CommandExecutedEventData e,int r,CancellationToken ct=default){Record("SQL_NONQUERY",new{command=Info(c,e,e.Duration.TotalMilliseconds),affected=r});return ValueTask.FromResult(r);}
        public override Task CommandFailedAsync(DbCommand c,CommandErrorEventData e,CancellationToken ct=default){Record("SQL_FAIL",Info(c,e,e.Duration.TotalMilliseconds,e.Exception));return Task.CompletedTask;}
    }
}
