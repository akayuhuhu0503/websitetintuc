using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

Console.OutputEncoding = System.Text.Encoding.UTF8;
try
{
    var command = args.FirstOrDefault() ?? "help";
    if(command=="uat-sql-probe")return await UatSqlProbe.ExecuteAsync(args);
    var workspace = PortalConfiguration.FindWorkspace();
    if(command=="schema-script")
    {
        // Offline artifact only: no connection is opened and no migration applied.
        var output=Path.GetFullPath(Option("--output")??throw new InvalidDataException("--output required"),workspace);
        var allowed=Path.GetFullPath(Path.Combine(workspace,".local/publish"))+Path.DirectorySeparatorChar;
        if(!output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||File.Exists(output))throw new InvalidDataException("New private publish SQL artifact required");
        // Model-only generation also works after a clean Git clone without .env,
        // credentials or allocated SQL. This context is never opened/migrated.
        await using var offline=new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer("Server=127.0.0.1;Database=LandPortal_OfflineSchemaOnly;Integrated Security=true;Encrypt=true").Options);
        var migrator=offline.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        var sql=":ON ERROR EXIT\n;THROW 51000, 'UNALLOCATED UAT TARGET: bind a new SQLCMD artifact using prepare-uat-target.ps1 after operator approval. Do not execute this template.', 1;\nGO\n-- LANDPORTAL_SCHEMA_BODY\n"+migrator.GenerateScript(options:Microsoft.EntityFrameworkCore.Migrations.MigrationsSqlGenerationOptions.Idempotent);
        if(Option("--target-database") is string target)
        {
            var server=Option("--server-identity")??throw new InvalidDataException("DBA-confirmed --server-identity required; IP/port is not the physical SQL identity.");
            var header=UatSqlScripts.Header(target,server);
            sql=header+migrator.GenerateScript(options:Microsoft.EntityFrameworkCore.Migrations.MigrationsSqlGenerationOptions.Idempotent);
            var roleOutput=Path.GetFullPath(Option("--runtime-role-output")??throw new InvalidDataException("New --runtime-role-output required"),workspace);
            if(!roleOutput.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||roleOutput==output||File.Exists(roleOutput))throw new InvalidDataException("New separate private publish role artifact required");
            var login=Option("--app-login")??throw new InvalidDataException("--app-login required");UatSqlScripts.RequireLogin(login);
            Directory.CreateDirectory(Path.GetDirectoryName(roleOutput)!);await File.WriteAllTextAsync(roleOutput,UatSqlScripts.RuntimeRole(header,login));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);await File.WriteAllTextAsync(output,sql);Console.WriteLine("Offline idempotent schema script generated; SQL execution NOT RUN.");return 0;
    }
    var config = PortalConfiguration.Load(workspace);
    var storage = StorageOptions.FromConfiguration(config);
    if(command=="dev-demo-accounts"){await DevDemoAccounts.ExecuteAsync(args,config,workspace);return 0;}
    if(command=="uat-preflight")return await UatPreflightCommand.ExecuteAsync(args,config);
    if(command is "source-detail-plan" or "nukeviet-bundle")return await Phase3FCommand.ExecuteAsync(args,workspace);
    if(command is "source-audit" or "nukeviet-preview")return await Phase3DCommand.OfflineAsync(args,workspace);
    var manifestPath = Path.GetFullPath(Option("--manifest") ?? Path.Combine(storage.SnapshotRoot, "sample-manifest.json"), workspace);
    if (command == "help") { Console.WriteLine("sample --limit 40 --request-budget 350 [--refresh]; import|verify|reconcile --manifest <path>; cutover; bootstrap; migrate; site-initialize; backfill-search"); return 0; }
    await using var db = new PortalDbContext(DatabaseSettings.Options(config, forMigration: command is "migrate" or "migrate-schema-only"||command.StartsWith("safe-",StringComparison.Ordinal)));
    if(command=="migrate-schema-only")
    {
        var database=config["Sql:ExpectedDatabase"]!;
        await SchemaManager.VerifyLocalProjectAsync(db,database);
        await ApprovedBatchExecution.VerifyBackupAsync(db,workspace,database,Option("--backup-receipt")??throw new InvalidDataException("Verified project backup receipt required"));
        var applied=(await db.Database.GetAppliedMigrationsAsync()).LastOrDefault();var pending=(await db.Database.GetPendingMigrationsAsync()).ToArray();
        if(applied!=Option("--from")||pending.Length!=1||pending[0]!=Option("--migration"))throw new InvalidDataException("Schema differs from the exact reviewed additive migration; stop without schema/permission/backfill writes.");
        await SchemaManager.MigrateAsync(db,database);
        Console.WriteLine("Exact local schema migration applied: "+pending[0]+"; runtime grants, Identity, source backfill and content import NOT performed.");return 0;
    }
    if(command=="name-decision-test")
    {
        await Phase3DCommand.ApplyNameDecisionTestAsync(db,config,workspace,Option("--decision")??throw new InvalidDataException("--decision required"),Option("--decision-sha")??throw new InvalidDataException("--decision-sha required"),Option("--backup-receipt")??throw new InvalidDataException("--backup-receipt required"));
        Console.WriteLine("Explicit FR17 name decision applied on Test; content and menu publications unchanged.");return 0;
    }
    if (command.StartsWith("safe-", StringComparison.Ordinal)) return await SafeBatchCommand.ExecuteAsync(args, workspace, config, db);
    if (command.StartsWith("recovery-", StringComparison.Ordinal)) return await RecoveryCommand.ExecuteAsync(args, workspace, config, db);
    if (command.StartsWith("batch-", StringComparison.Ordinal)) return await BatchCommand.ExecuteAsync(args, workspace, config, db);
    if (command.StartsWith("inventory-", StringComparison.Ordinal)) return await InventoryCommand.ExecuteAsync(args, workspace, config, db);
    if (command == "migrate") { await SchemaManager.MigrateAsync(db, config["Sql:ExpectedDatabase"]!); var created = await SearchProjectionService.BackfillAsync(db); await RuntimeRoleInterceptor.ProvisionAsync(db, config["Runtime:RolePassword"]!); Console.WriteLine("Project database migration applied; search projection backfill rows=" + created + "."); return 0; }
    if (command == "backfill-search") { await SchemaManager.VerifyLocalProjectAsync(db, config["Sql:ExpectedDatabase"]!); Console.WriteLine("Search projection rows created=" + await SearchProjectionService.BackfillAsync(db) + "; immutable source revisions unchanged."); return 0; }
    if (command == "site-initialize") {
        var media=MediaUploadOptions.FromConfiguration(config,Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")??"Production");
        if(UatDeployment.Enabled(config)){await UatSiteInitialization.ExecuteAsync(args,config,db,media);return 0;}
        await SchemaManager.VerifyLocalProjectAsync(db, config["Sql:ExpectedDatabase"]!); var result = await new SiteConfigurationService(db, storage,media).InitializeAsync("migration-cli"); Console.WriteLine("Site configuration initialized=" + result + "; existing live/draft configuration is preserved."); return 0;
    }
    if (command == "bootstrap")
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddDataProtection();
        services.AddScoped<PortalDbContext>(_ => new PortalDbContext(DatabaseSettings.Options(config)));
        services.AddIdentity<PortalUser, IdentityRole>(PortalIdentityPolicy.Configure).AddEntityFrameworkStores<PortalDbContext>().AddDefaultTokenProviders();
        using var provider = services.BuildServiceProvider(); await BootstrapIdentity.InitializeAsync(provider, config);
        Console.WriteLine("Explicit Identity bootstrap completed; no credentials logged."); return 0;
    }
    if (command == "cutover")
    {
        var setting = await db.SiteSettings.FindAsync("SyncEnabled");
        if (setting is null) db.SiteSettings.Add(new() { Key = "SyncEnabled", Value = "false" }); else setting.Value = "false";
        var when = await db.SiteSettings.FindAsync("CutoverUtc");
        if (when is null) db.SiteSettings.Add(new() { Key = "CutoverUtc", Value = DateTimeOffset.UtcNow.ToString("O") });
        db.AuditLogs.Add(new() { Actor = "migration-cli", Action = "Cutover", Detail = "Source synchronization disabled durably." });
        await db.SaveChangesAsync(); Console.WriteLine("Source synchronization disabled in SQL; restart preserves cutover."); return 0;
    }
    var manifest = File.Exists(manifestPath) ? JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(manifestPath), SampleCollector.JsonOptions)! : new SampleManifest();
    if (manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported manifest schema version.");
    if (command is "sample" or "repair-assets" or "normalize-offline")
    {
        await SynchronizationGuard.EnsureEnabledAsync(db, config);
        var limit = int.Parse(Option("--limit") ?? "40"); var budget = int.Parse(Option("--request-budget") ?? "350");
        if (limit is < 1 or > 50 || budget is < 1 or > 500) throw new InvalidOperationException("Phase-1 bounds: content limit 1–50 and request budget 1–500.");
        using var cancel = new CancellationTokenSource(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        var checkpointImporter = new SqlImporter(db, storage);
        var collector = new SampleCollector(storage, manifestPath, manifest, args.Contains("--refresh"), m => checkpointImporter.PersistCrawlAsync(m, CancellationToken.None));
        if (command == "normalize-offline") await collector.NormalizeOfflineAsync(cancel.Token);
        else if (command == "repair-assets") await collector.RepairAssetsAsync(budget, cancel.Token);
        else await collector.RunAsync(limit, budget, cancel.Token);
        await new SqlImporter(db, storage).PersistCrawlAsync(manifest, CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(new { manifest.Status, Staged = manifest.Contents.Count, UniqueAssets = manifest.Assets.Select(x => x.Id).Distinct().Count(), manifest.RequestsUsed, Issues = manifest.Issues.Count }, SampleCollector.JsonOptions));
        return manifest.Status == "Failed" ? 1 : 0;
    }
    var importer = new SqlImporter(db, storage);
    if (command == "import") await SynchronizationGuard.EnsureEnabledAsync(db, config);
    object report = command == "import" ? await importer.ImportAsync(manifest) : command is "verify" or "reconcile" ? await importer.VerifyAsync(manifest) : throw new InvalidOperationException("Unknown CLI command.");
    var reportJson = JsonSerializer.Serialize(report, SampleCollector.JsonOptions);
    Directory.CreateDirectory(Path.Combine(workspace, "docs/evidence"));
    await File.WriteAllTextAsync(Path.Combine(workspace, "docs/evidence/" + command + "-report.json"), reportJson);
    await SourceReports.WriteAsync(manifest, storage, db, Path.Combine(workspace, "docs/evidence"));
    Console.WriteLine(reportJson);
    using var reportDocument = JsonDocument.Parse(reportJson);
    var failed = reportDocument.RootElement.TryGetProperty("Failed", out var failures) && failures.GetInt32() > 0;
    return reportJson.Contains("VerificationFailed") || failed ? 1 : 0;
}
catch (Exception exception)
{
    // Connection values are intentionally omitted. A detailed private SQL exception stays out of public output.
    Console.Error.WriteLine("Migration CLI failed: " + exception.GetType().Name + ": " + exception.Message); return 1;
}
string? Option(string name)
{
    var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
