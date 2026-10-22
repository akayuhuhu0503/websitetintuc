using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.Controllers;
[Authorize,Route("admin/outbox"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class OutboxAdminController(OutboxReconciliationService reconcile,CurrentPermissionService permissions):Controller
{
    private Task<ContentActor> Actor()=>permissions.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("")]
    public async Task<IActionResult> Index(int page=1)
    {var actor=await Actor();if(!actor.Has(Permissions.OutboxReconcile))return StatusCode(403);page=Math.Clamp(page,1,10000);var rows=reconcile.Scope(actor).Where(x=>x.State==InteractionMailState.Uncertain);return View(new OutboxQueue(await rows.OrderBy(x=>x.AvailableUtc).ThenBy(x=>x.Id).Skip((page-1)*20).Take(20).ToListAsync(),await rows.CountAsync(),page));}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id)
    {var actor=await Actor();if(!actor.Has(Permissions.OutboxReconcile))return StatusCode(403);var row=await reconcile.Scope(actor).SingleOrDefaultAsync(x=>x.Id==id);if(row==null)return NotFound();ViewData["Row"]=row;return View(new OutboxReconcileForm{CommandId=Guid.NewGuid(),RowVersion=Convert.ToBase64String(row.RowVersion)});}
    [HttpPost("{id:guid}")]
    public async Task<IActionResult> Apply(Guid id,OutboxReconcileForm form)
    {if(!ModelState.IsValid)return BadRequest("Đối soát không hợp lệ.");try{await reconcile.ReconcileAsync(await Actor(),id,Convert.FromBase64String(form.RowVersion),form.CommandId,form.Decision,form.Evidence,form.AcknowledgeDuplicateRisk);return RedirectToAction(nameof(Detail),new{id});}catch(UnauthorizedAccessException){return StatusCode(403);}catch(DbUpdateConcurrencyException){return Conflict("Dòng thư đã đổi.");}catch(Exception e)when(e is InvalidOperationException or FormatException){return BadRequest(e.Message);}}
}
