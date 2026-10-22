using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public sealed record PublicInquiryPage(IReadOnlyList<PublicInteraction> Items,int Total,int Page,string? Query,string? Field);
public sealed partial class InteractionService
{
    public async Task<PublicInquiryPage> PublicInquiryPageAsync(string? q,string? field,int page=1)
    {
        if(q?.Length>200||field?.Length>120)throw new InvalidOperationException("Bộ lọc hỏi đáp quá dài.");page=Math.Clamp(page,1,10000);
        var rows=from s in db.InteractionSubmissions.AsNoTracking() join p in db.InteractionPublicSnapshots.AsNoTracking() on s.PublicSnapshotId equals p.Id where s.Kind==InteractionKind.Inquiry&&s.State!=InteractionState.Rejected&&s.PublicConsent&&p.SubmissionId==s.Id select new{s,p};
        if(!string.IsNullOrWhiteSpace(q)){var text=q.Trim();rows=rows.Where(x=>EF.Functions.Collate(x.p.Text,"Vietnamese_100_CI_AI").Contains(text)||EF.Functions.Collate(x.p.AgencyAnswer,"Vietnamese_100_CI_AI").Contains(text));}
        if(!string.IsNullOrWhiteSpace(field)){var value=field.Trim();rows=rows.Where(x=>EF.Functions.Collate(x.s.InquiryField!,"Vietnamese_100_CI_AI")==value);}
        var total=await rows.CountAsync();var items=await rows.OrderByDescending(x=>x.p.ApprovedUtc).ThenBy(x=>x.p.Id).Skip((page-1)*20).Take(20).Select(x=>new PublicInteraction(x.p.DisplayName,x.p.Text,x.p.AgencyAnswer,x.p.ApprovedUtc)).ToListAsync();return new(items,total,page,q,field);
    }
}
