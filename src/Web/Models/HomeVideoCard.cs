namespace LandPortal.Web.Models;
public sealed record HomeVideoCard(ContentCard Content, Guid VideoId, double? DurationSeconds)
{
    public string PosterUrl => $"/video-media/{VideoId}/poster";
    public string? Duration => DurationSeconds is > 0 and < 86400
        ? TimeSpan.FromSeconds(DurationSeconds.Value).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : null;
}
