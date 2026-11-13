using System.Reflection;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.Extensions.Caching.Memory;

namespace LandPortal.PublicPresentationHost;

public static class EntryPoint
{
    public static async Task Main(string[] args)
    {
        if(args.Length!=3 || !Directory.Exists(Path.Combine(args[0],"src","Web","wwwroot")) || !int.TryParse(args[1],out var port) || port<1024 || port>65535)
            throw new ArgumentException("Repository root, loopback port and fixture data directory required.");
        var root=Path.GetFullPath(args[0]);var fixtures=Path.GetFullPath(args[2]);
        if(!fixtures.StartsWith(Path.Combine(root,".local","tests")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Only isolated fixture files below checkout .local/tests are allowed.");
        Directory.CreateDirectory(fixtures);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=[],ContentRootPath=Path.Combine(root,"src","Web"),WebRootPath=Path.Combine(root,"src","Web","wwwroot"),EnvironmentName="Development"});
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Public:BaseUrl"]=$"http://127.0.0.1:{port}/",["DOTNET_ENVIRONMENT"]="Development"});
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddHttpContextAccessor();builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        builder.Services.AddSingleton(new FixtureFiles(fixtures));
        builder.Services.AddScoped<FixtureData>();
        builder.Services.AddSingleton<PublicUrlPolicy>();
        builder.Services.AddScoped(sp=>
        {
            // Test-only immutable presentation snapshot: never load configuration,
            // operational credentials, SQL, public content or scanner services.
            var service=new PublicSiteService(null!,null!,null!,new(),sp.GetRequiredService<IHttpContextAccessor>());
            typeof(PublicSiteService).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(service,Task.FromResult<SitePublicConfiguration?>(sp.GetRequiredService<FixtureData>().Site));
            return service;
        });
        builder.Services.AddScoped(sp=>new PublicTraffic(null!,sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(),sp.GetRequiredService<IMemoryCache>(),sp.GetRequiredService<ILogger<PublicTraffic>>(),null!));
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(LandPortal.Web.Controllers.HomeController).Assembly)
            .ConfigureApplicationPartManager(parts=>{parts.FeatureProviders.Add(new TestControllersOnly());parts.FeatureProviders.Add(new TestComponents());});
        var app=builder.Build();
        app.Services.GetRequiredService<IMemoryCache>().Set<PublicTrafficCounts?>("public-traffic-counts",null);
        app.Use(async(context,next)=>{context.Request.Headers.UserAgent="GDLA-Presentation-fixture-bot";await next();});
        app.UseStaticFiles();app.MapControllers();await app.RunAsync();
    }
}

public sealed class TestControllersOnly:IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts,ControllerFeature feature)
    {foreach(var type in feature.Controllers.Where(x=>x.AsType()!=typeof(PresentationController)).ToArray())feature.Controllers.Remove(type);}
}
public sealed class TestComponents:IApplicationFeatureProvider<ViewComponentFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts,ViewComponentFeature feature)
    {foreach(var type in feature.ViewComponents.Where(x=>x.Namespace=="LandPortal.Web.ViewComponents"&&new[]{"HomeVideosViewComponent","PopularArticlesViewComponent","LandStatisticsViewComponent"}.Contains(x.Name)).ToArray())feature.ViewComponents.Remove(type);}
}
public sealed record FixtureFiles(string Root);

