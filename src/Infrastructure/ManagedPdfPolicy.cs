using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace LandPortal.Infrastructure;

// Structural validation for the opt-in CMS path only. This is NOT an antivirus result.
public static class ManagedPdfPolicy
{
    public static void Validate(string path)
    {
        try
        {
            using var pdf = PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = false, MaxStackDepth = 64 });
            var seen = new HashSet<IndirectReference>();
            var count = 0;
            void Visit(IToken token, int depth)
            {
                if (++count > 100_000 || depth > 64) throw new InvalidDataException("PDF vượt giới hạn cấu trúc.");
                switch (token)
                {
                    case IndirectReferenceToken reference:
                        if (seen.Add(reference.Data)) Visit(pdf.Structure.GetObject(reference.Data).Data, depth + 1);
                        break;
                    case StreamToken stream: Visit(stream.StreamDictionary, depth + 1); break;
                    case ArrayToken array:
                        foreach (var child in array.Data) Visit(child, depth + 1);
                        break;
                    case DictionaryToken dictionary:
                        foreach (var entry in dictionary.Data)
                        {
                            // Parsed names handle escaped PDF names and indirect/compressed objects.
                            if (entry.Key is "A" or "AA" or "OpenAction" or "JS" or "JavaScript" or "Launch"
                                or "EmbeddedFiles" or "EmbeddedFile" or "EF" or "AF" or "RichMedia" or "RichMediaContent"
                                or "XFA" or "AcroForm" or "SubmitForm" or "ImportData" or "GoToR" or "GoToE")
                                throw new InvalidDataException("PDF có hành động, biểu mẫu hoặc tệp nhúng không được phép.");
                            Visit(entry.Value, depth + 1);
                        }
                        break;
                }
            }
            Visit(pdf.Structure.Catalog.CatalogDictionary, 0);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or OperationCanceledException))
        { throw new InvalidDataException("PDF không đạt kiểm tra cấu trúc an toàn; không phải kết quả quét antivirus.", error); }
    }
}
