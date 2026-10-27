using System.Linq.Expressions;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Services;

// Reuses AuditLog and the protected PublicTraffic reader session. No parallel counter store.
public sealed class ArticleDiscovery(PortalDbContext db, PublicContentQuery query)
{
    public const string ViewAction = "PublicArticleView";
    public async Task<IReadOnlyList<ContentCard>> PopularAsync(int days, int take = 5)
    {
        if (days is not (7 or 30)) throw new ArgumentOutOfRangeException(nameof(days));
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var views = db.AuditLogs.Where(x => x.Action == ViewAction && x.CreatedUtc >= since);
        var ranked = query.Published().Where(x=>x.Kind==ContentKind.Article&&views.Any(v=>v.ContentId==x.Id))
            .OrderByDescending(x=>views.LongCount(v=>v.ContentId==x.Id)).ThenByDescending(x=>x.PublishedUtc).ThenBy(x=>x.Id);
        return (await query.CardsPageAsync(ranked, 0, Math.Clamp(take, 1, 5))).Select(PublicContentQuery.Card).ToArray();
    }

    public async Task<IReadOnlyList<ContentCard>> RelatedAsync(PublicRow article, string? tags)
    {
        // Build a bounded SQL CASE expression: shared tag, then primary category, then recency.
        var row = Expression.Parameter(typeof(PublicRow), "row");
        Expression score = Expression.Constant(0);
        foreach (var tag in (tags ?? "").Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(30))
        {
            var value = "," + tag + ",";
            var normalized = Expression.Call(Expression.Property(row, nameof(PublicRow.Tags)), nameof(string.Replace), null, Expression.Constant(";"), Expression.Constant(","));
            // SQL REPLACE handles spaces around delimiters, while preserving spaces inside a tag.
            normalized = Expression.Call(normalized, nameof(string.Replace), null, Expression.Constant(", "), Expression.Constant(","));
            normalized = Expression.Call(normalized, nameof(string.Replace), null, Expression.Constant(" ,"), Expression.Constant(","));
            var wrapped = Expression.Call(typeof(string).GetMethod(nameof(string.Concat),[typeof(string),typeof(string),typeof(string)])!, Expression.Constant(","), normalized, Expression.Constant(","));
            var found = Expression.Call(wrapped, nameof(string.Contains), null, Expression.Constant(value));
            score = Expression.OrElse(score.Type == typeof(bool) ? score : Expression.Constant(false), found);
        }
        var shared = score.Type == typeof(bool) ? score : Expression.Constant(false);
        var sameCategory = article.CategoryId == null ? (Expression)Expression.Constant(false) : Expression.Equal(Expression.Property(row, nameof(PublicRow.CategoryId)), Expression.Constant(article.CategoryId, typeof(int?)));
        var priority = Expression.Lambda<Func<PublicRow, int>>(Expression.Condition(shared, Expression.Constant(2), Expression.Condition(sameCategory, Expression.Constant(1), Expression.Constant(0))), row);
        var candidates = query.Published().Where(x => x.Id != article.Id && x.Kind == article.Kind)
            .OrderByDescending(priority).ThenByDescending(x => x.SourceDate).ThenByDescending(x => x.PublishedUtc).ThenBy(x => x.Id);
        return (await query.CardsPageAsync(candidates, 0, 5)).Select(PublicContentQuery.Card).ToArray();
    }
}
