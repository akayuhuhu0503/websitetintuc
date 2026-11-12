using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.Extensions.Configuration;

internal static class LocatorRuntimeChecks
{
    public static async Task RunAsync(string repo)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("These checks require Windows path semantics.");
        var run = Path.Combine(Path.GetTempPath(), "LandPortal-IisToolDiscovery-" + Guid.NewGuid().ToString("N"));
        var package = Path.Combine(run, "WWW_GDLA", "publish", "TTDL-IIS");
        var web = Path.Combine(package, "web");
        var runtime = Path.Combine(web, ".runtime");
        var worker = Path.Combine(runtime, "worker");
        var fixedTools = Path.Combine(package, "tools", "ffmpeg");
        var data = Path.Combine(package, "private");
        foreach (var path in new[] { web, runtime, worker, fixedTools, data }) Directory.CreateDirectory(path);
        // Discovery reads only the existence of this marker; authorization still belongs to the package validator.
        File.WriteAllText(Path.Combine(runtime, "manifest.json"), "{}");
        var checks = new List<string>();
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            checks.Add(name);
            Console.WriteLine("PASS " + name);
        }

        var settings = new Dictionary<string, string?>
        {
            ["Deployment:Profile"] = "UAT", ["DOTNET_ENVIRONMENT"] = "Staging", ["ASPNETCORE_ENVIRONMENT"] = "Staging",
            ["Deployment:ExpectedSqlServer"] = "isolated-sql.invalid", ["Deployment:ExpectedSqlServerIdentity"] = "ISOLATED-SQL",
            ["Deployment:SqlAuthentication"] = "Windows", ["Sql:ExpectedDatabase"] = "LandPortal_UatFixture",
            ["ConnectionStrings:Portal"] = "Server=isolated-sql.invalid;Database=LandPortal_UatFixture;Integrated Security=True;Encrypt=True;TrustServerCertificate=False",
            ["Runtime:RolePassword"] = "SyntheticTestValueNotAnOperationalSecret",
            ["Public:BaseUrl"] = "https://uat.invalid/", ["Deployment:ExpectedHost"] = "uat.invalid",
            ["WorkspaceRoot"] = data,
            ["Storage:MediaRoot"] = Path.Combine(data, "media"), ["Storage:SnapshotRoot"] = Path.Combine(data, "snapshots"),
            ["Storage:TempRoot"] = Path.Combine(data, "temp"), ["Storage:KeysRoot"] = Path.Combine(data, "keys")
        };
        using var json = new MemoryStream(Encoding.UTF8.GetBytes("{\"Video\":{\"Enabled\":true,\"AllowManagedVideoWithoutAv\":true}}"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).AddJsonStream(json).Build();
        Check(config["Video:Enabled"] == "True", "Actual JSON provider reproduces capitalized boolean value");
        var options = VideoOptions.FromConfiguration(config, "Staging");
        Check(options.Enabled && options.ApprovedManagedVideoWithoutAv, "JSON boolean enables video through unchanged UAT technical guards");
        var storage = StorageOptions.FromConfiguration(config);
        foreach (var root in new[] { storage.MediaRoot, storage.SnapshotRoot, storage.TempRoot, storage.KeysRoot }) Directory.CreateDirectory(root);

        foreach (var component in new[] { web, web + "\\", worker, worker + "\\" })
        {
            var locator = new VideoToolLocator(component, "");
            Console.WriteLine(JsonSerializer.Serialize(new { fixtureComponent = Path.GetRelativePath(package, component), expectedTools = fixedTools, actualTools = locator.FixedDirectory }));
            Check(locator.FixedDirectory == fixedTools, "WebBundle component resolves sibling tools: " + Path.GetRelativePath(package, component));
            Check(PortablePackageLayout.Resolve(component).WebRoot == web, "Component ending separators retain canonical Web root");
        }
        const string targetTools = @"D:\WWW_GDLA\publish\TTDL-IIS\tools\ffmpeg";
        Check(new VideoToolLocator(@"D:\WWW_GDLA\publish\TTDL-IIS\web\", "").FixedDirectory == targetTools,
            "Literal IIS D drive Web path resolves expected sibling tools (path-only)");
        Check(new VideoToolLocator(@"D:\WWW_GDLA\publish\TTDL-IIS\web\.runtime\worker\", "").FixedDirectory == targetTools,
            "Literal IIS D drive Worker path resolves expected sibling tools (path-only)");

        // Use installed binaries already recorded by the isolated test environment; never download or redistribute.
        using var approved = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo, ".local", "video-tools.json")));
        foreach (var name in new[] { "ffmpeg", "ffprobe" })
        {
            var source = approved.RootElement.GetProperty(name + "Path").GetString()!;
            var expected = approved.RootElement.GetProperty(name + "Sha256").GetString();
            Check(await VideoUploadService.HashFileAsync(source) == expected, "Installed " + name + " matches existing local test evidence");
            var destination = Path.Combine(fixedTools, name + ".exe");
            File.Copy(source, destination, false);
            Check(await VideoUploadService.HashFileAsync(destination) == expected, "Isolated tool copy retains " + name + " bytes");
        }
        var webLocator = new VideoToolLocator(web + "\\", "");
        var workerLocator = new VideoToolLocator(worker + "\\", "");
        var found = webLocator.Find(options);
        Check(found.Ffmpeg == Path.Combine(fixedTools, "ffmpeg.exe") && found.Ffprobe == Path.Combine(fixedTools, "ffprobe.exe"),
            "Tools found in actual mirrored Windows publish tree without PATH or overrides");
        Check(workerLocator.Find(options) == found, "Worker discovers exactly the same binaries as Web");
        var health = await new VideoProcessor(options, storage, webLocator).HealthAsync();
        Check(health.Ready && health.FfmpegFound && health.FfprobeFound && health.FfmpegVersion != null && health.FfprobeVersion != null,
            "Real FFmpeg/FFprobe version and H264 AAC JPEG encoder execution passes");
        Check(health.FfmpegSha256 == approved.RootElement.GetProperty("ffmpegSha256").GetString()
              && health.FfprobeSha256 == approved.RootElement.GetProperty("ffprobeSha256").GetString(),
            "Observed runtime hashes match installed bytes");
        var disabled = await new VideoProcessor(new VideoOptions(), storage, webLocator).HealthAsync();
        Check(disabled.Code == "VIDEO_DISABLED" && !disabled.Ready && !disabled.FfmpegFound && !disabled.FfprobeFound,
            "Disabled video reports not checked rather than authorizing execution");
        var badPin = await new VideoProcessor(new VideoOptions { Enabled = true, FfmpegSha256 = new string('0', 64) }, storage, webLocator).HealthAsync();
        Check(!badPin.Ready && badPin.Code == "VIDEO_PROCESSOR_HASH_MISMATCH", "Optional pinned hash guard remains enforced");
        var missing = await new VideoProcessor(options, storage, new VideoToolLocator(Path.Combine(run, "missing"), "")).HealthAsync();
        Check(!missing.Ready && missing.Code == "VIDEO_PROCESSOR_NOT_AVAILABLE", "Actual missing tools still block processing");
        var result = new { passed = checks.Count, checks, run, sqlConnections = 0, iisTouched = false, fullRegression = false };
        File.WriteAllText(Path.Combine(run, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, run }));
    }
}
