using LandPortal.Infrastructure;
using LandPortal.Tests;

var root=PortalConfiguration.FindWorkspace();
var config=PortalConfiguration.Load(root);
try
{
    await OwnPasswordHttpTests.RunAsync(root,config,DatabaseSettings.Options(config,forMigration:true));
    return 0;
}
catch(Exception error)
{
    // Fixture failures contain assertion descriptions, never credentials or forms.
    Console.WriteLine("FAIL "+error.GetType().Name+": "+error.Message);
    return 1;
}
