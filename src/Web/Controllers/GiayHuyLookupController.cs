using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LandPortal.Web.Controllers;

[Route("cong-khai-thong-tin/giay-chung-nhan-sai-hong-huy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("interaction")]
public sealed class GiayHuyLookupController(IGiayHuyLookup lookup) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int? year, string? province, string? q, int page = 1)
    {
        if (!Request.Query.ContainsKey("year") && !Request.Query.ContainsKey("applied")) year = 2026;
        if (!Request.Query.ContainsKey("province") && !Request.Query.ContainsKey("applied")) province = "ha-noi";
        if (!Valid(year, province, q, page)) return BadRequest();
        var result = await lookup.SearchAsync(year, province, q, page, HttpContext.RequestAborted);
        if (!result.Unavailable && result.Total>0 && result.Rows.Count==0 && page>1)
            return RedirectToAction(nameof(Index),new{year=year?.ToString()??"",province=province??"",q,page=(result.Total+19)/20,applied=1});
        return View(result);
    }

    private bool Valid(int? year, string? province, string? q, int page) => ModelState.IsValid &&
        (year == null || year is >= 1 and <= 9999) && (string.IsNullOrEmpty(province)||province==GiayHuyProvince.Unknown||GiayHuyProvince.All.Any(x=>x.Key==province)) && q?.Length is not > 200 && page is >= 1 and <= 1000000;
}
