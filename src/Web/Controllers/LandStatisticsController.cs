using System.Security.Claims;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

public sealed class LandStatisticsController(PortalDbContext db,LandStatisticsService statistics,CurrentPermissionService rights,IWebHostEnvironment environment,ILogger<LandStatisticsController> logger,IConfiguration configuration):Controller
{
    string Actor=>User.FindFirstValue(ClaimTypes.NameIdentifier)??"";
    [HttpGet("thong-ke-dat-dai")]
    public async Task<IActionResult> Index(int? year,string? q,string sort="province")
    {
        if(q?.Length>100||sort is not("province" or "total" or "agricultural" or "nonagricultural" or "unused"))return BadRequest();
        try
        {
            var available=await db.Set<LandStatisticsVersion>().Where(x=>x.IsCurrent).OrderByDescending(x=>x.Year).Select(x=>x.Year).ToArrayAsync();
            var version=await db.Set<LandStatisticsVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.IsCurrent&&x.Year==(year??available.FirstOrDefault()));
            return View(new LandStatisticsPage(version,version==null?[]:JsonSerializer.Deserialize<LandAreaRow[]>(version.RowsJson)??[],available,q,sort));
        }
        catch(Microsoft.Data.SqlClient.SqlException e)when(e.Number==208){logger.LogWarning("Land statistics schema not yet deployed; no invented data displayed.");return View(new LandStatisticsPage(null,[],[],q,sort));}
    }
    [Authorize,HttpGet("admin/thong-ke-dat-dai")]
    public async Task<IActionResult> Admin()
    {
        var actor=await rights.GetAsync(Actor);if(!actor.Has(Permissions.ManageSite)&&!actor.Has(Permissions.PublishSite))return Forbid();
        ViewData["CanPublish"]=actor.Has(Permissions.PublishSite);ViewData["CanManage"]=actor.Has(Permissions.ManageSite);
        ViewData["LanScannerRestricted"]=LanDemoDeployment.Enabled(configuration)&&!LanStatisticsSamples.Enabled(configuration,environment.EnvironmentName);
        ViewData["LanSampleOnly"]=LanDemoDeployment.Enabled(configuration);
        try{ViewData["LiveVersions"]=await db.Set<LandStatisticsVersion>().AsNoTracking().Where(x=>x.IsCurrent).ToDictionaryAsync(x=>x.Year,x=>x.Id);return View("Admin",await db.Set<LandStatisticsVersion>().AsNoTracking().OrderByDescending(x=>x.CreatedUtc).Take(50).ToListAsync());}
        catch(Microsoft.Data.SqlClient.SqlException e)when(e.Number==208){return StatusCode(503,"Chưa triển khai schema thống kê. Cần migration theo quy trình sao lưu và xác minh đích.");}
    }
    [Authorize(Policy=Permissions.ManageSite),HttpGet("admin/thong-ke-dat-dai/sample/{type}")]
    public async Task<IActionResult> Sample(string type)
    {
        if(!(await rights.GetAsync(Actor)).Has(Permissions.ManageSite))return Forbid();
        if(!LanStatisticsSamples.Enabled(configuration,environment.EnvironmentName)||type is not("valid" or "invalid"))return NotFound();
        return File(LanStatisticsSamples.Workbook(type=="invalid"),OfficeDocumentPolicy.Xlsx,"SAMPLE_TEST_34_tinh_"+type+".xlsx");
    }
    [Authorize,HttpGet("admin/thong-ke-dat-dai/{id:guid}")]
    public async Task<IActionResult> Version(Guid id)
    {
        var actor=await rights.GetAsync(Actor);if(!actor.Has(Permissions.ManageSite)&&!actor.Has(Permissions.PublishSite))return Forbid();
        var version=await db.Set<LandStatisticsVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id);if(version==null)return NotFound();
        var rows=JsonSerializer.Deserialize<LandAreaRow[]>(version.RowsJson)??[];
        // A read-only history view cannot re-confirm another user's preview or a saved/published revision.
        ViewData["CanConfirm"]=version.State=="Preview"&&version.OwnerId==Actor&&actor.Has(Permissions.ManageSite);
        var warnings=rows.Where(x=>x.Total!=x.Agricultural+x.NonAgricultural+x.Unused).Select(x=>new LandAreaIssue(0,x.Code,"Tổng diện tích khác tổng ba nhóm đất.",true)).ToArray();
        return View("Preview",((LandStatisticsVersion?)version,new LandAreaPreview(rows,warnings)));
    }
    [Authorize(Policy=Permissions.ManageSite),HttpPost("admin/thong-ke-dat-dai/preview"),ValidateAntiForgeryToken,RequestSizeLimit(11*1024*1024)]
    [RequestFormLimits(MemoryBufferThreshold=11*1024*1024,MultipartBodyLengthLimit=11*1024*1024)]
    public async Task<IActionResult> Preview(IFormFile file,int year,string unit,string source,CancellationToken ct)
    {
        if(file==null||file.Length is <=0 or >LandStatisticsService.MaximumBytes)return BadRequest("Cần tệp XLSX không quá 10 MiB.");
        if(file.ContentType!=OfficeDocumentPolicy.Xlsx||Path.GetExtension(file.FileName).ToLowerInvariant()!=".xlsx")return BadRequest("Cần đúng MIME và phần mở rộng XLSX.");
        try{await using var input=file.OpenReadStream();var result=await statistics.PreviewAsync(input,file.FileName,year,unit,source,Actor,environment.EnvironmentName,ct);ViewData["CanConfirm"]=result.Version!=null;return View("Preview",result);}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(Exception e)when(e is InvalidDataException or InvalidOperationException){logger.LogWarning("Land statistics upload rejected: {Type}",e.GetType().Name);ModelState.AddModelError("","Chưa nhập số liệu. Kiểm tra tệp XLSX, năm, đơn vị, nguồn và trạng thái scanner. Không có ngoại lệ bỏ qua quét.");return View("Error");}
    }
    [Authorize(Policy=Permissions.ManageSite),HttpPost("admin/thong-ke-dat-dai/{id:guid}/confirm"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid id,bool confirmed,CancellationToken ct)
    {try{await statistics.ConfirmAsync(id,Actor,confirmed,ct);return RedirectToAction(nameof(Admin));}catch(UnauthorizedAccessException){return Forbid();}catch(Exception e)when(e is InvalidDataException or InvalidOperationException){return Conflict("Không xác nhận được phiên bản; tải lại và kiểm tra quyền/trạng thái.");}}
    [Authorize(Policy=Permissions.PublishSite),HttpPost("admin/thong-ke-dat-dai/{id:guid}/publish"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(Guid id,Guid? expectedLive,bool replace,CancellationToken ct)
    {try{await statistics.PublishAsync(id,expectedLive,replace,Actor,environment.EnvironmentName,ct);return RedirectToAction(nameof(Admin));}catch(UnauthorizedAccessException){return Forbid();}catch(Exception e)when(e is InvalidDataException or InvalidOperationException){return Conflict("Không xuất bản: kiểm tra phiên bản công khai, quyền và scanner; dữ liệu cũ được giữ nguyên.");}}
}
public sealed record LandStatisticsPage(LandStatisticsVersion? Version,IReadOnlyList<LandAreaRow> Rows,IReadOnlyList<int> Years,string? Query,string Sort);
