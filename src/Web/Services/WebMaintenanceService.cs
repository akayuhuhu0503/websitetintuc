using LandPortal.Infrastructure;

namespace LandPortal.Web.Services;

// Reuses durable queues and their SQL claim/lease guards. A recycled host never
// treats an uncertain SMTP acceptance as a reason to send the message again.
public sealed class WebMaintenanceService(
    IServiceScopeFactory scopes, InteractionMailOptions mail,
    IConfiguration configuration, IHostEnvironment environment,
    ILogger<WebMaintenanceService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(ScanLoopAsync(stoppingToken), MailLoopAsync(stoppingToken));

    private async Task ScanLoopAsync(CancellationToken ct)
    {
        var lastHealth=DateTimeOffset.MinValue;
        var lastSweep=DateTimeOffset.MinValue;
        var ready=false;
        IInquiryScanner? scanner=null;
        while(!ct.IsCancellationRequested)
        {
            try
            {
                if(DateTimeOffset.UtcNow-lastHealth>=TimeSpan.FromSeconds(30))
                {
                    // Scanner factory errors must not stop Web startup or approved
                    // synchronous no-AV CMS uploads.
                    if(scanner==null){await using var scope=scopes.CreateAsyncScope();scanner=scope.ServiceProvider.GetRequiredService<IInquiryScanner>();}
                    ready=(await scanner.HealthAsync(ct)).Ready;
                    lastHealth=DateTimeOffset.UtcNow;
                }
                // Approved CMS no-AV uploads are validated synchronously and do not
                // enter these scan queues. Citizen attachments/legacy quarantine
                // still require a real scanner; an outage consumes no attempts.
                if(ready)
                {
                    EditorialDocumentService.RequireTestInfrastructure(configuration,environment.EnvironmentName);
                    await using(var scope=scopes.CreateAsyncScope())
                        await scope.ServiceProvider.GetRequiredService<EditorialDocumentService>().ScanOnceAsync(scanner!,ct:ct);
                    await using(var scope=scopes.CreateAsyncScope())
                    {
                        var files=scope.ServiceProvider.GetRequiredService<InquiryAttachmentService>();
                        if(DateTimeOffset.UtcNow-lastSweep>=TimeSpan.FromMinutes(5))
                        {
                            await files.SweepAsync();
                            lastSweep=DateTimeOffset.UtcNow;
                        }
                        await scope.ServiceProvider.GetRequiredService<InquiryScanWorker>().RunOnceAsync(ct:ct);
                    }
                }
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception e){logger.LogWarning("File queue iteration failed: {ErrorType}; unverified files stay private",e.GetType().Name);}
            if(!await DelayAsync(TimeSpan.FromSeconds(5),ct))break;
        }
    }

    private async Task MailLoopAsync(CancellationToken ct)
    {
        if(!mail.Available)return; // Disabled stays disabled; no SMTP connection or SQL claim.
        while(!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                // Existing sender has a bounded SMTP deadline. Let an in-flight
                // send finish during normal shutdown; crash recovery is Uncertain.
                await scope.ServiceProvider.GetRequiredService<InteractionOutboxService>().RunOnceAsync();
            }
            catch(Exception e){logger.LogWarning("Mail queue iteration failed: {ErrorType}; durable acceptance state retained",e.GetType().Name);}
            if(!await DelayAsync(TimeSpan.FromSeconds(2),ct))break;
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay,CancellationToken ct)
    {
        try{await Task.Delay(delay,ct);return true;}
        catch(OperationCanceledException)when(ct.IsCancellationRequested){return false;}
    }
}
