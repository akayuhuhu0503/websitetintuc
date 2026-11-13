using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

static class MaintenanceChecks
{
    public static async Task RunAsync()
    {
        var checks = 0;
        void Check(bool pass, string text) { if (!pass) throw new Exception(text); checks++; Console.WriteLine("PASS " + text); }
        var scanner = new OfflineScanner(); var scopes = new ScannerOnlyScopes(scanner);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mail:Transport"] = "Disabled" }).Build();
        var environment = new TestEnvironment();
        using (var maintenance = new WebMaintenanceService(scopes, new InteractionMailOptions(), config, environment, NullLogger<WebMaintenanceService>.Instance))
        {
            await maintenance.StartAsync(CancellationToken.None);
            await scanner.Called.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(5200); // Includes a second polling tick while scanner remains unavailable.
            Check(scopes.ProhibitedCalls == 0 && scopes.Calls == 1, "Scanner outage plus Disabled mail resolves only scanner, no SQL or mail service");
            Check(scanner.Scans == 0, "No scanner call or fabricated scan result for queued files");
            Check(!maintenance.ExecuteTask!.IsFaulted, "Scanner outage keeps Web background loop alive");
            await maintenance.StopAsync(CancellationToken.None);
            Check(maintenance.ExecuteTask.IsCompletedSuccessfully, "Maintenance delay cancels promptly on Web shutdown");
        }
        var failedFactory = new ScannerOnlyScopes(scanner, true);
        using (var maintenance = new WebMaintenanceService(failedFactory, new InteractionMailOptions(), config, environment, NullLogger<WebMaintenanceService>.Instance))
        {
            await maintenance.StartAsync(CancellationToken.None);
            await failedFactory.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(100);
            Check(failedFactory.ProhibitedCalls == 0 && !maintenance.ExecuteTask!.IsFaulted, "Scanner factory failure is isolated and cannot crash Web or resolve SQL");
            await maintenance.StopAsync(CancellationToken.None);
        }
        var nonexistent = Path.Combine(Path.GetTempPath(), "landportal-disabled-video-" + Guid.NewGuid().ToString("N"));
        var storage = new StorageOptions { MediaRoot = nonexistent, TempRoot = nonexistent, SnapshotRoot = nonexistent, KeysRoot = nonexistent };
        using (var video = new WebVideoProcessingService(scopes, new VideoOptions(), storage, config, environment, NullLogger<WebVideoProcessingService>.Instance))
        {
            await video.StartAsync(CancellationToken.None); await video.ExecuteTask!;
            Check(scopes.Calls == 1 && scopes.ProhibitedCalls == 0 && !Directory.Exists(nonexistent), "Video disabled opens no queue, tool, heartbeat or storage directory");
        }
        Console.WriteLine($"RESULT MAINTENANCE_COMPLETE checks={checks} sqlConnections=0 smtpConnections=0");
    }
    sealed class ScannerOnlyScopes(IInquiryScanner scanner, bool fail = false) : IServiceScopeFactory
    {
        public int Calls;
        public int ProhibitedCalls;
        public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IServiceScope CreateScope() { Interlocked.Increment(ref Calls); return new Scope(this); }
        sealed class Scope(ScannerOnlyScopes owner) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public void Dispose() { }
            public object? GetService(Type type)
            {
                if (type != typeof(IInquiryScanner)) { Interlocked.Increment(ref owner.ProhibitedCalls); throw new InvalidOperationException("Only scanner resolution permitted"); }
                owner.Attempted.TrySetResult();
                if (owner.Fail) throw new IOException("Scanner unavailable fixture");
                return owner.Scanner;
            }
        }
        private bool Fail => fail;
        private IInquiryScanner Scanner => scanner;
    }
    sealed class OfflineScanner : IInquiryScanner
    {
        public int Scans;
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<(bool Ready, string Code)> HealthAsync(CancellationToken cancellation = default) { Called.TrySetResult(); return Task.FromResult((false, "ScannerNotReady")); }
        public Task<InquiryScanResult> ScanAsync(Stream file, CancellationToken cancellation = default) { Interlocked.Increment(ref Scans); throw new InvalidOperationException("Unavailable scanner must not be invoked"); }
    }
    sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "LandPortal.IsolatedMaintenanceTest";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
