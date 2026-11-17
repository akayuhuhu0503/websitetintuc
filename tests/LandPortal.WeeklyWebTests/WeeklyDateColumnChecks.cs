using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using LandPortal.WeeklyImport;

static class WeeklyDateColumnChecks
{
    public static async Task RunAsync(string path)
    {
        var checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { Check(true, name); return; } throw new Exception("NOT DENIED " + name); }
        var source = WeeklyDateSource.Read(path);
        var report = WeeklyDates.Inspect(source);
        Check(report.Count == 43 && report.Count(x => x.CellType == "n") == 17 && report.Count(x => x.Status == "VerifiedText") == 26,
            "43 column-B dates: 26 Vietnamese text dates and 17 unresolved numeric conventions");
        Check(report[39].StoredDate == "2026-10-19" && report[40].StoredDate == "2026-10-26"
            && report.Skip(39).Take(2).All(x => x.Status == "VerifiedText" && x.Warnings.Length == 0), "STT40/41 resolved solely from B despite unrelated PDF warnings");
        Check(report[0].StoredDate == "2026-05-01" && report[0].SwappedCandidate == "2026-01-05" && report[0].ProposedDate == null,
            "Serial46143 decoded as May1; January5 never automatically selected");
        Check(report[42].StoredDate == "2026-04-11" && report[42].SwappedCandidate == "2026-11-04" && report[42].ProposedDate == null,
            "STT43 derives alternatives only from B, no title week-start inference");
        Check(report[12].RawValue.StartsWith(' ') && report[12].StoredDate == "2026-04-06", "Leading whitespace preserved in report, strict Vietnamese date parsed");
        var baseline = JsonSerializer.Serialize(report);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "en-US", "vi-VN", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Check(JsonSerializer.Serialize(WeeklyDates.Inspect(source)) == baseline, "Machine locale cannot alter dates: " + name);
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }

        WeeklyDateSource Mutate(Action<XDocument, XDocument, XDocument> change)
        {
            using var input = new ZipArchive(new MemoryStream(source.Workbook));
            var sheet = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(input.GetEntry(source.SheetPath)!));
            var book = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(input.GetEntry("xl/workbook.xml")!));
            var styles = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(input.GetEntry("xl/styles.xml")!));
            change(sheet, book, styles);
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                foreach (var entry in input.Entries)
                {
                    using var stream = zip.CreateEntry(entry.FullName).Open();
                    var xml = entry.FullName == source.SheetPath ? sheet : entry.FullName == "xl/workbook.xml" ? book : entry.FullName == "xl/styles.xml" ? styles : null;
                    if (xml != null) xml.Save(stream); else { using var original = entry.Open(); original.CopyTo(stream); }
                }
            var bytes = output.ToArray();
            return source with { Workbook = bytes, WorkbookSha256 = WeeklyPackage.Hash(bytes) };
        }
        XElement Cell(XDocument sheet, string address) => sheet.Descendants(WeeklyPackage.S + "c").Single(x => (string?)x.Attribute("r") == address);
        var changedTitle = Mutate((sheet, _, _) =>
        {
            foreach (var c in sheet.Descendants(WeeklyPackage.S + "c").Where(x => ((string?)x.Attribute("r"))?.StartsWith('C') == true))
            { c.SetAttributeValue("t", "inlineStr"); c.RemoveNodes(); c.Add(new XElement(WeeklyPackage.S + "is", new XElement(WeeklyPackage.S + "t", "Unrelated date 01/01/2099"))); }
        });
        Check(JsonSerializer.Serialize(WeeklyDates.Inspect(changedTitle)) == baseline, "Changing every title cannot affect any publication date");
        var package = new WeeklyPackage { ZipSha256 = source.ZipSha256, WorkbookSha256 = source.WorkbookSha256, Workbook = source.Workbook,
            SheetPath = source.SheetPath, PdfBytes = new Dictionary<int, byte[]>(), Rows = report.Select(x => new WeeklyRow(x.Stt, x.Stt + 6,
                "Ignore title", "s", "01/01/2099", "", "", "", 0, 0, 0, "wrong week", "wrong PDF", ["PDF mismatch"])).ToArray() };
        Check(JsonSerializer.Serialize(WeeklyDates.Inspect(package)) == baseline, "Cached row dates, titles and PDF warnings cannot replace workbook column B");
        Check(WeeklyDates.Resolve(source, null).Count == 26 && !WeeklyDates.Resolve(source, null).ContainsKey(1), "Unconfirmed numeric dates remain unwritable");
        DateDecisions Decision(int stt, string date) => new(source.ZipSha256, source.WorkbookSha256, [new(stt, date, "Synthetic fixture only")]);
        Check(WeeklyDates.Resolve(source, Decision(1, "2026-05-01"))[1] == "2026-05-01"
            && WeeklyDates.Resolve(source, Decision(1, "2026-01-05"))[1] == "2026-01-05", "Explicit source-bound numeric interpretation supported without culture conversion");
        Reject(() => WeeklyDates.Resolve(source, Decision(43, "2026-11-02")), "Title-derived date not in column-B alternatives rejected");
        Reject(() => WeeklyDates.Resolve(source, Decision(40, "2026-10-12")), "Confirmed text date cannot be replaced by title/PDF date");
        Reject(() => WeeklyDates.Resolve(source, Decision(1, "2026-01-05") with { WorkbookSha256 = "wrong" }), "Wrong workbook confirmation rejected");
        Reject(() => WeeklyDates.Resolve(source, Decision(1, "2026-01-05") with { Rows = [new(1, "2026-01-05", null)] }), "Unattributed interpretation rejected");
        Reject(() => WeeklyDates.Inspect(source with { WorkbookSha256 = "wrong" }), "Changed workbook bytes rejected before normalization");
        foreach (var pair in new[] { ("1", "1900-01-01"), ("59", "1900-02-28"), ("61", "1900-03-01") })
        {
            var test = Mutate((sheet, _, _) => Cell(sheet, "B7").Element(WeeklyPackage.S + "v")!.Value = pair.Item1);
            Check(WeeklyDates.Inspect(test)[0].StoredDate == pair.Item2, "Excel1900 serial/leap-day conversion: " + pair.Item1);
        }
        foreach (var serial in new[] { "60", "46143.5", "NaN", "100000", "-1" })
        {
            var test = Mutate((sheet, _, _) => Cell(sheet, "B7").Element(WeeklyPackage.S + "v")!.Value = serial);
            Check(WeeklyDates.Inspect(test)[0].StoredDate == null && !WeeklyDates.Resolve(test, null).ContainsKey(1), "Invalid/fractional Excel date held: " + serial);
        }
        var alternateEpoch = Mutate((sheet, book, _) =>
        {
            var props = book.Root!.Element(WeeklyPackage.S + "workbookPr");
            if (props == null) { props = new XElement(WeeklyPackage.S + "workbookPr"); book.Root.AddFirst(props); }
            props.SetAttributeValue("date1904", "1"); Cell(sheet, "B7").Element(WeeklyPackage.S + "v")!.Value = "0";
        });
        Check(WeeklyDates.Inspect(alternateEpoch)[0].StoredDate == "1904-01-01", "Workbook1904 epoch honored independently of machine locale");
        var formula = Mutate((sheet, _, _) => Cell(sheet, "B7").Add(new XElement(WeeklyPackage.S + "f", "TODAY()")));
        Check(WeeklyDates.Inspect(formula)[0].StoredDate == null && !WeeklyDates.Resolve(formula, null).ContainsKey(1), "Formula cache cannot become an approved publication date");
        var custom = Mutate((sheet, _, styles) =>
        {
            var formats = styles.Root!.Element(WeeklyPackage.S + "numFmts");
            if (formats == null) { formats = new XElement(WeeklyPackage.S + "numFmts"); styles.Root.AddFirst(formats); }
            formats.Add(new XElement(WeeklyPackage.S + "numFmt", new XAttribute("numFmtId", "65000"), new XAttribute("formatCode", "dd/mm/yyyy")));
            var xfs = styles.Root.Element(WeeklyPackage.S + "cellXfs")!;
            Cell(sheet, "B7").SetAttributeValue("s", xfs.Elements().Count());
            xfs.Add(new XElement(WeeklyPackage.S + "xf", new XAttribute("numFmtId", "65000")));
        });
        Check(WeeklyDates.Inspect(custom)[0].NumberFormat == "dd/mm/yyyy" && WeeklyDates.Inspect(custom)[0].StoredDate == "2026-05-01", "Explicit display format cannot swap the underlying calendar date");
        Check(!WeeklyDates.TryVietnamese("12/31/2026", out _) && !WeeklyDates.TryVietnamese("31/02/2026", out _)
            && WeeklyDates.TryVietnamese("6/4/2026", out var vi) && vi == new DateOnly(2026, 4, 6), "Text dates accept Vietnamese day/month only");

        var outputPath = Path.Combine(Directory.GetCurrentDirectory(), ".local", "tests", "weekly-date-column-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);
        var zipPath = Path.Combine(outputPath, "workbook-only.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var stream = zip.CreateEntry(WeeklyPackage.WorkbookName).Open()) stream.Write(source.Workbook);
            using var writer = new StreamWriter(zip.CreateEntry("Lich Tuan/1_invalid.pdf").Open()); writer.Write("Deliberately not a PDF; date report must not open it.");
        }
        var zipHash = WeeklyPackage.Hash(File.ReadAllBytes(zipPath));
        await WebImportCommand.RunAsync(["web-date-report", "--zip", zipPath, "--out", outputPath]);
        var saved = JsonSerializer.Deserialize<WeeklyDateRow[]>(File.ReadAllBytes(Directory.GetFiles(outputPath, "date-report-*.json").Single()))!;
        Check(JsonSerializer.Serialize(saved) == baseline, "Real DateReport CLI succeeds with malformed/unavailable PDFs and never needs CMS credentials");
        Check(WeeklyPackage.Hash(File.ReadAllBytes(zipPath)) == zipHash && WeeklyPackage.Hash(source.Workbook) == source.WorkbookSha256, "Source ZIP/workbook cells and styles unchanged");
        var decisionPath = Path.Combine(outputPath, "date-decisions.json"); var decisionBytes = File.ReadAllBytes(decisionPath);
        var decisions = JsonSerializer.Deserialize<DateDecisions>(decisionBytes)!;
        Check(decisions.Rows.Count == 17 && decisions.Rows.All(x => x.ConfirmedDate == null && x.ConfirmedBy == null), "Report does not invent confirmations");
        await WeeklyDates.ReportAsync(WeeklyDateSource.Read(zipPath), outputPath);
        Check(File.ReadAllBytes(decisionPath).SequenceEqual(decisionBytes), "Repeated report preserves existing decisions");
        // Test confirmation is explicitly synthetic, never copied to the user's source approval.
        var confirmed = decisions with { Rows = report.Where(x => x.CellType == "n").Select(x => new DateDecision(x.Stt, x.SwappedCandidate, "Synthetic fixture only")).ToArray() };
        await File.WriteAllBytesAsync(decisionPath, JsonSerializer.SerializeToUtf8Bytes(confirmed));
        var normalized = WeeklyDates.Resolve(WeeklyDateSource.Read(zipPath), confirmed);
        Check(normalized.Count == 43 && normalized[1] == "2026-01-05" && normalized[43] == "2026-11-04"
            && normalized[40] == "2026-10-19" && normalized[41] == "2026-10-26", "17 explicit interpretations plus 26 text dates normalize all43, including STT1/40/41/43");
        var completedReport = Path.Combine(outputPath, "confirmed-report"); Directory.CreateDirectory(completedReport);
        File.Copy(decisionPath, Path.Combine(completedReport, "date-decisions.json"));
        await WebImportCommand.RunAsync(["web-date-report", "--zip", zipPath, "--out", completedReport]);
        var completed = JsonSerializer.Deserialize<WeeklyDateRow[]>(File.ReadAllBytes(Directory.GetFiles(completedReport, "date-report-*.json").Single()))!;
        Check(completed.All(x => x.ProposedDate == normalized[x.Stt]) && completed.Count(x => x.Status == "ConfirmedNumeric") == 17
            && completed.Count(x => x.Status == "VerifiedText") == 26, "Offline report shows exact resolved CMS dates, zero pending, no fabricated approval");
        Check(WeeklyDates.Resolve(source, confirmed with { ZipSha256 = source.ZipSha256 }).OrderBy(x => x.Key).SequenceEqual(normalized.OrderBy(x => x.Key)),
            "Normalization is deterministic and never falls back to current date");
        WeeklyDateChecks.PlanGuards(package);
        Console.WriteLine($"DATE COLUMN PASS: {checks}/{checks}; plus 4 plan guards; no PDF parsing, HTTP or SQL.");
        await File.WriteAllTextAsync(Path.Combine(outputPath, "result.json"), JsonSerializer.Serialize(new { status = "PASS", checks, planGuards = 4, pdfParses = 0, sqlConnections = 0, httpRequests = 0 }));
    }
}
