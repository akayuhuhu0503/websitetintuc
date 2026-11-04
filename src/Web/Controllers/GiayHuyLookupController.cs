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
        if (!Valid(year, province, q, page)) return BadRequest();
        return View(await lookup.SearchAsync(year, province, q, page, HttpContext.RequestAborted));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, int? year, string? province, string? q, int page = 1)
    {
        if (!Valid(year, province, q, page)) return BadRequest();
        var result = await lookup.DetailAsync(id, HttpContext.RequestAborted);
        if (result.Unavailable) { Response.StatusCode = 503; return View("Unavailable"); }
        if (result.Row == null) return NotFound(); // Same result for missing, unconfirmed and unauthorized.
        ViewData["Year"] = year; ViewData["Province"] = province; ViewData["Query"] = q; ViewData["Page"] = page;
        return View(result.Row);
    }

    private bool Valid(int? year, string? province, string? q, int page) => ModelState.IsValid &&
        (year == null || year is >= 1 and <= 9999) && province?.Length is not > 200 && q?.Length is not > 200 && page is >= 1 and <= 10000;
}
