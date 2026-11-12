using System.Security.Cryptography;
using System.Text.Json;

namespace LandPortal.Infrastructure;

public sealed record VideoToolPaths(string? Ffmpeg, string? Ffprobe);

// Discovery only: no download, executable invocation, recursive disk scan or trust grant.
public sealed class VideoToolLocator(string? baseDirectory = null, string? searchPath = null)
{
    private static readonly string[] BundledFiles = ["ffmpeg.exe", "ffprobe.exe", "package-origin.json", "LICENSE", "README.txt"];
    private const string ManifestPrefix = ".runtime/tools/ffmpeg/";

    public string FixedDirectory => LocateDirectory().Directory;

    private (string Directory, PortablePackageLayout? Bundle) LocateDirectory()
    {
        var component = new DirectoryInfo(baseDirectory ?? AppContext.BaseDirectory);
        PortablePackageLayout? layout = null;
        // Only an unknown component layout is ordinary development. Malformed bundle
        // metadata must never be caught here and redirected to PATH.
        try { layout = PortablePackageLayout.Resolve(component.FullName); }
        catch (InvalidOperationException) { }
        if (layout?.WebBundle == true)
        {
            var embedded = Path.Combine(layout.Root, "tools", "ffmpeg");
            if (HasBundledTools(layout, embedded)) return (embedded, layout);
            return (Path.Combine(Directory.GetParent(layout.WebRoot)!.FullName, "tools", "ffmpeg"), null);
        }
        if (Directory.Exists(Path.Combine(component.FullName, ".runtime")))
            throw new VideoProcessingException("VIDEO_TOOL_PACKAGE_INVALID", true);
        var root = component.Name is "web" or "worker" ? component.Parent! : component;
        return (Path.Combine(root.FullName, "tools", "ffmpeg"), null);
    }

    public VideoToolPaths Find(VideoOptions options)
    {
        var location = LocateDirectory();
        if (location.Bundle != null) VerifyBundledTools(location.Bundle, location.Directory);
        return new(FindOne("ffmpeg.exe", options.FfmpegPath, location.Directory),
            FindOne("ffprobe.exe", options.FfprobePath, location.Directory));
    }

    private static bool HasBundledTools(PortablePackageLayout layout,string directory)
    {
        if(BundledFiles.Any(name => File.Exists(Path.Combine(directory, name)))) return true;
        try {
            RequireNoLinks(layout.Manifest);
            using var document=JsonDocument.Parse(File.ReadAllBytes(layout.Manifest));
            return document.RootElement.GetProperty("files").EnumerateArray().Any(x =>
                BundledFiles.Any(name => x.GetProperty("path").GetString() == ManifestPrefix + name));
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) {
            throw new VideoProcessingException("VIDEO_TOOL_PACKAGE_INVALID",true);
        }
    }

    private string? FindOne(string name, string legacyOverride, string fixedDirectory)
    {
        // Preserve explicit existing installations. Paths/hashes are optional, never required.
        if (!string.IsNullOrWhiteSpace(legacyOverride)) return Checked(legacyOverride);
        var fixedPath = Path.Combine(fixedDirectory, name);
        if (File.Exists(fixedPath)) return Checked(fixedPath);
        foreach (var entry in (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator).Take(128).Select(x => x.Trim().Trim('"')).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entry) || !Path.IsPathFullyQualified(entry) || entry.StartsWith(@"\\", StringComparison.Ordinal)) continue;
            var candidate = Path.Combine(entry, name);
            if (File.Exists(candidate)) return Checked(candidate);
        }
        return null;
    }

    private static void VerifyBundledTools(PortablePackageLayout layout, string directory)
    {
        try {
            RequireNoLinks(layout.Manifest);
            using var document=JsonDocument.Parse(File.ReadAllBytes(layout.Manifest));
            var files = document.RootElement.GetProperty("files").EnumerateArray().ToArray();
            foreach (var name in BundledFiles)
            {
                var path = Path.Combine(directory, name);
                RequireNoLinks(path);
                var records = files.Where(x => x.GetProperty("path").GetString() == ManifestPrefix + name).ToArray();
                if (records.Length != 1 || !File.Exists(path)) throw new InvalidOperationException();
                using var stream = File.OpenRead(path);
                if (records[0].GetProperty("bytes").GetInt64() != stream.Length ||
                    records[0].GetProperty("sha256").GetString() != Convert.ToHexStringLower(SHA256.HashData(stream)))
                    throw new InvalidOperationException();
            }
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException) {
            throw new VideoProcessingException("VIDEO_TOOL_PACKAGE_INVALID",true);
        }
    }

    private static string? Checked(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new VideoProcessingException("VIDEO_TOOL_PATH_UNSAFE", true);
        RequireNoLinks(path);
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    internal static void RequireNoLinks(string path)
    {
        for (string? node = Path.GetFullPath(path); node != null; node = Path.GetDirectoryName(node))
            if ((File.Exists(node) || Directory.Exists(node)) && (File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0)
                throw new VideoProcessingException("VIDEO_TOOL_PATH_UNSAFE", true);
    }
}

public sealed record VideoToolHealth(bool Ready, string Code, bool FfmpegFound = false, bool FfprobeFound = false,
    string? FfmpegVersion = null, string? FfprobeVersion = null, string? FfmpegSha256 = null, string? FfprobeSha256 = null,
    bool BinaryChanged = false);
