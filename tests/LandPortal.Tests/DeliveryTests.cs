using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Tests;
public static class DeliveryTests
{
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,IConfiguration source)
    {
        IConfiguration Uat(string key="",string? value=null)
        {
            var home=Path.Combine(root,".local/uat-technical-fixture");
            var d=new Dictionary<string,string?>{{"Deployment:Profile","UAT"},{"Deployment:ExpectedSqlServer","uat.invalid"},{"Deployment:ExpectedHost","uat.landportal.invalid"},{"DOTNET_ENVIRONMENT","Staging"},{"ASPNETCORE_ENVIRONMENT","Staging"},{"ConnectionStrings:Portal","Server=uat.invalid;Database=LandPortal_Uat;Integrated Security=true;Encrypt=true;TrustServerCertificate=false"},{"Sql:ExpectedDatabase","LandPortal_Uat"},{"Public:BaseUrl","https://uat.landportal.invalid"},{"WorkspaceRoot",home},{"Scanner:Provider","Clamd"},{"Scanner:ClamdPort","3310"},{"Scanner:PolicyReference","Technical configuration fixture, no target connection"},{"Runtime:RolePassword",new string('x',32)}};
            foreach(var name in new[]{"MediaRoot","SnapshotRoot","TempRoot","KeysRoot"})d["Storage:"+name]=Path.Combine(home,name);
            d["Deployment:TargetApproved"]="true";d["Deployment:TargetApprovalReference"]="Technical fixture only, not operator authority";
            if(key.Length>0)d[key]=value;return new ConfigurationBuilder().AddInMemoryCollection(d).Build();
        }
        yield return("Delivery isolated UAT profile exact DB server TLS host private stores and no scanner bypass",()=>
        {
            Check(UatDeployment.Validate(Uat(),"Staging"),"Positive UAT guard");
            UatSiteInitialization.RequireAuthority(["site-initialize","--confirm-target","LandPortal_Uat","--approval-reference","Technical fixture only, not operator authority"],Uat(),"Staging");
            foreach(var confirm in new[]{Array.Empty<string>(),new[]{"--confirm-target","LandPortal_Dev","--approval-reference","Technical fixture only, not operator authority"},new[]{"--confirm-target","LandPortal_Uat","--approval-reference","wrong"}})
            {var rejected=false;try{UatSiteInitialization.RequireAuthority(confirm,Uat(),"Staging");}catch(InvalidOperationException){rejected=true;}Check(rejected,"UAT initialization confirmation/authority failed open");}
            foreach(var pair in new[]{("DOTNET_ENVIRONMENT","Production"),("Sql:ExpectedDatabase","LandPortal_Dev"),("Deployment:ExpectedSqlServer","wrong.invalid"),("Scanner:Provider","DefenderTest"),("Media:AllowUnscannedDevelopmentUploads","true"),("Storage:KeysRoot",Path.Combine(root,"src/Web/wwwroot")),("Public:BaseUrl","http://uat.landportal.invalid"),("Storage:KeysRoot",Path.Combine(root,".local/uat-technical-fixture/MediaRoot/nested"))})
            {var denied=false;try{UatDeployment.Validate(Uat(pair.Item1,pair.Item2),"Staging");}catch(InvalidOperationException){denied=true;}Check(denied,"UAT negative guard: "+pair.Item1);}
            Check(!UatDeployment.Validate(source,"Development"),"Existing local profile preserved");return Task.CompletedTask;
        });
        yield return("Delivery UAT allocated database approval required and reserved targets denied",()=>
        {
            foreach(var name in new[]{"LandPortal_Dev","landportal_test","master","LandPortal_Restore3J_abcd","LandPortal_Production","REPLACE_APPROVED_DATABASE","unsafe];DROP",""})
                Check(!UatDeployment.IsTargetDatabase(name),"Reserved/unsafe target denied");
            foreach(var pair in new[]{("Deployment:TargetApproved","false"),("Deployment:TargetApprovalReference",""),("Deployment:ExpectedSqlServer","REPLACE_SERVER")})
            {bool denied=false;try{UatDeployment.Validate(Uat(pair.Item1,pair.Item2),"Staging");}catch(InvalidOperationException){denied=true;}Check(denied,"Unallocated target denied");}
            var allocated=new ConfigurationBuilder().AddConfiguration(Uat()).AddInMemoryCollection(new Dictionary<string,string?>{
                ["Sql:ExpectedDatabase"]="AgencyPortal_UatAllocated",["ConnectionStrings:Portal"]="Server=uat.invalid;Database=AgencyPortal_UatAllocated;Integrated Security=true;Encrypt=true;TrustServerCertificate=false"}).Build();
            Check(UatDeployment.Validate(allocated,"Staging"),"Allocated name accepted only with matching private target profile");return Task.CompletedTask;
        });
        yield return("Delivery SMTP explicit UAT STARTTLS policy opt-in and test sink fences",()=>
        {
            IConfiguration Mail(params (string,string)[] entries)=>new ConfigurationBuilder().AddConfiguration(Uat()).AddInMemoryCollection(entries.ToDictionary(x=>x.Item1,x=>(string?)x.Item2)).Build();
            var enabled=Mail(("Mail:Transport","Smtp"),("Mail:DeliveryEnabled","true"),("Mail:PolicyReference","Operator UAT mail approval pending real relay"),("Mail:Host","smtp.invalid"),("Mail:Port","587"),("Mail:From","portal@landportal.invalid"),("Mail:Tls","StartTls"));
            Check(InteractionMailOptions.FromConfiguration(enabled,"Staging").Available,"Explicit UAT SMTP config");
            foreach(var entry in new[]{("Mail:DeliveryEnabled","false"),("Mail:Tls","None"),("Mail:From","bad\r\nRCPT TO:x@invalid"),("Mail:Port","0")})
            {var denied=false;try{var c=new ConfigurationBuilder().AddConfiguration(enabled).AddInMemoryCollection(new Dictionary<string,string?>{{entry.Item1,entry.Item2}}).Build();denied=!InteractionMailOptions.FromConfiguration(c,"Staging").Available;}catch(InvalidOperationException){denied=true;}Check(denied,"SMTP negative: "+entry.Item1);}
            var test=new ConfigurationBuilder().AddConfiguration(source).AddInMemoryCollection(new Dictionary<string,string?>{{"Mail:Transport","Smtp"},{"Mail:DeliveryEnabled","true"}}).Build();
            var blocked=false;try{InteractionMailOptions.FromConfiguration(test,"Development");}catch(InvalidOperationException){blocked=true;}Check(blocked,"Real SMTP forbidden on local Test profile");return Task.CompletedTask;
        });
        yield return("Delivery approved source policy exact hash URL scope and public upload limit unchanged",()=>
        {
            var p=SourceResourcePolicy.Read(source)??throw new InvalidOperationException("Run-specific policy required");
            Check(p.Approval.MaxPdfBytes==48L*1024*1024,"Approved source cap");
            var selected=p.Approval.Urls.First();Check(SourceResourcePolicy.MediaOptions(source,selected).MaxPdfBytes==48L*1024*1024,"Scoped approved PDF options");
            Check(SourceResourcePolicy.MediaOptions(source,"https://qldd.mae.gov.vn/not-approved.pdf").MaxPdfBytes==32L*1024*1024,"Unapproved URL retains old cap");
            Check(MediaUploadOptions.FromConfiguration(source,"Development").MaxPdfBytes==32L*1024*1024,"Public upload cap unchanged");
            var tampered=new ConfigurationBuilder().AddConfiguration(source).AddInMemoryCollection(new Dictionary<string,string?>{{"SourceImport:PolicySha256",new string('0',64)}}).Build();var denied=false;
            try{SourceResourcePolicy.Read(tampered);}catch(InvalidDataException){denied=true;}Check(denied,"Policy hash tamper must fail");return Task.CompletedTask;
        });
    }
}
