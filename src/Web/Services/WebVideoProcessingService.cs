using LandPortal.Infrastructure;

namespace LandPortal.Web.Services;

// Uses the existing SQL queue/lease, not an in-memory queue. Multiple web processes
// and an overlapping recycle still share VideoWorker's database exclusion.
public sealed class WebVideoProcessingService(
    IServiceScopeFactory scopes, VideoOptions options, StorageOptions storage,
    IConfiguration configuration, IHostEnvironment environment,
    ILogger<WebVideoProcessingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        IInquiryScanner? scanner = null;
        var processor = new VideoProcessor(options, storage);
        var health = new VideoToolHealth(false, "VIDEO_PROCESSOR_NOT_AVAILABLE");
        var scannerReady = false;
        var lastHealth = DateTimeOffset.MinValue;
        var lastSweep = DateTimeOffset.MinValue;

        void Observe(string state)
        {
            try { VideoWorkerStatus.Write(storage, processor.LastHealth ?? health, scannerReady, options.ApprovedManagedVideoWithoutAv, state); }
            catch (Exception e)
            { logger.LogWarning("Video status unavailable: {ErrorType}", e.GetType().Name); }
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // A previously verified Ready output can be attached after a
                    // recycle even if FFmpeg/scanner is currently unavailable.
                    await using(var scope=scopes.CreateAsyncScope())
                    {
                        var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                        await new VideoDraftAttachmentService(db,storage,options,
                            scope.ServiceProvider.GetRequiredService<CurrentPermissionService>(),
                            scope.ServiceProvider.GetRequiredService<WorkflowService>(),environment.IsDevelopment()).ProcessReadyAsync(stoppingToken);
                    }
                    scanner ??= InquiryScannerFactory.Create(configuration, environment.EnvironmentName, options.MaxFileBytes);
                    if (DateTimeOffset.UtcNow - lastHealth >= TimeSpan.FromSeconds(30))
                    {
                        health = await processor.HealthAsync(stoppingToken);
                        scannerReady = (await scanner.HealthAsync(stoppingToken)).Ready;
                        lastHealth = DateTimeOffset.UtcNow;
                    }
                    Observe("Running");
                    // Tool/scanner outages must not consume attempts or turn Pending into Blocked.
                    if (health.Ready && (scannerReady || options.ApprovedManagedVideoWithoutAv))
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                        var rights = scope.ServiceProvider.GetRequiredService<CurrentPermissionService>();
                        if (DateTimeOffset.UtcNow - lastSweep >= TimeSpan.FromMinutes(5))
                        {
                            await new VideoUploadService(db, storage, options, rights).SweepAsync(stoppingToken);
                            lastSweep = DateTimeOffset.UtcNow;
                        }
                        await new VideoWorker(db, storage, options, scanner, processor, rights,development:environment.IsDevelopment()).RunOnceAsync(ct: stoppingToken);
                        // Subtitle policy stays scanner-required, even when video no-AV is enabled.
                        if (scannerReady)
                            await new VideoSubtitleService(db, storage, options, rights).ScanOnceAsync(scanner, ct: stoppingToken);
                        Observe("Running");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception e)
                {
                    Observe("Faulted");
                    // No raw exception message/SQL/path is sent to logs; keep the Web host available.
                    logger.LogWarning("Video processing iteration failed: {ErrorType}; durable lease retained", e.GetType().Name);
                }
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
        finally { Observe("Stopped"); }
    }
}
