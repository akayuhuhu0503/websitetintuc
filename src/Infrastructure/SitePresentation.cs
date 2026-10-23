namespace LandPortal.Infrastructure;

// Official BA value in one place; the live website configuration can override it.
public static class SitePresentation
{
    public const string OfficialEnglishName = "Department of Land Administration";
    public static string EnglishName(string? configured) => string.IsNullOrWhiteSpace(configured) ? OfficialEnglishName : configured;
}
