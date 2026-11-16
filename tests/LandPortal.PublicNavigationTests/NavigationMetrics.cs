using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore.Diagnostics;
using LandPortal.Web.Services;

// Test host only: no SQL text, parameter values, cookies or secrets in the output.
public sealed class NavigationSample
{
    public string Phase="entry";
    public double DueMs,ActionMs,RazorMs,OpenMs,SqlMs;
    public int Opens,Commands;
    public List<object> Queries=[];
    public Dictionary<Guid,long> Opening=[];
}
public static class NavigationMetrics
{
    public static readonly AsyncLocal<NavigationSample?> Current=new();
    private static readonly object Gate=new();
    public static async Task Measure(HttpContext c,RequestDelegate next,string run)
    {
        var s=new NavigationSample();Current.Value=s;var watch=Stopwatch.StartNew();var sample=Guid.NewGuid().ToString("N");
        c.Response.OnStarting(()=>{c.Response.Headers["X-Navigation-Sample"]=sample;c.Response.Headers["Server-Timing"]=FormattableString.Invariant($"due;dur={s.DueMs:F2},action;dur={s.ActionMs:F2},open;dur={s.OpenMs:F2},ef;dur={s.SqlMs:F2},preheaders;dur={watch.Elapsed.TotalMilliseconds:F2}");return Task.CompletedTask;});
        try{await next(c);}finally{
            var json=JsonSerializer.Serialize(new{sample,path=c.Request.Path.Value,status=c.Response.StatusCode,elapsedMs=watch.Elapsed.TotalMilliseconds,s.DueMs,s.ActionMs,s.RazorMs,s.OpenMs,s.SqlMs,s.Opens,s.Commands,s.Queries});
            lock(Gate)File.AppendAllText(Path.Combine(run,"http.jsonl"),json+Environment.NewLine);
            Current.Value=null;
        }
    }
    public static async Task Due(HttpContext c,RequestDelegate next)
    {
        var s=Current.Value;var w=Stopwatch.StartNew();if(s!=null)s.Phase="due";
        await new DuePublicationMiddleware(async context=>{if(s!=null){s.DueMs=w.Elapsed.TotalMilliseconds;s.Phase="controller";}await next(context);})
            .InvokeAsync(c,c.RequestServices.GetRequiredService<WebPublicationScheduler>(),c.RequestServices.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>(),c.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DuePublicationMiddleware>>());
    }
}
public sealed class NavigationConnectionMetrics:DbConnectionInterceptor
{
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection c,ConnectionEventData d,InterceptionResult r,CancellationToken ct=default){if(NavigationMetrics.Current.Value is {} s)s.Opening[d.ConnectionId]=Stopwatch.GetTimestamp();return ValueTask.FromResult(r);}
    public override Task ConnectionOpenedAsync(DbConnection c,ConnectionEndEventData d,CancellationToken ct=default){if(NavigationMetrics.Current.Value is {} s){s.Opens++;if(s.Opening.Remove(d.ConnectionId,out var start))s.OpenMs+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;}return Task.CompletedTask;}
}
public sealed class NavigationCommandMetrics:DbCommandInterceptor
{
    static void End(DbCommand c,CommandExecutedEventData d){if(NavigationMetrics.Current.Value is not {} s)return;s.Commands++;s.SqlMs+=d.Duration.TotalMilliseconds;var kind=c.CommandText.Contains("PublicationJobs")?"schedule":c.CommandText.Contains("PublicVisit")?"visits":c.CommandText.Contains("AuditLogs")?"content/audit":c.CommandText.Contains("SiteConfiguration")||c.CommandText.Contains("SiteMenu")?"site/content":"content";s.Queries.Add(new{phase=s.Phase,kind,ms=d.Duration.TotalMilliseconds});}
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand c,CommandExecutedEventData d,DbDataReader r,CancellationToken ct=default){End(c,d);return ValueTask.FromResult(r);}
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand c,CommandExecutedEventData d,int r,CancellationToken ct=default){End(c,d);return ValueTask.FromResult(r);}
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand c,CommandExecutedEventData d,object? r,CancellationToken ct=default){End(c,d);return ValueTask.FromResult(r);}
}
public sealed class NavigationMvcMetrics:IAsyncActionFilter,IAsyncResultFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext c,ActionExecutionDelegate next){var w=Stopwatch.StartNew();try{await next();}finally{if(NavigationMetrics.Current.Value is {} s)s.ActionMs=w.Elapsed.TotalMilliseconds;}}
    public async Task OnResultExecutionAsync(ResultExecutingContext c,ResultExecutionDelegate next){var s=NavigationMetrics.Current.Value;if(s!=null)s.Phase="razor/response-start";var w=Stopwatch.StartNew();try{await next();}finally{if(s!=null)s.RazorMs=w.Elapsed.TotalMilliseconds;}}
}
