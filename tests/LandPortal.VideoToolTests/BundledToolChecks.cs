using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Infrastructure;

internal static class BundledToolChecks
{
    public static async Task RunAsync(string extractedTools)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows executable checks require Windows.");
        var run = Path.Combine(Path.GetTempPath(), "LandPortal-BundledVideoTools-" + Guid.NewGuid().ToString("N"));
        var web = Path.Combine(run, "package", "web");
        var runtime = Path.Combine(web, ".runtime");
        var tools = Path.Combine(runtime, "tools", "ffmpeg");
        var external = Path.Combine(run, "package", "tools", "ffmpeg");
        var pathTools = Path.Combine(run, "path-tools");
        var held = Path.Combine(run, "held");
        foreach (var directory in new[] { tools, external, pathTools, held }) Directory.CreateDirectory(directory);
        string[] names = ["ffmpeg.exe", "ffprobe.exe", "package-origin.json", "LICENSE", "README.txt"];
        foreach (var name in names) File.Copy(Path.Combine(extractedTools, name), Path.Combine(tools, name));
        // Deliberately invalid fallback binaries must never execute when the bundle is incomplete.
        foreach (var directory in new[] { external, pathTools })
            foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" }) File.WriteAllText(Path.Combine(directory, name), "not an executable");
        var checks = new List<string>();
        void Check(bool result, string name)
        {
            if (!result) throw new InvalidOperationException(name);
            checks.Add(name);
            Console.WriteLine("PASS " + name);
        }
        string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        var records = names.Select(name => new { path = ".runtime/tools/ffmpeg/" + name, bytes = new FileInfo(Path.Combine(tools, name)).Length, sha256 = Hash(Path.Combine(tools, name)) }).ToArray();
        var manifest = Path.Combine(runtime, "manifest.json");
        var validManifest = JsonSerializer.Serialize(new { files = records });
        File.WriteAllText(manifest, validManifest);
        var options = new VideoOptions { Enabled = true };
        var locator = new VideoToolLocator(web, pathTools);
        Check(locator.Find(options) == new VideoToolPaths(Path.Combine(tools, "ffmpeg.exe"), Path.Combine(tools, "ffprobe.exe")), "Embedded manifest-bound pair precedes sibling tools and PATH");
        Check(new VideoToolLocator(Path.Combine(runtime, "worker"), pathTools).Find(options) == locator.Find(options), "Web and legacy component agree on embedded pair");
        void Reject(string name)
        {
            try { locator.Find(options); throw new InvalidOperationException("Unexpected acceptance: " + name); }
            catch (VideoProcessingException e) { Check(e.Message == "VIDEO_TOOL_PACKAGE_INVALID", name); }
        }
        foreach (var name in names)
        {
            var file = Path.Combine(tools, name);
            var saved = Path.Combine(held, name);
            File.Move(file, saved);
            try { Reject("Missing " + name + " blocks without fallback or download"); }
            finally { File.Move(saved, file); }
            var originalLength = new FileInfo(file).Length;
            using (var stream = new FileStream(file, FileMode.Append, FileAccess.Write)) stream.WriteByte(0x42);
            try { Reject("Corrupt " + name + " blocks before executable invocation"); }
            finally { using var stream = new FileStream(file, FileMode.Open, FileAccess.Write); stream.SetLength(originalLength); }
        }
        foreach (var name in names) File.Move(Path.Combine(tools, name), Path.Combine(held, name));
        try
        {
            Reject("Manifest still requires bundled files when every tool is missing");
            foreach (var bad in new[] { "{\"files\":42}", "{\"files\":[{\"path\":17}]}", "{}", "not-json" })
            {
                File.WriteAllText(manifest, bad);
                Reject("Malformed manifest cannot trigger ordinary-development fallback: " + bad);
            }
        }
        finally
        {
            foreach (var name in names) File.Move(Path.Combine(held, name), Path.Combine(tools, name));
            File.WriteAllText(manifest, validManifest);
        }
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { files = records.Concat([records[0]]) }));
        try { Reject("Duplicate executable record rejected"); } finally { File.WriteAllText(manifest, validManifest); }
        File.WriteAllText(manifest, validManifest.Replace("\"bytes\":" + records[0].bytes, "\"bytes\":1", StringComparison.Ordinal));
        try { Reject("Length mismatch rejected independently of SHA"); } finally { File.WriteAllText(manifest, validManifest); }
        File.Move(manifest, Path.Combine(held, "manifest.json"));
        try { Reject("Missing manifest in existing embedded runtime never falls back"); }
        finally { File.Move(Path.Combine(held, "manifest.json"), manifest); }

        var legacyWeb = Path.Combine(run, "legacy", "web");
        var legacyRuntime = Path.Combine(legacyWeb, ".runtime");
        var legacyTools = Path.Combine(run, "legacy", "tools", "ffmpeg");
        Directory.CreateDirectory(legacyRuntime);
        Directory.CreateDirectory(legacyTools);
        File.WriteAllText(Path.Combine(legacyRuntime, "manifest.json"), "{\"files\":[]}");
        foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" }) File.WriteAllText(Path.Combine(legacyTools, name), "legacy discovery only");
        Check(new VideoToolLocator(legacyWeb, pathTools).Find(options).Ffmpeg == Path.Combine(legacyTools, "ffmpeg.exe"), "Legacy bundle without native package retains sibling discovery");
        Check(new VideoToolLocator(Path.Combine(run, "ordinary"), pathTools).Find(options).Ffmpeg == Path.Combine(pathTools, "ffmpeg.exe"), "Ordinary unbundled development retains bounded PATH discovery");

        var storage = new StorageOptions { MediaRoot = Path.Combine(run, "media"), TempRoot = Path.Combine(run, "temp"), KeysRoot = Path.Combine(run, "keys"), SnapshotRoot = Path.Combine(run, "snapshots") };
        foreach (var directory in new[] { storage.MediaRoot, storage.TempRoot, storage.KeysRoot, storage.SnapshotRoot }) Directory.CreateDirectory(directory);
        var health = await new VideoProcessor(options, storage, locator).HealthAsync();
        Check(health.Ready && health.Code == "VIDEO_PROCESSOR_CONFIGURED", "Actual packaged executables run version and required encoder checks");
        Check(health.FfmpegVersion?.StartsWith("9.0.2-", StringComparison.Ordinal) == true && health.FfprobeVersion?.StartsWith("9.0.2-", StringComparison.Ordinal) == true, "Both real tools report pinned 9.0.2 release");
        Check(health.FfmpegSha256 == records[0].sha256 && health.FfprobeSha256 == records[1].sha256, "Observed runtime hashes match packaged binary manifest");
        var badPin = await new VideoProcessor(new VideoOptions { Enabled = true, FfmpegSha256 = new string('0', 64) }, storage, locator).HealthAsync();
        Check(!badPin.Ready && badPin.Code == "VIDEO_PROCESSOR_HASH_MISMATCH", "Existing optional hash pin remains effective");
        var result = new { passed = checks.Count, checks, run, sqlConnections = 0, iisTouched = false, runtimeDownloads = 0, fullRegression = false };
        File.WriteAllText(Path.Combine(run, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, run }));
    }
}
