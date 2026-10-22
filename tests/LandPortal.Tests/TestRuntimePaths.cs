namespace LandPortal.Tests;
public static class TestRuntimePaths
{
    public static string Assembly(string workspace, string project)
    {
        var isolated = Environment.GetEnvironmentVariable("LandPortal__BuildRoot");
        if (string.IsNullOrEmpty(isolated)) return Path.Combine(workspace,"src",project,"bin","Debug","net10.0",project+".dll");
        var full = Path.GetFullPath(isolated,workspace);
        if (!full.StartsWith(Path.Combine(workspace,".local")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Isolated Test build must remain project local.");
        return Path.Combine(full,"bin",project,"debug",project+".dll");
    }
}
