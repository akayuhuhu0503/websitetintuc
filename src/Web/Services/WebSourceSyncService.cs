using LandPortal.Infrastructure;
namespace LandPortal.Web.Services;

public sealed class WebSourceSyncService(IServiceScopeFactory scopes,ILogger<WebSourceSyncService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Never crawl on startup's request path. A short SQL-only poll notices
        // an Admin request; durable AvailableUtc enforces the five-minute cycle.
        while(!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20),ct);
                await using var scope=scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SourceSyncRunner>().RunOnceAsync(ct);
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception e){logger.LogWarning("QLDD sync paused: {ErrorType}; queue retained",e.GetType().Name);}
        }
    }
}
