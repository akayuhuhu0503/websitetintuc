using System.IO.Compression;
using System.Security.Cryptography;
using System.Buffers.Binary;
using LandPortal.Domain;

namespace LandPortal.Infrastructure;

// ZIP is an editorial attachment, never an Office no-AV document. No entry is extracted to disk.
public static class EditorialZipPolicy
{
    public const string Mime = "application/zip";
    public const long Maximum = 128L * 1024 * 1024;
    public const long ExpandedMaximum = 512L * 1024 * 1024;
    public const int EntryMaximum = 256;
    public const int ValidationSeconds = 60;
    private static readonly SemaphoreSlim Slot = new(1, 1);
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i =>
    {
        var value = (uint)i;
        for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320U ^ (value >> 1) : value >> 1;
        return value;
    }).ToArray();

    public sealed record Entry(string Name, long Bytes, string Sha256);

    // Scan both the container and each validated leaf. Do not trust a scanner's archive-recursion setting.
    public static async Task<InquiryScanResult> ScanAsync(string path, IInquiryScanner scanner, CancellationToken ct = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(180));
        var token = budget.Token;
        var inventory = await ValidateAsync(path, token);
        using var file = File.OpenRead(path);
        var result = await scanner.ScanAsync(file, token);
        if (result.State != InquiryScanState.Clean) return result;
        if (string.IsNullOrWhiteSpace(result.EngineVersion)) return new(InquiryScanState.ScanFailed, "ZipScannerEvidenceMissing", null);
        file.Position = 0;
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        var outputs = new List<string?> { result.OutputSha256 };
        foreach (var expected in inventory)
        {
            token.ThrowIfCancellationRequested();
            using var stream = zip.GetEntry(expected.Name)!.Open();
            var leaf = await scanner.ScanAsync(stream, token);
            if (leaf.State != InquiryScanState.Clean) return leaf;
            if (leaf.EngineVersion != result.EngineVersion || leaf.Provider != result.Provider || leaf.SignatureVersion != result.SignatureVersion)
                return new(InquiryScanState.ScanFailed, "ZipScannerVersionChanged", null);
            outputs.Add(leaf.OutputSha256);
        }
        token.ThrowIfCancellationRequested();
        return result with { OutputSha256 = Convert.ToHexStringLower(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { inventory, outputs }))) };
    }

    public static async Task<IReadOnlyList<Entry>> ValidateAsync(string path, CancellationToken ct = default)
    {
        if (!await Slot.WaitAsync(0, ct)) throw new MediaUploadException("Đang kiểm định một ZIP khác. Vui lòng thử lại sau.", 429);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(ValidationSeconds));
            return Validate(path, budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new MediaUploadException("ZIP vượt thời gian kiểm định 60 giây; chưa tiếp nhận.", 408); }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or NotSupportedException)
        { throw new MediaUploadException("ZIP lỗi cấu trúc, mã hóa hoặc dùng kiểu nén không được hỗ trợ."); }
        finally { Slot.Release(); }
    }

    private static IReadOnlyList<Entry> Validate(string path, CancellationToken ct)
    {
        using var input = File.OpenRead(path);
        EditorialDocumentPolicy.RequireSize(input.Length, Mime);
        Span<byte> magic = stackalloc byte[4];
        if (input.Read(magic) != 4 || !magic.SequenceEqual("PK\x03\x04"u8))
            throw new MediaUploadException("ZIP không đúng định dạng; không nhận tệp tự giải nén.");
        RequireBoundedDirectory(input);
        input.Position = 0;
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count is < 1 or > EntryMaximum) throw new MediaUploadException("ZIP cần từ 1 đến 256 mục.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = entry.FullName.Normalize(System.Text.NormalizationForm.FormC);
            var directory = name.EndsWith('/');
            var parts = name.TrimEnd('/').Split('/');
            if (name.Length is < 1 or > 240 || name.StartsWith('/') || name.Contains('\\') || name.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c))
                || parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || Reserved(p))
                || !names.Add(name.TrimEnd('/')) || entry.IsEncrypted
                || ((entry.ExternalAttributes >> 16) & 0xf000) is not (0 or 0x8000 or 0x4000)
                || (entry.ExternalAttributes & 0x400) != 0)
                throw new MediaUploadException("ZIP có tên/đường dẫn nguy hiểm, liên kết, mục trùng hoặc mã hóa.");
            if (directory && entry.Length != 0) throw new MediaUploadException("Thư mục ZIP chứa dữ liệu không hợp lệ.");
            if (directory) continue;
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension == ".zip") throw new MediaUploadException("ZIP lồng chưa thuộc chính sách tài liệu được phép; giữ nguyên nguồn để rà soát.");
            if (extension is not (".pdf" or ".docx" or ".xlsx"))
                throw new MediaUploadException("ZIP chỉ nhận PDF/DOCX/XLSX; không nhận tệp thực thi, DOC/XLS cũ hoặc loại khác.");
            var mime = OfficeDocumentPolicy.Mime(name);
            EditorialDocumentPolicy.RequireSize(entry.Length, mime);
            expanded = checked(expanded + entry.Length);
            if (expanded > ExpandedMaximum || entry.Length > Math.Max(1024, entry.CompressedLength * 100))
                throw new MediaUploadException("ZIP vượt giới hạn bung nén 512 MiB hoặc tỷ lệ nén an toàn.");
        }
        // Reject file/directory collisions independently of entry ordering.
        foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
            if (zip.Entries.Any(e => e.FullName.Normalize(System.Text.NormalizationForm.FormC).StartsWith(entry.FullName.Normalize(System.Text.NormalizationForm.FormC) + "/", StringComparison.OrdinalIgnoreCase)))
                throw new MediaUploadException("ZIP có tệp trùng đường dẫn thư mục.");
        var result = new List<Entry>();
        foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            ct.ThrowIfCancellationRequested();
            using var source = entry.Open();
            using var bytes = new MemoryStream((int)entry.Length);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var crc = uint.MaxValue;
            var buffer = new byte[65536]; int read;
            while ((read = source.Read(buffer)) != 0)
            {
                ct.ThrowIfCancellationRequested();
                if (bytes.Length + read > entry.Length) throw new MediaUploadException("ZIP bung nén vượt kích thước khai báo.");
                bytes.Write(buffer, 0, read); hash.AppendData(buffer, 0, read);
                for (var i = 0; i < read; i++) crc = CrcTable[(crc ^ buffer[i]) & 0xff] ^ (crc >> 8);
            }
            if (bytes.Length != entry.Length || ~crc != entry.Crc32) throw new MediaUploadException("ZIP sai kích thước hoặc CRC; tệp có thể bị hỏng.");
            bytes.Position = 0;
            var mime = OfficeDocumentPolicy.Mime(entry.FullName);
            if (mime == "application/pdf") MediaService.ValidatePdfContent(bytes, EditorialDocumentPolicy.PdfMaximumPages, ct);
            else OfficeDocumentPolicy.ValidateOffice(bytes, mime, managedWithoutAv: true);
            ct.ThrowIfCancellationRequested();
            result.Add(new(entry.FullName, entry.Length, Convert.ToHexStringLower(hash.GetHashAndReset())));
        }
        if (result.Count == 0) throw new MediaUploadException("ZIP không có tài liệu.");
        return result;
    }

    private static bool Reserved(string part)
    {
        var stem = part.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 &&
            (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9';
    }

    // Reject ZIP64/multi-volume/oversized directories before ZipArchive allocates its entry objects.
    private static void RequireBoundedDirectory(Stream input)
    {
        var tail = new byte[(int)Math.Min(input.Length, 65557)];
        input.Position = input.Length - tail.Length; input.ReadExactly(tail);
        for (var at = tail.Length - 22; at >= 0; at--)
        {
            var record = tail.AsSpan(at);
            if (!record[..4].SequenceEqual("PK\x05\x06"u8) || at + 22 + BinaryPrimitives.ReadUInt16LittleEndian(record[20..]) != tail.Length) continue;
            var count = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            if (BinaryPrimitives.ReadUInt16LittleEndian(record[4..]) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(record[6..]) != 0
                || count != BinaryPrimitives.ReadUInt16LittleEndian(record[8..]) || count is < 1 or > EntryMaximum
                || size > 256 * 1024 || (long)offset + size != input.Length - tail.Length + at)
                throw new MediaUploadException("ZIP nhiều phần, ZIP64 hoặc danh mục vượt giới hạn; chưa tiếp nhận.");
            RequireMatchingHeaders(input, count, offset, size);
            return;
        }
        throw new MediaUploadException("ZIP thiếu danh mục kết thúc hợp lệ.");
    }

    private static void RequireMatchingHeaders(Stream input, int count, long directoryOffset, long directorySize)
    {
        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: true);
        var ranges = new List<(long Start, long End)>();
        var next = directoryOffset;
        for (var i = 0; i < count; i++)
        {
            input.Position = next;
            if (reader.ReadUInt32() != 0x02014b50) throw new InvalidDataException();
            reader.ReadUInt16(); reader.ReadUInt16();
            var flags = reader.ReadUInt16(); var method = reader.ReadUInt16();
            reader.ReadUInt32(); var crc = reader.ReadUInt32(); var compressed = reader.ReadUInt32(); var expanded = reader.ReadUInt32();
            var nameLength = reader.ReadUInt16(); var extra = reader.ReadUInt16(); var comment = reader.ReadUInt16();
            var disk = reader.ReadUInt16(); reader.ReadUInt16(); reader.ReadUInt32(); var local = reader.ReadUInt32();
            next += 46L + nameLength + extra + comment;
            if ((flags & ~0x0808) != 0 || method is not (0 or 8) || disk != 0 || nameLength is < 1 or > 1024 || extra > 4096
                || compressed == uint.MaxValue || expanded == uint.MaxValue || next > directoryOffset + directorySize || local + 30L > directoryOffset)
                throw new MediaUploadException("ZIP có kiểu nén, mã hóa hoặc header ngoài chính sách.");
            var name = reader.ReadBytes(nameLength);
            input.Position = local;
            if (reader.ReadUInt32() != 0x04034b50) throw new InvalidDataException();
            reader.ReadUInt16();
            if (reader.ReadUInt16() != flags || reader.ReadUInt16() != method) throw new InvalidDataException();
            reader.ReadUInt32(); var localCrc = reader.ReadUInt32(); var localCompressed = reader.ReadUInt32(); var localExpanded = reader.ReadUInt32();
            var localName = reader.ReadUInt16(); var localExtra = reader.ReadUInt16();
            if (localName != nameLength || localExtra > 4096 || !reader.ReadBytes(localName).AsSpan().SequenceEqual(name)
                || ((flags & 8) == 0 && (localCrc != crc || localCompressed != compressed || localExpanded != expanded))) throw new InvalidDataException();
            var end = local + 30L + localName + localExtra + compressed;
            if (end > directoryOffset) throw new InvalidDataException();
            if ((flags & 8) != 0)
            {
                input.Position = end;
                var descriptorCrc = reader.ReadUInt32(); end += 12;
                if (descriptorCrc == 0x08074b50) { descriptorCrc = reader.ReadUInt32(); end += 4; }
                if (descriptorCrc != crc || reader.ReadUInt32() != compressed || reader.ReadUInt32() != expanded) throw new InvalidDataException();
            }
            ranges.Add((local, end));
        }
        long covered = 0;
        foreach (var range in ranges.OrderBy(r => r.Start))
        {
            if (range.Start != covered || range.End <= range.Start) throw new MediaUploadException("ZIP có vùng chồng lấn hoặc nội dung ẩn ngoài danh mục.");
            covered = range.End;
        }
        if (covered != directoryOffset || next != directoryOffset + directorySize) throw new InvalidDataException();
    }
}
