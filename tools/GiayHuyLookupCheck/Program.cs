using LandPortal.Web.Services;
using Microsoft.Data.SqlClient;
using LandPortal.Infrastructure;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
if(args is ["--check-lan-model"]){
 var db=new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer("Server=fixture;Database=LandPortal_LanDemo;Integrated Security=True;Encrypt=True").Options);
 if(!PortalBuildCapabilities.LegacyLanSchema15||db.Model.GetEntityTypes().Any(t=>t.ClrType==typeof(ContentMetadata)&&t.FindProperty("ConsultationEmail")!=null))throw new Exception("Legacy LAN mapping guard failed");
 Console.WriteLine("LAN schema15 model PASS; FR06 email unmapped and workflow rejects nonempty values; no SQL connection");return;
}
if(args is ["--probe-private",var path]){
 var environment=Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??"Production";
 if(environment is not("Development" or "Staging"))throw new InvalidOperationException("Readonly probe requires explicit Development/LAN or Staging/UAT context; never Production.");
 var cfg=new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(path)).AddInMemoryCollection(new Dictionary<string,string?>{["DOTNET_ENVIRONMENT"]=environment,["Deployment:Profile"]=environment=="Development"?"LAN_DEMO":"UAT",["Sql:ExpectedDatabase"]=environment=="Development"?"LandPortal_LanDemo":"website_tin_tuc"}).Build();
 var service=new GiayHuyLookup(cfg,NullLogger<GiayHuyLookup>.Instance);
 var first=await service.SearchAsync(2026,"ha-noi",null,1,default);var second=await service.SearchAsync(2026,"ha-noi",null,2,default);
 var accented=await service.SearchAsync(null,null,"quyết định",1,default);var plain=await service.SearchAsync(null,null,"quyet dinh",1,default);
 var all=await service.SearchAsync(null,null,null,1,default);
 if(new[]{first,second,accented,plain,all}.Any(x=>x.Unavailable||x.Rows.Count>20)||all.Rows.Count!=Math.Min(20,all.Total)||first.Rows.Select(x=>x.Id).Intersect(second.Rows.Select(x=>x.Id)).Any()||accented.Total!=plain.Total)throw new Exception("Live read-only probe failed; no record contents logged");
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{status="LIVE_VIEW_READ_PASS",all=all.Total,defaults=first.Total,firstRows=first.Rows.Count,secondRows=second.Rows.Count,accentEquivalent=true,noPageOverlap=true,defaultMs=first.QueryMilliseconds,secondMs=second.QueryMilliseconds,allMs=all.QueryMilliseconds,years=all.Years}));return;
}
if(args is ["--write-view",var output]){if(File.Exists(output))throw new InvalidOperationException("Use a new View artifact path; existing files are preserved.");File.WriteAllText(output,GiayHuyViewDefinition.Build(),new System.Text.UTF8Encoding(false));Console.WriteLine("View definition generated offline");return;}
if(args is ["--check"]){
 var count=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
 Check(GiayHuyProvince.All.Length==34&&GiayHuyProvince.All.Select(x=>x.Key).Distinct().Count()==34,"verified 34-province catalog");
 Check(new GiayHuyPublicRow(Guid.Empty,"","","","","","A","B","").OwnerName=="A; B"&&new GiayHuyPublicRow(Guid.Empty,"","","","","","","B","").OwnerName=="B","owner display preserves independent names");
 var view=GiayHuyViewDefinition.Build();Check(view.Contains("THEN [yr].giaTri ELSE")&&view.Contains("DA_XAC_NHAN")&&view.Contains("LOAI_BO")&&!view.Contains("diaChi")&&!view.Contains("cccd",StringComparison.OrdinalIgnoreCase),"final values and eligible scope only");
 Check(GiayHuyLookup.PageSql.Contains("FETCH NEXT 20")&&GiayHuyLookup.PageSql.Contains("IssueDate DESC,v.RecordId")&&!GiayHuyLookup.PageSql.Contains("SELECT *"),"SQL paging and stable ordering");
 using var cmd=new SqlCommand();GiayHuyLookup.AddFilters(cmd,2026,"ha-noi","x%_[\\");Check((string)cmd.Parameters["@term"].Value=="%x\\%\\_\\[\\\\%","parameterized LIKE escaping");
 var fixture="Server=45.120.229.43,1433;Database=QuanLyDuAnTrungTamDuLieuDatDai;User ID=landportal_giayhuy_reader;Password=FIXTURE_ONLY;Encrypt=True;TrustServerCertificate=True";
 IConfiguration Config(string environment,string profile)=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:GiayHuyReadOnly"]=fixture,["GiayHuy:PublicationScope"]=GiayHuyLookup.PublicationScope,["GiayHuy:LanUatTlsException"]="OwnerApproved-20261104",["DOTNET_ENVIRONMENT"]=environment,["Deployment:Profile"]=profile,["Sql:ExpectedDatabase"]="LandPortal_LanDemo"}).Build();
 Check(GiayHuyLookup.ValidateConnection(Config("Development","LAN_DEMO")).TrustServerCertificate,"LAN-only TLS exception");
 var blocked=false;try{GiayHuyLookup.ValidateConnection(Config("Production","LAN_DEMO"));}catch(InvalidOperationException){blocked=true;}Check(blocked,"Production rejects TLS exception");
 Console.WriteLine($"FOCUSED {count}/{count}; no SQL writes/connections");return;
}
Console.WriteLine("Use --check or --write-view <path>. Fixture web host retired; approved live data uses the existing website.");
