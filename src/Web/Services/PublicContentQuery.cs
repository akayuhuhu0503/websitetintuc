using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace LandPortal.Web.Services;

public sealed class PublicContentQuery(PortalDbContext db, MediaUploadOptions? mediaOptions = null)
{
    public IQueryable<PublicRow> Published()
    {
        var today=DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var legacy=(mediaOptions??new MediaUploadOptions()).IsDevelopmentLoopback;
        var verifiedSource=(mediaOptions??new MediaUploadOptions()).VerifiedSourceAllowed;
        var uploaded=(mediaOptions??new MediaUploadOptions()).DevelopmentExceptionEnabled;
        // Keep legacy Source NotScanned policy intact. A card must not point at an
        // uploaded/quarantined/Test-only file the current media endpoint will deny.
        var thumbnails=db.MediaAssets.AsNoTracking().Where(m=>!m.IsQuarantined
            && (m.MimeType=="image/jpeg"||m.MimeType=="image/png"||m.MimeType=="image/gif"||m.MimeType=="image/webp")
            && (m.Origin=="Source"&&(m.ScanStatus=="Clean"||legacy&&m.ScanStatus=="NotScanned")&&(m.ValidationStatus!=BatchMediaProof.Validation||verifiedSource&&m.ScanStatus=="Clean")
                ||m.Origin=="Uploaded"&&m.ValidationStatus=="Validated"&&(m.ScanStatus=="Clean"||uploaded&&m.ScanStatus=="NotScanned")))
            .Select(m=>(Guid?)m.Id);
        return
        from content in db.Contents.AsNoTracking()
        join revision in db.Revisions.AsNoTracking() on content.PublishedRevisionId equals (Guid?)revision.Id
        join category in db.Categories.AsNoTracking() on revision.PrimaryCategoryId equals (int?)category.Id into categories
        from category in categories.DefaultIfEmpty()
        join projection in db.Set<RevisionSearchProjection>().AsNoTracking() on revision.Id equals projection.RevisionId into projections
        from projection in projections.DefaultIfEmpty()
        join configuration in db.Set<SiteConfiguration>().AsNoTracking() on 1 equals configuration.Id into configurations
        from configuration in configurations.DefaultIfEmpty()
        join configuredCategory in db.Set<SiteCategoryConfiguration>().AsNoTracking()
            on new { RevisionId = configuration.LiveRevisionId, CategoryId = revision.PrimaryCategoryId }
            equals new { RevisionId = (Guid?)configuredCategory.RevisionId, CategoryId = (int?)configuredCategory.CategoryId } into configuredCategories
        from configuredCategory in configuredCategories.DefaultIfEmpty()
        where content.IsPublic && !content.IsTrashed && (revision.Metadata.DisclosureGroupId==null||db.DisclosureGroups.Any(g=>g.Id==revision.Metadata.DisclosureGroupId&&g.Status=="NameApproved"))
        select new PublicRow
        {
            Id = content.Id, RevisionId = revision.Id, Kind = content.Kind,
            Title = revision.Title, Summary = revision.Summary,
            SearchText = projection == null ? revision.SearchText : projection.Text, Tags = revision.Metadata.Tags ?? "",
            NumberSearch = projection == null ? "" : projection.Number,
            AuthoritySearch = projection == null ? "" : projection.Authority,
            TypeSearch = projection == null ? "" : projection.DocumentType,
            FieldSearch = projection == null ? "" : projection.Field,
            Number = revision.Metadata.Number, Authority = revision.Metadata.IssuingBody, DocumentType = revision.Metadata.DocumentType, Field = revision.Metadata.Field, IssuedDate = revision.Metadata.IssuedDate,
            Signer = revision.Metadata.Signer, EffectivenessStatus =
                (revision.Metadata.EffectivenessStatus=="ConfirmedEffective"||revision.Metadata.EffectivenessStatus=="ConfirmedPartial")&&revision.Metadata.EffectivenessEvidence!=""&&
                (revision.Metadata.EffectivenessBoundary=="StartOfVietnamDay"&&revision.Metadata.EffectiveUntilDate<=today||revision.Metadata.EffectivenessBoundary=="EndOfVietnamDay"&&revision.Metadata.EffectiveUntilDate<today)?"ConfirmedExpired":revision.Metadata.EffectivenessStatus,
            SourceDate = revision.Metadata.PublicationDate ?? content.SourcePublishedDate, PublishedUtc = content.PublishedAtUtc,
            OriginalDateText = content.OriginalDateText, CategoryId = revision.PrimaryCategoryId,
            CategoryName = category == null ? null : configuredCategory == null ? category.Name : configuredCategory.DisplayName,
            CategorySlug = category == null ? null : category.Slug,
            ThumbnailId = revision.CoverAssetId!=null&&thumbnails.Contains(revision.CoverAssetId)&&db.ContentAssets.Any(a=>a.RevisionId==revision.Id&&a.MediaAssetId==revision.CoverAssetId)
                ?revision.CoverAssetId:db.ContentAssets.Where(a => a.RevisionId == revision.Id && thumbnails.Contains(a.MediaAssetId))
                .OrderBy(a => a.Usage == "Thumbnail" || a.Usage == "Cover" ? 0 : 1).ThenBy(a => a.SortOrder)
                .Select(a => (Guid?)a.MediaAssetId).FirstOrDefault()
        };
    }

