param([Parameter(Mandatory=$true)][string]$DotnetPath,[Parameter(Mandatory=$true)][string]$PlaywrightModule,[ValidateSet('before','after','feedback')][string]$Phase='before',[string]$FixtureRoot='')
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $root
if(!$FixtureRoot){$FixtureRoot=Join-Path $root ('.local/tests/nav-'+[Guid]::NewGuid().ToString('N').Substring(0,12))}
$FixtureRoot=[IO.Path]::GetFullPath($FixtureRoot)
if(!$FixtureRoot.StartsWith((Join-Path $root '.local/tests/nav-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Isolated fixture path required'}
[void][IO.Directory]::CreateDirectory($FixtureRoot)
$buildRoot=Join-Path $FixtureRoot ($Phase+'-build-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$hostRoot=Join-Path $buildRoot 'host';[void][IO.Directory]::CreateDirectory($hostRoot)
$program=[IO.File]::ReadAllText((Join-Path $root 'src/Web/Program.cs'))
foreach($anchor in @('var builder = WebApplication.CreateBuilder(args);','try{config=PortalConfiguration.Load();}','builder.Services.AddScoped<DbContextOptions<PortalDbContext>>(_ => DatabaseSettings.Options(builder.Configuration));','var app = builder.Build();','app.UseMiddleware<DuePublicationMiddleware>();')){
 if(!$program.Contains($anchor)){throw 'Test host injection anchor changed; no SQL work'}
}
$program=$program.Replace('var builder = WebApplication.CreateBuilder(args);','var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args=args, ApplicationName=typeof(LandPortal.Web.Controllers.PortalController).Assembly.GetName().Name });')
$program=$program.Replace('try{config=PortalConfiguration.Load();}','try{config=new ConfigurationBuilder().AddEnvironmentVariables().Build();}')
$program=$program.Replace('builder.Services.AddScoped<DbContextOptions<PortalDbContext>>(_ => DatabaseSettings.Options(builder.Configuration));','builder.Services.AddScoped<DbContextOptions<PortalDbContext>>(_ => new DbContextOptionsBuilder<PortalDbContext>(DatabaseSettings.Options(builder.Configuration)).AddInterceptors(new NavigationConnectionMetrics(),new NavigationCommandMetrics()).Options);')
$inject=@'
var testSql=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);
var testRoot=builder.Configuration["NavigationTestRoot"] ?? throw new InvalidOperationException("Test root required");
var metricsRoot=builder.Configuration["NavigationMetricsRoot"]!;
if(!System.Text.RegularExpressions.Regex.IsMatch(testSql.DataSource,@"^\(localdb\)\\LPNav_[a-f0-9]{12}$")||testSql.InitialCatalog!="LandPortal_Test"||!testSql.IntegratedSecurity||!builder.Environment.IsDevelopment()||!publicUri.IsLoopback||builder.Configuration["Mail:Transport"]!="Disabled"||!Path.GetFullPath(storage.MediaRoot).StartsWith(Path.GetFullPath(testRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated navigation host only");
builder.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o=>o.Filters.Add(new NavigationMvcMetrics()));
// Measure request catch-up with real SQL. Polling is stopped only in this test
// host to make before/after request attribution repeatable; no operating switch.
foreach(var item in builder.Services.Where(x=>x.ServiceType==typeof(Microsoft.Extensions.Hosting.IHostedService)).ToArray())builder.Services.Remove(item);
var app = builder.Build();
app.Use((c,next)=>NavigationMetrics.Measure(c,next,metricsRoot));
'@
$program=$program.Replace('var app = builder.Build();',$inject).Replace('app.UseMiddleware<DuePublicationMiddleware>();','app.Use((c,next)=>NavigationMetrics.Due(c,next));')
[IO.File]::WriteAllText((Join-Path $hostRoot 'Program.cs'),$program,[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NavigationMetrics.cs') -Destination $hostRoot
$web=[Security.SecurityElement]::Escape((Join-Path $root 'src/Web/Web.csproj'))
[IO.File]::WriteAllText((Join-Path $hostRoot 'NavigationHost.csproj'),('<Project Sdk="Microsoft.NET.Sdk.Web"><ItemGroup><ProjectReference Include="'+$web+'" /></ItemGroup></Project>'))
$dotnet=(Resolve-Path $DotnetPath).Path
& $dotnet build (Join-Path $hostRoot 'NavigationHost.csproj') -c Release --artifacts-path (Join-Path $buildRoot 'artifacts') -v minimal
if($LASTEXITCODE){throw 'Host build failed; no SQL work'}
& $dotnet build (Join-Path $PSScriptRoot 'LandPortal.PublicNavigationTests.csproj') -c Release --artifacts-path (Join-Path $buildRoot 'artifacts') -v minimal
if($LASTEXITCODE){throw 'Runner build failed; no SQL work'}
& $dotnet (Join-Path $buildRoot 'artifacts/bin/LandPortal.PublicNavigationTests/release/LandPortal.PublicNavigationTests.dll') $Phase $FixtureRoot (Join-Path $buildRoot 'artifacts/bin/NavigationHost/release/NavigationHost.dll') $dotnet $PlaywrightModule
if($LASTEXITCODE){throw 'Measurement failed; inspect isolated report'}
Write-Output ('Fixture for comparison: '+$FixtureRoot)
