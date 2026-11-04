using System.Threading.RateLimiting;
using LandPortal.Web.Controllers;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.DataProtection;

// Explicit technical fixture host. No PortalConfiguration, database, Identity, SMTP or source credentials.
var rows = Enumerable.Range(1, 24).Select(i => new GiayHuyPublicRow(
    Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}"), i <= 12 ? "2026" : "2025",
    i % 2 == 0 ? "Thành phố Hà Nội" : "Tỉnh Bắc Ninh", $"THU-NGHIEM-{i:00}/QĐ",
    $"GCN-THU-{i:00}", "DỮ LIỆU THỬ KỸ THUẬT — không phải quyết định chính thức.")).ToArray();
if (args.Contains("--check"))
{
    var pass = 0;
    void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("FAIL " + name); Console.WriteLine("PASS " + name); pass++; }
    var all = GiayHuyLookup.Filter(rows, null, null, null, 1);
    Check(all.Total == 24 && all.Rows.Count == 10 && all.Years.SequenceEqual(new[] { 2026, 2025 }), "year/list/count/page");
    Check(GiayHuyLookup.Filter(rows, 2026, "Tỉnh Bắc Ninh", null, 1).Total == 6, "combined year/province");
    Check(GiayHuyLookup.Filter(rows, 2026, "Tỉnh không tồn tại", null, 1).Province == null, "stale province cleared");
    Check(GiayHuyLookup.Filter(rows, null, null, "quyet dinh", 1).Total == 24, "Vietnamese accent-insensitive search");
    Check(GiayHuyLookup.Filter(rows, null, null, "THU-NGHIEM-01", 1).Total == 1, "decision number search");
    Check(GiayHuyLookup.Filter(rows, null, null, "GCN-THU-24", 1).Total == 1, "certificate search");
    Check(GiayHuyLookup.Filter(rows, null, null, null, 3).Rows.Count == 4, "pagination last page");
    Check(GiayHuyProvince.Normalize("TP. HÀ NỘI") == "Thành phố Hà Nội" && GiayHuyProvince.Normalize("tphcm") == "Thành phố Hồ Chí Minh"
        && GiayHuyProvince.Normalize("không rõ") == "Chưa xác định Tỉnh/Thành phố", "exact province aliases only");
    var approval = new GiayHuyPublication(rows[0].Id, GiayHuyLookup.PublicationHash(rows[0]), true);
    Check(GiayHuyLookup.Approved(rows[0], approval) && !GiayHuyLookup.Approved(rows[0] with { DecisionContent = "changed" }, approval)
        && !GiayHuyLookup.Approved(rows[1], approval), "publication ID/hash fail closed on source drift");
    var noPublication = new GiayHuyLookup(new ConfigurationBuilder().Build(), NullLogger<GiayHuyLookup>.Instance);
    Check((await noPublication.SearchAsync(null, null, null, 1, default)).Total == 0 &&
        (await noPublication.DetailAsync(rows[0].Id, default)).Row == null, "unapproved source rows denied without SQL");
    var expired = new GiayHuyLookup(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["GiayHuy:Publication:ApprovalReference"] = "technical fixture", ["GiayHuy:Publication:ExpiresUtc"] = "2000-01-01T00:00:00Z"
    }).Build(), NullLogger<GiayHuyLookup>.Instance);
    Check((await expired.SearchAsync(null, null, null, 1, default)).Total == 0, "expired approval denied");
    var blocked = 0;
    foreach (var raw in new[] {
        "Server=45.120.229.43,1433;Database=QuanLyDuAnTrungTamDuLieuDatDai;User ID=sa;Password=FIXTURE_ONLY;Encrypt=True",
        "Server=45.120.229.43,1433;Database=website_tin_tuc;User ID=reader;Password=FIXTURE_ONLY;Encrypt=True",
        "Server=45.120.229.43,1433;Database=QuanLyDuAnTrungTamDuLieuDatDai;User ID=reader;Password=FIXTURE_ONLY;Encrypt=True;TrustServerCertificate=True"
    }) { try { GiayHuyLookup.ValidateConnection(raw, "reader"); } catch (InvalidOperationException) { blocked++; } }
    Check(blocked == 3, "sa/wrong database/TLS bypass rejected");
    var sql = GiayHuyLookup.PublicRowsSql;
    Check(sql.Contains("THEN y.giaTri ELSE CONVERT") && sql.Contains("d.hieuLuc=1 AND d.isDel=0") && sql.Contains("LOAI_BO")
        && !sql.Contains("SELECT *") && !sql.Contains("NOLOCK") && !sql.Contains("tenChuSuDung") && !sql.Contains("diaChi"), "narrow final-value/confirmed/document SQL projection");
    Console.WriteLine($"FOCUSED CHECKS {pass}/{pass}; NO SQL CONNECTIONS");
    return;
}
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = Path.GetFullPath("src/Web/wwwroot") });
builder.WebHost.UseUrls("http://127.0.0.1:5192"); // Loopback only; never a source-data public demo.
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddSingleton<IGiayHuyLookup>(new FixtureLookup(rows));
builder.Services.AddControllersWithViews(options => options.Filters.Add<FixtureLayout>())
    .AddApplicationPart(typeof(GiayHuyLookupController).Assembly);
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.AddPolicy("interaction", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseStaticFiles(); app.UseRouting(); app.UseRateLimiter();
app.MapGet("/", () => Results.Redirect(GiayHuyLookup.Route));
app.MapControllers();
await app.RunAsync();

sealed class FixtureLayout : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    { if (context.Controller is Controller controller) controller.ViewData["GiayHuyLayout"] = "/Views/Shared/_LookupCheckLayout.cshtml"; }
    public void OnActionExecuted(ActionExecutedContext context) { }
}
sealed class FixtureLookup(IReadOnlyList<GiayHuyPublicRow> rows) : IGiayHuyLookup
{
    public Task<GiayHuyLookupModel> SearchAsync(int? year, string? province, string? query, int page, CancellationToken cancellationToken) =>
        Task.FromResult(query == "SOURCE_UNAVAILABLE" ? new GiayHuyLookupModel { Unavailable = true } : GiayHuyLookup.Filter(rows, year, province, query, page));
    public Task<(GiayHuyPublicRow? Row, bool Unavailable)> DetailAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult((rows.SingleOrDefault(x => x.Id == id), false));
}
