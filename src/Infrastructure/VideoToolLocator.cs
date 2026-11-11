namespace LandPortal.Infrastructure;

public sealed record VideoToolPaths(string? Ffmpeg, string? Ffprobe);

// Discovery only: no download, executable invocation, recursive disk scan or trust grant.
public sealed class VideoToolLocator(string? baseDirectory = null, string? searchPath = null)
{
    public string FixedDirectory
    {
        get
        {
            var component = new DirectoryInfo(baseDirectory ?? AppContext.BaseDirectory);
            try {
                var layout=PortablePackageLayout.Resolve(component.FullName);
                if(layout.WebBundle)return Path.Combine(Directory.GetParent(layout.WebRoot)!.FullName,"tools","ffmpeg");
            }catch(InvalidOperationException){ /* Ordinary development layout. */ }
            var root = component.Name is "web" or "worker" ? component.Parent! : component;
            return Path.Combine(root.FullName, "tools", "ffmpeg");
        }
    }

    public VideoToolPaths Find(VideoOptions options) => new(
        FindOne("ffmpeg.exe", options.FfmpegPath), FindOne("ffprobe.exe", options.FfprobePath));

    private string? FindOne(string name, string legacyOverride)
    {
        // Preserve explicit existing installations. Paths/hashes are optional, never required.
        if (!string.IsNullOrWhiteSpace(legacyOverride)) return Checked(legacyOverride);
        var fixedPath = Path.Combine(FixedDirectory, name);
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