public sealed class FixtureData(IHttpContextAccessor accessor)
{
    public int Count(string name,int fallback)=>int.TryParse(accessor.HttpContext?.Request.Query[name],out var n)?Math.Clamp(n,0,3):fallback;
    public bool Flag(string name)=>accessor.HttpContext?.Request.Query[name].ToString()=="true";
    public static Guid Id(int n)=>new($"00000000-0000-0000-0000-{n:000000000000}");
    public IReadOnlyList<ContentCard> News=>Flag("noNews")?[]:Enumerable.Range(1,10).Select(i=>new ContentCard(Id(i),"tin-kiem-tra-"+i,
        i==1?"Cục Quản lý đất đai nghiên cứu, học tập, quán triệt và triển khai thực hiện Nghị quyết Hội nghị lần thứ tư Ban Chấp hành Trung ương Đảng khóa XIV":i%3==0?"Đẩy nhanh xây dựng, ban hành các văn bản triển khai thực hiện chính sách quản lý đất đai và phát triển bền vững":i%2==0?"Thành phố Bắc Ninh quyết liệt đẩy nhanh tiến độ xây dựng cơ sở dữ liệu đất đai":"Tăng cường quản lý và sử dụng đất đai hiệu quả tại địa phương",
        "Nội dung kiểm tra giao diện cô lập, không phải dữ liệu công bố.","13/11/2026",Flag("noImages")||i==3?null:$"/fixture/image/{i}","Article","Tin tức – Sự kiện","tin-tuc")).ToArray();
    public IReadOnlyList<ContentCard> Documents=>Enumerable.Range(20,4).Select(i=>new ContentCard(Id(i),"van-ban-"+i,i==20?"QUY ĐỊNH NGƯNG HIỆU LỰC THI HÀNH KHOẢN 5 ĐIỀU 6 THÔNG TƯ SỐ 33/2017/TT-BTNMT VÀ QUẢN LÝ ĐẤT ĐAI":"Thông tư ban hành định mức kinh tế – kỹ thuật vận hành hệ thống cơ sở dữ liệu ảnh viễn thám quốc gia",null,"12/11/2026",null,"LegalDocument"){DocumentNumber="36/2026/TT-BNNMT"}).ToArray();
    public IReadOnlyList<ContentCard> Notices=>Enumerable.Range(30,4).Select(i=>new ContentCard(Id(i),"thong-bao-"+i,i==30?"Triển khai Chỉ thị và kế hoạch công tác theo chương trình của cơ quan":"Thư mời quan tâm và thông tin kế hoạch thực hiện nhiệm vụ",null,"12/11/2026",null,"Announcement")).ToArray();
    public IReadOnlyList<HomeVideoCard> Videos=>Enumerable.Range(40,Flag("hideVideo")?0:Count("videos",1)).Select(i=>new HomeVideoCard(new(Id(i),"video-"+i,"Video kiểm tra giao diện và phát lại – không phải nội dung công bố",null,"13/11/2026",null,"Video"),Id(i),i==42?null:1)).ToArray();
    public SitePublicConfiguration Site
    {
        get
        {
            var r=new SiteConfigurationRevision{OrganizationName="CỤC QUẢN LÝ ĐẤT ĐAI",EnglishName="Department of Land Administration",ContactAddress="Địa chỉ kiểm tra riêng",ContactPhone="",ContactEmail="fixture@example.invalid",FooterText="Thông tin fixture dùng kiểm tra bố cục, không ghi dữ liệu vận hành."};
            var menus=new List<SiteNavigationItem>();var names=new[]{("Trang chủ","/"),("Giới thiệu","/gioi-thieu"),("Tin tức – Sự kiện","/tin-tuc"),("Công khai thông tin","/cong-khai-thong-tin"),("Văn bản","/van-ban"),("Hỏi đáp","/hoi-dap"),("Sổ tay đất đai","/so-tay-dat-dai"),("Video","/video"),("Lịch tuần","/lich-tuan"),("Thư điện tử","/thu-dien-tu"),("Liên hệ","/lien-he")};
            for(var i=0;i<names.Length;i++)menus.Add(new(Id(100+i),null,"main",names[i].Item1,names[i].Item2,false,i));
            menus.Add(new(Id(120),Id(101),"main","Chức năng, nhiệm vụ","/gioi-thieu/chuc-nang",false,1));
            menus.Add(new(Id(121),Id(101),"main","Cơ cấu tổ chức","/gioi-thieu/co-cau",false,2));
            menus.Add(new(Id(122),Id(102),"main","Hoạt động của Cục","/chuyen-muc/hoat-dong",false,1));
            menus.Add(new(Id(123),Id(102),"main","Tin địa phương","/chuyen-muc/dia-phuong",false,2));
            menus.Add(new(Id(124),Id(103),"main","Giấy chứng nhận sai hỏng, hủy","/cong-khai-thong-tin/giay-chung-nhan-sai-hong-huy",false,1));
            foreach(var item in names.Take(6))menus.Add(new(Guid.NewGuid(),null,"footer",item.Item1,item.Item2,false,menus.Count));
            var banners=Enumerable.Range(0,Count("banners",1)).Select(i=>new SitePublicBanner(Id(200+i),"Banner sự kiện fixture", "Banner kiểm tra tỷ lệ 117:10", "home_event_strip", "/fixture/banner/"+i,"/tin-tuc",false,i,"Mô tả quản trị không hiện ở banner")).ToList();
            if(Flag("sidebar"))banners.Add(new(Id(210),"Banner phụ","Banner phụ từ cấu hình fixture","sidebar","/fixture/image/2","/van-ban",false,0));
            r.Blocks.Add(new(){Type="Videos",Enabled=!Flag("hideVideo"),Limit=3});
            return new(r,menus,banners,"/fixture/image/99",false);
        }
    }
    public ConfiguredHomeModel Configured
    {
        get
        {
            var site=Site;var blocks=new List<SiteHomeBlockResult>();
            void Add(string type,string title,IReadOnlyList<ContentCard> items,string? url){var block=new SiteHomeBlock{Type=type,Title=title,Limit=items.Count,SortOrder=blocks.Count,Enabled=true};site.Revision.Blocks.Add(block);blocks.Add(new(block,items,url));}
            if(!Flag("hideFeatured"))Add("Featured","Tin nổi bật",News.Take(4).ToArray(),"/tin-tuc");
            Add("Recent","Tin mới cập nhật",News.Skip(4).ToArray(),"/tin-tuc");
            if(!Flag("hideDocuments"))Add("LegalDocuments","Văn bản mới cập nhật",Documents,"/van-ban");
            if(!Flag("hideNotices"))Add("Announcements","Thông báo",Notices,"/thong-bao");
            if(!Flag("noCategories"))
            {
                Add("Category","Xây dựng, phổ biến chính sách, pháp luật",News.Skip(2).Take(3).ToArray(),"/chuyen-muc/chinh-sach");
                Add("Category","Dữ liệu và Thông tin đất đai",News.Skip(5).Take(2).ToArray(),"/chuyen-muc/du-lieu");
                Add("Category","Điều tra, đánh giá tài nguyên đất",News.Skip(1).Take(3).ToArray(),"/chuyen-muc/dieu-tra");
                Add("Category","Giao đất, cho thuê đất, chuyển mục đích sử dụng đất",News.Skip(7).Take(1).ToArray(),"/chuyen-muc/giao-dat");
            }
            return new(){Site=site,Blocks=blocks};
        }
    }
    public HomeModel Fallback=>new(){News=News,Documents=Flag("hideDocuments")?[]:Documents,Announcements=Flag("hideNotices")?[]:Notices,Sections=Flag("noCategories")?[]:[new("Xây dựng, phổ biến chính sách, pháp luật","chinh-sach",News.Skip(2).Take(3).ToArray()),new("Dữ liệu và Thông tin đất đai","du-lieu",News.Skip(5).Take(2).ToArray())]};
}

