using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.ViewComponents;
public sealed record ArticleInteractionsModel(Guid Id,InteractionKind Kind,bool Open,IReadOnlyList<PublicInteraction> Comments);
public sealed class ArticleInteractionsViewComponent(PortalDbContext db,InteractionService service):ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid id)
    {
        var kind=await db.Contents.Where(x=>x.Id==id&&x.IsPublic&&!x.IsTrashed).Select(x=>(ContentKind?)x.Kind).SingleOrDefaultAsync();
        if(kind is not(ContentKind.Article or ContentKind.DraftDocument))return Content("");
        var interaction=kind==ContentKind.Article?InteractionKind.Comment:InteractionKind.Consultation;
        return View(new ArticleInteractionsModel(id,interaction,await service.CanReceiveAsync(interaction,id),kind==ContentKind.Article?await service.PublicAsync(InteractionKind.Comment,id):[]));
    }
}
