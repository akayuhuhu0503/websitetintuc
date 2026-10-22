using System.ComponentModel.DataAnnotations;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public sealed class InteractionBatchItem
{
    public Guid Id { get; set; }
    public Guid CommandId { get; set; }
    [Required,StringLength(128)]public string RowVersion { get; set; }="";
    [Required,StringLength(30)]public string Action { get; set; }="";
    [StringLength(120)]public string DisplayName { get; set; }="";
    [StringLength(8000)]public string Text { get; set; }="";
    [StringLength(8000)]public string Answer { get; set; }="";
    [StringLength(500)]public string Reason { get; set; }="";
}
public sealed record InteractionBatchResult(Guid Id,string Status);
public sealed class InteractionBatchService(PortalDbContext db,InteractionService interaction,CurrentPermissionService permissions)
{
    // Independent transactions per row. One forbidden/stale row never produces a claim that all succeeded.
    public async Task<IReadOnlyList<InteractionBatchResult>> ApplyAsync(string userId,IReadOnlyList<InteractionBatchItem> items)
    {
        if(items.Count is <1 or >20||items.Select(x=>x.Id).Distinct().Count()!=items.Count)throw new InvalidOperationException("Chọn1–20 hồ sơ khác nhau.");
        var results=new List<InteractionBatchResult>();
        foreach(var item in items)
        {
            try
            {
                Validator.ValidateObject(item,new ValidationContext(item),true);if(item.Action is not("publish" or "reject" or "hide"))throw new InvalidOperationException("Thao tác hàng loạt không hỗ trợ.");
                var kind=await db.InteractionSubmissions.AsNoTracking().Where(x=>x.Id==item.Id).Select(x=>(InteractionKind?)x.Kind).SingleOrDefaultAsync();if(kind is not(InteractionKind.Comment or InteractionKind.Inquiry))throw new InvalidOperationException("Liên hệ/góp ý không thuộc batch public moderation.");
                await interaction.ModerateAsync(await permissions.GetAsync(userId),item.Id,Convert.FromBase64String(item.RowVersion),item.CommandId,item.Action,item.DisplayName??"",item.Text??"",item.Answer??"",item.Reason??"");results.Add(new(item.Id,"Success"));
            }
            catch(UnauthorizedAccessException){results.Add(new(item.Id,"Forbidden"));}
            catch(DbUpdateConcurrencyException){results.Add(new(item.Id,"Conflict"));}
            catch(Exception e)when(e is ValidationException or InvalidOperationException or FormatException or KeyNotFoundException){results.Add(new(item.Id,"Invalid"));}
            catch(DbUpdateException){results.Add(new(item.Id,"Failed"));}
            finally{db.ChangeTracker.Clear();}
        }
        return results;
    }
}
