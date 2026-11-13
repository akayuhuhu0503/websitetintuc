param([Parameter(Mandatory=$true)][string]$DotnetPath,[ValidateSet('All','VideoAndScreens','Screens','WeeklyOnly')][string]$Focus='All')
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $root
$id=[Guid]::NewGuid().ToString('N').Substring(0,12)
$instance='LPCms_'+$id
$run=Join-Path $root ('.local/tests/cms-editor-'+$id)
[void][IO.Directory]::CreateDirectory($run)
$hostRoot=Join-Path $run 'host';[void][IO.Directory]::CreateDirectory($hostRoot)
$program=[IO.File]::ReadAllText((Join-Path $root 'src/Web/Program.cs'))
# Compile the real middleware/DI/routes from this checkout, not a substitute controller pipeline.
# Only configuration source and the explicitly authorized test-only media policy differ.
$old='var builder = WebApplication.CreateBuilder(args);'
if(!$program.Contains($old)){throw 'Web startup anchor changed; review test host generation.'}
$program=$program.Replace($old,'var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args=args, ApplicationName=typeof(LandPortal.Web.Controllers.AdminController).Assembly.GetName().Name });')
$old='try{config=PortalConfiguration.Load();}'
if(!$program.Contains($old)){throw 'Configuration anchor changed.'}
$program=$program.Replace($old,'try{config=new ConfigurationBuilder().AddEnvironmentVariables().Build();}')
$old='var app = builder.Build();'
if(!$program.Contains($old)){throw 'Service anchor changed.'}
$inject=@'
var testSql=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);
var testRun=builder.Configuration["ManagedHttpTestRoot"] ?? throw new InvalidOperationException("Test root required");
if(!System.Text.RegularExpressions.Regex.IsMatch(testSql.DataSource,@"^\(localdb\)\\LPCms_[a-f0-9]{12}$") || testSql.InitialCatalog!="LandPortal_Test" || !testSql.IntegratedSecurity || builder.Environment.EnvironmentName!="Development" || !publicUri.IsLoopback || !Path.GetFullPath(storage.MediaRoot).StartsWith(Path.GetFullPath(testRun)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || builder.Configuration["Mail:Transport"]!="Disabled") throw new InvalidOperationException("Isolated HTTP test host only");
Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<MediaUploadOptions>(builder.Services);
builder.Services.AddSingleton(new MediaUploadOptions { ApprovedManagedImageWithoutAv=true, ApprovedManagedDocumentsWithoutAv=true });
var app = builder.Build();
'@
$program=$program.Replace($old,$inject)
[IO.File]::WriteAllText((Join-Path $hostRoot 'Program.cs'),$program,[Text.UTF8Encoding]::new($false))
$web=[Security.SecurityElement]::Escape((Join-Path $root 'src/Web/Web.csproj'))
[IO.File]::WriteAllText((Join-Path $hostRoot 'HttpHost.csproj'),('<Project Sdk="Microsoft.NET.Sdk.Web"><ItemGroup><ProjectReference Include="'+$web+'" /></ItemGroup></Project>'))
$dotnet=(Resolve-Path -LiteralPath $DotnetPath).Path
& $dotnet build (Join-Path $hostRoot 'HttpHost.csproj') -c Release --artifacts-path (Join-Path $run 'build')
if($LASTEXITCODE){throw 'Test host build failed; no SQL instance created.'}
& $dotnet build (Join-Path $PSScriptRoot 'LandPortal.CmsEditorTests.csproj') -c Release --artifacts-path (Join-Path $run 'build')
if($LASTEXITCODE){throw 'Focused runner build failed; no SQL instance created.'}
& SqlLocalDB.exe create $instance -s
if($LASTEXITCODE){throw 'New LocalDB instance unavailable.'}
try {
 & $dotnet (Join-Path $run 'build/bin/LandPortal.CmsEditorTests/release/LandPortal.CmsEditorTests.dll') $instance $run (Join-Path $run 'build/bin/HttpHost/release/HttpHost.dll') $dotnet $Focus
 $result=$LASTEXITCODE
} finally {
 # Stop only the generated instance. Retain its DB and receipt for inspection; no DROP/delete.
 & SqlLocalDB.exe stop $instance
}
Write-Output ('Isolated evidence: '+$run)
exit $result
