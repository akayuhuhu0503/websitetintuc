using Microsoft.Extensions.Configuration;
namespace LandPortal.Infrastructure;
public static class SynchronizationGuard
{
    public static async Task EnsureEnabledAsync(PortalDbContext db, IConfiguration config)
    {
        if (!bool.TryParse(config["Sync:Enabled"], out var enabled) || !enabled || !string.IsNullOrWhiteSpace(config["Sync:CutoverUtc"]))
            throw new InvalidOperationException("Đồng bộ nguồn đã tắt trong cấu hình hoặc có mốc cutover.");
        var flag = await db.SiteSettings.FindAsync("SyncEnabled");
        var cutover = await db.SiteSettings.FindAsync("CutoverUtc");
        if (string.Equals(flag?.Value, "false", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(cutover?.Value))
            throw new InvalidOperationException("Đồng bộ nguồn bị chặn bởi mốc cutover đã lưu trong SQL; restart không bật lại.");
    }
}
