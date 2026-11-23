using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Only a freshly created, uniquely named LocalDB. No operational configuration.
static class MaterialsFilterChecks
{
    public static async Task Run(string checkout, string output)
    {
        var root = Path.GetFullPath(checkout); var run = Path.GetFullPath(output);
        if (!run.StartsWith(Path.Combine(root, ".local", "tests", "materials-"), StringComparison.OrdinalIgnoreCase) || Directory.Exists(run)) throw new Exception("New isolated output required");
        Directory.CreateDirectory(run);
        var instance = "LPMaterials_" + Guid.NewGuid().ToString("N")[..12];
        async Task LocalDb(string action)
        {
            var si = new ProcessStartInfo("SqlLocalDB.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            si.ArgumentList.Add(action); si.ArgumentList.Add(instance);
            using var p = Process.Start(si)!; var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(); await stdout; await stderr;
            if (p.ExitCode != 0) throw new Exception("LocalDB " + action + " failed");
        }
        var checks = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); Console.WriteLine("PASS " + name); }
        var started = false;
        try
        {
            await LocalDb("create"); await LocalDb("start"); started = true;
            var sql = new SqlConnectionStringBuilder { DataSource = @"(localdb)\" + instance, InitialCatalog = "LandPortal_Test", IntegratedSecurity = true, Encrypt = true, TrustServerCertificate = true, Pooling = false };
            await using (var connection = new SqlConnection(new SqlConnectionStringBuilder(sql.ConnectionString) { InitialCatalog = "master" }.ConnectionString))
            {
                await connection.OpenAsync(); using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB')),DB_ID('LandPortal_Test')";
                using (var r = await cmd.ExecuteReaderAsync()) { await r.ReadAsync(); Check(r.GetInt32(0) == 1 && r.IsDBNull(1), "Fresh LocalDB, no existing database"); }
                cmd.CommandText = "CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'" + Path.Combine(run, "test.mdf").Replace("'", "''") + "') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'" + Path.Combine(run, "test.ldf").Replace("'", "''") + "')";
                await cmd.ExecuteNonQueryAsync();
            }
            var options = new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(sql.ConnectionString).Options;
            await using (var db = new PortalDbContext(options))
            {
                await db.Database.MigrateAsync(); Check((await db.Database.GetAppliedMigrationsAsync()).Count() == 18, "Isolated schema18");
                db.Users.Add(new() { Id = "fixture", UserName = "fixture" });
                var site = new SiteConfigurationRevision();
                foreach (var (id, name, parent, visible) in new[] { (84,"Tài liệu",(int?)null,true), (86,"Hủy phôi GCN",(int?)84,true), (77,"QHKHSDD",(int?)null,true), (87,"Ẩn",(int?)84,false), (88,"Không liên quan",(int?)null,true), (89,"Nhóm trống",(int?)84,true) })
                {
                    db.Categories.Add(new() { Id = id, Name = name, Slug = "fixture-" + id });
                    site.Categories.Add(new() { CategoryId = id, DisplayName = name, Slug = "fixture-" + id, ParentCategoryId = parent, ShowInNavigation = visible, SortOrder = id });
                }
                site.Menus.Add(new() { Label = "Tài liệu", TargetKind = "Route", RouteKey = "Materials", CategoryId = 84 });
                db.Add(site); await db.SaveChangesAsync(); db.Add(new SiteConfiguration { LiveRevisionId = site.Id }); await db.SaveChangesAsync();
            }
            async Task Add(string title, int primary, int? secondary = 84, bool published = true, bool quarantined = false, ContentKind kind = ContentKind.Article)
            {
                await using var db = new PortalDbContext(options);
                var c = new Content { OwnerUserId = "fixture", Kind = kind, PrimaryCategoryId = primary }; db.Contents.Add(c); await db.SaveChangesAsync();
                var r = new ContentRevision { ContentId = c.Id, Title = title, SearchText = title, PrimaryCategoryId = primary, BodyHtml = "<p>Fixture</p>", ContentHash = Guid.NewGuid().ToString("N"), Metadata = new() { PublicationDate = new DateOnly(2026, 1, 30) } };
                db.Revisions.Add(r); await db.SaveChangesAsync();
                c.CurrentRevisionId = r.Id; c.PublishedRevisionId = published ? r.Id : null; c.IsPublic = published; c.State = published ? WorkflowState.Published : WorkflowState.Draft; c.PublishedAtUtc = DateTimeOffset.UtcNow;
                if (secondary.HasValue) db.RevisionCategories.Add(new() { RevisionId = r.Id, CategoryId = secondary.Value });
                var m = new MediaAsset { OriginalName = title + ".pdf", StorageKey = "fixture/" + Guid.NewGuid() + ".pdf", MimeType = "application/pdf", ByteLength = 10, Sha256 = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(title))), Origin = "EditorialDocument", ScanStatus = "Clean", ValidationStatus = "Validated", IsQuarantined = quarantined };
                // Synthetic SQL eligibility fixture only; not a scan or real media upload.
                db.MediaAssets.Add(m); db.EditorialDocuments.Add(new() { MediaAssetId = m.Id, ContentId = c.Id, OwnerId = "fixture", ScanState = InquiryScanState.Clean, ScannedSha256 = m.Sha256, ScannedUtc = DateTimeOffset.UtcNow, EngineVersion = "SyntheticFixture" });
                db.ContentAssets.Add(new() { RevisionId = r.Id, MediaAssetId = m.Id, Usage = "Attachment" }); await db.SaveChangesAsync();
            }
            await Add("HuyPhoi",86); await Add("QHKH",77); await Add("Secondary",84,86);
            await Add("Outside",86,null); await Add("Draft",86,published:false); await Add("Quarantine",86,quarantined:true); await Add("Legal",86,kind:ContentKind.LegalDocument);
            async Task<IActionResult> Page(int? category = null, string? q = null)
            {
                await using var db = new PortalDbContext(options); var policy = new MediaUploadOptions(); var query = new PublicContentQuery(db,policy);
                var storage = new StorageOptions { MediaRoot = Path.Combine(run,"media"), SnapshotRoot = Path.Combine(run,"snapshots"), TempRoot = Path.Combine(run,"temp"), KeysRoot = Path.Combine(run,"keys") };
                var sites = new PublicSiteService(db,new SiteConfigurationService(db,storage,policy),query,policy,new HttpContextAccessor());
                return await new MaterialsController(db,query,sites,policy).Index(q,categoryId:category);
            }
            var all = (MaterialsModel)((ViewResult)await Page()).Model!;
            Check(all.Total == 3, "Public Materials scope excludes draft, quarantine, legal and outside scope");
            Check(all.Categories.Select(x=>x.Id).SequenceEqual(new[]{77,86,89}), "Visible populated categories and empty configured children only");
            var filtered = (MaterialsModel)((ViewResult)await Page(86)).Model!;
            Check(filtered.Total == 2 && filtered.Items.All(x=>x.Content.Title is "HuyPhoi" or "Secondary"), "Primary/secondary filter without broadening scope");
            Check(((MaterialsModel)((ViewResult)await Page(77)).Model!).Total == 1, "Existing QHKHSDD retained");
            Check(((MaterialsModel)((ViewResult)await Page(86,"HuyPhoi")).Model!).Total == 1, "Search combines with category");
            Check(((MaterialsModel)((ViewResult)await Page(89)).Model!).Total == 0, "Empty configured child is empty, not all documents");
            foreach(var id in new[]{87,88,999}) Check(await Page(id) is NotFoundResult,"Hidden/unrelated/unknown category denied " + id);
            Check(filtered.Items.All(x=>x.Content.DisplayDate=="30/01/2026"),"Metadata display date unchanged");
            await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{checks,productionWrites=0}));
        }
        finally { SqlConnection.ClearAllPools(); if(started) await LocalDb("stop"); }
    }
}
