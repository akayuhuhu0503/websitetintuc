using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace LandPortal.Infrastructure;

// Preserve signed bytes. Accept only signature fields and inert navigation actions;
// this does not authenticate the signer or assert cryptographic signature validity.
public static class TrustedInternalPdfPolicy
{
    public const int MaximumTokens = 1_000_000;
    public static void Validate(Stream input, CancellationToken ct = default)
    {
        try { ValidateCore(input, ct); }
        catch (Exception error) when (error is not (OutOfMemoryException or OperationCanceledException or InvalidDataException))
        { throw new InvalidDataException("PDF nội bộ không đọc được hoặc cấu trúc không hợp lệ.", error); }
    }
    private static void ValidateCore(Stream input, CancellationToken ct)
    {
        input.Position = 0;
        using var pdf = PdfDocument.Open(input, new ParsingOptions { UseLenientParsing = false, MaxStackDepth = 64 });
        var seen = new HashSet<IndirectReference>();
        var fields = new HashSet<IndirectReference>();
        var count = 0;
        void Tick(int depth) { ct.ThrowIfCancellationRequested(); if (++count > MaximumTokens || depth > 64) throw new InvalidDataException("PDF nội bộ vượt giới hạn cấu trúc an toàn."); }
        IToken Resolve(IToken token)
        {
            var hops = new HashSet<IndirectReference>();
            while (token is IndirectReferenceToken r)
            {
                Tick(hops.Count);
                if (!hops.Add(r.Data)) throw new InvalidDataException("PDF có tham chiếu vòng không hợp lệ.");
                token = pdf.Structure.GetObject(r.Data).Data;
            }
            return token;
        }
        IToken? Get(DictionaryToken d, string key) => d.Data.TryGetValue(key, out var t) ? Resolve(t) : null;
        string? Name(IToken? t) => t is NameToken n ? n.Data : null;
        void Field(IToken token, string? inherited, int depth)
        {
            Tick(depth);
            if (token is IndirectReferenceToken r && !fields.Add(r.Data)) throw new InvalidDataException("PDF có cây chữ ký lặp.");
            if (Resolve(token) is not DictionaryToken d) throw new InvalidDataException("Trường chữ ký PDF không hợp lệ.");
            var type = Name(Get(d, "FT")) ?? inherited;
            if (type != "Sig") throw new InvalidDataException("Chỉ nhận trường chữ ký số, không nhận biểu mẫu tương tác.");
            if (Get(d, "Kids") is ArrayToken kids) foreach (var child in kids.Data) Field(child, type, depth + 1);
            if (Get(d, "V") is { } value)
            {
                if (value is not DictionaryToken sig || Get(sig, "ByteRange") is not ArrayToken ranges || ranges.Data.Count != 4
                    || Get(sig, "Contents") is not (HexToken or StringToken)) throw new InvalidDataException("Cấu trúc chữ ký PDF không hợp lệ.");
                var numbers = ranges.Data.Select(Resolve).OfType<NumericToken>().Select(x => x.Data).ToArray();
                if (numbers.Length != 4 || numbers.Any(x => x < 0 || x != Math.Truncate(x)) || numbers[0] != 0
                    || numbers[1] >= numbers[2] || numbers[2] > input.Length || numbers[3] > input.Length - numbers[2])
                    throw new InvalidDataException("Phạm vi byte chữ ký không hợp lệ.");
            }
            // Shared signature widgets may be reached from both AcroForm and page
            // annotations. Reject recursion cycles, not a second valid traversal.
            if (token is IndirectReferenceToken finished) fields.Remove(finished.Data);
        }
        void Action(IToken value)
        {
            if (Resolve(value) is not DictionaryToken d) throw new InvalidDataException("Hành động PDF không hợp lệ.");
            var kind = Name(Get(d, "S"));
            if (kind == "GoTo" && Get(d, "D") is not null && d.Data.Keys.All(k => k is "S" or "D" or "Type")) return;
            var uriText = Get(d, "URI") switch { StringToken s => s.Data, HexToken h => h.Data, _ => null };
            if (kind == "URI" && d.Data.Keys.All(k => k is "S" or "URI" or "Type")
                && Uri.TryCreate(uriText, UriKind.Absolute, out var address) && address.Scheme is "https" or "http"
                && string.IsNullOrEmpty(address.UserInfo)) return;
            throw new InvalidDataException("PDF có hành động ngoài chính sách điều hướng an toàn.");
        }
        void LayoutAttribute(IToken value)
        {
            // A on a tagged StructElem is an attribute, not an annotation action.
            // Accept only the inert layout fields observed in these signed PDFs.
            if (Resolve(value) is not DictionaryToken d || Name(Get(d, "O")) != "Layout"
                || d.Data.Keys.Any(k => k is not ("O" or "Placement" or "BBox"))
                || Get(d, "Placement") is { } placement && Name(placement) is not ("Block" or "Inline" or "Before" or "Start" or "End")
                || Get(d, "BBox") is { } box && (box is not ArrayToken a || a.Data.Count != 4 || a.Data.Select(Resolve).Any(x => x is not NumericToken n || !double.IsFinite(n.Data))))
                throw new InvalidDataException("Thuộc tính bố cục PDF không thuộc cấu trúc an toàn được hỗ trợ.");
        }
        void Visit(IToken token, int depth)
        {
            Tick(depth);
            switch (token)
            {
                case IndirectReferenceToken r: if (seen.Add(r.Data)) Visit(pdf.Structure.GetObject(r.Data).Data, depth + 1); break;
                case StreamToken s: Visit(s.StreamDictionary, depth + 1); break;
                case ArrayToken a: foreach (var item in a.Data) Visit(item, depth + 1); break;
                case DictionaryToken d:
                    foreach (var entry in d.Data)
                    {
                        if (entry.Key is "AA" or "OpenAction" or "JS" or "JavaScript" or "Launch"
                            or "EmbeddedFiles" or "EmbeddedFile" or "EF" or "AF" or "RichMedia" or "RichMediaContent"
                            or "XFA" or "SubmitForm" or "ImportData" or "GoToR" or "GoToE" or "Collection")
                            throw new InvalidDataException("PDF có nội dung thực thi, tự chạy hoặc tệp nhúng không được phép.");
                        if (entry.Key == "A")
                        {
                            if (Name(Get(d, "Type")) == "StructElem") LayoutAttribute(entry.Value);
                            else Action(entry.Value);
                        }
                        if (entry.Key == "FT" && Name(Resolve(entry.Value)) != "Sig") throw new InvalidDataException("Biểu mẫu PDF tương tác không được phép.");
                        if (entry.Key == "AcroForm")
                        {
                            if (Resolve(entry.Value) is not DictionaryToken form || Get(form, "Fields") is not ArrayToken fs
                                || form.Data.Keys.Any(k => k is not ("Fields" or "SigFlags" or "DA" or "DR" or "NeedAppearances"))
                                || Get(form, "NeedAppearances") is { } appearances && appearances is not BooleanToken { Data: false })
                                throw new InvalidDataException("Biểu mẫu PDF không phải cấu trúc chữ ký được phép.");
                            foreach (var f in fs.Data) Field(f, null, 0);
                        }
                        // Catch actions encoded under nonstandard keys as well as the A entry.
                        if (entry.Key == "S" && Name(Resolve(entry.Value)) is "JavaScript" or "Launch" or "SubmitForm" or "ImportData" or "GoToR" or "GoToE" or "Rendition" or "Movie" or "Sound")
                            throw new InvalidDataException("PDF có kiểu hành động nguy hiểm.");
                        Visit(entry.Value, depth + 1);
                    }
                    break;
            }
        }
        Visit(pdf.Structure.Catalog.CatalogDictionary, 0);
        ct.ThrowIfCancellationRequested();
    }
}
