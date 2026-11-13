using System.Diagnostics;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace LandPortal.Web.Services;

// One coordinator for request catch-up and polling. SQL leases still fence other
// Web processes. Public visitors do not grant rights: the saved scheduling actor,
// exact revision/hash, approval and media are rechecked by SchedulerService.
public sealed class WebPublicationScheduler(IServiceScopeFactory scopes)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string workerId = "web-" + Guid.NewGuid().ToString("N");

    public async Task<int> DrainDueAsync(int maximumJobs = 16, CancellationToken ct = default, TimeSpan? maximumGateWait = null)
    {
        if (maximumJobs is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(maximumJobs));
        if (maximumGateWait.HasValue)
        {
            if (!await gate.WaitAsync(maximumGateWait.Value, ct)) return 0;
        }
        else await gate.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<SchedulerService>();
            var watch = Stopwatch.StartNew();
            var handled = 0;
            do
            {
                ct.ThrowIfCancellationRequested();
                var lease = await scheduler.ClaimAsync(workerId, ct);
                if (lease == null) break;
                await scheduler.ExecuteAsync(lease, ct, allowLateWebCatchUp: true);
                handled++;
                // A soft work budget between transactions, not a transaction timeout.
                // An unfinished backlog is retained for the next poll/request.
            } while (handled < maximumJobs && watch.Elapsed < TimeSpan.FromSeconds(2));
            return handled;
        }
        finally { gate.Release(); }
    }

    public async Task HeartbeatAsync(string environment, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SchedulerService>().HeartbeatAsync(workerId, environment, ct);
    }
}

public sealed class WebPublicationBackgroundService(WebPublicationScheduler scheduler, SchedulerOptions options,
    IHostEnvironment environment, ILogger<WebPublicationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await scheduler.HeartbeatAsync(environment.EnvironmentName, stoppingToken);
                await scheduler.DrainDueAsync(16, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e)
            { logger.LogWarning("Web publication polling failed: {ErrorType}; SQL leases retained", e.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

public sealed class DuePublicationMiddleware(RequestDelegate next)
{
    public static bool ChecksRequest(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) return false;
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        // Only content-bearing pages. Health, static/media, admin, lookup and public
        // form endpoints are not delayed by publication work.
        return action?.ControllerName is "Home" or "Disclosure" or "WeeklySchedule" or "Sitemap" ||
            action?.ControllerName == "Portal" && !context.Request.Path.StartsWithSegments("/tep-nguon");
    }

    public async Task InvokeAsync(HttpContext context, WebPublicationScheduler scheduler, IHostApplicationLifetime lifetime,
        ILogger<DuePublicationMiddleware> logger)
    {
        if (ChecksRequest(context))
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            // A visitor disconnect must not cancel a claimed, approved publication
            // and consume its retry budget. Shutdown still cancels the host's work.
            try { await scheduler.DrainDueAsync(8, lifetime.ApplicationStopping, TimeSpan.FromSeconds(2)); }
            catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested) { throw; }
            catch (Exception e)
            { logger.LogWarning("Request publication catch-up failed: {ErrorType}; only committed content is served", e.GetType().Name); }
        }
        if (context.RequestAborted.IsCancellationRequested) return;
        await next(context);
    }
}
