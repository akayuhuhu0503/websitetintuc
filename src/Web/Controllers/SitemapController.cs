using System.Xml.Linq;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.Controllers;
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class SitemapController(PublicContentQuery query,PublicUrlPolicy urls):Controller
{
    private static readonly XNamespace Ns="http://www.sitemaps.org/schemas/sitemap/0.9";
    [HttpGet("sitemap.xml")]
    public async Task<IActionResult> Index()
    {
        var count=await query.Published().CountAsync();
        var pages=Math.Max(1,(count+999)/1000);
        return Content(new XDocument(new XElement(Ns+"sitemapindex",Enumerable.Range(1,pages).Select(p=>new XElement(Ns+"sitemap",new XElement(Ns+"loc",urls.Absolute("/sitemaps/"+p+".xml")))))).ToString(),"application/xml");
    }
    [HttpGet("sitemaps/{page:int}.xml")]
    public async Task<IActionResult> Page(int page)
    {
        if(page<1||page>100000)return NotFound();
        var ids=await query.Published().OrderBy(x=>x.Id).Skip((page-1)*1000).Take(1000).Select(x=>x.Id).ToListAsync();
        if(ids.Count==0&&page!=1)return NotFound();
        return Content(new XDocument(new XElement(Ns+"urlset",ids.Select(id=>new XElement(Ns+"url",new XElement(Ns+"loc",urls.Absolute("/bai-viet/"+id)))))).ToString(),"application/xml");
    }
}
