using System.Data;
using System.Data.Common;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

static class FocusedChecks
{
    public static async Task Run(string connection, string role, string fixture, HttpClient http, Guid article)
    {
        var count=0;
        void Check(bool ok,string name) { if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++; }
        var counter=new OpenCounter();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Runtime:RolePassword"]=role }).Build();
        var options=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(connection)
            .AddInterceptors(new RuntimeRoleInterceptor(config),counter).Options;
        await using var services=new ServiceCollection().AddScoped(_=>new PortalDbContext(options)).BuildServiceProvider();
        async Task Request(string controller, bool throws=false)
        {
            await using var scope=services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var context=new DefaultHttpContext { RequestServices=scope.ServiceProvider };context.Request.Method="GET";
            context.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(new ControllerActionDescriptor { ControllerName=controller }),"fixture"));
            var before=counter.Opens;
            try
            {
                await new PublicRequestConnectionMiddleware(async _=> {
                    if(controller=="Admin") { Check(db.Database.GetDbConnection().State==ConnectionState.Closed,"Admin does not open a public request connection");return; }
                    Check(db.Database.GetDbConnection().State==ConnectionState.Open,"Public scope retains one live connection");
                    Check(await db.Database.SqlQueryRaw<string>("SELECT USER_NAME() AS Value").SingleAsync()=="LandPortalRuntime","Actual application role active on the retained session");
                    await db.Contents.CountAsync();await db.Categories.CountAsync();
                    await using(var tx=await db.Database.BeginTransactionAsync()) { await db.Contents.CountAsync();await tx.CommitAsync(); }
                    if(throws)throw new FixtureException();
                }).InvokeAsync(context);
            }catch(FixtureException)when(throws) { Check(true,"Downstream exception propagates"); }
            Check(db.Database.GetDbConnection().State==ConnectionState.Closed,"Physical connection closed after "+controller+(throws?" failure":" success"));
            Check(counter.Opens-before==(controller=="Admin"?0:1),"One fresh role activation per public request; none for Admin");
        }
        await Request("Portal");await Request("Home",true);await Request("Admin");await Request("Portal");
        await using(var db=new PortalDbContext(options))
        {
            var now=DateTimeOffset.UtcNow;var vn=now.ToOffset(TimeSpan.FromHours(7));var day=new DateTimeOffset(vn.Year,vn.Month,vn.Day,0,0,0,vn.Offset);var month=new DateTimeOffset(vn.Year,vn.Month,1,0,0,0,vn.Offset);
            var rows=db.Set<PublicVisit>().AsNoTracking();
            var expected=new[]{await rows.LongCountAsync(x=>x.LastSeenUtc>=now.AddMinutes(-5)),await rows.LongCountAsync(x=>x.StartedUtc>=day),await rows.LongCountAsync(x=>x.StartedUtc>=month),await rows.LongCountAsync()};
            using var cache=new MemoryCache(new MemoryCacheOptions());
            var traffic=new PublicTraffic(db,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(fixture,"keys"))),cache,NullLogger<PublicTraffic>.Instance,new PublicContentQuery(db));
            var actual=await traffic.ReadAsync();Check(actual!=null&&expected.SequenceEqual(new[]{actual.Active,actual.Today,actual.Month,actual.Total}),"Combined traffic aggregate matches four independent SQL counts");
        }
        // Synthetic fixture only. Verify a new request observes revocation, not a
        // request-spanning page/config cache, then restore exact fixture values.
        var operatorOptions=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(connection).Options;
        await using(var db=new PortalDbContext(operatorOptions))
        {
            var item=await db.Contents.SingleAsync(x=>x.Id==article);var header=await db.Set<SiteConfiguration>().SingleAsync();
            var live=header.LiveRevisionId;var wasPublic=item.IsPublic;
            var replacement=new SiteConfigurationRevision { OrganizationName="FIXTURE UPDATED CONFIG",EnglishName="Fixture" };
            db.Add(replacement);await db.SaveChangesAsync();
            try
            {
                item.IsPublic=false;header.LiveRevisionId=replacement.Id;await db.SaveChangesAsync();
                var ticker=await http.GetStringAsync("/tin-tuc/moi-nhat");
                Check(!ticker.Contains(article.ToString(),StringComparison.OrdinalIgnoreCase),"Ticker omits withdrawn article immediately");
                using var hidden=await http.GetAsync("/bai-viet/"+article);
                Check(hidden.StatusCode==System.Net.HttpStatusCode.NotFound,"Withdrawn detail remains inaccessible on next request");
                Check((await http.GetStringAsync("/tin-tuc")).Contains("FIXTURE UPDATED CONFIG"),"Next page reads updated live site configuration");
                using var data=System.Text.Json.JsonDocument.Parse(ticker);
                Check(data.RootElement.GetArrayLength()<=10&&data.RootElement.EnumerateArray().All(x=>x.EnumerateObject().Count()==2),"Ticker exposes only bounded public ids and titles");
            }
            finally { item.IsPublic=wasPublic;header.LiveRevisionId=live;await db.SaveChangesAsync(); }
        }
        await File.WriteAllTextAsync(Path.Combine(fixture,"focused-result.json"),System.Text.Json.JsonSerializer.Serialize(new{passed=count,operationalSql=false}));
    }
    sealed class FixtureException:Exception;
    sealed class OpenCounter:DbConnectionInterceptor
    {
        public int Opens;
        public override Task ConnectionOpenedAsync(DbConnection c,ConnectionEndEventData d,CancellationToken ct=default) { Opens++;return Task.CompletedTask; }
    }
}
