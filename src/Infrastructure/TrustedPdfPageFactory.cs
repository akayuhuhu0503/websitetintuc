using System.Diagnostics.CodeAnalysis;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Logging;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Parser;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.PdfFonts;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Util;

namespace LandPortal.Infrastructure;

internal sealed record TrustedPdfPage(Page Page, int MissingGraphicsStates, int MissingXObjects);

// PdfPig 0.1.14's strict ResourceStore throws for an undefined `gs` resource,
// although its content processor supports a null state (keep the current state).
// An undefined Do resource is likewise a non-executable rendering defect. Allow
// only ABSENT names (not declared but broken objects), record a warning and require
// remaining readable page content. All present operations/resources are processed.
// The default page/content processor,
// strict xref/token/content parsing, fonts, XObjects and every other lookup stay
// unchanged. In particular, this does NOT enable global UseLenientParsing or
// SkipMissingFonts, or skip a failed page. No source bytes are rewritten.
internal sealed class TrustedPdfPageFactory : IPageFactory<TrustedPdfPage>
{
    private readonly GraphicsStateResources resources;
    private readonly IPageFactory<Page> strict;

    public TrustedPdfPageFactory(IPdfTokenScanner scanner, IResourceStore store,
        ILookupFilterProvider filters, IPageContentParser parser, ParsingOptions options)
    {
        if (options.UseLenientParsing || options.SkipMissingFonts) throw new InvalidOperationException("Trusted PDF requires strict parsing.");
        resources = new(store, scanner);
        // The default factory is internal in the pinned PdfPig release. Use its
        // existing implementation rather than copying its renderer. Fail closed
        // if a future package changes this constructor/interface; focused tests
        // exercise this adapter. No private fields or parser settings are changed.
        var factory = typeof(PdfDocument).Assembly.GetType("UglyToad.PdfPig.Parser.PageFactory", throwOnError: true)!;
        strict = Activator.CreateInstance(factory, scanner, resources, filters, new ResourceAwareParser(parser, resources), options) as IPageFactory<Page>
            ?? throw new InvalidOperationException("Unsupported PDF page factory.");
    }

    public TrustedPdfPage Create(int number, DictionaryToken dictionary, PageTreeMembers members, NamedDestinations destinations)
    {
        var before = resources.Missing;
        var beforeXObjects = resources.MissingXObjects;
        var page = strict.Create(number, dictionary, members, destinations);
        var missingXObjects = resources.MissingXObjects - beforeXObjects;
        if (missingXObjects > 0 && page.Letters.Count == 0 && page.NumberOfImages == 0 && page.Paths.Count == 0)
            throw new InvalidDataException("PDF thiếu tài nguyên và không còn nội dung trang đọc được.");
        return new(page, resources.Missing - before, missingXObjects);
    }

    private sealed class ResourceAwareParser(IPageContentParser inner, GraphicsStateResources resources) : IPageContentParser
    {
        public IReadOnlyList<IGraphicsStateOperation> Parse(int number, IInputBytes bytes, ILog log) =>
            inner.Parse(number, bytes, log).Select(op => op is InvokeNamedXObject invoke
                ? (IGraphicsStateOperation)new CheckedXObject(invoke, resources) : op).ToArray();
    }
    private sealed class CheckedXObject(InvokeNamedXObject inner, GraphicsStateResources resources) : IGraphicsStateOperation
    {
        public string Operator => inner.Operator;
        public void Write(Stream stream) => inner.Write(stream);
        public void Run(IOperationContext context)
        {
            if (!resources.DeclaresXObject(inner.Name)) { resources.RecordMissingXObject(); return; }
            inner.Run(context); // A declared but broken image/form still fails normally.
        }
    }
    private sealed class GraphicsStateResources(IResourceStore inner, IPdfTokenScanner scanner) : IResourceStore
    {
        private readonly Stack<HashSet<string>> declaredXObjects = new();
        public int Missing { get; private set; }
        public int MissingXObjects { get; private set; }
        public bool DeclaresXObject(NameToken name) => declaredXObjects.Any(scope => scope.Contains(name.Data));
        public void RecordMissingXObject()
        {
            if (++MissingXObjects > TrustedInternalPdfPolicy.MaximumTokens) throw new InvalidDataException("PDF vượt giới hạn thao tác đồ họa.");
        }
        public DictionaryToken? GetExtendedGraphicsStateDictionary(NameToken name)
        {
            try { return inner.GetExtendedGraphicsStateDictionary(name); }
            catch (KeyNotFoundException)
            {
                if (++Missing > TrustedInternalPdfPolicy.MaximumTokens) throw new InvalidDataException("PDF vượt giới hạn thao tác đồ họa.");
                return null;
            }
        }
        public void LoadResourceDictionary(DictionaryToken dictionary)
        {
            var names = dictionary.TryGet(NameToken.Xobject, out var value)
                ? DirectObjectFinder.Get<DictionaryToken>(value, scanner).Data.Keys.ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            inner.LoadResourceDictionary(dictionary);
            declaredXObjects.Push(names);
        }
        public void UnloadResourceDictionary() { inner.UnloadResourceDictionary(); declaredXObjects.Pop(); }
        public IFont? GetFont(NameToken name) => inner.GetFont(name);
        public IFont GetFontDirectly(IndirectReferenceToken token) => inner.GetFontDirectly(token);
        public bool TryGetXObject(NameToken name, [NotNullWhen(true)] out StreamToken? stream) => inner.TryGetXObject(name, out stream);
        public bool TryGetNamedColorSpace(NameToken name, out ResourceColorSpace color) => inner.TryGetNamedColorSpace(name, out color);
        public ColorSpaceDetails GetColorSpaceDetails(NameToken? name, DictionaryToken? dictionary) => inner.GetColorSpaceDetails(name, dictionary);
        public DictionaryToken? GetMarkedContentPropertiesDictionary(NameToken name) => inner.GetMarkedContentPropertiesDictionary(name);
        public IReadOnlyDictionary<NameToken, PatternColor> GetPatterns() => inner.GetPatterns();
        public Shading GetShading(NameToken name) => inner.GetShading(name);
    }
}
