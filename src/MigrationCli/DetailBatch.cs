using System.Text.Json;
using LandPortal.Infrastructure;

namespace LandPortal.MigrationCli;

// A collection work order, never an approval or a CMS import plan.
public sealed record DetailBatch(Guid InventoryId, string Checkpoint, string CheckpointSha256,
    int Number, string[] SourceIdentities, string[] Urls, string Status = "WORK_ORDER_ONLY_USE_RECORDED_AUTHORITY");

public static class DetailBatchPlanner
{
    public static DetailBatch[] Create(InventoryState state, SourceCoverageReport coverage, string checkpoint, string sha)
    {
        var pending = coverage.Candidates.Where(x => x.Reason == "NO_DETAIL_SNAPSHOT_BUDGET_OR_NOT_FETCHED").ToList();
        var verified = coverage.Categories.ToDictionary(x => x.Id, x => x.Verified);
        var ordered = new List<CandidateCoverage>();
        // Diversify the first batch, including categories which have no verified detail yet.
        var selected = new Dictionary<int, int>();
        while (pending.Count > 0)
        {
            int Score(CandidateCoverage x) => x.Categories.Select(c => verified.GetValueOrDefault(c) + selected.GetValueOrDefault(c)).DefaultIfEmpty(int.MaxValue).Min();
            var next = pending.OrderBy(Score).ThenBy(x => x.SourceIdentity, StringComparer.Ordinal).First();
            pending.Remove(next); ordered.Add(next);
            foreach (var c in next.Categories) selected[c] = selected.GetValueOrDefault(c) + 1;
        }
        return ordered.Chunk(20).Select((chunk, i) => new DetailBatch(state.Id, checkpoint, sha, i + 1,
            chunk.Select(x => x.SourceIdentity).ToArray(), chunk.Select(x => x.Urls.OrderBy(u => u.Contains("/Pages/", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ThenBy(u => u, StringComparer.Ordinal).First()).ToArray())).ToArray();
    }

    public static async Task<DetailBatch> ReadAsync(string workspace, string path, string sha, InventoryState current)
    {
        var full = Path.GetFullPath(path, workspace);
        if (!full.StartsWith(Path.Combine(workspace, ".local", "evidence", "phase3f") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Private Phase3F work order required.");
        SourceInventory.NoReparse(full);
        if (new FileInfo(full).Length > 128 * 1024) throw new InvalidDataException("Work order exceeds bound.");
        var raw = await File.ReadAllBytesAsync(full);
        if (SourceInventory.Hash(raw) != sha) throw new InvalidDataException("Work order SHA differs.");
        var batch = JsonSerializer.Deserialize<DetailBatch>(raw) ?? throw new InvalidDataException("Invalid work order.");
        var original = await SourceBatch.ReadCheckpointAsync(workspace, batch.Checkpoint, batch.CheckpointSha256);
        Validate(batch, original, current);
        return batch;
    }

    public static void Validate(DetailBatch batch, InventoryState original, InventoryState current)
    {
        if (batch.InventoryId != current.Id || original.Id != current.Id || batch.Urls.Length is < 1 or > 20 || batch.Urls.Length != batch.SourceIdentities.Length || batch.Urls.Distinct().Count() != batch.Urls.Length || batch.SourceIdentities.Distinct().Count() != batch.SourceIdentities.Length)
            throw new InvalidDataException("Work order identity/count differs; cannot start another session to reset quota.");
        for (var i = 0; i < batch.Urls.Length; i++)
        {
            bool Match(InventoryUrl n) => n.Url == batch.Urls[i] && n.Role == "Detail" && n.Kind + ":" + n.CandidateId == batch.SourceIdentities[i];
            if (!original.Urls.Any(Match) || !current.Urls.Any(Match) || original.Captures.Any(c => c.Url == batch.Urls[i]))
                throw new InvalidDataException("Work order must select known detail identities without existing snapshots; no arbitrary refresh or discovery expansion.");
        }
    }

    public static bool Includes(DetailBatch batch, InventoryUrl node) => node.Role == "Robots"
        || node.Role == "Detail" && batch.Urls.Contains(node.Url, StringComparer.Ordinal)
        || node.Role == "File" && node.FoundOn.Any(u => batch.Urls.Contains(u, StringComparer.Ordinal));
}
