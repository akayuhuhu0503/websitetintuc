param([Parameter(Mandatory=$true)][string]$DotnetPath,[ValidateSet('baseline','baseline-near','after','video')][string]$Label='baseline',[ValidateSet('Full','Near','Video','Ux','UxHome')][string]$Focus='Full',[string]$BaselineHost='', [string]$VideoTools='none')
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $root
$id=[Guid]::NewGuid().ToString('N').Substring(0,12)
$instance='LPPerf_'+$id
$run=Join-Path $root ('.local/tests/perf-upload-'+$id)
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
if(!System.Text.RegularExpressions.Regex.IsMatch(testSql.DataSource,@"^\(localdb\)\\LPPerf_[a-f0-9]{12}$") || testSql.InitialCatalog!="LandPortal_Test" || !testSql.IntegratedSecurity || builder.Environment.EnvironmentName!="Development" || !publicUri.IsLoopback || !Path.GetFullPath(storage.MediaRoot).StartsWith(Path.GetFullPath(testRun)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || builder.Configuration["Mail:Transport"]!="Disabled") throw new InvalidOperationException("Isolated HTTP test host only");
Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<MediaUploadOptions>(builder.Services);
builder.Services.AddSingleton(new MediaUploadOptions { ApprovedManagedImageWithoutAv=true, ApprovedManagedDocumentsWithoutAv=true });
if(builder.Configuration["PerfVideo"]=="true"){
Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<VideoOptions>(builder.Services);
builder.Services.AddSingleton(new VideoOptions{Enabled=true,ApprovedManagedVideoWithoutAv=true,MaxFileBytes=10*1024*1024,FfmpegPath=builder.Configuration["Video:FfmpegPath"]!,FfprobePath=builder.Configuration["Video:FfprobePath"]!,FfmpegSha256=builder.Configuration["Video:FfmpegSha256"]!,FfprobeSha256=builder.Configuration["Video:FfprobeSha256"]!});
}
var app = builder.Build();
'@
$program=$program.Replace($old,$inject)
$program=$program.Replace('builder.Services.AddScoped<DbContextOptions<PortalDbContext>>(_ => DatabaseSettings.Options(builder.Configuration));','builder.Services.AddScoped<DbContextOptions<PortalDbContext>>(_ => new DbContextOptionsBuilder<PortalDbContext>(DatabaseSettings.Options(builder.Configuration)).AddInterceptors(new PerfMetrics(), new PerfConnections()).Options);')
$program=$program.Replace('var app = builder.Build();','var app = builder.Build(); app.Use((context,next) => PerfMetrics.Measure(context,next,testRun));')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PerfMetrics.cs') -Destination (Join-Path $hostRoot 'PerfMetrics.cs')

[IO.File]::WriteAllText((Join-Path $hostRoot 'Program.cs'),$program,[Text.UTF8Encoding]::new($false))
$web=[Security.SecurityElement]::Escape((Join-Path $root 'src/Web/Web.csproj'))
[IO.File]::WriteAllText((Join-Path $hostRoot 'HttpHost.csproj'),('<Project Sdk="Microsoft.NET.Sdk.Web"><ItemGroup><ProjectReference Include="'+$web+'" /></ItemGroup></Project>'))
$dotnet=(Resolve-Path -LiteralPath $DotnetPath).Path
if(!$BaselineHost){ & $dotnet build (Join-Path $hostRoot 'HttpHost.csproj') -c Release --artifacts-path (Join-Path $run 'build')
if($LASTEXITCODE){throw 'Test host build failed; no SQL instance created.'} }
& $dotnet build (Join-Path $PSScriptRoot 'LandPortal.UploadPerformanceTests.csproj') -c Release --artifacts-path (Join-Path $run 'build')
if($LASTEXITCODE){throw 'Focused runner build failed; no SQL instance created.'}
if($BaselineHost){
 if(!(Test-Path -LiteralPath (Join-Path $BaselineHost 'HttpHost.dll')) -or !(Resolve-Path -LiteralPath $BaselineHost).Path.StartsWith((Join-Path $root '.local/tests/perf-upload-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Baseline fixture binary required.'}
 $destination=Join-Path $run 'build/bin/HttpHost/release';[void][IO.Directory]::CreateDirectory($destination)
 Copy-Item -Path (Join-Path $BaselineHost '*') -Destination $destination -Recurse
}
& SqlLocalDB.exe create $instance -s
if($LASTEXITCODE){throw 'New LocalDB instance unavailable.'}
try {
 & $dotnet (Join-Path $run 'build/bin/LandPortal.UploadPerformanceTests/release/LandPortal.UploadPerformanceTests.dll') $instance $run (Join-Path $run 'build/bin/HttpHost/release/HttpHost.dll') $dotnet $Focus $VideoTools
 $result=$LASTEXITCODE
} finally {
 # Stop only the generated instance. Retain its DB and receipt for inspection; no DROP/delete.
 & SqlLocalDB.exe stop $instance
}
Write-Output ('Isolated evidence: '+$run); [IO.File]::WriteAllText((Join-Path $PSScriptRoot ('../../.local/perf-'+$Label+'-current.txt')),$run)
exit $result
