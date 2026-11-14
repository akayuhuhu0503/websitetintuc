using System.Diagnostics;
using System.Security;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace LandPortal.Infrastructure;

// Always read live IIS-owned metadata. No manifest flag or cached snapshot can replace it.
internal static class IisStorageMetadata
{
    const int MaximumCharacters = 1024 * 1024;
    static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    internal static IisConfigurationException Failure(string code) =>
        new("IIS-CONFIG-003-" + code, PortableIisConfiguration.StartupHint(code));

    internal static string[] ReadRoots(string expectedWebRoot)
    {
        // A scheduled service does not inherit the pool SID or APP_POOL_CONFIG.
        // Explicit pool selection reads only its WAS-generated filtered configuration.
        var requestedPool = Environment.GetEnvironmentVariable("LANDPORTAL_IIS_APP_POOL");
        if (!string.IsNullOrWhiteSpace(requestedPool))
            return ReadPool(IsolationRoot(), requestedPool, expectedWebRoot);
        var poolFile = Environment.GetEnvironmentVariable("APP_POOL_CONFIG");
        if (!string.IsNullOrWhiteSpace(poolFile))
            return Roots(ReadFile(poolFile)); // Do not hide access denial with File.Exists/fallback.
        return ReadAppCmd();
    }

    static string IsolationRoot()
    {
        if (!OperatingSystem.IsWindows()) throw Failure("IIS_METADATA_UNAVAILABLE");
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WAS\Parameters", false);
            var configured = key?.GetValue("ConfigIsolationPath", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var path = configured is null
                ? Path.Combine(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!, "inetpub", "temp", "appPools")
                : configured as string ?? throw Failure("IIS_METADATA_INVALID");
            path = Environment.ExpandEnvironmentVariables(path);
            if (!Path.IsPathFullyQualified(path)) throw Failure("IIS_METADATA_INVALID");
            return Full(path);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException) { throw Failure("IIS_METADATA_ACCESS_DENIED"); }
    }

    internal static string[] ReadPool(string isolationRoot, string pool, string expectedWebRoot)
    {
        if (string.IsNullOrWhiteSpace(pool) || pool != pool.Trim() || pool is "." or ".." ||
            pool.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || pool.Contains('/') || pool.Contains('\\') ||
            pool.Contains(':') || pool.EndsWith('.')) throw Failure("IIS_METADATA_INVALID");
        var root = Full(isolationRoot);
        XDocument? document = null;
        // Modern and legacy native WAS layouts; never enumerate other pools or accept a copy in web/private.
        foreach (var path in new[] { Path.Combine(root, pool, pool + ".config"), Path.Combine(root, pool + ".config") })
        {
            try { document = ReadFile(path); break; }
            catch (IisConfigurationException e) when (e.Code == "IIS-CONFIG-003-IIS_METADATA_MISSING") { }
        }
        if (document is null) throw Failure("IIS_METADATA_MISSING");
        var web = Full(expectedWebRoot);
        var bound = document.Descendants("application").Any(a =>
            string.Equals((string?)a.Attribute("applicationPool"), pool, StringComparison.OrdinalIgnoreCase) &&
            a.Elements("virtualDirectory").Any(v => (string?)v.Attribute("path") == "/" &&
                string.Equals(PhysicalPath(v), web, StringComparison.OrdinalIgnoreCase)));
        if (!bound) throw Failure("IIS_MAPPING");
        return Roots(document);
    }

    static void NoLinks(string path)
    {
        for (string? p = Full(path); p != null; p = Path.GetDirectoryName(p))
        {
            try { if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw Failure("IIS_METADATA_INVALID"); }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { }
        }
    }

    static XDocument ReadFile(string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)) throw Failure("IIS_METADATA_INVALID");
            NoLinks(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length is 0 or > MaximumCharacters) throw Failure("IIS_METADATA_INVALID");
            using var reader = XmlReader.Create(stream, XmlSettings());
            return XDocument.Load(reader);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { throw Failure("IIS_METADATA_MISSING"); }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException) { throw Failure("IIS_METADATA_ACCESS_DENIED"); }
        catch (Exception e) when (e is XmlException or ArgumentException or NotSupportedException) { throw Failure("IIS_METADATA_INVALID"); }
        catch (IOException) { throw Failure("IIS_METADATA_UNAVAILABLE"); }
    }

    static XmlReaderSettings XmlSettings() => new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumCharacters };
    static string PhysicalPath(XElement node)
    {
        var path = Environment.ExpandEnvironmentVariables((string?)node.Attribute("physicalPath") ?? "");
        if (!Path.IsPathFullyQualified(path)) throw Failure("IIS_METADATA_INVALID");
        return Full(path);
    }
    static string[] Roots(XDocument doc)
    {
        var roots = doc.Descendants("virtualDirectory").Select(PhysicalPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length == 0) throw Failure("IIS_METADATA_INVALID");
        return roots;
    }

    static string[] ReadAppCmd()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var executable = Path.Combine(windows, Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32", "inetsrv", "appcmd.exe");
        if (!File.Exists(executable)) throw Failure("IIS_METADATA_UNAVAILABLE");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "list", "vdir", "/xml" }) start.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(start) ?? throw Failure("IIS_METADATA_UNAVAILABLE");
            var stdout = ReadBounded(process.StandardOutput); var stderr = ReadBounded(process.StandardError);
            if (!process.WaitForExit(10000)) { process.Kill(true); throw Failure("IIS_METADATA_UNAVAILABLE"); }
            var text = stdout.GetAwaiter().GetResult(); var error = stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
                throw Failure(text.Contains("80070005", StringComparison.OrdinalIgnoreCase) || error.Contains("80070005", StringComparison.OrdinalIgnoreCase) || process.ExitCode == 5
                    ? "IIS_METADATA_ACCESS_DENIED" : "IIS_METADATA_UNAVAILABLE");
            using var reader = XmlReader.Create(new StringReader(text), XmlSettings());
            var roots = XDocument.Load(reader).Descendants("VDIR").Select(PhysicalPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (roots.Length == 0) throw Failure("IIS_METADATA_INVALID");
            return roots;
        }
        catch (System.ComponentModel.Win32Exception e) { throw Failure(e.NativeErrorCode == 5 ? "IIS_METADATA_ACCESS_DENIED" : "IIS_METADATA_UNAVAILABLE"); }
        catch (XmlException) { throw Failure("IIS_METADATA_INVALID"); }
    }
    static async Task<string> ReadBounded(StreamReader reader)
    {
        var buffer = new char[4096]; var result = new System.Text.StringBuilder(); int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            if (result.Length + read > MaximumCharacters) throw Failure("IIS_METADATA_INVALID");
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }
}
