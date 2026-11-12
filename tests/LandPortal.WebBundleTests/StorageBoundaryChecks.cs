using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;
using LandPortal.Infrastructure;
using Microsoft.Extensions.Configuration;

internal static class StorageBoundaryChecks
{
    public static void Run()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows filesystem/ACL test required.");
        var fixture = Path.Combine(Path.GetTempPath(), "LandPortal-StorageBoundary-" + Guid.NewGuid().ToString("N"));
        var package = Path.Combine(fixture, "WWW_GDLA", "publish", "TTDL-IIS");
        var web = Path.Combine(package, "web");
        var runtime = Path.Combine(web, ".runtime");
        var home = Path.Combine(package, "private");
        var isolation = Path.Combine(fixture, "native-shape-appPools");
        const string pool = "WWW_GDLA";
        var poolFile = Path.Combine(isolation, pool, pool + ".config");
        var legacyFile = Path.Combine(isolation, pool + ".config");
        Directory.CreateDirectory(Path.Combine(runtime, "private"));
        Directory.CreateDirectory(Path.Combine(runtime, "worker"));
        Directory.CreateDirectory(Path.GetDirectoryName(poolFile)!);
        foreach (var store in new[] { "media", "temp", "keys", "snapshots" }) Directory.CreateDirectory(Path.Combine(home, store));

