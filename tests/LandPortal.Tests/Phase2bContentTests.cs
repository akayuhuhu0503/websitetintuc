using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;

public static class Phase2bContentTests
{
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        void Assert(bool ok,string detail) { if(!ok) throw new InvalidOperationException(detail); }
        var actor=new ContentActor("qa-2b-content-"+Guid.NewGuid(),Permissions.All.ToHashSet());
        WorkflowService Flow(PortalDbContext db)=>new(db,new HtmlPolicy(),MediaUploadOptions.FromConfiguration(config,"Development"),StorageOptions.FromConfiguration(config));
        async Task<(Guid Id,WorkflowResult Saved)> Create(PortalDbContext db,ContentKind kind)
        {
            var input=new DraftInput { Kind=kind,Title="QA 2B "+kind+" "+Guid.NewGuid(),BodyHtml="<p>Dữ liệu đất đai kiểm thử</p>",PrimaryCategoryId=75 };
            if(kind == ContentKind.LegalDocument) input.Metadata=new() { Number="123/2026-QĐ",DocumentType="Quyết định",IssuingBody="Cục Quản lý Đất đai",Signer="QA Test",Field="Đất đai",IssuedDate=new(2026,10,12) };
            if(kind == ContentKind.DraftDocument) input.Metadata=new() { ConsultationStartDate=new(2026,10,13),ConsultationEndDate=new(2026,10,14),ConsultationDateEvidence="QA date-only test" };
            if(kind == ContentKind.Announcement) input.Metadata=new() { Unit="QA đơn vị",PublicationDate=new(2026,10,13) };
            var saved=await Flow(db).SaveAsync(null,input,actor,null,Guid.NewGuid()); return(saved.ContentId,saved);
        }
        yield return("2B typed metadata approval/publication/restore for four content kinds",async()=>
        {
            foreach(var kind in new[]{ContentKind.Announcement,ContentKind.LegalDocument,ContentKind.DraftDocument,ContentKind.StaticPage})
            {
                await using var db=new PortalDbContext(options); var (id,saved)=await Create(db,kind); var f=Flow(db);
                var submitted=await f.SubmitAsync(id,saved.CurrentRevisionId,actor,saved.RowVersion,Guid.NewGuid());
                var approved=await f.ApproveAsync(id,submitted.CurrentRevisionId!.Value,actor,submitted.RowVersion,Guid.NewGuid());
                var published=await f.PublishApprovedAsync(id,approved.CurrentRevisionId!.Value,actor,approved.RowVersion,Guid.NewGuid());
                var revision=await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==published.PublishedRevisionId);
                Assert((await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==id)).Kind==kind,"Kind changed.");
                Assert(kind != ContentKind.LegalDocument || revision.Metadata.Number=="123/2026-QĐ","Legal metadata lost.");
                var withdrawn=await f.UnpublishAsync(id,actor,published.RowVersion,Guid.NewGuid());
                var restored=await f.RestoreRevisionAsync(id,revision.Id,actor,withdrawn.RowVersion,Guid.NewGuid());
                Assert(!restored.IsPublic && restored.CurrentRevisionId!=revision.Id,"Restore republished/modified history.");
            }
        });
        yield return("2B SQL legal draft metadata never replaces published projection or old approval",async()=>
        {
            await using var db=new PortalDbContext(options);var (id,saved)=await Create(db,ContentKind.LegalDocument);var f=Flow(db);
            var live=await f.PublishDirectAsync(id,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());var old=live.PublishedRevisionId!.Value;
            var edit=await f.BeginEditAsync(id,actor,live.RowVersion,Guid.NewGuid());
            var input=new DraftInput { Kind=ContentKind.LegalDocument,Title="QA unpublished legal marker",BodyHtml="<p>Hidden</p>",PrimaryCategoryId=75,Metadata=new() { Number="999/DRAFT-private",IssuingBody="Private authority",IssuedDate=new(2027,1,1) } };
            var changed=await f.SaveAsync(id,input,actor,edit.RowVersion,Guid.NewGuid());
            var visible=await db.Revisions.AsNoTracking().SingleAsync(x=>db.Contents.Any(c=>c.Id==id && c.IsPublic && c.PublishedRevisionId==x.Id));
            Assert(visible.Id==old && visible.Metadata.Number=="123/2026-QĐ","Working metadata leaked.");
            var projection=await db.Set<RevisionSearchProjection>().AsNoTracking().SingleAsync(x=>x.RevisionId==old);
            Assert(!projection.Text.Contains("draft-private"),"Published projection was updated from draft.");
            var sub=await f.SubmitAsync(id,changed.CurrentRevisionId,actor,changed.RowVersion,Guid.NewGuid());var approval=await f.ApproveAsync(id,sub.CurrentRevisionId!.Value,actor,sub.RowVersion,Guid.NewGuid());
            var reopened=await f.BeginEditAsync(id,actor,approval.RowVersion,Guid.NewGuid()); input.Metadata.Number="888/NEW";var newer=await f.SaveAsync(id,input,actor,reopened.RowVersion,Guid.NewGuid());
            try { await f.PublishApprovedAsync(id,approval.CurrentRevisionId!.Value,actor,newer.RowVersion,Guid.NewGuid());throw new Exception("Old approval accepted."); } catch(Exception ex) when(ex is InvalidOperationException or DbUpdateConcurrencyException) { }
            Assert((await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==id)).PublishedRevisionId==old,"Denied publish changed live pointer.");
        });
        yield return("2B date-only consultation uses Vietnam next-day exclusive and rejects reversed/effectiveness guesses",async()=>
        {
            var m=new ContentMetadata { ConsultationStartDate=new(2026,10,13),ConsultationEndDate=new(2026,10,14) };
            Assert(m.ConsultationState(new DateTimeOffset(2026,10,12,16,59,59,TimeSpan.Zero))=="Chưa mở","Start boundary wrong.");
            Assert(m.ConsultationState(new DateTimeOffset(2026,10,14,16,59,59,TimeSpan.Zero))=="Đang mở","End date closed early.");
            Assert(m.ConsultationState(new DateTimeOffset(2026,10,14,17,0,0,TimeSpan.Zero))=="Đã đóng","Exclusive boundary wrong.");
            await using var db=new PortalDbContext(options);var f=Flow(db);
            foreach(var input in new[]{new DraftInput { Kind=ContentKind.DraftDocument,Metadata=new() { ConsultationStartDate=new(2026,10,15),ConsultationEndDate=new(2026,10,14) } },new DraftInput { Kind=ContentKind.LegalDocument,Metadata=new() { EffectiveDate=new(2026,10,14) } }})
            { try { await f.SaveAsync(null,input,actor,null,Guid.NewGuid());throw new Exception("Invalid metadata accepted."); } catch(InvalidOperationException) { } }
        });
        yield return("2B evidence-only/mapping-only changes create new immutable revision and reject cross-kind/mixed-date metadata",async()=>
        {
            await using var db=new PortalDbContext(options);var f=Flow(db);var input=new DraftInput { Kind=ContentKind.LegalDocument,Title="QA metadata-only "+Guid.NewGuid(),BodyHtml="<p>QA</p>",PrimaryCategoryId=75 };
            var first=await f.SaveAsync(null,input,actor,null,Guid.NewGuid());input.Metadata.MappingStatus="Reviewed";var mapped=await f.SaveAsync(first.ContentId,input,actor,first.RowVersion,Guid.NewGuid());
            Assert(mapped.CurrentRevisionId!=first.CurrentRevisionId,"Mapping-only save reused wrong revision hash.");
            input.Metadata.EffectivenessEvidence="QA evidence pending confirmation";var evidence=await f.SaveAsync(first.ContentId,input,actor,mapped.RowVersion,Guid.NewGuid());Assert(evidence.CurrentRevisionId!=mapped.CurrentRevisionId,"Evidence-only save reused wrong hash.");
            var immutable=await db.Revisions.SingleAsync(x=>x.Id==first.CurrentRevisionId);immutable.Metadata.Number="Illegal history edit";
            try { await db.SaveChangesAsync();throw new Exception("Owned historical metadata mutated."); } catch(InvalidOperationException) { db.ChangeTracker.Clear(); }
            var invalid=new[]{new DraftInput { Kind=ContentKind.Article,Metadata=new() { EffectivenessEvidence="Cross-kind" } },new DraftInput { Kind=ContentKind.DraftDocument,Metadata=new() { ConsultationStartDate=new(2026,10,15),ConsultationEndUtc=new DateTimeOffset(2026,10,14,16,59,59,TimeSpan.Zero) } }};
            foreach(var bad in invalid) { try { await f.SaveAsync(null,bad,actor,null,Guid.NewGuid());throw new Exception("Mixed/cross-kind metadata accepted."); } catch(InvalidOperationException) { } }
        });
        yield return("2B real SQL Vietnamese projection slash numbers literal wildcards stable unique paging and withdrawn guard",async()=>
        {
            await using var db=new PortalDbContext(options);var f=Flow(db);var marker=Guid.NewGuid().ToString("N");
            var input=new DraftInput { Kind=ContentKind.LegalDocument,Title="QA "+marker+" ĐẤT ĐAI",BodyHtml="<p>Dữ liệu đất đai</p>",PrimaryCategoryId=75,SecondaryCategoryIds=[82],Metadata=new() { Number="111/2026-QĐ_%[",IssuingBody="Cục Quản lý Đất đai",DocumentType="Quyết định",Field="Đất đai",IssuedDate=new(2026,10,12) } };
            var saved=await f.SaveAsync(null,input,actor,null,Guid.NewGuid());var live=await f.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());
            var published=from c in db.Contents.AsNoTracking() join p in db.Set<RevisionSearchProjection>().AsNoTracking() on c.PublishedRevisionId equals (Guid?)p.RevisionId where c.IsPublic&&!c.IsTrashed select new { c.Id,p.Text,p.Number,p.Authority };
            foreach(var term in new[]{"ĐẤT ĐAI","dat dai","đất đai","111/2026-QĐ","_%["})
            { var normalized=TextNormalizer.Search(term);Assert(await published.AnyAsync(x=>x.Id==saved.ContentId&&x.Text.Contains(normalized)),"SQL normalized/literal search failed "+term); }
            var counted=published.Where(x=>x.Id==saved.ContentId && db.RevisionCategories.Any(r=>r.RevisionId==live.PublishedRevisionId&&(r.CategoryId==75||r.CategoryId==82)));
            Assert(await counted.CountAsync()==1,"Multiple categories duplicated total.");
            Assert((await counted.OrderBy(x=>x.Id).Skip(0).Take(1).Select(x=>x.Id).ToListAsync()).SequenceEqual(await counted.OrderBy(x=>x.Id).Skip(0).Take(1).Select(x=>x.Id).ToListAsync()),"Paging unstable.");
            await f.UnpublishAsync(saved.ContentId,actor,live.RowVersion,Guid.NewGuid());Assert(!await published.AnyAsync(x=>x.Id==saved.ContentId),"Withdrawn item leaked via persisted projection.");
        });
        yield return("2B public query pages 23 equal-date multi-category fixtures at SQL with stable non-overlapping IDs",async()=>
        {
            var marker="qapaging"+Guid.NewGuid().ToString("N");
            for(var i=0;i<23;i++)
            {
                await using var db=new PortalDbContext(options);var f=Flow(db);
                var input=new DraftInput { Kind=ContentKind.LegalDocument,Title=marker,BodyHtml="<p>QA đất đai</p>",PrimaryCategoryId=75,SecondaryCategoryIds=[82],Metadata=new() { PublicationDate=new(2026,10,13),IssuedDate=new(2026,10,12),Number="QA/2026-"+i,IssuingBody="QA Cục Đất đai",DocumentType="Quyết định",Field="Đất đai" } };
                var saved=await f.SaveAsync(null,input,actor,null,Guid.NewGuid());await f.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());
            }
            await using var read=new PortalDbContext(options);var query=new LandPortal.Web.Services.PublicContentQuery(read);
            var rows=query.Filter(query.Published(),marker).Where(x=>x.Kind==ContentKind.LegalDocument&&x.SourceDate==new DateOnly(2026,10,13)&&x.IssuedDate==new DateOnly(2026,10,12)&&read.RevisionCategories.Any(c=>c.RevisionId==x.RevisionId&&(c.CategoryId==75||c.CategoryId==82)));
            Assert(await rows.CountAsync()==23,"Combined filters/count inflated or lost rows.");
            var order=rows.OrderBy(x=>x.Title).ThenBy(x=>x.Id);var pages=new List<Guid[]>();
            for(var page=0;page<3;page++) pages.Add(await order.Skip(page*10).Take(10).Select(x=>x.Id).ToArrayAsync());
            Assert(pages[0].Length==10&&pages[1].Length==10&&pages[2].Length==3&&pages.SelectMany(x=>x).Distinct().Count()==23,"SQL paging overlaps or omits tied rows.");
            var repeat=await order.Skip(10).Take(10).Select(x=>x.Id).ToArrayAsync();
            Assert(pages[1].SequenceEqual(repeat),"Tie-breaker changed between requests.");
            Assert(order.Skip(10).Take(10).ToQueryString().Contains("OFFSET"),"Paging was not translated to SQL.");
            for(var page=0;page<3;page++)Assert((await query.CardsPageAsync(order,page*10,10)).Select(x=>x.Id).SequenceEqual(pages[page]),"Bounded card materialization changed SQL page order or identities.");
        });
    }
}
