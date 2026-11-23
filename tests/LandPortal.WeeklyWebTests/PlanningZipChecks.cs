using AngleSharp.Html.Parser;
using LandPortal.Infrastructure;
using LandPortal.WeeklyImport;

internal static class PlanningZipChecks
{
    public static async Task RunAsync(string source)
    {
        if (PlanningPackage.FileHash(source) != PlanningZipSupplement.ApprovedSha256) throw new Exception("Wrong read-only source ZIP fixture");
        var count = 0; var requests = 0; var scan = "NotScanned"; var corrupt = false;
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine($"PASS {++count} {name}"); }
        async Task Reject(Func<Task> action, string name) { try { await action(); } catch (InvalidDataException) { Check(true, name); return; } throw new Exception("Accepted " + name); }
        var id = PlanningZipSupplement.ApprovedContentId; var pdfId = Guid.NewGuid(); var zipId = Guid.NewGuid();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapGet("/admin/documents/{content:guid}/items", (Guid content) =>
        {
            requests++; return Results.Json(new { items = new[] { new { id = zipId, originalName = PlanningZipSupplement.ApprovedName,
                mimeType = EditorialZipPolicy.Mime, byteLength = 124514760L, scanStatus = scan, usable = scan == "Clean" } } });
        });
        app.MapGet("/admin/documents/file/{asset:guid}", (Guid asset) =>
        {
            requests++;
            return corrupt ? Results.Bytes("different bytes"u8.ToArray(), EditorialZipPolicy.Mime) : Results.File(source, EditorialZipPolicy.Mime);
        });
        await app.StartAsync();
        try
        {
            using var cms = new WebCmsClient(new Uri(app.Urls.Single() + "/"), true);
            var html = new HtmlParser().ParseDocument($"<form id='editor-form'><ul id='selected-attachments'><li data-asset='{zipId}'><span>{PlanningZipSupplement.ApprovedName}</span></li></ul></form>");
            var editor = new WebEditor(id, "Draft", true, "fixture", html.QuerySelector("form")!);
            var row = new PlanningRow(new WeeklyRow(21, 27, "Fixture", "s", "2026-07-09", "21.pdf", "21.pdf", "pdf-hash", 100, 1, 1, null, null, []),
                [new PlanningFile("21.pdf", "21.pdf", 100, "pdf-hash", 1, [])], []);
            Check(await PlanningZipSupplement.VerifyExtraAsync(cms, editor, row, [pdfId]) == null && requests == 0, "no ZIP keeps original PDF reconciliation without HTTP");
            await Reject(() => PlanningZipSupplement.VerifyExtraAsync(cms, editor, row with { Source = row.Source with { Stt = 20 } }, [pdfId, zipId]), "other STT cannot inherit ZIP scope");
            await Reject(() => PlanningZipSupplement.VerifyExtraAsync(cms, editor with { Id = Guid.NewGuid() }, row, [pdfId, zipId]), "different ContentId rejected");
            await Reject(() => PlanningZipSupplement.VerifyExtraAsync(cms, editor, row, [pdfId, zipId, Guid.NewGuid()]), "additional unexpected asset rejected");
            Check(requests == 0, "scope failures make no CMS request");
            await Reject(() => PlanningZipSupplement.VerifyExtraAsync(cms, editor, row, [pdfId, zipId]), "NotScanned ZIP rejected despite owner attachment request");
            scan = "Clean"; corrupt = true;
            await Reject(() => PlanningZipSupplement.VerifyExtraAsync(cms, editor, row, [pdfId, zipId]), "CMS metadata cannot replace byte/hash proof");
            corrupt = false;
            Check(await PlanningZipSupplement.VerifyExtraAsync(cms, editor, row, [pdfId, zipId]) == zipId && row.Files.Count == 1, "exact ZIP recognized separately without becoming PDF number 34");
            Check(PlanningPackage.FileHash(source) == PlanningZipSupplement.ApprovedSha256, "source ZIP unchanged");
            Console.WriteLine($"ZIP RECONCILE {count} PASS: HTTP localhost contract fixture only; mock scan states, no real AV, no authentication/SQL/production CMS test.");
        }
        finally { await app.StopAsync(); }
    }
}