        var checks = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("FAIL " + description);
            checks++;
            Console.WriteLine("PASS " + description);
        }
        void Denied(Action action, string code, string description)
        {
            try { action(); }
            catch (IisConfigurationException error)
            {
                Check(error.Code == "IIS-CONFIG-003-" + code, description + " (" + error.Code + ")");
                Check(!error.Message.Contains(fixture, StringComparison.OrdinalIgnoreCase), "Safe diagnostic does not disclose the filesystem path");
                return;
            }
            throw new InvalidOperationException("FAIL accepted: " + description);
        }

        var configFile = Path.Combine(runtime, "private", "uat.json");
        File.WriteAllText(configFile, "{\"Deployment\":{\"Profile\":\"UAT\"}}");
        var relativeFiles = new[] { "Web.dll", "Infrastructure.dll", "web.config", ".runtime/worker/Worker.dll", ".runtime/worker/Infrastructure.dll" };
        foreach (var relative in relativeFiles) File.WriteAllText(Path.Combine(web, relative), "Synthetic binding fixture: " + relative);
        var manifestFile = Path.Combine(runtime, "manifest.json");
        var manifest = JsonSerializer.Serialize(new
        {
            privateMode = "WebBundle",
            privateConfiguration = new { path = "private/uat.json", confidential = true, sha256 = Hash(configFile) },
            files = relativeFiles.Select(relative => new { path = relative, sha256 = Hash(Path.Combine(web, relative)) }).ToArray()
        });
        File.WriteAllText(manifestFile, manifest);
        var layout = PortablePackageLayout.Resolve(web);
        IConfigurationRoot Config()
        {
            var config = layout.LoadBoundSettings();
            layout.SetRuntimeLocations(config);
            return config;
        }
        static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        XDocument Metadata(string appPool, string mappedWeb, params string[] additionalRoots)
        {
            XElement Application(string path, string physicalPath) => new("application",
                new XAttribute("path", path), new XAttribute("applicationPool", appPool),
                new XElement("virtualDirectory", new XAttribute("path", "/"), new XAttribute("physicalPath", physicalPath)));
            var site = new XElement("site", new XAttribute("name", "fixture-site"), Application("/", mappedWeb));
            foreach (var (root, index) in additionalRoots.Select((root, index) => (root, index)))
                site.Add(Application("/fixture-" + index, root));
            return new XDocument(new XElement("configuration",
                new XElement("system.applicationHost", new XElement("sites", site))));
        }
        void SaveMetadata(string appPool = pool, string? mappedWeb = null, params string[] additionalRoots) =>
            File.WriteAllText(poolFile, Metadata(appPool, mappedWeb ?? web, additionalRoots).ToString());
        void Validate(IConfiguration? config = null) => StorageOptions.FromConfiguration(config ?? Config()).PortableBoundary!.EnsureValidated();

        var oldPoolFile = Environment.GetEnvironmentVariable("APP_POOL_CONFIG");
        var oldPool = Environment.GetEnvironmentVariable("LANDPORTAL_IIS_APP_POOL");
        try
        {
            Environment.SetEnvironmentVariable("LANDPORTAL_IIS_APP_POOL", null);
            Environment.SetEnvironmentVariable("APP_POOL_CONFIG", poolFile);
            SaveMetadata();
            Check(IisStorageMetadata.ReadPool(isolation, pool, web).SequenceEqual(new[] { web }), "Named pool reads modern WAS layout without global appcmd");
            Validate();
            Check(true, "Real Windows publish/private layout passes shared storage validator with bound Web/Worker files");
            var workerLayout = PortablePackageLayout.Resolve(Path.Combine(runtime, "worker") + Path.DirectorySeparatorChar);
            Check(workerLayout == layout, "Worker trailing separator resolves exactly the same bundle, web and private roots");
            Check(IisStorageMetadata.ReadPool(isolation, pool, web + Path.DirectorySeparatorChar).Single() == web, "Named pool comparison canonicalizes webroot separator");

            File.Move(poolFile, legacyFile);
            Check(IisStorageMetadata.ReadPool(isolation, pool, web).Single() == web, "Historical flat native WAS layout remains supported");
            File.Move(legacyFile, poolFile);
            Denied(() => IisStorageMetadata.ReadPool(isolation, "OTHER_POOL", web), "IIS_METADATA_MISSING", "Wrong selector never enumerates or borrows another pool");
            Denied(() => IisStorageMetadata.ReadPool(isolation, "../WWW_GDLA", web), "IIS_METADATA_INVALID", "Pool selector cannot traverse filesystem");
            SaveMetadata("OTHER_POOL");
            Denied(() => IisStorageMetadata.ReadPool(isolation, pool, web), "IIS_MAPPING", "Readable metadata must bind the selected pool");
            SaveMetadata(pool, Path.Combine(package, "other-web"));
            Denied(() => IisStorageMetadata.ReadPool(isolation, pool, web), "IIS_MAPPING", "Readable metadata must bind exact deployed webroot");
            Denied(() => Validate(), "IIS_MAPPING", "Shared storage rejects wrong IIS webroot, distinct from permissions");

            SaveMetadata(pool, web, home);
            Denied(() => Validate(), "IIS_MAPPING", "IIS virtual directory exposing private is blocked");
            SaveMetadata(pool, web, Path.Combine(home, "media"));
            Denied(() => Validate(), "IIS_MAPPING", "IIS mapping exposing a private child is blocked");
            SaveMetadata(pool, web, package);
            Denied(() => Validate(), "IIS_MAPPING", "IIS mapping exposing an ancestor of private is blocked");
            SaveMetadata(pool, web, runtime);
            Denied(() => Validate(), "IIS_MAPPING", "IIS mapping exposing embedded runtime is blocked");
            try { MediaService.ValidateStorage(StorageOptions.FromConfiguration(Config())); throw new InvalidOperationException("Unsafe runtime mapping accepted by upload service"); }
            catch (MediaUploadException error) { Check(error.StatusCode == 503, "Runtime upload retains MediaUploadException 503 contract for unsafe boundary"); }
            SaveMetadata();
            var wrongStore = Config(); wrongStore["Storage:MediaRoot"] = Path.Combine(web, "media");
            Denied(() => Validate(wrongStore), "STORAGE_PATH", "Storage inside IIS web remains forbidden");
            var nested = Config(); nested["Storage:TempRoot"] = Path.Combine(home, "media", "nested-temp");
            Denied(() => Validate(nested), "STORAGE_PATH", "Nested private stores remain forbidden");
            var duplicate = Config(); duplicate["Storage:KeysRoot"] = duplicate["Storage:TempRoot"];
            Denied(() => Validate(duplicate), "STORAGE_PATH", "Duplicate private stores remain forbidden");
            var wrongHome = Config(); wrongHome["WorkspaceRoot"] = Path.Combine(package, "other-private");
            Denied(() => Validate(wrongHome), "STORAGE_PATH", "Wrong private home cannot borrow manifest boundary");
            var production = Config(); production["DOTNET_ENVIRONMENT"] = "Production";
            Denied(() => Validate(production), "STORAGE_PATH", "Portable publish exception remains Staging/UAT only");

            var preTamper = Config();
            File.AppendAllText(Path.Combine(web, "Infrastructure.dll"), "altered");
            Denied(() => Validate(preTamper), "STORAGE_MANIFEST", "Mixed release binary fails manifest binding before IIS discovery");
            File.WriteAllText(Path.Combine(web, "Infrastructure.dll"), "Synthetic binding fixture: Infrastructure.dll");
            var preConfigTamper = Config();
            File.WriteAllText(configFile, "{}");
            Denied(() => Validate(preConfigTamper), "STORAGE_MANIFEST", "Edited private JSON cannot bypass manifest integrity");
            File.WriteAllText(configFile, "{\"Deployment\":{\"Profile\":\"UAT\"}}");
            File.WriteAllText(poolFile, "<configuration><broken>");
            Denied(() => Validate(), "IIS_METADATA_INVALID", "Malformed live XML is not treated as a missing manifest");
            File.WriteAllText(poolFile, "<!DOCTYPE configuration [<!ENTITY ext SYSTEM 'file:///not-read'>]><configuration>&ext;</configuration>");
            Denied(() => Validate(), "IIS_METADATA_INVALID", "External XML entity is rejected");
            SaveMetadata(pool, "relative/web");
            Denied(() => Validate(), "IIS_METADATA_INVALID", "Relative IIS physicalPath is rejected");
            File.WriteAllText(poolFile, new string('x', 1024 * 1024 + 1));
            Denied(() => Validate(), "IIS_METADATA_INVALID", "Metadata reader has a strict size bound");
            File.Delete(poolFile);
            Denied(() => Validate(), "IIS_METADATA_MISSING", "Explicit APP_POOL_CONFIG missing file does not fall back to appcmd");
            SaveMetadata();

            // Real NTFS access denial, solely on this test's file; restore even on assertion failure.
            // A valid legacy file exists to prove an unreadable preferred file never triggers fallback.
            File.WriteAllText(legacyFile, Metadata(pool, web).ToString());
            var file = new FileInfo(poolFile);
            var savedDacl = file.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            File.WriteAllText(Path.Combine(fixture, "original-test-metadata-dacl.sddl"), savedDacl);
            var deniedAcl = file.GetAccessControl();
            deniedAcl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny));
            try
            {
                file.SetAccessControl(deniedAcl);
                Denied(() => IisStorageMetadata.ReadPool(isolation, pool, web), "IIS_METADATA_ACCESS_DENIED", "Actual NTFS denial is specific and does not fall back to legacy XML");
                Denied(() => Validate(), "IIS_METADATA_ACCESS_DENIED", "Actual NTFS denial reaches shared boundary without becoming mapping/hash error");
            }
            finally
            {
                var restored = new FileSecurity();
                restored.SetSecurityDescriptorSddlForm(savedDacl, AccessControlSections.Access);
                file.SetAccessControl(restored);
            }
            Validate();
            Check(true, "Exact file read permission restoration makes the same boundary pass without manifest/data changes");
            using var finalManifest = JsonDocument.Parse(manifest);
            Check(Hash(configFile) == finalManifest.RootElement.GetProperty("privateConfiguration").GetProperty("sha256").GetString(), "Final fixture private configuration hash remains unchanged");
            Check(relativeFiles.All(relative => File.ReadAllText(Path.Combine(web, relative)) == "Synthetic binding fixture: " + relative), "All fixture Web/Worker bytes restored; no operational files used");
        }
        finally
        {
            Environment.SetEnvironmentVariable("APP_POOL_CONFIG", oldPoolFile);
            Environment.SetEnvironmentVariable("LANDPORTAL_IIS_APP_POOL", oldPool);
        }
        Console.WriteLine($"STORAGE_BOUNDARY_CHECKS_PASS={checks}; sqlOpened=false; realIisChanged=false; fixture={fixture}");
    }
}
