using System.Text;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.MigrationCli;

namespace LandPortal.Tests;

public static class SrcsetParserTests
{
    static void Equal(IEnumerable<string> actual, params string[] expected)
    { if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("srcset URL/order mismatch: " + JsonSerializer.Serialize(actual)); }

    public static IEnumerable<(string Name, Func<Task> Test)> Cases(string root)
    {
        yield return ("srcset width density whitespace and stable candidate order", () => {
            Equal(SourceSrcset.Urls(" a.jpg 800w,\tb.jpg\t1.5x,\nc.jpg .5x, d.jpg 1e2x, e.jpg"), "a.jpg", "b.jpg", "c.jpg", "d.jpg", "e.jpg");
            Equal(SourceSrcset.Urls("a.jpg, b.jpg, c.jpg 400w 200h"), "a.jpg", "b.jpg", "c.jpg");
            return Task.CompletedTask;
        });
        yield return ("srcset commas inside URL tokens and encoded filenames are preserved", () => {
            Equal(SourceSrcset.Urls("https://media-qldd.mae.gov.vn/a,b.jpg 1x, data:image/png;base64,AA== 2x, c%20d.jpg 3x"),
                "https://media-qldd.mae.gov.vn/a,b.jpg", "data:image/png;base64,AA==", "c%20d.jpg");
            return Task.CompletedTask;
        });
        yield return ("srcset malformed and conflicting descriptors never yield truncated URLs", () => {
            Equal(SourceSrcset.Urls("a.jpg 0w, b.jpg -1x, c.jpg 1x 2x, d.jpg 2w 1x, e.jpg 2h, f.jpg calc(1,2), g.jpg 12q, full name.jpg 2x, valid.jpg 2x"), "valid.jpg");
            Equal(SourceSrcset.Urls("full name.jpg 1x 2x", "full name.jpg"));
            Equal(SourceSrcset.Urls("full name.jpg 2x", "other name.jpg"));
            Equal(SourceSrcset.Urls("full name.jpg 2x", "full name.jpg"), "full name.jpg");
            return Task.CompletedTask;
        });
        yield return ("srcset real Article3075 raw hash five complete source images no fabricated sixth URL", async () => {
            const string raw = ".local/phase3a/9d95c26f85a54a8aa2df61def03cf360/responses/6784c4fb93abcf41f5d48005f1e6f4961459c7d2ce2d6b24ba9d6deb2a1a3ca7.bin";
            var bytes = await File.ReadAllBytesAsync(Path.Combine(root, raw));
            if (SourceInventory.Hash(bytes) != Path.GetFileNameWithoutExtension(raw)) throw new InvalidDataException("Real raw fixture changed.");
            var html = new UTF8Encoding(false, true).GetString(bytes);
            var parsed = SourceParser.Parse(html, "https://qldd.mae.gov.vn/Pages/chi-tiet-tin-tuc.aspx?ItemID=3075", ContentKind.Article, "3075");
            var expected = SourceParser.Document(parsed.BodyHtml).QuerySelectorAll("img[src]").Select(x => new Uri(new Uri(parsed.SourceUrl), x.GetAttribute("src")!).AbsoluteUri).Distinct().ToArray();
            if (expected.Length != 5) throw new InvalidDataException("Expected five real source images.");
            Equal(parsed.AssetUrls, expected);
            if (!parsed.AssetUrls.Any(x => x.EndsWith("%C4%90%E1%BB%97%20%C4%90%E1%BB%A9c%20Duy_fdd2a81ce2.jpg", StringComparison.Ordinal))) throw new InvalidOperationException("Complete Vietnamese filename lost.");
            await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root), "article3075-parser.json"), new { raw, rawSha = SourceInventory.Hash(bytes), parsed.SourceId, parsed.AssetUrls, PreservedImageOrder = true, NetworkRequests = 0 });
        });
    }
}
