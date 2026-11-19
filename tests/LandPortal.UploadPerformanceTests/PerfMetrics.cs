using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Included only in the generated, isolated test host. No SQL text, parameters or credentials.
public sealed class PerfMetrics : DbCommandInterceptor
{
    public sealed class Sample { public int Commands; public double SqlMs; public int UserClaims; public int Opens; public double OpenMs; public List<object> CategoryQueries = []; }
    public static readonly AsyncLocal<Sample?> Current = new();
    private static void End(DbCommand command, CommandExecutedEventData data)
    {
        if(Current.Value is not { } value)return;
        value.Commands++; value.SqlMs += data.Duration.TotalMilliseconds;
        if(command.CommandText.StartsWith("-- HomeCategory",StringComparison.Ordinal))value.CategoryQueries.Add(new {tag=command.CommandText.Split('\n')[0].Trim(),ms=data.Duration.TotalMilliseconds});
        if(command.CommandText.Contains("AspNetUserClaims",StringComparison.Ordinal))value.UserClaims++;
    }
    public override DbDataReader ReaderExecuted(DbCommand command,CommandExecutedEventData data,DbDataReader result){End(command,data);return result;}
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData data,DbDataReader result,CancellationToken ct=default){End(command,data);return ValueTask.FromResult(result);}
    public override int NonQueryExecuted(DbCommand command,CommandExecutedEventData data,int result){End(command,data);return result;}
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command,CommandExecutedEventData data,int result,CancellationToken ct=default){End(command,data);return ValueTask.FromResult(result);}
    public override object? ScalarExecuted(DbCommand command,CommandExecutedEventData data,object? result){End(command,data);return result;}
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command,CommandExecutedEventData data,object? result,CancellationToken ct=default){End(command,data);return ValueTask.FromResult(result);}
    private static readonly object Gate=new();
    public static async Task Measure(HttpContext context,RequestDelegate next,string run)
    {
        var sample=new Sample();Current.Value=sample;var timer=Stopwatch.StartNew();
        try{await next(context);}finally{
            timer.Stop();Current.Value=null;
            var row=JsonSerializer.Serialize(new{method=context.Request.Method,path=context.Request.Path.Value,elapsedMs=timer.Elapsed.TotalMilliseconds,sqlMs=sample.SqlMs,sqlCommands=sample.Commands,userClaimsQueries=sample.UserClaims,connectionOpens=sample.Opens,connectionMs=sample.OpenMs,categoryQueries=sample.CategoryQueries,status=context.Response.StatusCode});
            lock(Gate)File.AppendAllText(Path.Combine(run,"http-metrics.jsonl"),row+Environment.NewLine);
        }
    }
}

public sealed class PerfConnections : DbConnectionInterceptor
{
    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData data, CancellationToken ct=default)
    {
        if(PerfMetrics.Current.Value is {} sample){sample.Opens++;sample.OpenMs+=data.Duration.TotalMilliseconds;}
        return Task.CompletedTask;
    }
}
