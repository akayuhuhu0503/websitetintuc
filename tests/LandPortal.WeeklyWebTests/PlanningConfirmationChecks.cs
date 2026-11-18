using System.Text.Json;
using LandPortal.WeeklyImport;

// Offline checks for the owner-reviewed source scope and dates. No SQL, login or HTTP requests.
static class PlanningConfirmationChecks
{
    public static async Task RunAsync(string zip, string bundle)
    {
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidDataException(name); count++; Console.WriteLine("PASS " + name); }
        void Denied(Action action, string name)
        {
            try { action(); }
            catch (InvalidDataException) { Check(true, name); return; }
            throw new InvalidDataException("NOT DENIED " + name);
        }
        var scope = JsonSerializer.Deserialize<PlanningScope>(File.ReadAllBytes(Path.Combine(bundle, "source-scope.json")))!;
        var decisions = JsonSerializer.Deserialize<DateDecisions>(File.ReadAllBytes(Path.Combine(bundle, "date-decisions.json")))!;
        var source = PlanningPackage.Read(zip, scope);
        Check(source.Rows.Count == 32 && source.Rows.Sum(x => x.Files.Count) == 33, "All 32 records and 33 direct PDFs retained");
        var row21 = source.Rows.Single(x => x.Stt == 21);
        Check(row21.Files.Count == 1 && row21.Files[0].Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            && !row21.CanImport && !row21.Files[0].Blocks.Any(x => x.StartsWith("ZIP ")), "ZIP21 excluded; primary PDF preserved with its independent format block");
        Check(source.Archives.Count == 1 && JsonSerializer.Serialize(source.Archives).Contains("ExcludedFromImport"), "Excluded archive remains explicitly recorded in inventory");
        Check(source.Rows.Where(x => x.CanImport).Select(x => x.Stt).SequenceEqual(new[] { 7,8,9,10,11,13,22,27,31 }), "Nine file-eligible records, 23 file blocks unchanged");
        var dates = WeeklyDates.Resolve(source.Dates, decisions);
        Check(dates.Count == 32, "All 32 dates resolved exclusively from column B");
        var serials = new Dictionary<int,string> { [1]="2026-06-01",[3]="2026-04-02",[5]="2026-04-03",[8]="2026-06-04",[11]="2026-04-05",[14]="2026-03-06",[19]="2026-05-08",[21]="2026-07-09",[24]="2026-08-10",[27]="2026-05-11",[30]="2026-02-12" };
        Check(serials.All(x => dates[x.Key] == x.Value), "11 serials use confirmed stored calendar, no month/day swap");
        Check(dates[31] == "2026-12-07", "STT31 confirmed as 07/12/2026");
        Check(WeeklyDates.Inspect(source.Dates).Where(x => x.Status == "VerifiedText").All(x => dates[x.Stt] == x.StoredDate), "20 unambiguous text dates unchanged");
        Denied(() => WeeklyDates.Resolve(source.Dates, decisions with { SheetName = null }), "Weekly sheet approval cannot authorize QHKHSDD");
        Denied(() => WeeklyDates.Resolve(source.Dates, decisions with { Rows = [new(1,"2026-11-17","Test")] }), "Date not from column B rejected");
        Denied(() => PlanningPackage.Read(zip, scope with { ZipSha256 = new string('0',64) }), "Archive exclusion cannot apply to a different source ZIP");
        Denied(() => PlanningPackage.Read(zip, scope with { ExcludedArchives = [new(row21.Files[0].Entry,row21.Files[0].Sha256,"Test")] }), "PDF cannot be excluded through archive scope");
        Denied(() => PlanningPackage.Read(zip, scope with { ExcludedArchives = [scope.ExcludedArchives[0] with { Sha256 = new string('0',64) }] }), "Exact excluded archive SHA checked, not merely its name");
        using var cms = new WebCmsClient(new Uri("https://gdla.gov.vn/")); // Constructed only; no request is sent.
        var importer = new PlanningWebImporter(cms);
        var plan = new PlanningPlan(PlanningPackage.Profile,source.Dates.SheetName,cms.Origin.AbsoluteUri,cms.UserId,
            source.Dates.ZipSha256,source.Dates.WorkbookSha256,source.Rows.Select(x => new PlanningPlanRow(x.Stt,"Blocked",dates[x.Stt],null,new Dictionary<string,Guid>(),null,[])).ToArray()) { SourceScopeSha256 = source.ScopeSha256 };
        importer.Validate(source,plan,new PlanningJournal(PlanningWebImporter.PlanHash(plan),[]),decisions);
        Check(true, "Reviewed scope accepted in plan/journal validation without connecting to CMS");
        var changed = plan with { SourceScopeSha256 = new string('0',64) };
        Denied(() => importer.Validate(source,changed,new PlanningJournal(PlanningWebImporter.PlanHash(changed),[]),decisions), "Plan cannot silently change reviewed source scope");
        var output = Path.Combine(Path.GetTempPath(),"gdla-planning-confirmations-"+Guid.NewGuid().ToString("N"));
        await PlanningCommand.WriteReportAsync(source,output,decisions);
        using var inventory = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output,"planning-inventory.json")));
        Check(inventory.RootElement.GetProperty("dates").EnumerateArray().All(x => x.GetProperty("Status").GetString() != "NeedsConfirmation"), "Offline report includes resolved dates, no stale pending labels");
        Check(File.ReadAllBytes(Path.Combine(output,"date-decisions.json")).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(decisions,WeeklyPackage.Json))
            && JsonSerializer.Deserialize<PlanningScope>(File.ReadAllBytes(Path.Combine(output,"source-scope.json")))!.ZipSha256 == scope.ZipSha256, "New run freezes the reviewed dates and exclusion");
        Check(PlanningPackage.FileHash(zip) == source.Dates.ZipSha256 && WeeklyPackage.Hash(source.Dates.Workbook) == source.Dates.WorkbookSha256, "Source ZIP and original workbook unchanged");
        Console.WriteLine($"PLANNING CONFIRMATIONS {count}/{count} PASS; HTTP/SQL writes=0; report=" + output);
    }
}