public sealed class PresentationController(FixtureData data,FixtureFiles files):Controller
{
    [HttpGet("/")]public IActionResult Home()=>View(data.Flag("fallback")?"~/Views/Home/Index.cshtml":"~/Views/Home/Configured.cshtml",data.Flag("fallback")?(object)data.Fallback:data.Configured);
    [HttpGet("/fixture/image/{id}")][HttpGet("/video-media/{id}/poster")]
    public IActionResult Image(string id)
    {
        var paths=Directory.GetFiles(files.Root,"photo-*.jpg").Order().ToArray();
        if(paths.Length>0){var number=id.Where(char.IsDigit).Select(c=>c-'0').Sum();return PhysicalFile(paths[number%paths.Length],"image/jpeg");}
        return Content("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 640 360'><defs><linearGradient id='g' x2='1' y2='1'><stop stop-color='#739cac'/><stop offset='1' stop-color='#bfd1a4'/></linearGradient></defs><path fill='url(#g)' d='M0 0h640v360H0z'/><path fill='#648e60' d='M0 250L180 110 340 240 540 150 640 290V360H0z'/><path fill='#d7cc9d' d='M130 360l210-170 40 30-160 140'/></svg>","image/svg+xml");
    }
    [HttpGet("/fixture/banner/{id}")]public IActionResult Banner(int id)=>Content($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1170 100'><rect width='1170' height='100' fill='{(id%2==0?"#8b2e2e":"#702424")}'/><text x='585' y='60' fill='white' text-anchor='middle' font-family='sans-serif' font-size='28'>BANNER KIỂM TRA BỐ CỤC {id+1}</text></svg>","image/svg+xml");
    [HttpGet("/fixture/sample.mp4")]public IActionResult Movie()=>PhysicalFile(Path.Combine(files.Root,"sample.mp4"),"video/mp4",enableRangeProcessing:true);
    [HttpGet("/bai-viet/{id:guid}")]public IActionResult Detail(Guid id)=>Content($"<!doctype html><html lang='vi'><meta charset='utf-8'><title>Bài kiểm tra riêng</title><h1>Bài kiểm tra riêng</h1>{(data.Videos.Any(x=>x.Content.Id==id)?"<video controls width='640' src='/fixture/sample.mp4'></video>":"<p>Đích liên kết nội dung kiểm tra giao diện.</p>")}<a href='/'>Trang chủ</a></html>","text/html");
    [HttpGet("/tim-kiem")]public IActionResult Search(string? q)=>Content("Tìm kiếm fixture: "+q,"text/plain");
    [HttpGet("/tin-tuc")]public IActionResult NewsList()=>Content("Danh sách fixture dùng cho điều hướng/prefetch giao diện; không truy vấn dữ liệu vận hành.","text/plain");
}

[ViewComponent(Name="HomeVideos")]public sealed class FixtureHomeVideosViewComponent(FixtureData data):ViewComponent
{public IViewComponentResult Invoke(SitePublicConfiguration? site=null)=>View("~/Views/Shared/Components/HomeVideos/Default.cshtml",data.Videos);}
[ViewComponent(Name="PopularArticles")]public sealed class FixturePopularViewComponent(FixtureData data):ViewComponent
{public IViewComponentResult Invoke()=>View("~/Views/Shared/Components/PopularArticles/Default.cshtml",(Week:data.News.Take(4).ToArray() as IReadOnlyList<ContentCard>,Month:data.News.Skip(2).Take(4).ToArray() as IReadOnlyList<ContentCard>));}
[ViewComponent(Name="LandStatistics")]public sealed class FixtureStatisticsViewComponent(FixtureData data):ViewComponent
{public IViewComponentResult Invoke()=>data.Flag("statistics")?View("~/Views/Shared/Components/LandStatistics/Default.cshtml",new LandStatisticsVersion{Year=2026,Unit="ha",Source="Dữ liệu kiểm tra giao diện cô lập, không phải số liệu công bố",RowsJson="[]"}):Content("");}
