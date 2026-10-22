using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Services;

public sealed class GeneralSearchRow
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public int Kind { get; set; }
    public DateOnly? Date { get; set; }
}
public sealed class GeneralSearchKey
{
    public Guid Id { get; set; }
    public int Kind { get; set; }
    public DateOnly? Date { get; set; }
    public bool TitleMatch { get; set; }
}

// Count/sort/page only narrow keys in SQL. Fetch at most ten reviewed/public bodies AFTER paging.
// Preserve the existing ID-first strategy instead of sorting long answer/text columns.
public sealed class GeneralSearchQuery(PortalDbContext db, PublicContentQuery content)
{
    private IQueryable<InteractionPublicSnapshot> Answers() =>
        from s in db.InteractionSubmissions.AsNoTracking() join p in db.InteractionPublicSnapshots.AsNoTracking() on s.PublicSnapshotId equals p.Id
        where s.Kind == InteractionKind.Inquiry && s.State != InteractionState.Rejected && s.PublicConsent && p.SubmissionId == s.Id select p;
    public IQueryable<GeneralSearchKey> Rows(string? q, string? kind, int? categoryId, DateOnly? from, DateOnly? to)
    {
        var articles = content.Filter(content.Published(), q);
        if (!string.IsNullOrEmpty(kind) && kind != "Inquiry")
        {
            if (!Enum.TryParse<ContentKind>(kind, out var type) || !Enum.IsDefined(type)) throw new ArgumentException("Loại nội dung không hợp lệ.");
            articles = articles.Where(x => x.Kind == type);
        }
        if (categoryId != null) articles = articles.Where(x => x.CategoryId == categoryId || db.RevisionCategories.Any(c => c.RevisionId == x.RevisionId && c.CategoryId == categoryId));
        var term = (q ?? "").Trim().Replace('đ', 'd').Replace('Đ', 'D');
        var a = articles.Select(x => new GeneralSearchKey { Id = x.Id, Kind = (int)x.Kind, Date = x.SourceDate ?? (x.PublishedUtc == null ? null : DateOnly.FromDateTime(x.PublishedUtc.Value.AddHours(7).Date)), TitleMatch = term != "" && EF.Functions.Collate(x.Title.Replace("đ", "d").Replace("Đ", "D"), "Latin1_General_100_CI_AI").Contains(term) });
        var answers = Answers();
        if (term != "") answers = answers.Where(x => EF.Functions.Collate(x.Text.Replace("đ", "d").Replace("Đ", "D"), "Latin1_General_100_CI_AI").Contains(term) || EF.Functions.Collate(x.AgencyAnswer.Replace("đ", "d").Replace("Đ", "D"), "Latin1_General_100_CI_AI").Contains(term));
        var b = answers.Select(x => new GeneralSearchKey { Id = x.Id, Kind = 99, Date = DateOnly.FromDateTime(x.ApprovedUtc.AddHours(7).Date), TitleMatch = term != "" && EF.Functions.Collate(x.Text.Replace("đ", "d").Replace("Đ", "D"), "Latin1_General_100_CI_AI").Contains(term) });
        IQueryable<GeneralSearchKey> rows = kind == "Inquiry" ? (categoryId == null ? b : b.Where(x => false)) : string.IsNullOrEmpty(kind) && categoryId == null ? a.Concat(b) : a;
        if (from != null) rows = rows.Where(x => x.Date >= from);
        if (to != null) rows = rows.Where(x => x.Date <= to);
        return rows;
    }
    public IOrderedQueryable<GeneralSearchKey> Order(IQueryable<GeneralSearchKey> rows, string sort, string? q) => sort switch
    {
        "title" => rows.OrderBy(x => x.Kind == 99 ? db.InteractionPublicSnapshots.Where(p => p.Id == x.Id).Select(p => p.Text.Substring(0, 240)).FirstOrDefault() : (from c in db.Contents join r in db.Revisions on c.PublishedRevisionId equals (Guid?)r.Id where c.Id == x.Id select r.Title).FirstOrDefault()).ThenBy(x => x.Kind).ThenBy(x => x.Id),
        "oldest" => rows.OrderBy(x => x.Date).ThenBy(x => x.Kind).ThenBy(x => x.Id),
        "relevant" when !string.IsNullOrWhiteSpace(q) => rows.OrderByDescending(x => x.TitleMatch).ThenByDescending(x => x.Date).ThenBy(x => x.Kind).ThenBy(x => x.Id),
        _ => rows.OrderByDescending(x => x.Date).ThenBy(x => x.Kind).ThenBy(x => x.Id)
    };
    public async Task<List<GeneralSearchRow>> PageAsync(IQueryable<GeneralSearchKey> rows, string sort, string? q, int page)
    {
        var keys = await Order(rows, sort, q).Skip((page - 1) * 10).Take(10).ToArrayAsync();
        var articles = keys.Where(x => x.Kind != 99).Select(x => x.Id).ToArray(); var answers = keys.Where(x => x.Kind == 99).Select(x => x.Id).ToArray();
        var items = articles.Length == 0 ? [] : await content.Published().Where(x => articles.Contains(x.Id)).Select(x => new GeneralSearchRow { Id = x.Id, Title = x.Title, Summary = x.Summary, Kind = (int)x.Kind }).ToListAsync();
        if (answers.Length > 0) items.AddRange(await Answers().Where(x => answers.Contains(x.Id)).Select(x => new GeneralSearchRow { Id = x.Id, Title = x.Text, Summary = x.AgencyAnswer, Kind = 99 }).ToListAsync());
        foreach (var item in items) item.Date = keys.Single(x => x.Id == item.Id && x.Kind == item.Kind).Date;
        return keys.Select(key => items.SingleOrDefault(x => x.Id == key.Id && x.Kind == key.Kind)).Where(x => x != null).Select(x => x!).ToList();
    }
}
