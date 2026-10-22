namespace LandPortal.Tests;
public static class TestEvidencePaths
{
    public static string At(string workspace,params string[] parts)
    {
        var overrideRoot=Environment.GetEnvironmentVariable("LandPortal__EvidenceRoot");
        var root=overrideRoot is null?Path.Combine(workspace,".local","evidence"):Path.GetFullPath(overrideRoot,workspace);
        if(overrideRoot!=null&&!new[]{"phase2c","phase2d","phase3a","phase3b","phase3c","phase3d","phase3e","phase3f"}.Any(phase=>root.StartsWith(Path.Combine(workspace,".local","evidence",phase)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Evidence must stay in a unique approved project run directory.");
        return Path.Combine([root,..parts]);
    }
}