    public IQueryable<PublicRow> Filter(IQueryable<PublicRow> rows, string? query)
    {
        var value = TextNormalizer.Search(query);
        return value.Length == 0 ? rows : rows.Where(x => x.SearchText.Contains(value));
    }
    public static Task<Guid[]> PageRevisionIdsAsync(IOrderedQueryable<PublicRow> ordered,int skip,int take)
    {
        if(skip<0||skip>100_000||take<1||take>100)throw new ArgumentOutOfRangeException(nameof(take));
        // The SQL sort/page carries identifiers and ordering keys only. Avoid
        // sorting wide summaries or search/body strings under a memory grant.
        return ordered.Skip(skip).Take(take).Select(x=>x.RevisionId).ToArrayAsync();
    }
    public async Task<List<PublicRow>> CardsPageAsync(IOrderedQueryable<PublicRow> ordered,int skip,int take)
    {
        var ids=await PageRevisionIdsAsync(ordered,skip,take);
        if(ids.Length==0)return [];
        var rows=await ForDisplay(Published().Where(x=>ids.Contains(x.RevisionId))).ToListAsync();
        var byRevision=rows.ToDictionary(x=>x.RevisionId);
        // Recheck the live pointer on materialization. A withdrawn or replaced
        // revision between the two reads is omitted, never served from a cache.
        return ids.Where(byRevision.ContainsKey).Select(id=>byRevision[id]).ToList();
    }
    public IQueryable<PublicRow> DocumentRows(Guid[] revisionIds)=>Published().Where(x=>revisionIds.Contains(x.RevisionId))
        .Select(x=>new PublicRow{Id=x.Id,RevisionId=x.RevisionId,Kind=x.Kind,Title=x.Title,SourceDate=x.SourceDate,PublishedUtc=x.PublishedUtc,OriginalDateText=x.OriginalDateText,CategoryName=x.CategoryName,CategorySlug=x.CategorySlug,Number=x.Number,Authority=x.Authority,DocumentType=x.DocumentType,IssuedDate=x.IssuedDate,EffectivenessStatus=x.EffectivenessStatus});
    public static IOrderedQueryable<PublicRow> Relevant(IQueryable<PublicRow> rows, string? query)
    {
        var term=TextNormalizer.Search(query);
        if(term.Length==0)return Recent(rows);
        // Ranking remains SQL: exact document identifier, then title match,
        // then other body/metadata matches. No arbitrary relevance percentage.
        return rows.OrderByDescending(x=>x.NumberSearch==term)
            .ThenByDescending(x=>x.Title.Contains(query!.Trim()))
            .ThenByDescending(x=>x.NumberSearch.Contains(term))
            .ThenByDescending(x=>x.SourceDate ?? (x.PublishedUtc==null?(DateOnly?)null:DateOnly.FromDateTime(x.PublishedUtc.Value.AddHours(7).Date)))
            .ThenByDescending(x=>x.PublishedUtc).ThenBy(x=>x.Id);
    }
    public static IQueryable<PublicRow> ForDisplay(IQueryable<PublicRow> rows) => rows.Select(x => new PublicRow { Id = x.Id, RevisionId = x.RevisionId, Kind = x.Kind, Title = x.Title, Summary = x.Summary, SourceDate = x.SourceDate, PublishedUtc = x.PublishedUtc, OriginalDateText = x.OriginalDateText, CategoryId = x.CategoryId, CategoryName = x.CategoryName, CategorySlug = x.CategorySlug, ThumbnailId = x.ThumbnailId });
    // New editorial content has a real system publication time and no source date.
    // Only the ordering uses that known VN date; original source metadata remains unchanged.
    public static IOrderedQueryable<PublicRow> Recent(IQueryable<PublicRow> rows) => rows
        .OrderByDescending(x => x.SourceDate ?? (x.PublishedUtc == null ? (DateOnly?)null : DateOnly.FromDateTime(x.PublishedUtc.Value.AddHours(7).Date)))
        .ThenByDescending(x => x.PublishedUtc).ThenBy(x => x.Id);
    public static ContentCard Card(PublicRow x) => new(x.Id, Slug(x.Title), x.Title, Empty(x.Summary), DisplayDate(x), x.ThumbnailId is null ? null : $"/media/{x.ThumbnailId}", KindLabel(x.Kind), x.CategoryName, x.CategorySlug);
    public static string Slug(string value) { var slug = Regex.Replace(TextNormalizer.Search(value), "[^a-z0-9]+", "-").Trim('-'); return slug[..Math.Min(120, slug.Length)]; }
    public static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    public static string? DisplayDate(PublicRow row) => row.SourceDate?.ToString("dd/MM/yyyy") ??
        // Legal issue dates and consultation dates belong to document metadata;
        // an unknown source publication date must stay unknown.
        (row.Kind is ContentKind.LegalDocument or ContentKind.DraftDocument ? null : Empty(row.OriginalDateText) ?? row.PublishedUtc?.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy"));
    public static string KindLabel(ContentKind kind) => kind switch { ContentKind.Article => "Tin tức", ContentKind.Announcement => "Thông báo", ContentKind.StaticPage => "Giới thiệu", ContentKind.LegalDocument => "Văn bản pháp luật", ContentKind.DraftDocument => "Văn bản dự thảo", ContentKind.Video => "Video", _ => "Nội dung" };
}

public sealed class PublicRow
{
    public string Tags { get; init; } = "";
    public string Signer { get; init; }="";
    public string? EffectivenessStatus { get; init; }
    public string NumberSearch { get; init; } = "";
    public string AuthoritySearch { get; init; } = "";
    public string TypeSearch { get; init; } = "";
    public string FieldSearch { get; init; } = "";
    public string Number { get; init; } = "";
    public string Authority { get; init; } = "";
    public string DocumentType { get; init; } = "";
    public string Field { get; init; } = "";
    public DateOnly? IssuedDate { get; init; }
    public Guid Id { get; init; }
    public Guid RevisionId { get; init; }
    public ContentKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public string SearchText { get; init; } = "";
    public DateOnly? SourceDate { get; init; }
    public DateTimeOffset? PublishedUtc { get; init; }
    public string OriginalDateText { get; init; } = "";
    public int? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public string? CategorySlug { get; init; }
    public Guid? ThumbnailId { get; init; }
}
