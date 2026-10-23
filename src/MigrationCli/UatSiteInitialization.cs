using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace LandPortal.MigrationCli;

// Explicit operator provisioning of reference metadata only. Never an import,
// account grant, automatic web startup action or a relaxation of local guards.
public static class UatSiteInitialization
{
    public static void RequireAuthority(string[] args,IConfiguration config,string environment)
    {
        string? Option(string name){var n=Array.IndexOf(args,name);return n>=0&&n+1<args.Length?args[n+1]:null;}
        if(!UatDeployment.Validate(config,environment)||Option("--confirm-target")!=config["Sql:ExpectedDatabase"]||Option("--approval-reference")!=config["Deployment:TargetApprovalReference"])
            throw new InvalidOperationException("Explicit approved isolated UAT target, exact confirmation and allocation authority reference required; no Dev/Test/Production initialization.");
    }
    public static async Task ExecuteAsync(string[] args,IConfiguration config,PortalDbContext db,MediaUploadOptions media)
    {
        RequireAuthority(args,config,Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??"Production");
        await UatPreflightCommand.VerifySqlTargetAsync(db,config);
        var catalog=SourceCatalog.Categories.Select((c,i)=>new Category{Id=c.Id,Name=c.Name,Slug=c.Slug,SourceUrl=SourceCatalog.BaseUrl+c.Path,SortOrder=i}).ToArray();
        var created=await new SiteConfigurationService(db,StorageOptions.FromConfiguration(config),media).InitializeAsync("uat-operator",catalog);
        Console.WriteLine(created?"UAT reference catalog and initial site configuration created atomically; no source content, Identity or grants imported.":"UAT site configuration already exists; no catalog/configuration overwrite.");
    }
}
